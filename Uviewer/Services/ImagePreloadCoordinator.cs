using Microsoft.Graphics.Canvas;
using System;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImagePreloadCoordinator
    {
        private readonly IImagePreloadHost _host;

        public ImagePreloadCoordinator(IImagePreloadHost host)
        {
            _host = host;
        }

        public void StartImagePreload(
            bool prioritizeNext,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> loadBitmapForPreloadAsync)
        {
            _ = _host.PreloadManager.StartPreloadAsync(
                _host.CurrentIndex,
                _host.ImageEntries,
                _host.IsPdfMode,
                _host.ZoomLevel,
                _host.CurrentBitmap,
                _host.LeftBitmap,
                _host.RightBitmap,
                loadBitmapForPreloadAsync,
                () => _host.MainCanvas?.Invalidate(),
                prioritizeNext: prioritizeNext,
                requireSharpening: _host.SharpenEnabled);
        }

        public Task<CanvasBitmap?> LoadBitmapForPreloadAsync(
            ImageEntry entry,
            Func<ImageBitmapLoaderContext> createLoaderContext,
            CancellationToken token)
        {
            return _host.ImageBitmapLoader.LoadBitmapForPreloadAsync(entry, createLoaderContext(), token);
        }
    }
}
