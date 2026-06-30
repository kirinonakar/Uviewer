using Microsoft.Graphics.Canvas;
using System;

namespace Uviewer.Services
{
    internal sealed class ImageBitmapLifetimeCoordinator
    {
        private readonly IImageBitmapLifetimeHost _host;

        public ImageBitmapLifetimeCoordinator(IImageBitmapLifetimeHost host)
        {
            _host = host;
        }

        public bool IsBitmapInCache(CanvasBitmap? bitmap)
        {
            if (bitmap == null) return false;
            if (bitmap == _host.CurrentBitmap || bitmap == _host.LeftBitmap || bitmap == _host.RightBitmap) return true;
            if (_host.ImageCache.IsBitmapInCache(bitmap)) return true;
            if (_host.AnimatedWebpService.IsBitmapInCache(bitmap)) return true;
            return false;
        }

        public void ReleaseBitmapIfUnused(CanvasBitmap? bitmap)
        {
            if (bitmap != null && !IsBitmapInCache(bitmap))
            {
                _host.ImageCache.SafeDisposeBitmap(bitmap);
            }
        }

        public void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap)
        {
            _host.IsAnimatedFrameActive = true;
            var oldBitmap = _host.CurrentBitmap;
            _host.CurrentBitmap = newBitmap;

            if (oldBitmap != null && oldBitmap != newBitmap && !IsBitmapInCache(oldBitmap))
            {
                _host.ImageCache.SafeDisposeBitmap(oldBitmap);
            }

            _host.MainCanvas?.Invalidate();
        }

        public void OnAnimatedWebpAnimationStopped(object? sender, EventArgs e)
        {
            try
            {
                _host.IsAnimatedFrameActive = false;
                var bitmap = _host.CurrentBitmap;
                if (bitmap != null && _host.AnimatedWebpService.IsBitmapInCache(bitmap))
                {
                    _host.CurrentBitmap = null;
                    _host.MainCanvas?.Invalidate();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping animated image: {ex.Message}");
            }
        }

        public void ClearImageResources()
        {
            _host.ImageLoadingCts?.Cancel();
            _host.PreloadManager.CancelAll();
            _host.ImageCache.ClearAll();
            _host.AnimatedWebpService.Stop();

            _host.CurrentBitmap = null;
            _host.LeftBitmap = null;
            _host.RightBitmap = null;

            _host.MainCanvas?.Invalidate();
            _host.LeftCanvas?.Invalidate();
            _host.RightCanvas?.Invalidate();

            _host.FileNameText.Text = "";
            _host.ImageInfoText.Text = "";
            _host.ImageIndexText.Text = "";
        }
    }
}
