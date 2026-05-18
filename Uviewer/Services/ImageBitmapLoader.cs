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
                                _imageCache.CacheSharpenedImage(entryIndex, sharpened, context.CurrentIndex);
                                return sharpened;
                            }
                        }

                        return cachedBitmap;
                    }
                }

                CanvasBitmap? originalBitmap = await LoadOriginalBitmapAsync(entry, canvas, context, token);
                if (originalBitmap == null) return null;

                if (context.SharpenEnabled && !entry.IsPdfEntry)
                {
                    CanvasBitmap? sharpened = entryIndex >= 0 ? _imageCache.GetSharpenedImage(entryIndex) : null;
                    if (sharpened != null) return sharpened;

                    sharpened = await ApplySharpenAsync(originalBitmap, context.SharpenParams);
                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        if (entryIndex >= 0)
                        {
                            _imageCache.CacheSharpenedImage(entryIndex, sharpened, context.CurrentIndex);
                        }

                        _imageCache.SafeDisposeBitmap(originalBitmap);
                        return sharpened;
                    }
                }

                return originalBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image bitmap: {ex.Message}");
                return null;
            }
        }

        public async Task<CanvasBitmap?> LoadBitmapForPreloadAsync(
            ImageEntry entry,
            ImageBitmapLoaderContext context,
            CancellationToken token)
        {
            CanvasBitmap? bitmap = null;

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
                    StartSharpenPreload(bitmap, entryIndex, context, token);
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
                return await LoadImageFromPathAsync(entry.FilePath, canvas);
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
                        return await LoadImageFromPathAsync(tempPath, canvas);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error downloading WebDAV image for display: {ex.Message}");
                }
            }

            return null;
        }

        private async Task<CanvasBitmap?> LoadImageFromPathAsync(string filePath, CanvasControl canvas)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var device = canvas.Device ?? CanvasDevice.GetSharedDevice();
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
                return await LoadImageFromPathAsync(imageEntry.FilePath, canvas);
            }

            try
            {
                if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
                {
                    return await LoadImageFromPathAsync(imageEntry.FilePath, canvas);
                }

                var bytes = await context.ArchiveSession.ReadEntryBytesAsync(entryKey, token);
                if (bytes == null || token.IsCancellationRequested) return null;

                if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
                {
                    return await LoadImageFromPathAsync(imageEntry.FilePath, canvas);
                }

                using var memoryStream = new MemoryStream(bytes);

                return await CanvasBitmap.LoadAsync(
                    canvas.Device ?? CanvasDevice.GetSharedDevice(),
                    memoryStream.AsRandomAccessStream(),
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
                        _imageCache.CacheSharpenedImage(entryIndex, sharpened, context.CurrentIndex);
                        _dispatcherQueue.TryEnqueue(() => context.InvalidateCanvas());
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
