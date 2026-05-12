using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
            double panX,
            ref double panY)
        {
            if (currentBitmap == null) return;

            try
            {
                if (!TryGetBitmapSize(currentBitmap, out var imageSize)) return;

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
                    DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), isPdfMode);

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
                        gap);
                }
                else
                {
                    DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), isPdfMode: false);
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
                if (!TryGetBitmapSize(bitmap, out var imageSize)) return;

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
            double gap)
        {
            double currentTop = currentY;
            for (int i = 1; i <= 5; i++)
            {
                int prevIndex = currentIndex - i;
                if (prevIndex < 0) break;

                var prev = sharpenEnabled && !isPdfMode ? imageCache.GetSharpenedImage(prevIndex) : null;
                prev ??= imageCache.GetPreloadedImage(prevIndex, zoomLevel);
                if (prev != null && TryGetBitmapSize(prev, out var prevSize) && prev != currentBitmap)
                {
                    var fit = Math.Min(canvasSize.Width / prevSize.Width, canvasSize.Height / prevSize.Height);
                    var scaledSize = new Size(prevSize.Width * fit * zoomLevel, prevSize.Height * fit * zoomLevel);
                    var position = new Point(
                        (canvasSize.Width - scaledSize.Width) / 2 + panX,
                        currentTop - scaledSize.Height - gap);

                    DrawBitmap(ds, prev, new Rect(position, scaledSize), isPdfMode);
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
                if (next != null && TryGetBitmapSize(next, out var nextSize) && next != currentBitmap)
                {
                    var fit = Math.Min(canvasSize.Width / nextSize.Width, canvasSize.Height / nextSize.Height);
                    var scaledSize = new Size(nextSize.Width * fit * zoomLevel, nextSize.Height * fit * zoomLevel);
                    var position = new Point(
                        (canvasSize.Width - scaledSize.Width) / 2 + panX,
                        currentBottom + gap);

                    DrawBitmap(ds, next, new Rect(position, scaledSize), isPdfMode);
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
            bool isPdfMode)
        {
            if (isPdfMode)
            {
                ds.DrawImage(bitmap, destination);
            }
            else
            {
                ds.DrawImage(bitmap, destination, bitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
            }
        }

        private static bool TryGetBitmapSize(CanvasBitmap? bitmap, out Size size)
        {
            size = default;

            try
            {
                if (bitmap == null || bitmap.Device == null) return false;
                size = bitmap.Size;
                return size.Width > 0 && size.Height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
