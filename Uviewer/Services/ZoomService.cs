using System;

namespace Uviewer.Services
{
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
    }
}
