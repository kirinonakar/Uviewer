using Microsoft.UI.Dispatching;
using System;

namespace Uviewer.Services
{
    public class FullscreenOverlayManager
    {
        // 호버 인식 영역 상수
        public const double TopHoverZone = 80;
        public const double LeftHoverZone = 60;
        private const int HideDelayMs = 1000;

        private DispatcherQueueTimer? _toolbarTimer;
        private DispatcherQueueTimer? _sidebarTimer;

        public bool IsToolbarTimerRunning { get; private set; }
        public bool IsSidebarTimerRunning { get; private set; }

        // MainWindow에게 UI를 숨기라고 알리는 이벤트
        public event EventHandler? HideToolbarRequested;
        public event EventHandler? HideSidebarRequested;

        public void Initialize(DispatcherQueue dispatcher)
        {
            _toolbarTimer = dispatcher.CreateTimer();
            _toolbarTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
            _toolbarTimer.IsRepeating = false;
            _toolbarTimer.Tick += (s, e) =>
            {
                IsToolbarTimerRunning = false;
                HideToolbarRequested?.Invoke(this, EventArgs.Empty);
            };

            _sidebarTimer = dispatcher.CreateTimer();
            _sidebarTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
            _sidebarTimer.IsRepeating = false;
            _sidebarTimer.Tick += (s, e) =>
            {
                IsSidebarTimerRunning = false;
                HideSidebarRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        public void StartToolbarTimer()
        {
            if (IsToolbarTimerRunning) return;
            IsToolbarTimerRunning = true;
            _toolbarTimer?.Start();
        }

        public void StopToolbarTimer()
        {
            if (!IsToolbarTimerRunning) return;
            _toolbarTimer?.Stop();
            IsToolbarTimerRunning = false;
        }

        public void StartSidebarTimer()
        {
            if (IsSidebarTimerRunning) return;
            IsSidebarTimerRunning = true;
            _sidebarTimer?.Start();
        }

        public void StopSidebarTimer()
        {
            if (!IsSidebarTimerRunning) return;
            _sidebarTimer?.Stop();
            IsSidebarTimerRunning = false;
        }

        public void StopAll()
        {
            StopToolbarTimer();
            StopSidebarTimer();
        }
    }
}
