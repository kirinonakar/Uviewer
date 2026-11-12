using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer.Services
{
    public class PreloadManager : IDisposable
    {
        private readonly ImageCacheManager _imageCache;
        private readonly DispatcherQueue _dispatcherQueue;
        internal const int ImagePreloadCount = 5;
        private const int PdfCacheKeepRange = 20;

        private CancellationTokenSource? _preloadCts;
        private PdfPreloadRequest? _pdfRequest;
        private Task? _pdfPreloadTask;

        private sealed record PdfPreloadRequest(
            int CurrentIndex, List<ImageEntry> Entries, double Zoom, int Generation,
            CanvasBitmap? CurrentBitmap, CanvasBitmap? LeftBitmap, CanvasBitmap? RightBitmap,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> Load,
            Action Invalidate, bool PrioritizeNext);

        public PreloadManager(ImageCacheManager imageCache, DispatcherQueue dispatcherQueue)
        {
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        // 기존 프리로드 작업 취소
        public void CancelAll()
        {
            _pdfRequest = null;
            _pdfPreloadTask = null;
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = null;
        }

        // Next/Prev 방향을 통합한 프리로드 시작 메서드
        public async Task StartPreloadAsync(
            int currentIndex,
            List<ImageEntry> entries,
            bool isPdfMode,
            double zoomLevel,
            CanvasBitmap? currentBitmap,
            CanvasBitmap? leftBitmap,
            CanvasBitmap? rightBitmap,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> loadBitmapFunc,
            Action invalidateCanvasAction,
            bool prioritizeNext = true,
            bool requireSharpening = false)
        {
            try
            {
                if (isPdfMode)
                {
                    await StartPdfPreloadAsync(new PdfPreloadRequest(
                        currentIndex, entries, zoomLevel, _imageCache.Generation,
                        currentBitmap, leftBitmap, rightBitmap, loadBitmapFunc,
                        invalidateCanvasAction, prioritizeNext));
                    return;
                }

                CancelAll();

                _preloadCts = new CancellationTokenSource();
                var token = _preloadCts.Token;
                int generation = _imageCache.Generation;

                if (token.IsCancellationRequested || entries == null || entries.Count == 0) return;

                var tasks = new List<Task>();
                int nextIndex = currentIndex;
                int previousIndex = currentIndex;

                for (int d = 0; d < ImagePreloadCount; d++)
                {
                    if (token.IsCancellationRequested) break;

                    int next = FileExplorerService.GetNextImageIndex(entries, nextIndex, 1, isNext: true);
                    int previous = FileExplorerService.GetNextImageIndex(entries, previousIndex, 1, isNext: false);
                    if (next == nextIndex && previous == previousIndex) break;

                    int[] targets = prioritizeNext
                        ? new[] { next, previous }
                        : new[] { previous, next };

                    foreach (int index in targets)
                    {
                        if (index == nextIndex || index == previousIndex) continue;

                        if (_imageCache.ShouldSkipPreload(index, false, zoomLevel, requireSharpening)) continue;
                        if (!_imageCache.TryMarkForLoading(index, generation)) continue;

                        var entry = entries[index];
                        var capturedIndex = index;

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                if (token.IsCancellationRequested) return;

                                // MainWindow에서 전달받은 디코딩 콜백 실행
                                CanvasBitmap? bitmap = await loadBitmapFunc(entry, token);

                                if (token.IsCancellationRequested)
                                {
                                    _imageCache.ReleaseBitmapIfUncached(bitmap);
                                    return;
                                }

                                if (bitmap != null)
                                {
                                    if (!_imageCache.UpdateCache(capturedIndex, bitmap, false, zoomLevel,
                                        currentBitmap, generation, token)) return;

                                    _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                                    {
                                        if (!token.IsCancellationRequested && generation == _imageCache.Generation)
                                            invalidateCanvasAction();
                                    });
                                }
                            }
                            catch { }
                            finally
                            {
                                _imageCache.UnmarkLoading(capturedIndex, generation);
                            }
                        })); // Run finally even if cancellation preceded scheduling.
                    }

                    nextIndex = next;
                    previousIndex = previous;
                }

                await Task.WhenAll(tasks);

                if (!token.IsCancellationRequested && generation == _imageCache.Generation)
                {
                    int keepCount = ImagePreloadCount * 2;
                    int firstIndex = FileExplorerService.GetNextImageIndex(entries, currentIndex, keepCount, isNext: false);
                    int lastIndex = FileExplorerService.GetNextImageIndex(entries, currentIndex, keepCount, isNext: true);
                    _imageCache.CleanupOldPreloadedImages(firstIndex, lastIndex, currentBitmap, leftBitmap, rightBitmap);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preload error: {ex.Message}");
            }
        }

        private Task StartPdfPreloadAsync(PdfPreloadRequest request)
        {
            var previousTask = _pdfPreloadTask;
            // Navigation updates the desired window without cancelling useful
            // in-flight pages. Document/cache/zoom changes still cancel the scope.
            if (_pdfRequest == null ||
                !ReferenceEquals(_pdfRequest.Entries, request.Entries) ||
                _pdfRequest.Generation != request.Generation ||
                _pdfRequest.Zoom != request.Zoom)
            {
                CancelAll();
                _preloadCts = new CancellationTokenSource();
            }

            _pdfRequest = request;
            if (_pdfPreloadTask == null || _pdfPreloadTask.IsCompleted)
                _pdfPreloadTask = RunPdfPreloadAsync(_preloadCts!.Token, previousTask);
            return _pdfPreloadTask;
        }

        private async Task RunPdfPreloadAsync(CancellationToken token, Task? previousTask)
        {
            // Capture UI-owned canvas/context on the caller's dispatcher, not
            // inside Task.Run. Native rendering itself is asynchronous.
            await Task.Yield();
            // A replaced zoom scope must release its loading marks before the
            // new scope selects pages, otherwise the nearest pages can be skipped.
            if (previousTask != null)
            {
                try { await previousTask; }
                catch (OperationCanceledException) { }
            }
            var attempted = new HashSet<int>();
            PdfPreloadRequest? previousRequest = null;
            while (!token.IsCancellationRequested)
            {
                var request = _pdfRequest;
                if (request == null || request.Generation != _imageCache.Generation) return;
                if (!ReferenceEquals(previousRequest, request)) attempted.Clear();
                previousRequest = request;

                var batch = new List<Task>(2);
                // Zooming out exposes more than three pages on either side.
                int previewRange = Math.Clamp((int)Math.Ceiling(2 / request.Zoom) + 1, 3, 20);
                for (int distance = 1; distance <= previewRange && batch.Count < 2; distance++)
                {
                    int direction = request.PrioritizeNext ? 1 : -1;
                    foreach (int index in new[] {
                        request.CurrentIndex + distance * direction,
                        request.CurrentIndex - distance * direction })
                    {
                        if (batch.Count == 2) break;
                        if (index < 0 || index >= request.Entries.Count ||
                            !request.Entries[index].IsPdfEntry ||
                            _imageCache.GetPreloadedImage(index) != null ||
                            !attempted.Add(index)) continue;
                        batch.Add(LoadPdfPreviewAsync(request, index, token));
                    }
                }

                if (batch.Count == 0)
                {
                    _imageCache.CleanupOldPreloadedImages(request.CurrentIndex - PdfCacheKeepRange,
                        request.CurrentIndex + PdfCacheKeepRange, request.CurrentBitmap, request.LeftBitmap, request.RightBitmap);
                    return;
                }

                // Only two pages are queued at a time. After each batch, read the
                // newest navigation direction instead of rendering a stale queue.
                await Task.WhenAll(batch);
            }
        }

        private async Task LoadPdfPreviewAsync(PdfPreloadRequest request, int index, CancellationToken token)
        {
            if (!_imageCache.TryMarkForLoading(index, request.Generation)) return;
            try
            {
                token.ThrowIfCancellationRequested();
                var bitmap = await request.Load(request.Entries[index], token);
                if (bitmap == null) return;
                if (_imageCache.UpdateCache(index, bitmap, true, request.Zoom,
                    _pdfRequest?.CurrentBitmap, request.Generation, token, preserveExisting: true))
                {
                    if (!token.IsCancellationRequested) request.Invalidate();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF preload error: {ex.Message}");
            }
            finally
            {
                _imageCache.UnmarkLoading(index, request.Generation);
            }
        }

        public void Dispose()
        {
            CancelAll();
        }
    }
}
