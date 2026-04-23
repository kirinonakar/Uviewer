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
        private const int DefaultPreloadCount = 5;

        private CancellationTokenSource? _preloadCts;

        public PreloadManager(ImageCacheManager imageCache, DispatcherQueue dispatcherQueue)
        {
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        // 기존 프리로드 작업 취소
        public void CancelAll()
        {
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
                CancelAll();

                _preloadCts = new CancellationTokenSource();
                var token = _preloadCts.Token;

                // PDF의 경우 연속 스크롤 디바운스를 위해 잠시 대기
                if (isPdfMode)
                {
                    await Task.Delay(100, token).ContinueWith(_ => { }, TaskContinuationOptions.None);
                }

                if (token.IsCancellationRequested || entries == null || entries.Count == 0) return;

                int preloadDist = isPdfMode ? 10 : DefaultPreloadCount;
                var tasks = new List<Task>();

                for (int d = 1; d <= preloadDist; d++)
                {
                    if (token.IsCancellationRequested) break;

                    // 우선순위 방향에 따라 배열 순서 변경
                    int[] targets = prioritizeNext
                        ? new[] { currentIndex + d, currentIndex - d }
                        : new[] { currentIndex - d, currentIndex + d };

                    foreach (int index in targets)
                    {
                        if (index < 0 || index >= entries.Count || index == currentIndex) continue;
                    
                        if (!FileExplorerService.IsNavigableImage(entries[index])) continue;

                        bool isPdfEntry = entries[index].IsPdfEntry && isPdfMode;

                        if (_imageCache.ShouldSkipPreload(index, isPdfEntry, zoomLevel, requireSharpening)) continue;
                        if (!_imageCache.TryMarkForLoading(index)) continue;

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
                                    _imageCache.SafeDisposeBitmap(bitmap);
                                    return;
                                }

                                if (bitmap != null)
                                {
                                    _imageCache.UpdateCache(capturedIndex, bitmap, isPdfEntry, zoomLevel, currentBitmap);

                                    _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                                    {
                                        invalidateCanvasAction();
                                    });
                                }
                            }
                            catch { }
                            finally
                            {
                                _imageCache.UnmarkLoading(capturedIndex);
                            }
                        }, token));
                    }
                }

                await Task.WhenAll(tasks);

                if (!token.IsCancellationRequested)
                {
                    _imageCache.CleanupOldPreloadedImages(currentIndex, isPdfMode, DefaultPreloadCount, currentBitmap, leftBitmap, rightBitmap);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preload error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            CancelAll();
        }
    }
}
