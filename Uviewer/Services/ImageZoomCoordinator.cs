namespace Uviewer.Services
{
    internal sealed class ImageZoomCoordinator
    {
        private readonly IImageZoomHost _host;

        public ImageZoomCoordinator(IImageZoomHost host)
        {
            _host = host;
        }

        public void ZoomActual()
        {
            if (_host.IsCurrentViewSideBySide && !_host.IsPdfMode) return;

            if (CanvasBitmapHelper.TryGetBitmapSize(_host.CurrentBitmap, out var bitmapSize))
            {
                var containerWidth = _host.ImageArea.ActualWidth;
                var containerHeight = _host.ImageArea.ActualHeight;

                if (containerWidth > 0 && containerHeight > 0)
                {
                    _host.ZoomService.CalculateActualZoom(
                        containerWidth,
                        containerHeight,
                        bitmapSize.Width,
                        bitmapSize.Height,
                        _host.MainCanvas.Dpi / 96.0f,
                        _host.IsPdfMode);
                    ApplyZoom();
                }
            }
        }

        public void ZoomIn()
        {
            if (_host.IsCurrentViewSideBySide && !_host.IsPdfMode) return;
            _host.ZoomService.ZoomIn();
            ApplyZoom();
        }

        public void ZoomOut()
        {
            if (_host.IsCurrentViewSideBySide && !_host.IsPdfMode) return;
            _host.ZoomService.ZoomOut();
            ApplyZoom();
        }

        public void FitToWindow()
        {
            _host.ZoomService.FitToWindow();
            ApplyZoom();
        }

        public void ApplyZoom()
        {
            if (!CanvasBitmapHelper.IsUsable(_host.CurrentBitmap) ||
                _host.ImageArea.ActualWidth <= 0 ||
                _host.ImageArea.ActualHeight <= 0)
            {
                return;
            }

            if (!_host.IsCurrentViewSideBySide || _host.IsPdfMode)
            {
                _host.MainCanvas?.Invalidate();
            }
            else
            {
                _host.LeftCanvas?.Invalidate();
                _host.RightCanvas?.Invalidate();
            }

            _host.MainToolbar.SetZoomLevel(_host.ZoomLevel);

            if (_host.IsPdfMode && !_host.ImageViewportNavigationService.IsSmoothZoomRunning)
            {
                _ = _host.RerenderPdfCurrentPageAsync();
            }
        }
    }
}
