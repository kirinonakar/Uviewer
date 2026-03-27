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
        private CancellationTokenSource? _pdfCurrentPageUpgradeCts;

        public PreloadManager(ImageCacheManager imageCache, DispatcherQueue dispatcherQueue)
        {
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        // 기존 프리로드 및 해상도 업그레이드 작업 취소
        public void CancelAll()
        {
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = null;

            _pdfCurrentPageUpgradeCts?.Cancel();
            _pdfCurrentPageUpgradeCts?.Dispose();
            _pdfCurrentPageUpgradeCts = null;
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
            Func<ImageEntry, bool, CancellationToken, Task<CanvasBitmap?>> loadBitmapFunc,
            Action invalidateCanvasAction,
            bool prioritizeNext = true)
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
                    bool isPreviewQuality = isPdfEntry && d >= 3;

                    if (_imageCache.ShouldSkipPreload(index, isPdfEntry, zoomLevel, isPreviewQuality)) continue;
                    if (!_imageCache.TryMarkForLoading(index)) continue;

                    var entry = entries[index];
                    var capturedIsPreview = isPreviewQuality;
                    var capturedIndex = index;

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            // MainWindow에서 전달받은 디코딩 콜백 실행
                            CanvasBitmap? bitmap = await loadBitmapFunc(entry, capturedIsPreview, token);

                            if (token.IsCancellationRequested)
                            {
                                _imageCache.SafeDisposeBitmap(bitmap);
                                return;
                            }

                            if (bitmap != null)
                            {
                                _imageCache.UpdateCache(capturedIndex, bitmap, isPdfEntry, zoomLevel, capturedIsPreview, currentBitmap);

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

        // 저해상도 PDF 페이지 풀 해상도 업그레이드
        public void ScheduleCurrentPageUpgradeIfNeeded(
            int pageListIndex,
            Func<int> getCurrentIndexFunc, // 현재 페이지 유지 여부 확인용 콜백
            List<ImageEntry> entries,
            double zoomLevel,
            CanvasBitmap? currentBitmap,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> loadHighResBitmapFunc,
            Action invalidateCanvasAction)
        {
            if (pageListIndex < 0 || pageListIndex >= entries.Count) return;
            if (!_imageCache.NeedsHighResUpgrade(pageListIndex)) return;

            var entry = entries[pageListIndex];
            if (!entry.IsPdfEntry) return;

            _pdfCurrentPageUpgradeCts?.Cancel();
            _pdfCurrentPageUpgradeCts = new CancellationTokenSource();
            var token = _pdfCurrentPageUpgradeCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(50, token);
                    if (token.IsCancellationRequested) return;
                    
                    // 다른 페이지로 이미 이동했으면 중단
                    if (getCurrentIndexFunc() != pageListIndex) return;

                    CanvasBitmap? bitmap = await loadHighResBitmapFunc(entry, token);

                    if (bitmap == null || token.IsCancellationRequested)
                    {
                        _imageCache.SafeDisposeBitmap(bitmap);
                        return;
                    }

                    if (getCurrentIndexFunc() != pageListIndex)
                    {
                        _imageCache.SafeDisposeBitmap(bitmap);
                        return;
                    }

                    _imageCache.UpdateCache(pageListIndex, bitmap, true, zoomLevel, false, currentBitmap);

                    _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                    {
                        invalidateCanvasAction();
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception) { }
            });
        }

        public void Dispose()
        {
            CancelAll();
        }
    }
}
