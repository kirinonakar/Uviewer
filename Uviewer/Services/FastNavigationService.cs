using Microsoft.UI.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public class FastNavigationService
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private DateTime _lastNavigationTime = DateTime.MinValue;
        private readonly TimeSpan _fastNavigationThreshold = TimeSpan.FromMilliseconds(40);
        private CancellationTokenSource? _fastNavigationResetCts;
        private DispatcherQueueTimer? _fastNavOverlayTimer;

        // State for UI updates during fast navigation
        public int CurrentIndex { get; private set; }
        public int TotalCount { get; private set; }
        public string DisplayName { get; private set; } = string.Empty;
        public bool IsSideBySide { get; private set; }

        public FastNavigationService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public string GetOverlayMessage() => $"빠른 탐색 중... ({CurrentIndex + 1}/{TotalCount})";

        public string GetImageIndexMessage()
        {
            if (IsSideBySide)
            {
                int displayIndex = (CurrentIndex / 2) + 1;
                int totalPairs = (TotalCount + 1) / 2;
                return $"{displayIndex} / {totalPairs} (B)";
            }
            else
            {
                return $"{CurrentIndex + 1} / {TotalCount}";
            }
        }

        public void UpdateState(int currentIndex, int totalCount, string displayName, bool isSideBySide)
        {
            CurrentIndex = currentIndex;
            TotalCount = totalCount;
            DisplayName = displayName;
            IsSideBySide = isSideBySide;
        }

        public bool DetectFastNavigation(Func<Task> onResetCallback)
        {
            var now = DateTime.Now;
            var timeSinceLastNavigation = now - _lastNavigationTime;
            _lastNavigationTime = now;

            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts = new CancellationTokenSource();
            var token = _fastNavigationResetCts.Token;

            if (timeSinceLastNavigation < _fastNavigationThreshold)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(50, token);
                        if (!token.IsCancellationRequested)
                        {
                            await HandleReset(onResetCallback);
                        }
                    }
                    catch (OperationCanceledException) { }
                });

                return true;
            }

            return false;
        }

        public void ShowOverlay(Action showCallback, Action hideCallback)
        {
            showCallback?.Invoke();

            _fastNavOverlayTimer?.Stop();
            _fastNavOverlayTimer ??= _dispatcherQueue.CreateTimer();
            _fastNavOverlayTimer.Interval = TimeSpan.FromMilliseconds(200);
            _fastNavOverlayTimer.Tick += (s, e) =>
            {
                _fastNavOverlayTimer?.Stop();
                hideCallback?.Invoke();
            };
            _fastNavOverlayTimer.Start();
        }

        public void StopOverlayTimer()
        {
             _fastNavOverlayTimer?.Stop();
        }

        private Task HandleReset(Func<Task> onResetCallback)
        {
            var tcs = new TaskCompletionSource();
            _dispatcherQueue.TryEnqueue(async () =>
            {
                _fastNavOverlayTimer?.Stop(); 

                try
                {
                    if (onResetCallback != null)
                    {
                        await onResetCallback();
                    }
                }
                finally
                {
                    tcs.TrySetResult();
                }
            });
            return tcs.Task;
        }

        public void StopTimers()
        {
            _fastNavOverlayTimer?.Stop();
            _fastNavigationResetCts?.Cancel();
        }

        public void Dispose()
        {
            StopTimers();
            _fastNavigationResetCts?.Dispose();
        }
    }
}
