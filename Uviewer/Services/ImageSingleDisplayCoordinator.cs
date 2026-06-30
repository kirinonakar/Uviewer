using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageSingleDisplayCoordinator
    {
        private readonly IImageSingleDisplayHost _host;
        private readonly Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> _loadImageBitmapAsync;
        private readonly Func<CanvasBitmap, bool> _isBitmapInCache;
        private readonly Action _fitToWindow;
        private readonly Action _showImageUi;
        private readonly Action<ImageEntry, CanvasBitmap> _updateStatusBar;
        private readonly Action _updateSharpenButtonState;
        private readonly Action<ImageEntry> _syncSidebarSelection;

        public ImageSingleDisplayCoordinator(
            IImageSingleDisplayHost host,
            Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> loadImageBitmapAsync,
            Func<CanvasBitmap, bool> isBitmapInCache,
            Action fitToWindow,
            Action showImageUi,
            Action<ImageEntry, CanvasBitmap> updateStatusBar,
            Action updateSharpenButtonState,
            Action<ImageEntry> syncSidebarSelection)
        {
            _host = host;
            _loadImageBitmapAsync = loadImageBitmapAsync;
            _isBitmapInCache = isBitmapInCache;
            _fitToWindow = fitToWindow;
            _showImageUi = showImageUi;
            _updateStatusBar = updateStatusBar;
            _updateSharpenButtonState = updateSharpenButtonState;
            _syncSidebarSelection = syncSidebarSelection;
        }

        public async Task DisplaySingleImageAsync(CancellationToken token)
        {
            if (_host.CurrentIndex < 0 || _host.CurrentIndex >= _host.ImageEntries.Count) return;

            var entry = _host.ImageEntries[_host.CurrentIndex];
            _host.IsAnimatedFrameActive = false;
            _host.AnimatedWebpService.Stop();

            try
            {
                if (token.IsCancellationRequested) return;

                var bitmap = await _loadImageBitmapAsync(entry, _host.MainCanvas, token);

                if (token.IsCancellationRequested)
                {
                    if (bitmap != null && !_isBitmapInCache(bitmap))
                    {
                        _host.ImageCache.SafeDisposeBitmap(bitmap);
                    }
                    return;
                }

                if (bitmap != null)
                {
                    DisplayLoadedBitmap(entry, bitmap);
                }
                else
                {
                    _host.FileNameText.Text = Strings.LoadImageError;
                    return;
                }

                StartAnimationIfSupported(entry, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _host.FileNameText.Text = $"이미지 로드 오류: {ex.Message}";
            }
        }

        private void DisplayLoadedBitmap(ImageEntry entry, CanvasBitmap bitmap)
        {
            var oldBitmap = _host.CurrentBitmap;
            _host.CurrentBitmap = bitmap;

            if (_host.ZoomLevel <= 1.01)
            {
                _host.ZoomLevel = 1.0;
                _fitToWindow();
            }

            _host.ImageViewportNavigationService.ResetPanForBitmap(
                _host.MainCanvas,
                bitmap,
                _host.ZoomLevel);
            _showImageUi();
            _updateStatusBar(entry, _host.CurrentBitmap);
            _updateSharpenButtonState();
            _host.MainCanvas?.Invalidate();
            _syncSidebarSelection(entry);

            if (oldBitmap != null && !_isBitmapInCache(oldBitmap) && oldBitmap != bitmap)
            {
                _host.ImageCache.SafeDisposeBitmap(oldBitmap);
            }
        }

        private void StartAnimationIfSupported(ImageEntry entry, CancellationToken token)
        {
            if (!_host.AnimatedWebpService.IsAnimationSupported(entry))
            {
                return;
            }

            _host.FileNameText.Text += Strings.Loading;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    await _host.AnimatedWebpService.StartAsync(
                        entry,
                        _host.MainCanvas!,
                        token,
                        (float)_host.ImageOptions.UpscaleFactor,
                        (float)_host.ImageOptions.SharpenAmount,
                        (float)_host.ImageOptions.SharpenThreshold,
                        (float)_host.ImageOptions.UnsharpAmount,
                        (float)_host.ImageOptions.UnsharpRadius,
                        _host.SharpenEnabled);

                    _host.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!token.IsCancellationRequested && _host.CurrentBitmap != null)
                        {
                            _updateStatusBar(entry, _host.CurrentBitmap);
                        }
                    });
                }
                catch
                {
                    _host.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested || _host.CurrentBitmap == null) return;
                        _updateStatusBar(entry, _host.CurrentBitmap);
                    });
                }
            }, token);
        }
    }
}
