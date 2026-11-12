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
                DrawMainSurface(
                    args.DrawingSession,
                    sender.Size,
                    currentBitmap,
                    imageEntries,
                    imageCache,
                    currentIndex,
                    zoomLevel,
                    isPdfMode,
                    isCurrentViewSideBySide,
                    sharpenEnabled,
                    preferAnimationSpeed,
                    panX,
                    ref panY);
            }
            catch
            {
                // Bitmap resources can be released while Win2D is drawing.
            }
        }

        public static void DrawMainSurface(
            CanvasDrawingSession ds,
            Size canvasSize,
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
            if (currentBitmap == null ||
                !CanvasBitmapHelper.TryGetBitmapSize(currentBitmap, out var imageSize) ||
                canvasSize.Width <= 0 || canvasSize.Height <= 0)
                return;

            if (isPdfMode)
            {
                foreach (var page in PdfPageLayout.GetDisplayPages(currentBitmap, imageCache,
                    currentIndex, imageEntries.Count, canvasSize, zoomLevel, panX, panY))
                {
                    DrawBitmap(ds, page.Bitmap, page.Bounds, true, preferAnimationSpeed);
                }
                return;
            }

            var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
            var scaledSize = new Size(imageSize.Width * fitRatio * zoomLevel, imageSize.Height * fitRatio * zoomLevel);
            var position = new Point(
                (canvasSize.Width - scaledSize.Width) / 2,
                (canvasSize.Height - scaledSize.Height) / 2);

            if (zoomLevel > 1.01 && !isCurrentViewSideBySide)
            {
                double maxPan = Math.Max(0, (scaledSize.Height - canvasSize.Height) / 2);
                double clampMargin = canvasSize.Height + 500;
                if (panY > maxPan + clampMargin) panY = maxPan + clampMargin;
                if (panY < -maxPan - clampMargin) panY = -maxPan - clampMargin;

                position.X = (canvasSize.Width - scaledSize.Width) / 2 + panX;
                position.Y = (canvasSize.Height - scaledSize.Height) / 2 + panY;
                DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), false, preferAnimationSpeed);

                DrawAdjacentImages(ds, canvasSize, imageCache, currentBitmap, imageEntries, currentIndex,
                    zoomLevel, sharpenEnabled, panX, position.Y, forward: false, preferAnimationSpeed);
                DrawAdjacentImages(ds, canvasSize, imageCache, currentBitmap, imageEntries, currentIndex,
                    zoomLevel, sharpenEnabled, panX, position.Y + scaledSize.Height, forward: true, preferAnimationSpeed);
            }
            else
            {
                DrawBitmap(ds, currentBitmap, new Rect(position, scaledSize), false, preferAnimationSpeed);
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
                DrawSideSurface(args.DrawingSession, sender.Size, bitmap, zoomLevel, alignRight);
            }
            catch
            {
                // Bitmap resources can be released while Win2D is drawing.
            }
        }

        public static void DrawSideSurface(
            CanvasDrawingSession ds,
            Size canvasSize,
            CanvasBitmap? bitmap,
            double zoomLevel,
            bool alignRight)
        {
            if (bitmap == null ||
                !CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize) ||
                canvasSize.Width <= 0 || canvasSize.Height <= 0)
                return;

            var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
            var scaledSize = new Size(imageSize.Width * fitRatio * zoomLevel, imageSize.Height * fitRatio * zoomLevel);
            var position = new Point(
                alignRight ? canvasSize.Width - scaledSize.Width : 0,
                (canvasSize.Height - scaledSize.Height) / 2);

            DrawBitmap(ds, bitmap, new Rect(position, scaledSize), isPdfMode: false);
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
            bool sharpenEnabled,
            double panX,
            double edge,
            bool forward,
            bool preferAnimationSpeed)
        {
            double gap = 20 * zoomLevel;
            for (int i = 0; i < PreloadManager.ImagePreloadCount; i++)
            {
                int index = FileExplorerService.GetNextImageIndex(imageEntries, currentIndex, 1, forward);
                if (index == currentIndex) break;
                currentIndex = index;

                var bitmap = sharpenEnabled ? imageCache.GetSharpenedImage(index) : null;
                bitmap ??= imageCache.GetPreloadedImage(index, zoomLevel);
                if (bitmap == currentBitmap) continue;
                if (bitmap == null || !CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var size)) break;

                var scaledSize = ZoomService.CalculateScaledSize(canvasSize, size, zoomLevel);
                var position = new Point((canvasSize.Width - scaledSize.Width) / 2 + panX,
                    forward ? edge + gap : edge - scaledSize.Height - gap);

                DrawBitmap(ds, bitmap, new Rect(position, scaledSize), false, preferAnimationSpeed);
                double bottom = position.Y + scaledSize.Height;
                edge = forward ? bottom : position.Y;
                if (forward ? position.Y > canvasSize.Height + 500 : bottom < -500) break;
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
