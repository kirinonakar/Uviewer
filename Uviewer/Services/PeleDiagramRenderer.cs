using Jint;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services;

/// <summary>Offline Mermaid-to-image rendering for the native Markdown reader.</summary>
internal static class PeleDiagramRenderer
{
    private const int MaxSourceLength = 50_000;
    private const int MaxCacheBytes = 16 * 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly object CacheSync = new();
    private static readonly SemaphoreSlim RasterGate = new(2);
    private static readonly ConcurrentDictionary<string, (byte[]? Raster, string? Error)> Cache = new();
    private static readonly ThumbnailDiskCache DiskCache = new(
        Path.Combine(Path.GetTempPath(), "Uviewer", "Cache", "PeleRaster-v1"), 64 * 1024 * 1024, 512);
    private static int _cacheBytes;
    private static Engine? _engine;
    private static bool _hasFullBundle;
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> Bundle = new(() => LoadBundle("Uviewer.Pele.js"));
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> CommonBundle = new(() => LoadBundle("Uviewer.Pele.Common.js"));

    private static Prepared<Acornima.Ast.Script> LoadBundle(string resource)
    {
        using var stream = typeof(PeleDiagramRenderer).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The bundled Pele renderer is missing.");
        using var reader = new StreamReader(stream);
        return Engine.PrepareScript(reader.ReadToEnd(), resource);
    }
    private static readonly Regex Variable = new(@"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*))?\)", RegexOptions.Compiled);
    private static readonly Regex Declaration = new(@"(?:^|;)\s*(--[\w-]+)\s*:\s*([^;]+)", RegexOptions.Compiled);

    public static bool TryGetError(string source, out string? error)
    {
        error = source.Length > MaxSourceLength ? "Diagram exceeds Pele's 50,000-character limit." : null;
        if (error == null && Cache.TryGetValue(GetKey(source), out var cached)) error = cached.Error;
        return error != null;
    }

    private static string GetKey(string source) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes("pele-1.12.0-bgra4x-v1\n" + Environment.OSVersion.Version + "\n" + source)));

    public static bool TryRender(string source, out PeleDiagramFrame? frame, out string? error)
    {
        frame = null;
        error = null;
        if (source.Length > MaxSourceLength)
        {
            error = "Diagram exceeds Pele's 50,000-character limit.";
            return false;
        }

        string key = GetKey(source);
        if (TryReadCache(key, out frame, out error)) return error == null;

        // This method runs on an image-loader worker. A disk hit bypasses Jint,
        // Svg.Skia and all first-use initialization, including after app restarts.
        var stored = DiskCache.ReadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        if (stored != null && (frame = PeleDiagramFrameCodec.Decode(stored)) != null)
        {
            StoreCache(key, stored, null);
            return true;
        }
        RasterGate.Wait();
        try
        {
            if (TryReadCache(key, out frame, out error)) return error == null;
            string svg;
            try
            {
                lock (Sync)
                {
                    try
                    {
                        var engine = _engine ??= CreateEngine(IsCommonSource(source));
                        if (!_hasFullBundle && !engine.Invoke("supportsDiagram", source).AsBoolean())
                        {
                            var full = Bundle.Value;
                            engine.Execute(in full);
                            _hasFullBundle = true;
                        }
                        // Pass diagram text as data, never as executable JavaScript.
                        svg = engine.Invoke("renderDiagram", source).AsString();
                    }
                    catch
                    {
                        _engine?.Dispose();
                        _engine = null;
                        _hasFullBundle = false;
                        throw;
                    }
                }
                // Independent SVG parsers/rasterizers can overlap. Only mutable
                // JS execution is serialized, rather than the entire image path.
                frame = Rasterize(svg);
            }
            catch (Exception ex)
            {
                error = "Pele: " + ex.Message;
                if (error.Length > 1000) error = error[..1000] + "…";
                StoreCache(key, null, error);
                return false;
            }
            try
            {
                // LZ4 preserves every pixel while avoiding PNG compression,
                // Base64 strings, stream copies and WIC decoding on display.
                var packed = PeleDiagramFrameCodec.Encode(frame);
                StoreCache(key, packed, null);
                _ = SaveDiskCacheAsync(key, packed);
            }
            catch (Exception ex)
            {
                // Caching is optional; never hide a successfully rendered frame.
                System.Diagnostics.Debug.WriteLine($"Pele cache write skipped: {ex.Message}");
            }
            return true;
        }
        finally { RasterGate.Release(); }
    }

    private static bool TryReadCache(string key, out PeleDiagramFrame? frame, out string? error)
    {
        frame = null;
        error = null;
        if (!Cache.TryGetValue(key, out var cached)) return false;
        error = cached.Error;
        if (error != null) return true;
        frame = cached.Raster == null ? null : PeleDiagramFrameCodec.Decode(cached.Raster);
        return frame != null;
    }

    private static void StoreCache(string key, byte[]? raster, string? error)
    {
        int size = (raster?.Length ?? 0) + (error?.Length ?? 0) * 2;
        if (size > MaxCacheBytes) return;
        lock (CacheSync)
        {
            if (Cache.ContainsKey(key)) return;
            if (_cacheBytes + size > MaxCacheBytes || Cache.Count >= 128)
            {
                Cache.Clear();
                _cacheBytes = 0;
            }
            Cache[key] = (raster, error);
            _cacheBytes += size;
        }
    }

    private static async Task SaveDiskCacheAsync(string key, byte[] raster)
    {
        try { await DiskCache.WriteAsync(key, raster, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Pele disk cache write skipped: {ex.Message}"); }
    }

    private static bool IsCommonSource(string source)
    {
        bool first = true, frontmatter = false;
        foreach (var raw in source.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.IsEmpty) continue;
            if (frontmatter)
            {
                if (line.SequenceEqual("---")) frontmatter = false;
                continue;
            }
            if (first && line.SequenceEqual("---")) { first = false; frontmatter = true; continue; }
            first = false;
            if (line.StartsWith("%%")) continue;
            int end = line.IndexOfAny(' ', '\t', ';');
            var type = end < 0 ? line : line[..end];
            return type.SequenceEqual("flowchart") || type.SequenceEqual("flowchart-elk") ||
                type.SequenceEqual("graph") || type.SequenceEqual("sequenceDiagram");
        }
        return false;
    }

    private static Engine CreateEngine(bool common)
    {
        var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(5))
            .LimitMemory(128 * 1024 * 1024)
            .LimitRecursion(256));
        try
        {
            var bundle = common ? CommonBundle.Value : Bundle.Value;
            engine.Execute(in bundle);
            engine.Execute("""
                function supportsDiagram(source) { return pele.supports(source); }
                function renderDiagram(source) {
                    try {
                        return pele.render(source, {
                            maxWidth: 1200,
                            fontFamily: 'Segoe UI',
                            links: false,
                            images: false,
                            limit: 50000,
                            outputLimit: 4000000,
                            maxEdges: 1000
                        }).svg;
                    } finally {
                        pele.forgetTextWidths();
                    }
                }
                """);
            _hasFullBundle = !common;
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private static PeleDiagramFrame Rasterize(string markup)
    {
        // Pele uses CSS variables, which native SVG readers don't consistently
        // resolve. Materialize inherited values before passing it to Svg.Skia.
        var document = XDocument.Parse(markup);
        ResolveVariables(document.Root!, new Dictionary<string, string>());
        using var svg = new SKSvg();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)));
        var picture = svg.Load(stream) ?? throw new InvalidOperationException("Could not draw the Pele diagram.");
        var bounds = picture.CullRect;
        var layout = PeleRasterLayout.Create(bounds.Width, bounds.Height);
        var info = new SKImageInfo(layout.Width, layout.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not allocate the diagram image.");
        // A light panel keeps Pele's authored colors readable in all reader themes.
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(layout.Scale);
        surface.Canvas.Translate(-bounds.Left, -bounds.Top);
        surface.Canvas.DrawPicture(picture);
        using var pixels = surface.PeekPixels();
        var bytes = GC.AllocateUninitializedArray<byte>(checked(layout.Width * layout.Height * 4));
        var source = pixels.GetPixelSpan();
        int rowBytes = checked(layout.Width * 4);
        for (int row = 0; row < layout.Height; row++)
            source.Slice(row * pixels.RowBytes, rowBytes).CopyTo(bytes.AsSpan(row * rowBytes, rowBytes));
        return new(bytes, layout.Width, layout.Height, layout.Dpi);
    }

    private static void ResolveVariables(XElement element, Dictionary<string, string> inherited)
    {
        var variables = new Dictionary<string, string>(inherited);
        foreach (Match match in Declaration.Matches((string?)element.Attribute("style") ?? ""))
            variables[match.Groups[1].Value] = match.Groups[2].Value;

        foreach (var attribute in element.Attributes())
        {
            string value = attribute.Value;
            for (int i = 0; i < 16 && value.Contains("var(", StringComparison.Ordinal); i++)
            {
                string resolved = Variable.Replace(value, match => variables.TryGetValue(match.Groups[1].Value, out var replacement)
                    ? replacement : match.Groups[2].Value);
                if (resolved == value) break;
                value = resolved;
            }
            attribute.Value = value;
        }
        foreach (var child in element.Elements()) ResolveVariables(child, variables);
    }
}
