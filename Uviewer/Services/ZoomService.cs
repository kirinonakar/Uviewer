using System;
using Windows.Foundation;

namespace Uviewer.Services
{
    internal readonly record struct ImageViewportTransform(double ZoomLevel, double PanX, double PanY);

    public class ZoomService
    {
        public double Level { get; private set; } = 1.0;
        public const double Step = 0.25;
        public const double MinZoom = 0.1;
        public const double MaxZoom = 10.0;

        public void SetLevel(double level)
        {
            Level = Math.Clamp(level, MinZoom, MaxZoom);
        }

        public void ZoomIn()
        {
            Level = Math.Min(Level + Step, MaxZoom);
        }

        public void ZoomOut()
        {
            Level = Math.Max(Level - Step, MinZoom);
        }

        public void FitToWindow()
        {
            Level = 1.0;
        }

        public void CalculateActualZoom(double containerWidth, double containerHeight, double bitmapWidth, double bitmapHeight, double dpiScale, bool isPdf)
        {
            if (containerWidth <= 0 || containerHeight <= 0 || bitmapWidth <= 0 || bitmapHeight <= 0)
                return;

            var fitRatio = Math.Min(containerWidth / bitmapWidth, containerHeight / bitmapHeight);

            if (isPdf)
            {
                // PDF의 경우 가로 너비 맞춤을 '원본 크기'의 기본 동작으로 유지
                Level = containerWidth / (bitmapWidth * fitRatio);
            }
            else
            {
                // 일반 이미지의 경우 HiDPI(DPI 배율)를 고려하여 실제 1:1 픽셀 매칭 수준으로 확대/축소
                if (dpiScale <= 0) dpiScale = 1.0;
                Level = 1.0 / (fitRatio * dpiScale);
            }
        }

        internal static ImageViewportTransform? CalculateZoomAtPosition(
            Size canvasSize,
            Size imageSize,
            double currentZoom,
            double currentPanX,
            double currentPanY,
            double zoomMultiplier,
            Point position)
        {
            if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return null;
            if (imageSize.Width <= 0 || imageSize.Height <= 0) return null;

            double fitRatio = CalculateFitRatio(canvasSize, imageSize);
            double oldScaledW = imageSize.Width * fitRatio * currentZoom;
            double oldScaledH = imageSize.Height * fitRatio * currentZoom;
            double oldVisualLeft = (canvasSize.Width - oldScaledW) / 2 + currentPanX;
            double oldVisualTop = (canvasSize.Height - oldScaledH) / 2 + currentPanY;

            double normX = (position.X - oldVisualLeft) / currentZoom;
            double normY = (position.Y - oldVisualTop) / currentZoom;

            double newZoom = Math.Clamp(currentZoom * zoomMultiplier, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - currentZoom) < double.Epsilon) return null;

            double newScaledW = imageSize.Width * fitRatio * newZoom;
            double newScaledH = imageSize.Height * fitRatio * newZoom;

            double newVisualLeft = position.X - (normX * newZoom);
            double newVisualTop = position.Y - (normY * newZoom);

            double panX = newVisualLeft - (canvasSize.Width - newScaledW) / 2;
            double panY = newVisualTop - (canvasSize.Height - newScaledH) / 2;

            double maxPanX = Math.Max(0, (newScaledW - canvasSize.Width) / 2);
            double maxPanY = Math.Max(0, (newScaledH - canvasSize.Height) / 2);

            return new ImageViewportTransform(
                newZoom,
                Math.Clamp(panX, -maxPanX, maxPanX),
                Math.Clamp(panY, -maxPanY, maxPanY));
        }

        internal static double CalculateInitialVerticalPan(
            Size canvasSize,
            Size imageSize,
            double zoomLevel,
            int scrollDirection)
        {
            if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return 0;
            if (imageSize.Width <= 0 || imageSize.Height <= 0) return 0;

            double fitRatio = CalculateFitRatio(canvasSize, imageSize);
            double scaledHeight = imageSize.Height * fitRatio * zoomLevel;
            double maxPan = scaledHeight > canvasSize.Height
                ? (scaledHeight - canvasSize.Height) / 2
                : 0;

            return scrollDirection == 1 ? maxPan : -maxPan;
        }

        internal static Size CalculateScaledSize(Size canvasSize, Size imageSize, double zoomLevel)
        {
            double fitRatio = CalculateFitRatio(canvasSize, imageSize);
            return new Size(imageSize.Width * fitRatio * zoomLevel, imageSize.Height * fitRatio * zoomLevel);
        }

        private static double CalculateFitRatio(Size canvasSize, Size imageSize)
        {
            return Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
        }
    }
}
