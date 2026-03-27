using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using System;

namespace Uviewer.Services
{
    public class WindowStateManager
    {
        private readonly Window _window;
        private readonly AppWindow _appWindow;

        // 창 상태 프로퍼티
        public bool IsFullscreen { get; set; }
        public bool IsPinned { get; set; } = true;
        public bool IsAlwaysOnTop { get; set; }
        public bool IsSidebarVisible { get; set; } = true;
        public int SidebarWidth { get; set; } = 320;
        public bool WasMaximizedBeforeFullscreen { get; set; }
        public RectInt32 LastNonMaximizedRect { get; set; } = new(100, 100, 1200, 800);

        public WindowStateManager(Window window)
        {
            _window = window;
            _appWindow = window.AppWindow;
        }

        // 전체화면 토글 로직
        public void ToggleFullscreen()
        {
            if (IsFullscreen)
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Default);
                IsFullscreen = false;

                // 전체화면 해제 시 항상 위(Always on Top) 상태 복구
                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.IsAlwaysOnTop = IsAlwaysOnTop;
                }
            }
            else
            {
                if (_appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    WasMaximizedBeforeFullscreen = overlapped.State == OverlappedPresenterState.Maximized;
                }
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                IsFullscreen = true;
            }
        }

        // 핀(Pin) 상태 토글
        public void TogglePin()
        {
            IsPinned = !IsPinned;
        }

        // 항상 위 상태 토글
        public void ToggleAlwaysOnTop()
        {
            IsAlwaysOnTop = !IsAlwaysOnTop;
            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = IsAlwaysOnTop;
            }
        }

        // 타이틀바 캡션 버튼(최소화, 최대화, 닫기) 가시성 제어
        public void SetCaptionButtonsVisibility(bool isVisible)
        {
            if (_window.ExtendsContentIntoTitleBar && AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.PreferredHeightOption = isVisible
                    ? TitleBarHeightOption.Standard
                    : TitleBarHeightOption.Collapsed;
            }
        }

        // 창 크기 변경 시 이전 위치 저장 로직
        public void HandleAppWindowChanged(AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                if (_appWindow.Presenter is OverlappedPresenter overlapped &&
                    overlapped.State == OverlappedPresenterState.Restored)
                {
                    var pos = _appWindow.Position;
                    var size = _appWindow.Size;

                    if (size.Width >= 100 && size.Height >= 100)
                    {
                        var currentRect = new RectInt32(pos.X, pos.Y, size.Width, size.Height);
                        var area = DisplayArea.GetFromRect(currentRect, DisplayAreaFallback.None);

                        if (area != null &&
                            pos.X >= area.WorkArea.X && pos.Y >= area.WorkArea.Y &&
                            size.Width <= area.WorkArea.Width && size.Height <= area.WorkArea.Height)
                        {
                            LastNonMaximizedRect = currentRect;
                        }
                    }
                }
            }
        }
    }
}
