using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.Foundation;

namespace Uviewer.Services
{
    internal sealed class ImageViewportNavigationContext
    {
        public List<ImageEntry> ImageEntries { get; init; } = null!;
        public ImageCacheManager ImageCache { get; init; } = null!;
        public PreloadManager PreloadManager { get; init; } = null!;
        public CanvasControl MainCanvas { get; init; } = null!;

        public Func<int> GetCurrentIndex { get; init; } = null!;
        public Action<int> SetCurrentIndex { get; init; } = null!;
        public Func<double> GetZoomLevel { get; init; } = null!;
        public Action<double> SetZoomLevel { get; init; } = null!;
        public Func<CanvasBitmap?> GetCurrentBitmap { get; init; } = null!;
        public Action<CanvasBitmap?> SetCurrentBitmap { get; init; } = null!;
        public Func<CanvasBitmap?> GetLeftBitmap { get; init; } = null!;
        public Func<CanvasBitmap?> GetRightBitmap { get; init; } = null!;
        public Func<bool> IsPdfMode { get; init; } = null!;
        public Func<bool> IsSharpenEnabled { get; init; } = null!;
        public Func<CancellationToken> GetCancellationToken { get; init; } = null!;

        public Func<uint, CanvasControl, CancellationToken, Task<CanvasBitmap?>> LoadPdfPageBitmapAsync { get; init; } = null!;
        public Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> LoadImageBitmapAsync { get; init; } = null!;
        public Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> LoadBitmapForPreloadAsync { get; init; } = null!;

        public Action<ImageEntry, CanvasBitmap> UpdateStatusBar { get; init; } = null!;
        public Action<ImageEntry> SyncSidebarSelection { get; init; } = null!;
        public Action ApplyZoom { get; init; } = null!;
        public Action InvalidateCanvas { get; init; } = null!;
    }

    internal sealed class ImageViewportNavigationService : IDisposable
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Func<Task> _rerenderPdfCurrentPageAsync;
        private DispatcherQueueTimer? _smoothZoomTimer;
        private ImageViewportNavigationContext? _smoothZoomContext;
        private double _targetZoomLevel = 1.0;
        private Point _zoomPivot;

        public ImageViewportNavigationService(
            DispatcherQueue dispatcherQueue,
            Func<Task> rerenderPdfCurrentPageAsync)
        {
            _dispatcherQueue = dispatcherQueue;
            _rerenderPdfCurrentPageAsync = rerenderPdfCurrentPageAsync;
        }

        public double PanX { get; set; }
        public double PanY { get; set; }
        public bool IsTransitioning { get; set; }
        public int DisplayedPdfPageIndex { get; set; } = -1;
        public int ScrollDirection { get; set; } = 1;
        public bool IsSmoothZoomRunning => _smoothZoomTimer?.IsRunning == true;

        public void Reset(int scrollDirection = 1)
        {
            PanX = 0;
            PanY = 0;
            DisplayedPdfPageIndex = -1;
            IsTransitioning = false;
            ScrollDirection = scrollDirection;
            _targetZoomLevel = 1.0;
            StopSmoothZoom();
        }

        public void ResetPanForBitmap(CanvasControl canvas, CanvasBitmap bitmap, double zoomLevel)
        {
            if (CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize))
            {
                PanY = ZoomService.CalculateInitialVerticalPan(canvas.Size, imageSize, zoomLevel, ScrollDirection);
            }
            else
            {
                PanY = 0;
            }

            PanX = 0;
            IsTransitioning = false;
        }

        public void ZoomAtPosition(
            ImageViewportNavigationContext context,
            double zoomMultiplier,
            Point position)
        {
            var bitmap = context.GetCurrentBitmap();
            if (bitmap == null) return;
            if (!CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize)) return;

            var transform = ZoomService.CalculateZoomAtPosition(
                context.MainCanvas.Size,
                imageSize,
                context.GetZoomLevel(),
                PanX,
                PanY,
                zoomMultiplier,
                position,
                continuousVertical: context.IsPdfMode());

            if (!transform.HasValue) return;

            context.SetZoomLevel(transform.Value.ZoomLevel);
            PanX = transform.Value.PanX;
            PanY = transform.Value.PanY;
            context.ApplyZoom();
        }

        public void StartSmoothZoom(
            ImageViewportNavigationContext context,
            double targetMultiplier,
            Point pivot)
        {
            _smoothZoomContext = context;
            if (_smoothZoomTimer == null)
            {
                _smoothZoomTimer = _dispatcherQueue.CreateTimer();
                _smoothZoomTimer.Interval = TimeSpan.FromMilliseconds(16);
                _smoothZoomTimer.Tick += SmoothZoomTimer_Tick;
            }

            double currentZoom = context.GetZoomLevel();
            if (!_smoothZoomTimer.IsRunning || Math.Abs(currentZoom - _targetZoomLevel) < 0.001)
            {
                _targetZoomLevel = currentZoom;
            }

            _targetZoomLevel = Math.Clamp(
                _targetZoomLevel * targetMultiplier,
                ZoomService.MinZoom,
                ZoomService.MaxZoom);
            _zoomPivot = pivot;

            if (!_smoothZoomTimer.IsRunning)
            {
                _smoothZoomTimer.Start();
            }
        }

        public async Task HandleScrollAsync(
            ImageViewportNavigationContext context,
            double deltaX,
            double deltaY,
            bool allowPageTransition = true)
        {
            var bitmap = context.GetCurrentBitmap();
            bool isPdfMode = context.IsPdfMode();
            double zoomLevel = context.GetZoomLevel();

            if ((!isPdfMode && zoomLevel <= 1.01) ||
                (isPdfMode && DisplayedPdfPageIndex != context.GetCurrentIndex()) ||
                !CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize) ||
                IsTransitioning)
            {
                return;
            }

            try
            {
                var canvasSize = context.MainCanvas.Size;
                if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;

                var scaledSize = ZoomService.CalculateScaledSize(canvasSize, imageSize, zoomLevel);
                PanX = ClampHorizontalPan(PanX + deltaX, scaledSize.Width, canvasSize.Width);

                if (!allowPageTransition)
                {
                    double maxPanY = GetMaxPanY(scaledSize.Height, canvasSize.Height);
                    PanY = isPdfMode ? PanY + deltaY : Math.Clamp(PanY + deltaY, -maxPanY, maxPanY);
                    context.ApplyZoom();
                    return;
                }

                if (deltaY > 0)
                {
                    PanY += deltaY;
                    int currentIndex = context.GetCurrentIndex();
                    int targetIndex = FileExplorerService.GetNextImageIndex(context.ImageEntries, currentIndex, 1, false);

                    if (PanY > GetMaxPanY(scaledSize.Height, canvasSize.Height) + 1 && targetIndex != currentIndex)
                    {
                        double currentTop = (canvasSize.Height - scaledSize.Height) / 2 + PanY;
                        if (await TransitionToIndexAsync(context, bitmap!, targetIndex, canvasSize, currentTop, forward: false))
                        {
                            return;
                        }
                    }

                    if (targetIndex == currentIndex)
                    {
                        PanY = Math.Min(PanY, GetMaxPanY(scaledSize.Height, canvasSize.Height));
                    }
                }
                else if (deltaY < 0)
                {
                    PanY += deltaY;
                    int currentIndex = context.GetCurrentIndex();
                    int targetIndex = FileExplorerService.GetNextImageIndex(context.ImageEntries, currentIndex, 1, true);

                    if (PanY < -GetMaxPanY(scaledSize.Height, canvasSize.Height) - 1 && targetIndex != currentIndex)
                    {
                        double currentBottom = (canvasSize.Height - scaledSize.Height) / 2 + PanY + scaledSize.Height;
                        if (await TransitionToIndexAsync(context, bitmap!, targetIndex, canvasSize, currentBottom, forward: true))
                        {
                            return;
                        }
                    }

                    if (targetIndex == currentIndex)
                    {
                        PanY = Math.Max(PanY, -GetMaxPanY(scaledSize.Height, canvasSize.Height));
                    }
                }

                context.ApplyZoom();
            }
            finally
            {
                IsTransitioning = false;
            }
        }

        public void StopSmoothZoom()
        {
            _smoothZoomTimer?.Stop();
            _smoothZoomContext = null;
        }

        public void Dispose()
        {
            StopSmoothZoom();
            _smoothZoomContext = null;
        }

        private async Task<bool> TransitionToIndexAsync(
            ImageViewportNavigationContext context,
            CanvasBitmap currentBitmap,
            int targetIndex,
            Size canvasSize,
            double anchorEdge,
            bool forward)
        {
            try
            {
                IsTransitioning = true;

                int generation = context.ImageCache.Generation;
                var token = context.GetCancellationToken();
                if (token.IsCancellationRequested) return true;
                double zoomLevel = context.GetZoomLevel();
                bool isPdfMode = context.IsPdfMode();
                int oldIndex = context.GetCurrentIndex();
                context.ImageCache.UpdateCache(
                    oldIndex,
                    currentBitmap,
                    isPdf: isPdfMode,
                    currentZoom: zoomLevel,
                    currentDisplayingBitmap: context.GetCurrentBitmap());

                var targetBitmap = GetCachedTargetBitmap(context, targetIndex, zoomLevel);
                if (targetBitmap == null || !CanvasBitmapHelper.TryGetBitmapSize(targetBitmap, out var targetSize))
                {
                    targetBitmap = await LoadTargetBitmapAsync(context, targetIndex);
                    if (targetBitmap == null) return true;

                    if (!context.ImageCache.UpdateCache(
                        targetIndex,
                        targetBitmap,
                        isPdf: isPdfMode,
                        currentZoom: zoomLevel,
                        currentDisplayingBitmap: context.GetCurrentBitmap(),
                        expectedGeneration: generation,
                        token: token)) return true;
                    if (!CanvasBitmapHelper.TryGetBitmapSize(targetBitmap, out targetSize)) return true;
                }

                // Keep the page index paired with the bitmap until the asynchronous load
                // completes. Otherwise pointer hit tests can read another page's text.
                if (token.IsCancellationRequested || generation != context.ImageCache.Generation ||
                    context.GetCurrentIndex() != oldIndex || context.GetZoomLevel() != zoomLevel ||
                    context.GetCurrentBitmap() != currentBitmap) return true;

                PanY = CalculateTransitionPanY(canvasSize, targetSize, zoomLevel, anchorEdge, forward);
                context.SetCurrentIndex(targetIndex);
                context.SetCurrentBitmap(targetBitmap);
                if (context.IsPdfMode()) DisplayedPdfPageIndex = targetIndex;

                var entry = context.ImageEntries[targetIndex];
                context.UpdateStatusBar(entry, targetBitmap);
                context.SyncSidebarSelection(entry);
                context.InvalidateCanvas();
                StartPreload(context, targetIndex, forward);
                if (context.IsPdfMode())
                {
                    // A cached preload may have been rendered at a lower zoom or
                    // at the preload resolution cap. Promote it to visible quality.
                    _ = _rerenderPdfCurrentPageAsync();
                }

                return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                IsTransitioning = false;
            }
        }

        private CanvasBitmap? GetCachedTargetBitmap(
            ImageViewportNavigationContext context,
            int targetIndex,
            double zoomLevel)
        {
            CanvasBitmap? bitmap = context.IsSharpenEnabled() && !context.IsPdfMode()
                ? context.ImageCache.GetSharpenedImage(targetIndex)
                : null;

            return bitmap ?? context.ImageCache.GetPreloadedImage(targetIndex,
                context.IsPdfMode() ? null : zoomLevel);
        }

        private async Task<CanvasBitmap?> LoadTargetBitmapAsync(
            ImageViewportNavigationContext context,
            int targetIndex)
        {
            var token = context.GetCancellationToken();
            if (context.IsPdfMode())
            {
                return await context.LoadPdfPageBitmapAsync((uint)targetIndex, context.MainCanvas, token);
            }

            return await context.LoadImageBitmapAsync(context.ImageEntries[targetIndex], context.MainCanvas, token);
        }

        private void StartPreload(
            ImageViewportNavigationContext context,
            int targetIndex,
            bool prioritizeNext)
        {
            _ = context.PreloadManager.StartPreloadAsync(
                targetIndex,
                context.ImageEntries,
                context.IsPdfMode(),
                context.GetZoomLevel(),
                context.GetCurrentBitmap(),
                context.GetLeftBitmap(),
                context.GetRightBitmap(),
                context.LoadBitmapForPreloadAsync,
                context.InvalidateCanvas,
                prioritizeNext,
                requireSharpening: context.IsSharpenEnabled());
        }

        private void SmoothZoomTimer_Tick(object? sender, object e)
        {
            var context = _smoothZoomContext;
            if (context == null || context.GetCurrentBitmap() == null)
            {
                StopSmoothZoom();
                return;
            }

            double currentZoom = context.GetZoomLevel();
            if (Math.Abs(currentZoom - _targetZoomLevel) < 0.005)
            {
                ZoomAtPosition(context, _targetZoomLevel / currentZoom, _zoomPivot);
                StopSmoothZoom();
                if (context.IsPdfMode()) _ = _rerenderPdfCurrentPageAsync();
                return;
            }

            double nextZoom = currentZoom + (_targetZoomLevel - currentZoom) * 0.3;
            ZoomAtPosition(context, nextZoom / currentZoom, _zoomPivot);
        }

        private static double ClampHorizontalPan(double panX, double scaledWidth, double canvasWidth)
        {
            double maxPanX = Math.Max(0, (scaledWidth - canvasWidth) / 2);
            return Math.Clamp(panX, -maxPanX, maxPanX);
        }

        private static double GetMaxPanY(double scaledHeight, double canvasHeight)
        {
            return Math.Max(0, (scaledHeight - canvasHeight) / 2);
        }

        private static double CalculateTransitionPanY(
            Size canvasSize,
            Size targetImageSize,
            double zoomLevel,
            double anchorEdge,
            bool forward)
        {
            double fit = Math.Min(canvasSize.Width / targetImageSize.Width, canvasSize.Height / targetImageSize.Height);
            double targetScaledHeight = targetImageSize.Height * fit * zoomLevel;
            double gap = 20 * zoomLevel;

            return forward
                ? (anchorEdge + gap) - (canvasSize.Height - targetScaledHeight) / 2
                : (anchorEdge - gap - targetScaledHeight) - (canvasSize.Height - targetScaledHeight) / 2;
        }
    }
}
