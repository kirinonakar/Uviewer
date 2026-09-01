using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.Storage;

namespace Uviewer.Services
{
    internal delegate Task<CanvasBitmap?> PdfPageBitmapLoader(
        uint pageIndex,
        CanvasControl canvas,
        CancellationToken token,
        bool isPreload);

    internal sealed record ImageBitmapLoaderContext(
        List<ImageEntry> ImageEntries,
        int CurrentIndex,
        double ZoomLevel,
        bool SharpenEnabled,
        SharpenParams SharpenParams,
        bool IsHdrOutputActive,
        float HdrDisplayMaxLuminance,
        bool IsPdfMode,
        bool IsWebDavMode,
        ArchiveSession ArchiveSession,
        WebDavService WebDavService,
        CanvasControl MainCanvas,
        PdfPageBitmapLoader LoadPdfPageBitmapAsync,
        Action InvalidateCanvas);

    internal sealed class ImageBitmapLoader
    {
        private readonly ImageCacheManager _imageCache;
        private readonly ISharpeningService _sharpeningService;
        private readonly DispatcherQueue _dispatcherQueue;

        public ImageBitmapLoader(
            ImageCacheManager imageCache,
            ISharpeningService sharpeningService,
            DispatcherQueue dispatcherQueue)
        {
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _sharpeningService = sharpeningService ?? throw new ArgumentNullException(nameof(sharpeningService));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        public async Task<CanvasBitmap?> LoadImageBitmapAsync(
            ImageEntry entry,
            CanvasControl canvas,
            ImageBitmapLoaderContext context,
            CancellationToken token = default)
        {
            int generation = _imageCache.Generation;
            CanvasBitmap? ownedBitmap = null;
            try
            {
                if (token.IsCancellationRequested) return null;

                var entryIndex = context.ImageEntries.IndexOf(entry);
                if (entryIndex >= 0)
                {
                    var cachedBitmap = _imageCache.GetPreloadedImage(entryIndex, context.ZoomLevel);
                    if (cachedBitmap != null)
                    {
                        if (context.SharpenEnabled)
                        {
                            var sharpenedBitmap = _imageCache.GetSharpenedImage(entryIndex);
                            if (sharpenedBitmap != null) return sharpenedBitmap;

                            var sharpened = await ApplySharpenAsync(cachedBitmap, context.SharpenParams);
                            if (sharpened != null)
                            {
                                if (_imageCache.CacheSharpenedImage(entryIndex, sharpened,
                                    context.CurrentIndex, generation, token)) return sharpened;
                                return token.IsCancellationRequested || generation != _imageCache.Generation
                                    ? null : _imageCache.GetSharpenedImage(entryIndex);
                            }
                        }

                        return cachedBitmap;
                    }
                }

                CanvasBitmap? originalBitmap = await LoadOriginalBitmapAsync(entry, canvas, context, token);
                if (originalBitmap == null) return null;
                ownedBitmap = originalBitmap;
                if (token.IsCancellationRequested || generation != _imageCache.Generation) return null;

                if (context.SharpenEnabled && !entry.IsPdfEntry)
                {
                    CanvasBitmap? sharpened = entryIndex >= 0 ? _imageCache.GetSharpenedImage(entryIndex) : null;
                    if (sharpened != null) return sharpened;

                    sharpened = await ApplySharpenAsync(originalBitmap, context.SharpenParams);
                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        if (entryIndex >= 0)
                        {
                            if (!_imageCache.CacheSharpenedImage(entryIndex, sharpened,
                                context.CurrentIndex, generation, token))
                            {
                                return token.IsCancellationRequested || generation != _imageCache.Generation
                                    ? null : _imageCache.GetSharpenedImage(entryIndex);
                            }
                        }
                        else if (token.IsCancellationRequested || generation != _imageCache.Generation)
                        {
                            _imageCache.SafeDisposeBitmap(sharpened);
                            return null;
                        }

                        return sharpened;
                    }
                }

                if (token.IsCancellationRequested || generation != _imageCache.Generation) return null;
                ownedBitmap = null; // Ownership transfers to the display coordinator.
                return originalBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image bitmap: {ex.Message}");
                return null;
            }
            finally
            {
                _imageCache.ReleaseBitmapIfUncached(ownedBitmap);
            }
        }

        public async Task<CanvasBitmap?> LoadBitmapForPreloadAsync(
            ImageEntry entry,
            ImageBitmapLoaderContext context,
            CancellationToken token)
        {
            CanvasBitmap? bitmap = null;
            int generation = _imageCache.Generation;

            try
            {
                var entryIndex = context.ImageEntries.IndexOf(entry);
                if (entryIndex >= 0)
                {
                    bitmap = _imageCache.GetPreloadedImage(entryIndex);
                }

                if (bitmap != null && entry.IsPdfEntry && context.IsPdfMode)
                {
                    float dpiScale = context.MainCanvas.Dpi / 96.0f > 0
                        ? context.MainCanvas.Dpi / 96.0f
                        : 1.0f;

                    double canvasW = context.MainCanvas.Size.Width > 0 ? context.MainCanvas.Size.Width : 1000;
                    double canvasH = context.MainCanvas.Size.Height > 0 ? context.MainCanvas.Size.Height : 1000;
                    double pageAR = bitmap.Size.Height > 0 ? bitmap.Size.Width / bitmap.Size.Height : 1.0;
                    double targetW = Math.Clamp(
                        (pageAR > (canvasW / canvasH) ? canvasW : canvasH * pageAR) * context.ZoomLevel,
                        1920.0 / dpiScale,
                        6016.0 / dpiScale);

                    if (bitmap.Size.Width < targetW * 0.9)
                    {
                        bitmap = null;
                    }
                }

                if (bitmap == null)
                {
                    if (entry.IsPdfEntry && context.IsPdfMode)
                    {
                        bitmap = await context.LoadPdfPageBitmapAsync(entry.PdfPageIndex, context.MainCanvas, token, isPreload: true);
                    }
                    else
                    {
                        bitmap = await LoadOriginalBitmapAsync(entry, context.MainCanvas, context, token);
                    }
                }

                if (bitmap != null && context.SharpenEnabled && !entry.IsPdfEntry && !token.IsCancellationRequested && entryIndex >= 0)
                {
                    StartSharpenPreload(bitmap, entryIndex, context, generation, token);
                }
            }
            catch { }

            return bitmap;
        }

        private async Task<CanvasBitmap?> LoadOriginalBitmapAsync(
            ImageEntry entry,
            CanvasControl canvas,
            ImageBitmapLoaderContext context,
            CancellationToken token)
        {
            if (entry.FilePath != null)
            {
                return await LoadImageFromPathAsync(
                    entry.FilePath,
                    canvas,
                    token,
                    context.IsHdrOutputActive,
                    context.HdrDisplayMaxLuminance);
            }

            if (entry.IsArchiveEntry && context.ArchiveSession.HasArchive)
            {
                return await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, canvas, context, token);
            }

            if (entry.IsWebDavEntry && context.IsWebDavMode)
            {
                try
                {
                    var tempPath = await context.WebDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        entry.FilePath = tempPath;
                        return await LoadImageFromPathAsync(
                            tempPath,
                            canvas,
                            token,
                            context.IsHdrOutputActive,
                            context.HdrDisplayMaxLuminance,
                            entry.WebDavPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error downloading WebDAV image for display: {ex.Message}");
                }
            }

            return null;
        }

        private async Task<CanvasBitmap?> LoadImageFromPathAsync(
            string filePath,
            CanvasControl canvas,
            CancellationToken token,
            bool isHdrOutputActive,
            float hdrDisplayMaxLuminance,
            string? sourceName = null)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var device = canvas.Device ?? CanvasDevice.GetSharedDevice();

                bool shouldTryHdr = string.Equals(
                    Path.GetExtension(sourceName ?? filePath),
                    ".avif",
                    StringComparison.OrdinalIgnoreCase) && isHdrOutputActive;

                try
                {
                    if (shouldTryHdr)
                    {
                        var hdrBitmap = await HdrImageDecoder.TryLoadAsync(
                            device,
                            stream,
                            hdrDisplayMaxLuminance,
                            token);
                        if (hdrBitmap != null) return hdrBitmap;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    // If the installed Windows codec cannot provide RGBA16, retain
                    // the existing SDR decode path instead of failing to show it.
                    System.Diagnostics.Debug.WriteLine($"HDR decode unavailable, using SDR fallback: {ex.Message}");
                }

                token.ThrowIfCancellationRequested();
                stream.Seek(0);
                return await CanvasBitmap.LoadAsync(device, stream, 96.0f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image from path: {ex.Message}");
                return null;
            }
        }

        private async Task<CanvasBitmap?> LoadImageFromArchiveEntryAsync(
            string entryKey,
            CanvasControl canvas,
            ImageBitmapLoaderContext context,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            var imageEntry = context.ImageEntries.FirstOrDefault(e => e.ArchiveEntryKey == entryKey);
            if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
            {
                return await LoadImageFromPathAsync(
                    imageEntry.FilePath,
                    canvas,
                    token,
                    context.IsHdrOutputActive,
                    context.HdrDisplayMaxLuminance,
                    entryKey);
            }

            try
            {
                if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
                {
                    return await LoadImageFromPathAsync(
                        imageEntry.FilePath,
                        canvas,
                        token,
                        context.IsHdrOutputActive,
                        context.HdrDisplayMaxLuminance,
                        entryKey);
                }

                var bytes = await context.ArchiveSession.ReadEntryBytesAsync(entryKey, token);
                if (bytes == null || token.IsCancellationRequested) return null;

                if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
                {
                    return await LoadImageFromPathAsync(
                        imageEntry.FilePath,
                        canvas,
                        token,
                        context.IsHdrOutputActive,
                        context.HdrDisplayMaxLuminance,
                        entryKey);
                }

                using var memoryStream = new MemoryStream(bytes);
                using var randomAccessStream = memoryStream.AsRandomAccessStream();
                var device = canvas.Device ?? CanvasDevice.GetSharedDevice();

                try
                {
                    if (context.IsHdrOutputActive &&
                        string.Equals(Path.GetExtension(entryKey), ".avif", StringComparison.OrdinalIgnoreCase))
                    {
                        var hdrBitmap = await HdrImageDecoder.TryLoadAsync(
                            device,
                            randomAccessStream,
                            context.HdrDisplayMaxLuminance,
                            token);
                        if (hdrBitmap != null) return hdrBitmap;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HDR archive decode unavailable, using SDR fallback: {ex.Message}");
                }

                token.ThrowIfCancellationRequested();
                randomAccessStream.Seek(0);
                return await CanvasBitmap.LoadAsync(
                    device,
                    randomAccessStream,
                    96.0f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win2D Load Error: {ex.Message}");
                return null;
            }
        }

        private async Task<CanvasBitmap?> ApplySharpenAsync(CanvasBitmap bitmap, SharpenParams sharpenParams)
        {
            return await _sharpeningService.ApplySharpenToBitmapAsync(
                bitmap,
                sharpenParams.UpscaleFactor,
                sharpenParams.SharpenAmount,
                sharpenParams.SharpenThreshold,
                sharpenParams.UnsharpAmount,
                sharpenParams.UnsharpRadius,
                skipUpscale: false);
        }

        private void StartSharpenPreload(
            CanvasBitmap bitmap,
            int entryIndex,
            ImageBitmapLoaderContext context,
            int generation,
            CancellationToken token)
        {
            var capturedBitmap = bitmap;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    if (_imageCache.GetSharpenedImage(entryIndex) != null) return;

                    var sharpened = await ApplySharpenAsync(capturedBitmap, context.SharpenParams);

                    if (sharpened != null && sharpened != capturedBitmap && !token.IsCancellationRequested)
                    {
                        if (_imageCache.CacheSharpenedImage(entryIndex, sharpened,
                            context.CurrentIndex, generation, token))
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (!token.IsCancellationRequested && generation == _imageCache.Generation)
                                    context.InvalidateCanvas();
                            });
                        }
                    }
                    else if (sharpened != null && sharpened != capturedBitmap)
                    {
                        _imageCache.SafeDisposeBitmap(sharpened);
                    }
                }
                catch { }
            }, token);
        }
    }
}
