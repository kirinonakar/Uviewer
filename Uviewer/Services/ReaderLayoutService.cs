using System;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ReaderLayoutService
    {
        public static bool ShouldUseHorizontalTwoColumns(bool enabled, bool isAozora, bool isVertical, double width, double height) =>
            enabled && isAozora && !isVertical && double.IsFinite(width) && double.IsFinite(height) &&
            width > 0 && height > 0 && width >= height * 1.2;

        public ReaderPageLayout CreateHorizontalTextLayout(
            double canvasWidth,
            double canvasHeight,
            double rootWidth,
            double rootHeight,
            bool isMarkdown,
            double maxContentWidth,
            bool twoColumns = false)
        {
            var margins = ReaderPageMargins.HorizontalText;

            float availableHeight = (float)canvasHeight;
            if (availableHeight < 100) availableHeight = (float)rootHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            availableHeight -= margins.Vertical;

            float availableWidth = (float)canvasWidth;
            if (availableWidth < 50) availableWidth = (float)rootWidth - 100;
            if (availableWidth < 50) availableWidth = 800;
            availableWidth -= margins.Horizontal;

            float columnWidth = twoColumns ? Math.Max(1, (availableWidth - 40f) / 2f) : availableWidth;
            float maxWidth = isMarkdown
                ? columnWidth
                : Math.Min(columnWidth, (float)maxContentWidth);

            return new ReaderPageLayout(margins, availableWidth, availableHeight, maxWidth, twoColumns ? 2 : 1);
        }

        public ReaderPageLayout CreateHorizontalPageMapLayout(
            double canvasWidth,
            double canvasHeight,
            bool isMarkdown,
            double maxContentWidth,
            bool twoColumns = false)
        {
            var margins = ReaderPageMargins.HorizontalText;
            float availableWidth = Math.Max(0, (float)canvasWidth - margins.Horizontal);
            float availableHeight = Math.Max(0, (float)canvasHeight - margins.Vertical);
            float columnWidth = twoColumns ? Math.Max(1, (availableWidth - 40f) / 2f) : availableWidth;
            float maxWidth = isMarkdown
                ? columnWidth
                : Math.Min(columnWidth, (float)maxContentWidth);

            return new ReaderPageLayout(margins, availableWidth, availableHeight, maxWidth, twoColumns ? 2 : 1);
        }

        public ReaderPageLayout CreateVerticalTextLayout(
            double canvasWidth,
            double canvasHeight,
            double rootWidth,
            double rootHeight)
        {
            var margins = ReaderPageMargins.VerticalText;

            float availableHeight = (float)canvasHeight;
            if (availableHeight < 100) availableHeight = (float)rootHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            availableHeight -= margins.Vertical;

            float availableWidth = (float)canvasWidth;
            if (availableWidth < 100) availableWidth = (float)rootWidth - 100;
            if (availableWidth < 100) availableWidth = 1000;
            availableWidth -= margins.Horizontal;

            return new ReaderPageLayout(margins, availableWidth, availableHeight, availableWidth);
        }

        public ReaderPageLayout CreateVerticalPageMapLayout(double canvasWidth, double canvasHeight)
        {
            var margins = ReaderPageMargins.VerticalText;
            float availableWidth = Math.Max(0, (float)canvasWidth - margins.Horizontal);
            float availableHeight = Math.Max(0, (float)canvasHeight - margins.Vertical);
            return new ReaderPageLayout(margins, availableWidth, availableHeight, availableWidth);
        }

        public ReaderViewportSize CreateEpubViewport(
            double areaWidth,
            double areaHeight,
            double rootWidth,
            double rootHeight,
            double appWidth,
            double appHeight,
            double rasterizationScale)
        {
            float availableWidth = (float)areaWidth;
            if (availableWidth < 50)
            {
                availableWidth = (float)rootWidth;
                if (availableWidth < 50)
                {
                    double scale = rasterizationScale > 0 ? rasterizationScale : 1.0;
                    availableWidth = (float)(appWidth / scale);
                }
                if (availableWidth < 50) availableWidth = 800;
            }

            float availableHeight = (float)areaHeight;
            if (availableHeight < 50)
            {
                availableHeight = (float)rootHeight;
                if (availableHeight < 50)
                {
                    double scale = rasterizationScale > 0 ? rasterizationScale : 1.0;
                    availableHeight = (float)(appHeight / scale);
                }
                if (availableHeight < 50) availableHeight = 800;
            }

            return new ReaderViewportSize(availableWidth, availableHeight);
        }
    }
}
