using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Uviewer.Models;
using Uviewer.Services;
using Windows.Foundation;

namespace Uviewer.Renderers
{
    public static class ImageCanvasRenderer
    {
        public static void DrawMainCanvas(
            CanvasControl sender,
            CanvasDrawEventArgs args,
            CanvasBitmap? currentBitmap,
            IReadOnlyList<ImageEntry> imageEntries,
            ImageCacheManager imageCache,
            int currentIndex,
            double zoomLevel,
            bool isPdfMode,
            bool isCurrentViewSideBySide,
            bool sharpenEnabled,
            bool preferAnimationSpeed,
            double panX,
            ref double panY)
        {
            if (currentBitmap == null) return;

            try
            {
                if (!CanvasBitmapHelper.TryGetBitmapSize(currentBitmap, out var imageSize)) return;

                var ds = args.DrawingSession;
                var canvasSize = sender.Size;
                var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                var scaledSize = new Size(imageSize.Width * fitRatio * zoomLevel, imageSize.Height * fitRatio * zoomLevel);
                var position = new Point(
                    (canvasSize.Width - scaledSize.Width) / 2,
                    (canvasSize.Height - scaledSize.Height) / 2);

                if (isPdfMode || (zoomLevel > 1.01 && !isCurrentViewSideBySide))
                {
                    double maxPan = Math.Max(0, (scaledSize.Height - canvasSize.Height) / 2);
                    double clampMargin = canvasSize.Height + 500;
                    if (panY > maxPan + clampMargin) panY = maxPan + clampMargin;
                    if (panY < -maxPan - clampMargin) panY = -maxPan - clampMargin;

                    position.X = (canvasSize.Width - scaledSize.Width) / 2 + panX;
                    position.Y = (canvasSize.Height - scaledSize.Height) / 2 + panY;
                    DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), isPdfMode, preferAnimationSpeed);

                    double gap = 20 * zoomLevel;
                    DrawAdjacentImages(
                        ds,
                        canvasSize,
                        imageCache,
                        currentBitmap,
                        imageEntries,
                        currentIndex,
                        zoomLevel,
                        isPdfMode,
                        sharpenEnabled,
                        panX,
                        position.Y,
                        scaledSize.Height,
                        gap,
                        preferAnimationSpeed);
                }
                else
                {
                    DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), false, preferAnimationSpeed);
                }
            }
            catch
            {
                // Bitmap resources can be released while Win2D is drawing.
            }
        }

        public static void DrawSideCanvas(
            CanvasControl sender,
            CanvasDrawEventArgs args,
            CanvasBitmap? bitmap,
            double zoomLevel,
            bool alignRight)
        {
            if (bitmap == null) return;

            try
            {
                if (!CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize)) return;

                var ds = args.DrawingSession;
                var canvasSize = sender.Size;
                var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                var scaledSize = new Size(imageSize.Width * fitRatio * zoomLevel, imageSize.Height * fitRatio * zoomLevel);
                var position = new Point(
                    alignRight ? canvasSize.Width - scaledSize.Width : 0,
                    (canvasSize.Height - scaledSize.Height) / 2);

                DrawBitmap(ds, bitmap, new Rect(position, scaledSize), isPdfMode: false);
            }
            catch
            {
                // Bitmap resources can be released while Win2D is drawing.
            }
        }

        public static void DrawBitmapFit(
            CanvasDrawingSession ds,
            CanvasBitmap bitmap,
            Rect bounds,
            HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center)
        {
            if (!CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize)) return;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var fitRatio = Math.Min(bounds.Width / imageSize.Width, bounds.Height / imageSize.Height);
            var scaledSize = new Size(imageSize.Width * fitRatio, imageSize.Height * fitRatio);

            double x = bounds.X + (bounds.Width - scaledSize.Width) / 2;
            if (horizontalAlignment == HorizontalAlignment.Left) x = bounds.X;
            else if (horizontalAlignment == HorizontalAlignment.Right) x = bounds.X + bounds.Width - scaledSize.Width;

            double y = bounds.Y + (bounds.Height - scaledSize.Height) / 2;
            ds.DrawImage(
                bitmap,
                new Rect(x, y, scaledSize.Width, scaledSize.Height),
                bitmap.Bounds,
                1.0f,
                CanvasImageInterpolation.HighQualityCubic);
        }

        private static void DrawAdjacentImages(
            CanvasDrawingSession ds,
            Size canvasSize,
            ImageCacheManager imageCache,
            CanvasBitmap currentBitmap,
            IReadOnlyList<ImageEntry> imageEntries,
            int currentIndex,
            double zoomLevel,
            bool isPdfMode,
            bool sharpenEnabled,
            double panX,
            double currentY,
            double currentHeight,
            double gap,
            bool preferAnimationSpeed)
        {
            double currentTop = currentY;
            for (int i = 1; i <= 5; i++)
            {
                int prevIndex = currentIndex - i;
                if (prevIndex < 0) break;

                var prev = sharpenEnabled && !isPdfMode ? imageCache.GetSharpenedImage(prevIndex) : null;
                prev ??= imageCache.GetPreloadedImage(prevIndex, zoomLevel);
                if (prev != null && CanvasBitmapHelper.TryGetBitmapSize(prev, out var prevSize) && prev != currentBitmap)
                {
                    var fit = Math.Min(canvasSize.Width / prevSize.Width, canvasSize.Height / prevSize.Height);
                    var scaledSize = new Size(prevSize.Width * fit * zoomLevel, prevSize.Height * fit * zoomLevel);
                    var position = new Point(
                        (canvasSize.Width - scaledSize.Width) / 2 + panX,
                        currentTop - scaledSize.Height - gap);

                    DrawBitmap(ds, prev, new Rect(position, scaledSize), isPdfMode, preferAnimationSpeed);
                    currentTop = position.Y;

                    if (currentTop + scaledSize.Height < -500) break;
                }
                else if (prev == currentBitmap)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            double currentBottom = currentY + currentHeight;
            for (int i = 1; i <= 5; i++)
            {
                int nextIndex = currentIndex + i;
                if (nextIndex >= imageEntries.Count) break;

                var next = sharpenEnabled && !isPdfMode ? imageCache.GetSharpenedImage(nextIndex) : null;
                next ??= imageCache.GetPreloadedImage(nextIndex, zoomLevel);
                if (next != null && CanvasBitmapHelper.TryGetBitmapSize(next, out var nextSize) && next != currentBitmap)
                {
                    var fit = Math.Min(canvasSize.Width / nextSize.Width, canvasSize.Height / nextSize.Height);
                    var scaledSize = new Size(nextSize.Width * fit * zoomLevel, nextSize.Height * fit * zoomLevel);
                    var position = new Point(
                        (canvasSize.Width - scaledSize.Width) / 2 + panX,
                        currentBottom + gap);

                    DrawBitmap(ds, next, new Rect(position, scaledSize), isPdfMode, preferAnimationSpeed);
                    currentBottom = position.Y + scaledSize.Height;

                    if (position.Y > canvasSize.Height + 500) break;
                }
                else if (next == currentBitmap)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }
        }

        private static void DrawBitmap(
            CanvasDrawingSession ds,
            CanvasBitmap bitmap,
            Rect destination,
            bool isPdfMode,
            bool preferAnimationSpeed = false)
        {
            if (isPdfMode)
            {
                ds.DrawImage(bitmap, destination);
            }
            else
            {
                var interpolation = preferAnimationSpeed
                    ? CanvasImageInterpolation.Linear
                    : CanvasImageInterpolation.HighQualityCubic;
                ds.DrawImage(bitmap, destination, bitmap.Bounds, 1.0f, interpolation);
            }
        }
    }
}
