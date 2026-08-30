using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Uviewer.Controls
{
    public sealed class BreadcrumbWrapPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double availableWidth = double.IsInfinity(availableSize.Width)
                ? double.MaxValue
                : Math.Max(0, availableSize.Width);
            double rowWidth = 0;
            double rowHeight = 0;
            double measuredWidth = 0;
            double measuredHeight = 0;

            foreach (UIElement child in Children)
            {
                child.Measure(new Size(availableWidth, double.PositiveInfinity));
                Size childSize = child.DesiredSize;

                if (rowWidth > 0 && rowWidth + childSize.Width > availableWidth)
                {
                    measuredWidth = Math.Max(measuredWidth, rowWidth);
                    measuredHeight += rowHeight;
                    rowWidth = 0;
                    rowHeight = 0;
                }

                rowWidth += Math.Min(childSize.Width, availableWidth);
                rowHeight = Math.Max(rowHeight, childSize.Height);
            }

            measuredWidth = Math.Max(measuredWidth, rowWidth);
            measuredHeight += rowHeight;

            return new Size(
                double.IsInfinity(availableSize.Width) ? measuredWidth : Math.Min(measuredWidth, availableWidth),
                measuredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double availableWidth = Math.Max(0, finalSize.Width);
            double x = 0;
            double y = 0;
            double rowHeight = 0;

            foreach (UIElement child in Children)
            {
                Size childSize = child.DesiredSize;
                double childWidth = Math.Min(childSize.Width, availableWidth);

                if (x > 0 && x + childWidth > availableWidth)
                {
                    x = 0;
                    y += rowHeight;
                    rowHeight = 0;
                }

                child.Arrange(new Rect(x, y, childWidth, childSize.Height));
                x += childWidth;
                rowHeight = Math.Max(rowHeight, childSize.Height);
            }

            return finalSize;
        }
    }
}
