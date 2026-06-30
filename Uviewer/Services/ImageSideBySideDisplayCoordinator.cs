using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageSideBySideDisplayCoordinator
    {
        private readonly IImageSideBySideDisplayHost _host;
        private readonly Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> _loadImageBitmapAsync;
        private readonly Action<CanvasBitmap?> _releaseBitmapIfUnused;
        private readonly Func<CanvasBitmap, bool> _isBitmapInCache;
        private readonly Action _fitToWindow;
        private readonly Action _showImageUi;
        private readonly Action<ImageEntry, CanvasBitmap> _updateStatusBar;
        private readonly Action<ImageEntry> _syncSidebarSelection;

        public ImageSideBySideDisplayCoordinator(
            IImageSideBySideDisplayHost host,
            Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> loadImageBitmapAsync,
            Action<CanvasBitmap?> releaseBitmapIfUnused,
            Func<CanvasBitmap, bool> isBitmapInCache,
            Action fitToWindow,
            Action showImageUi,
            Action<ImageEntry, CanvasBitmap> updateStatusBar,
            Action<ImageEntry> syncSidebarSelection)
        {
            _host = host;
            _loadImageBitmapAsync = loadImageBitmapAsync;
            _releaseBitmapIfUnused = releaseBitmapIfUnused;
            _isBitmapInCache = isBitmapInCache;
            _fitToWindow = fitToWindow;
            _showImageUi = showImageUi;
            _updateStatusBar = updateStatusBar;
            _syncSidebarSelection = syncSidebarSelection;
        }

        public async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            try
            {
                var pair = await _host.SideBySideImageLoadService.LoadAsync(
                    _host.ImageEntries,
                    _host.CurrentIndex,
                    _host.NextImageOnRight,
                    _host.LeftCanvas,
                    _host.RightCanvas,
                    _loadImageBitmapAsync,
                    _releaseBitmapIfUnused,
                    token);

                if (pair == null || token.IsCancellationRequested)
                {
                    return;
                }

                var oldLeft = _host.LeftBitmap;
                var oldRight = _host.RightBitmap;

                _host.LeftBitmap = pair.LeftBitmap;
                _host.RightBitmap = pair.RightBitmap;
                _host.CurrentBitmap = pair.RightBitmap ?? pair.LeftBitmap;

                _host.ZoomLevel = 1.0;
                _fitToWindow();
                _showImageUi();

                var primaryEntry = _host.ImageEntries[_host.CurrentIndex];
                CanvasBitmap? primaryBitmap = pair.PrimaryBitmap ?? _host.CurrentBitmap;

                if (primaryBitmap != null)
                {
                    _updateStatusBar(primaryEntry, primaryBitmap);
                }
                else if (_host.CurrentBitmap != null)
                {
                    _updateStatusBar(primaryEntry, _host.CurrentBitmap);
                }

                _syncSidebarSelection(primaryEntry);

                ReleasePreviousBitmaps(oldLeft, oldRight, pair.LeftBitmap, pair.RightBitmap);
            }
            catch (Exception ex)
            {
                _host.FileNameText.Text = $"이미지 로드 실패: {ex.Message}";
            }
        }

        private void ReleasePreviousBitmaps(
            CanvasBitmap? oldLeft,
            CanvasBitmap? oldRight,
            CanvasBitmap? newLeft,
            CanvasBitmap? newRight)
        {
            if (oldLeft != null && !_isBitmapInCache(oldLeft) && oldLeft != newLeft && oldLeft != newRight)
            {
                _host.ImageCache.SafeDisposeBitmap(oldLeft);
            }

            if (oldRight != null && !_isBitmapInCache(oldRight) && oldRight != newLeft && oldRight != newRight)
            {
                _host.ImageCache.SafeDisposeBitmap(oldRight);
            }
        }
    }
}
