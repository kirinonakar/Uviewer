using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public class FastNavigationService : IDisposable
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private long? _lastNavigationTimestamp;
        private readonly TimeSpan _fastNavigationThreshold = TimeSpan.FromMilliseconds(40);
        private CancellationTokenSource? _fastNavigationResetCts;
        private DispatcherQueueTimer? _fastNavOverlayTimer;
        private Action? _hideOverlay;
        private bool _disposed;

        // State for UI updates during fast navigation
        public int CurrentIndex { get; private set; }
        public int TotalCount { get; private set; }
        public string DisplayName { get; private set; } = string.Empty;
        public bool IsSideBySide { get; private set; }

        public FastNavigationService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public string GetOverlayMessage() => $"{Strings.FastNavText} ({CurrentIndex + 1}/{TotalCount})";

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

        public bool DetectFastNavigation(Func<CancellationToken, Task> onResetCallback)
        {
            if (_disposed) return false;
            var now = Stopwatch.GetTimestamp();
            bool isFast = _lastNavigationTimestamp.HasValue &&
                Stopwatch.GetElapsedTime(_lastNavigationTimestamp.Value, now) < _fastNavigationThreshold;
            _lastNavigationTimestamp = now;

            CancelPendingReset();

            if (isFast)
            {
                _fastNavigationResetCts = new CancellationTokenSource();
                _ = ResetAfterDelayAsync(onResetCallback, _fastNavigationResetCts.Token);
            }
            else
            {
                // A running reset may already have stopped the overlay timer.
                // Normal navigation replaces that reset, so it must also take
                // responsibility for leaving fast-navigation UI state.
                StopOverlayTimer();
                HideOverlay();
            }

            return isFast;
        }

        public void ShowOverlay(Action showCallback, Action hideCallback)
        {
            if (_disposed) return;
            showCallback?.Invoke();

            _fastNavOverlayTimer?.Stop();
            if (_fastNavOverlayTimer == null)
            {
                _fastNavOverlayTimer = _dispatcherQueue.CreateTimer();
                _fastNavOverlayTimer.IsRepeating = false;
                _fastNavOverlayTimer.Tick += OnOverlayTimerTick;
            }
            _hideOverlay = hideCallback;
            _fastNavOverlayTimer.Interval = TimeSpan.FromMilliseconds(200);
            _fastNavOverlayTimer.Start();
        }

        private void OnOverlayTimerTick(DispatcherQueueTimer sender, object args)
        {
            StopOverlayTimer();
            HideOverlay();
        }

        public void StopOverlayTimer()
        {
            _fastNavOverlayTimer?.Stop();
        }

        private void HideOverlay()
        {
            var hideOverlay = _hideOverlay;
            _hideOverlay = null;
            hideOverlay?.Invoke();
        }

        private async Task ResetAfterDelayAsync(Func<CancellationToken, Task> onResetCallback, CancellationToken token)
        {
            try
            {
                await Task.Delay(50, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    // Cancellation can occur after enqueueing, while the UI is busy.
                    // Never let a stale reset load a replaced/closed document.
                    if (token.IsCancellationRequested || _disposed) return;
                    try
                    {
                        StopOverlayTimer();
                        await onResetCallback(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        // Dispatcher callbacks are async void; exceptions must not
                        // escape onto the UI thread, including after an await.
                        StartupDiagnostics.Record("Fast navigation reset", ex);
                    }
                    finally
                    {
                        // Complete only this reset. A canceled callback must not
                        // hide an overlay belonging to a newer navigation.
                        if (!token.IsCancellationRequested && !_disposed)
                        {
                            _fastNavigationResetCts?.Dispose();
                            _fastNavigationResetCts = null;
                            StopOverlayTimer();
                            HideOverlay();
                        }
                    }
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                StartupDiagnostics.Record("Scheduling fast navigation reset", ex);
            }
        }

        private void CancelPendingReset()
        {
            var cts = _fastNavigationResetCts;
            _fastNavigationResetCts = null;
            cts?.Cancel();
            cts?.Dispose();
        }

        public void StopTimers()
        {
            CancelPendingReset();
            _lastNavigationTimestamp = null;
            StopOverlayTimer();
            HideOverlay();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopTimers();
            if (_fastNavOverlayTimer != null)
            {
                _fastNavOverlayTimer.Tick -= OnOverlayTimerTick;
                _fastNavOverlayTimer = null;
            }
        }
    }
}
