using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Uviewer.Models;

namespace Uviewer.Services
{
    /// <summary>
    /// 뷰잉 컨텍스트: 이미지 리소스 서비스가 경로를 해석하기 위해 필요한
    /// 현재 뷰어 상태의 불변 스냅샷. record이므로 값(value) 의미론을 가진다.
    /// </summary>
    public record ViewingContext(
        bool IsEpubMode,
        bool IsWebDavMode,
        EpubSession? EpubSession,
        string? CurrentTextFilePath,
        string? CurrentTextArchiveEntryKey,
        ArchiveSession? ArchiveSession,
        string? CurrentWebDavItemPath,
        List<ImageEntry>? ImageEntries,
        Func<string, string?>? ResolveWebDavImagePath,
        WebDavService? WebDavService
    );

    /// <summary>
    /// 모든 렌더링 모드(Aozora 가로/세로, EPUB)가 공용으로 사용하는
    /// 이미지 리소스 로딩·캐싱 서비스. 기존에 세 partial 클래스에
    /// 분산되어 있던 DoesXxxImageExist / LoadXxxImageAsync 로직 통합.
    /// </summary>
    public class ImageResourceService
    {
        private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        // ------------------------------------------------------------------
        // 통합 이미지 캐시
        // 키 네임스페이스 규칙:
        //   - EPUB 이미지   : "epub:" + fullPath  (CloseCurrentEpub 시 선택적 제거)
        //   - 텍스트 이미지 : "text:" + relativePath
        // ------------------------------------------------------------------
        private readonly Dictionary<string, CanvasBitmap?> _cache = new();
        private readonly HashSet<string> _knownMissing = new();
        private readonly object _lock = new();
        private readonly Dictionary<(int Version, string Key), Task<CanvasBitmap?>> _diagramLoads = new();
        private int _cacheVersion;

        private readonly ISharpeningService _sharpeningService;

        public ImageResourceService(ISharpeningService sharpeningService)
        {
            _sharpeningService = sharpeningService;
        }

        // ------------------------------------------------------------------
        // 공개 API
        // ------------------------------------------------------------------

        /// <summary>
        /// 캐시된 비트맵을 가져온다. 없으면 null.
        /// </summary>
        public CanvasBitmap? TryGetCached(string cacheKey)
        {
            lock (_lock)
            {
                _cache.TryGetValue(cacheKey, out var bmp);
                if (bmp != null && !IsBitmapUsable(bmp))
                {
                    _cache.Remove(cacheKey);
                    return null;
                }
                return bmp;
            }
        }

        /// <summary>
        /// 이미 준비된 CanvasBitmap을 캐시에 직접 저장한다.
        /// EPUB처럼 FindEntryLoose 등 커스텀 추출 경로가 있는 경우 사용.
        /// </summary>
        public void StoreBitmap(string cacheKey, CanvasBitmap bitmap)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(cacheKey, out var existing) && existing != null)
                    return;
                _cache[cacheKey] = bitmap;
                _knownMissing.Remove(cacheKey);
            }
        }

        /// <summary>
        /// 지정 이미지가 현재 컨텍스트에서 존재하는지 빠르게 확인.
        /// (동기 메서드; I/O가 없으므로 렌더 루프 내에서 호출 가능)
        /// </summary>
        public bool DoesImageExist(string relativePath, ViewingContext ctx)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            try
            {
                if (IsExternalImage(relativePath))
                {
                    lock (_lock) return !_knownMissing.Contains(GetTextCacheKey(relativePath));
                }
                relativePath = NormalizeImagePath(relativePath);
                // (A) EPUB 아카이브
                if (ctx.IsEpubMode && ctx.EpubSession != null)
                {
                    return ctx.EpubSession.ContainsEntryLoose(relativePath);
                }

                // (B) WebDAV
                if (ctx.IsWebDavMode && !string.IsNullOrEmpty(ctx.CurrentWebDavItemPath))
                {
                    string key = GetTextCacheKey(relativePath);
                    lock (_lock)
                    {
                        if (_knownMissing.Contains(key)) return false;
                    }

                    if (ctx.ImageEntries != null &&
                        !relativePath.Contains('/') && !relativePath.Contains('\\'))
                    {
                        string? fullRemotePath = ctx.ResolveWebDavImagePath?.Invoke(relativePath);
                        if (fullRemotePath != null &&
                            !ctx.ImageEntries.Any(e =>
                                e.WebDavPath == fullRemotePath ||
                                string.Equals(e.WebDavPath, fullRemotePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            return false;
                        }
                    }
                    return true; // 실제 확인은 비동기 로드 시
                }

                // (C) 로컬 파일
                if (!string.IsNullOrEmpty(ctx.CurrentTextFilePath) &&
                    ctx.CurrentTextArchiveEntryKey == null)
                {
                    string fullPath = Path.Combine(
                        Path.GetDirectoryName(ctx.CurrentTextFilePath)!, relativePath);
                    return File.Exists(fullPath);
                }

                // (D) ZIP / 7z 아카이브
                if (ctx.ArchiveSession?.HasArchive == true &&
                    !string.IsNullOrEmpty(ctx.CurrentTextArchiveEntryKey))
                {
                    string targetKey = BuildArchiveKey(ctx.CurrentTextArchiveEntryKey!, relativePath);
                    return ctx.ArchiveSession.ContainsEntryLoose(targetKey);
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 이미지를 비동기로 로드하여 캐시에 저장한다.
        /// 이미 캐시에 있으면 즉시 반환.
        /// </summary>
        /// <param name="cacheKey">캐시 키 (GetEpubCacheKey / GetTextCacheKey 사용)</param>
        /// <param name="relativePath">아카이브/로컬 해석에 쓰이는 상대 경로</param>
        /// <param name="device">Win2D CanvasDevice</param>
        /// <param name="ctx">현재 뷰잉 컨텍스트</param>
        /// <param name="sharpenEnabled">샤프닝 적용 여부</param>
        /// <param name="sharpenParams">샤프닝 파라미터</param>
        /// <returns>로드된 CanvasBitmap (실패 시 null)</returns>
        public async Task<CanvasBitmap?> LoadAsync(
            string cacheKey,
            string relativePath,
            CanvasDevice device,
            ViewingContext ctx,
            bool sharpenEnabled,
            SharpenParams sharpenParams,
            Func<bool>? shouldKeepLoadedBitmap = null)
        {
            if (!PeleDiagramSource.IsDiagram(relativePath))
                return await LoadCoreAsync(cacheKey, relativePath, device, ctx, sharpenEnabled, sharpenParams, shouldKeepLoadedBitmap);

            // A second viewport request must wait for the actual result rather
            // than treating the cache's in-flight null placeholder as a failure.
            Task<CanvasBitmap?> pending;
            TaskCompletionSource<CanvasBitmap?>? owner = null;
            (int Version, string Key) key;
            lock (_lock)
            {
                key = (_cacheVersion, cacheKey);
                if (!_diagramLoads.TryGetValue(key, out pending!))
                {
                    // Selective cache clears also invalidate pending work. A null
                    // left by an older version must not masquerade as a cache hit.
                    if (_cache.TryGetValue(cacheKey, out var cached) && cached == null)
                        _cache.Remove(cacheKey);
                    owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending = owner.Task;
                    _diagramLoads[key] = pending;
                }
            }
            if (owner == null) return await pending;
            try
            {
                var result = await LoadCoreAsync(cacheKey, relativePath, device, ctx, sharpenEnabled, sharpenParams, shouldKeepLoadedBitmap);
                owner.TrySetResult(result);
                return result;
            }
            catch (Exception ex)
            {
                owner.TrySetException(ex);
                throw;
            }
            finally
            {
                lock (_lock) _diagramLoads.Remove(key);
            }
        }

        private async Task<CanvasBitmap?> LoadCoreAsync(
            string cacheKey,
            string relativePath,
            CanvasDevice device,
            ViewingContext ctx,
            bool sharpenEnabled,
            SharpenParams sharpenParams,
            Func<bool>? shouldKeepLoadedBitmap)
        {
            int loadVersion;

            // 캐시 확인 (이미 로드됨)
            lock (_lock)
            {
                if (_cache.TryGetValue(cacheKey, out var cached))
                    return cached;

                if (_knownMissing.Contains(cacheKey))
                    return null;

                // 로딩 중 표시 (null 플레이스홀더)
                loadVersion = _cacheVersion;
                _cache[cacheKey] = null;
            }

            try
            {
                byte[]? bytes = await ExtractBytesAsync(relativePath, ctx);

                if (bytes == null)
                {
                    lock (_lock)
                    {
                        if (loadVersion == _cacheVersion)
                        {
                            _knownMissing.Add(cacheKey);
                            _cache.Remove(cacheKey);
                        }
                    }
                    return null;
                }

                // [안정성 수정] using으로 감싸서 스트림 리소스 누수 방지
                CanvasBitmap originalBitmap;
                using (var ras = new InMemoryRandomAccessStream())
                {
                    using (var writer = new Windows.Storage.Streams.DataWriter(ras))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    ras.Seek(0);

                    // SVG badges/diagrams are common in Markdown but are not WIC bitmaps.
                    string prefix = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
                    if (Regex.IsMatch(prefix, @"<svg(?:\s|>)", RegexOptions.IgnoreCase))
                    {
                        using var svg = await CanvasSvgDocument.LoadAsync(device, ras);
                        var size = GetSvgSize(Encoding.UTF8.GetString(bytes));
                        var rendered = new CanvasRenderTarget(device, size.width, size.height, 144);
                        using (var session = rendered.CreateDrawingSession())
                        {
                            session.Clear(Microsoft.UI.Colors.Transparent);
                            session.DrawSvg(svg, new Windows.Foundation.Size(size.width, size.height));
                        }
                        originalBitmap = rendered;
                    }
                    else originalBitmap = await CanvasBitmap.LoadAsync(device, ras);
                }
                CanvasBitmap finalBitmap = originalBitmap;

                if (sharpenEnabled)
                {
                    var sharpened = await _sharpeningService.ApplySharpenToBitmapAsync(
                        originalBitmap,
                        sharpenParams.UpscaleFactor,
                        sharpenParams.SharpenAmount,
                        sharpenParams.SharpenThreshold,
                        sharpenParams.UnsharpAmount,
                        sharpenParams.UnsharpRadius,
                        skipUpscale: false);

                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        finalBitmap = sharpened;
                        originalBitmap.Dispose();
                    }
                }

                bool discardLoadedBitmap = false;
                if (shouldKeepLoadedBitmap != null && !shouldKeepLoadedBitmap())
                {
                    discardLoadedBitmap = true;
                }

                lock (_lock)
                {
                    if (discardLoadedBitmap || loadVersion != _cacheVersion || !_cache.ContainsKey(cacheKey))
                    {
                        discardLoadedBitmap = true;
                        if (loadVersion == _cacheVersion)
                            _cache.Remove(cacheKey);
                    }
                    else
                    {
                        _cache[cacheKey] = finalBitmap;
                    }
                }

                if (discardLoadedBitmap)
                {
                    SafeDispose(finalBitmap);
                    return null;
                }

                return finalBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageResourceService] LoadAsync failed: {ex.Message}");
                lock (_lock)
                {
                    if (loadVersion == _cacheVersion)
                    {
                        _cache.Remove(cacheKey);
                        if (IsExternalImage(relativePath)) _knownMissing.Add(cacheKey);
                    }
                }
                return null;
            }
        }

        public Task PreloadTextImagesAsync(
            IEnumerable<AozoraBindingModel> blocks,
            CanvasDevice device,
            ViewingContext ctx,
            bool sharpenEnabled,
            SharpenParams sharpenParams)
        {
            var loadTasks = blocks
                .Where(block => block.HasImage)
                .Select(block => block.Inlines.OfType<AozoraImage>().FirstOrDefault()?.Source)
                .Where(source => !string.IsNullOrEmpty(source))
                .Select(async source =>
                {
                    string relativePath = source!;
                    string cacheKey = GetTextCacheKey(relativePath);
                    if (TryGetCached(cacheKey) == null)
                    {
                        await LoadAsync(cacheKey, relativePath, device, ctx, sharpenEnabled, sharpenParams);
                    }
                })
                .ToList();

            return Task.WhenAll(loadTasks);
        }

        // ------------------------------------------------------------------
        // 캐시 관리
        // ------------------------------------------------------------------

        /// <summary>전체 캐시 비우기 (모드 전환, EPUB 닫기 등)</summary>
        public void Clear()
        {
            List<CanvasBitmap> bitmapsToDispose;
            lock (_lock)
            {
                _cacheVersion++;
                bitmapsToDispose = _cache.Values.Where(b => b != null).Cast<CanvasBitmap>().Distinct().ToList();
                _cache.Clear();
                _knownMissing.Clear();
            }

            foreach (var bmp in bitmapsToDispose)
                SafeDispose(bmp);
        }

        /// <summary>EPUB 전용 항목만 선택적으로 제거 ("epub:" 접두어 키)</summary>
        public void ClearEpubEntries()
        {
            List<CanvasBitmap> bitmapsToDispose;
            lock (_lock)
            {
                _cacheVersion++;
                var epubKeys = _cache.Keys.Where(k => k.StartsWith("epub:")).ToList();
                bitmapsToDispose = epubKeys
                    .Select(k => _cache[k])
                    .Where(b => b != null)
                    .Cast<CanvasBitmap>()
                    .Distinct()
                    .ToList();

                foreach (var k in epubKeys)
                {
                    _cache.Remove(k);
                }
                _knownMissing.RemoveWhere(k => k.StartsWith("epub:"));
            }

            foreach (var bmp in bitmapsToDispose)
                SafeDispose(bmp);
        }

        /// <summary>텍스트/아카이브 전용 항목만 선택적으로 제거 ("text:" 접두어 키)</summary>
        public void ClearTextEntries()
        {
            List<CanvasBitmap> bitmapsToDispose;
            lock (_lock)
            {
                _cacheVersion++;
                var textKeys = _cache.Keys.Where(k => k.StartsWith("text:")).ToList();
                bitmapsToDispose = textKeys
                    .Select(k => _cache[k])
                    .Where(b => b != null)
                    .Cast<CanvasBitmap>()
                    .Distinct()
                    .ToList();

                foreach (var k in textKeys)
                {
                    _cache.Remove(k);
                }
                _knownMissing.RemoveWhere(k => k.StartsWith("text:"));
            }

            foreach (var bmp in bitmapsToDispose)
                SafeDispose(bmp);
        }

        /// <summary>누락 경로 기록을 초기화한다 (WebDAV 재연결 후 등)</summary>
        public void ClearMissingCache()
        {
            lock (_lock) { _knownMissing.Clear(); }
        }

        // ------------------------------------------------------------------
        // 캐시 키 팩토리 (호출 측에서 사용)
        // ------------------------------------------------------------------
        public static string GetEpubCacheKey(string fullPath) => "epub:" + fullPath;
        public static string GetTextCacheKey(string relativePath) => "text:" + relativePath;

        // ------------------------------------------------------------------
        // 내부 헬퍼: 바이트 추출
        // ------------------------------------------------------------------

        private async Task<byte[]?> ExtractBytesAsync(string relativePath, ViewingContext ctx)
        {
            if (PeleDiagramSource.IsDiagram(relativePath))
            {
                return await Task.Run(() =>
                {
                    string source = PeleDiagramSource.Decode(relativePath);
                    if (!PeleDiagramRenderer.TryRender(source, out string image, out string? error))
                        throw new InvalidDataException(error);
                    return Convert.FromBase64String(image[(image.IndexOf(',') + 1)..]);
                });
            }
            if (relativePath.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                int comma = relativePath.IndexOf(',');
                if (comma < 0) return null;
                string payload = relativePath[(comma + 1)..];
                return relativePath[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }
            if (Uri.TryCreate(relativePath, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return await ImageHttpClient.GetByteArrayAsync(uri);
            relativePath = NormalizeImagePath(relativePath);
            // (A) EPUB 아카이브
            if (ctx.IsEpubMode && ctx.EpubSession != null)
            {
                return await ctx.EpubSession.ReadEntryBytesLooseAsync(relativePath);
            }

            // (B) WebDAV
            if (ctx.IsWebDavMode && !string.IsNullOrEmpty(ctx.CurrentWebDavItemPath) &&
                ctx.WebDavService != null)
            {
                string? fullRemotePath = ctx.ResolveWebDavImagePath?.Invoke(relativePath);
                if (fullRemotePath == null) return null;

                var tempPath = await ctx.WebDavService.DownloadToTempFileAsync(fullRemotePath);
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    return await File.ReadAllBytesAsync(tempPath);
                }

                // 다운로드 실패 → known-missing 등록
                string key = GetTextCacheKey(relativePath);
                lock (_lock) { _knownMissing.Add(key); }
                return null;
            }

            // (C) 로컬 파일
            if (!string.IsNullOrEmpty(ctx.CurrentTextFilePath) &&
                ctx.CurrentTextArchiveEntryKey == null)
            {
                string fullPath = Path.Combine(
                    Path.GetDirectoryName(ctx.CurrentTextFilePath)!, relativePath);
                if (File.Exists(fullPath))
                    return await File.ReadAllBytesAsync(fullPath);
                return null;
            }

            // (D) ZIP / 7z 아카이브
            if (ctx.ArchiveSession?.HasArchive == true &&
                !string.IsNullOrEmpty(ctx.CurrentTextArchiveEntryKey))
            {
                string targetKey = BuildArchiveKey(ctx.CurrentTextArchiveEntryKey!, relativePath);
                return await ctx.ArchiveSession.ReadEntryBytesLooseAsync(targetKey);
            }

            return null;
        }

        private static string BuildArchiveKey(string archiveEntryKey, string relativePath)
        {
            string normKey = archiveEntryKey.Replace('\\', '/');
            string? baseDir = "";
            int lastSlash = normKey.LastIndexOf('/');
            if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

            string subPath = relativePath.Replace('\\', '/').TrimStart('/');
            string targetKey = string.IsNullOrEmpty(baseDir)
                ? subPath
                : (baseDir.TrimEnd('/') + "/" + subPath);
            var segments = new List<string>();
            foreach (var segment in targetKey.Split('/'))
            {
                if (segment is "" or ".") continue;
                if (segment == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); }
                else segments.Add(segment);
            }
            return string.Join('/', segments);
        }

        private static bool IsExternalImage(string path) =>
            PeleDiagramSource.IsDiagram(path) ||
            path.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
            (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");

        private static (float width, float height) GetSvgSize(string source)
        {
            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(source);
            var root = document.DocumentNode.Descendants("svg").FirstOrDefault();
            float Length(string name) => float.TryParse(Regex.Match(root?.GetAttributeValue(name, "") ?? "", @"^\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*$").Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
            float width = Length("width"), height = Length("height");
            var box = Regex.Split(root?.GetAttributeValue("viewbox", "").Trim() ?? "", @"[\s,]+");
            if (box.Length == 4 && float.TryParse(box[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vw) &&
                float.TryParse(box[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vh) && vw > 0 && vh > 0)
            {
                if (width <= 0 && height > 0) width = height * vw / vh;
                if (height <= 0 && width > 0) height = width * vh / vw;
                if (width <= 0 && height <= 0) { width = vw; height = vh; }
            }
            width = width > 0 ? width : 300;
            height = height > 0 ? height : 150;
            float scale = Math.Min(1, 4096 / Math.Max(width, height));
            return (Math.Max(1, width * scale), Math.Max(1, height * scale));
        }

        private static string NormalizeImagePath(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) return uri.LocalPath;
            int suffix = path.IndexOfAny(['?', '#']);
            return Uri.UnescapeDataString(suffix >= 0 ? path[..suffix] : path);
        }

        private static bool IsBitmapUsable(CanvasBitmap bitmap)
        {
            try
            {
                return bitmap.Device != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SafeDispose(CanvasBitmap? bitmap)
        {
            try
            {
                bitmap?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageResourceService] Bitmap dispose failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 샤프닝 파라미터 값 객체
    /// </summary>
    public record SharpenParams(
        float UpscaleFactor,
        float SharpenAmount,
        float SharpenThreshold,
        float UnsharpAmount,
        float UnsharpRadius
    );
}
