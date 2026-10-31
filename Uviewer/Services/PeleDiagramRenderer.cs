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

namespace Uviewer.Services;

/// <summary>Offline Mermaid-to-image rendering for the native Markdown reader.</summary>
internal static class PeleDiagramRenderer
{
    private const int MaxSourceLength = 50_000;
    private const int MaxCacheCharacters = 16 * 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, (string Image, string? Error, float Dpi)> Cache = new();
    private static int _cacheCharacters;
    private static Engine? _engine;
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> Bundle = new(() =>
    {
        using var stream = typeof(PeleDiagramRenderer).Assembly.GetManifestResourceStream("Uviewer.Pele.js")
            ?? throw new InvalidOperationException("The bundled Pele renderer is missing.");
        using var reader = new StreamReader(stream);
        return Engine.PrepareScript(reader.ReadToEnd(), "pele-1.12.0.js");
    });
    private static readonly Regex Variable = new(@"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*))?\)", RegexOptions.Compiled);
    private static readonly Regex Declaration = new(@"(?:^|;)\s*(--[\w-]+)\s*:\s*([^;]+)", RegexOptions.Compiled);

    public static bool TryGetError(string source, out string? error)
    {
        error = source.Length > MaxSourceLength ? "Diagram exceeds Pele's 50,000-character limit." : null;
        if (error == null && Cache.TryGetValue(GetKey(source), out var cached)) error = cached.Error;
        return error != null;
    }

    private static string GetKey(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    public static bool TryRender(string source, out string image, out string? error)
        => TryRender(source, out image, out error, out _);

    public static bool TryRender(string source, out string image, out string? error, out float dpi)
    {
        image = "";
        error = null;
        dpi = 96;
        if (source.Length > MaxSourceLength)
        {
            error = "Diagram exceeds Pele's 50,000-character limit.";
            return false;
        }

        string key = GetKey(source);
        // Cache reads (including error fallback on the UI thread) never wait for
        // another diagram. JS execution remains serialized because Jint is mutable.
        if (Cache.TryGetValue(key, out var ready))
        {
            (image, error, dpi) = ready;
            return error == null;
        }
        lock (Sync)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                (image, error, dpi) = cached;
                return error == null;
            }
            try
            {
                var engine = _engine ??= CreateEngine();
                // Pass diagram text as data, never as executable JavaScript.
                string svg = engine.Invoke("renderDiagram", source).AsString();
                image = "data:image/png;base64," + Convert.ToBase64String(Rasterize(svg, out dpi));
            }
            catch (Exception ex)
            {
                // Discard partial state after syntax errors, limits or failures.
                _engine?.Dispose();
                _engine = null;
                error = "Pele: " + ex.Message;
                if (error.Length > 1000) error = error[..1000] + "…";
            }

            int size = image.Length + (error?.Length ?? 0);
            if (size <= MaxCacheCharacters)
            {
                if (_cacheCharacters + size > MaxCacheCharacters || Cache.Count >= 128)
                {
                    Cache.Clear();
                    _cacheCharacters = 0;
                }
                Cache[key] = (image, error, dpi);
                _cacheCharacters += size;
            }
            return error == null;
        }
    }

    private static Engine CreateEngine()
    {
        var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(5))
            .LimitMemory(128 * 1024 * 1024)
            .LimitRecursion(256));
        try
        {
            var bundle = Bundle.Value;
            engine.Execute(in bundle);
            engine.Execute("""
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
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private static byte[] Rasterize(string markup, out float dpi)
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
        dpi = layout.Dpi;
        using var surface = SKSurface.Create(new SKImageInfo(layout.Width, layout.Height))
            ?? throw new InvalidOperationException("Could not allocate the diagram image.");
        // A light panel keeps Pele's authored colors readable in all reader themes.
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(layout.Scale);
        surface.Canvas.Translate(-bounds.Left, -bounds.Top);
        surface.Canvas.DrawPicture(picture);
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
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
