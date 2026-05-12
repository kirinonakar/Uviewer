using Microsoft.Graphics.Canvas;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        System.IO.Compression.ZipArchive? EpubArchive,
        System.Threading.SemaphoreSlim? EpubArchiveLock,
        string? CurrentTextFilePath,
        string? CurrentTextArchiveEntryKey,
        IArchive? CurrentArchive,
        SevenZipExtractor.ArchiveFile? Current7zArchive,
        System.Threading.SemaphoreSlim? ArchiveLock,
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
        // ------------------------------------------------------------------
        // 통합 이미지 캐시
        // 키 네임스페이스 규칙:
        //   - EPUB 이미지   : "epub:" + fullPath  (CloseCurrentEpub 시 선택적 제거)
        //   - 텍스트 이미지 : "text:" + relativePath
        // ------------------------------------------------------------------
        private readonly Dictionary<string, CanvasBitmap?> _cache = new();
        private readonly HashSet<string> _knownMissing = new();
        private readonly object _lock = new();
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
                // (A) EPUB 아카이브
                if (ctx.IsEpubMode && ctx.EpubArchive != null)
                {
                    return FindEpubEntryLoose(ctx.EpubArchive, relativePath) != null;
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
                if ((ctx.CurrentArchive != null || ctx.Current7zArchive != null) &&
                    !string.IsNullOrEmpty(ctx.CurrentTextArchiveEntryKey))
                {
                    string targetKey = BuildArchiveKey(ctx.CurrentTextArchiveEntryKey!, relativePath);

                    if (ctx.CurrentArchive != null)
                    {
                        return ctx.CurrentArchive.Entries.Any(e => e.Key != null &&
                            (e.Key.Replace('\\', '/') == targetKey ||
                             string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
                    if (ctx.Current7zArchive != null)
                    {
                        return ctx.Current7zArchive.Entries.Any(e => e.FileName != null &&
                            (e.FileName.Replace('\\', '/') == targetKey ||
                             string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
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

                    originalBitmap = await CanvasBitmap.LoadAsync(device, ras);
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
                        _cache.Remove(cacheKey);
                }
                return null;
            }
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
            // (A) EPUB 아카이브
            if (ctx.IsEpubMode && ctx.EpubArchive != null && ctx.EpubArchiveLock != null)
            {
                var entry = FindEpubEntryLoose(ctx.EpubArchive, relativePath);

                if (entry != null)
                {
                    await ctx.EpubArchiveLock.WaitAsync();
                    try
                    {
                        using var s = entry.Open();
                        using var ms = new MemoryStream();
                        await s.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                    finally { ctx.EpubArchiveLock.Release(); }
                }
                return null;
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
            if ((ctx.CurrentArchive != null || ctx.Current7zArchive != null) &&
                !string.IsNullOrEmpty(ctx.CurrentTextArchiveEntryKey) &&
                ctx.ArchiveLock != null)
            {
                string targetKey = BuildArchiveKey(ctx.CurrentTextArchiveEntryKey!, relativePath);

                await ctx.ArchiveLock.WaitAsync();
                try
                {
                    if (ctx.CurrentArchive != null)
                    {
                        var entry = ctx.CurrentArchive.Entries.FirstOrDefault(e =>
                                e.Key != null && e.Key.Replace('\\', '/') == targetKey)
                            ?? ctx.CurrentArchive.Entries.FirstOrDefault(e =>
                                e.Key != null && string.Equals(
                                    e.Key.Replace('\\', '/'), targetKey,
                                    StringComparison.OrdinalIgnoreCase));

                        if (entry != null)
                        {
                            using var ms = new MemoryStream();
                            using var es = entry.OpenEntryStream();
                            es.CopyTo(ms);
                            return ms.ToArray();
                        }
                    }
                    else if (ctx.Current7zArchive != null)
                    {
                        var entry = ctx.Current7zArchive.Entries.FirstOrDefault(e =>
                                e.FileName != null && e.FileName.Replace('\\', '/') == targetKey)
                            ?? ctx.Current7zArchive.Entries.FirstOrDefault(e =>
                                e.FileName != null && string.Equals(
                                    e.FileName.Replace('\\', '/'), targetKey,
                                    StringComparison.OrdinalIgnoreCase));

                        if (entry != null)
                        {
                            using var ms = new MemoryStream();
                            entry.Extract(ms);
                            return ms.ToArray();
                        }
                    }
                }
                finally { ctx.ArchiveLock.Release(); }
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
            return targetKey.Replace("/./", "/");
        }

        private static System.IO.Compression.ZipArchiveEntry? FindEpubEntryLoose(
            System.IO.Compression.ZipArchive archive,
            string relativePath)
        {
            string normPath = relativePath.Replace('\\', '/').TrimStart('/');
            var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Replace('\\', '/') == normPath)
                ?? archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/'), normPath, StringComparison.OrdinalIgnoreCase));

            if (entry != null) return entry;

            string fileName = GetFileNameFromZipPath(normPath);
            if (string.IsNullOrEmpty(fileName)) return null;

            return archive.Entries.FirstOrDefault(e =>
                string.Equals(GetFileNameFromZipPath(e.FullName.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetFileNameFromZipPath(string path)
        {
            int lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path.Substring(lastSlash + 1) : Path.GetFileName(path);
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
