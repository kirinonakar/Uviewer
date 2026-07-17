using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;
using Uviewer.Controls;

namespace Uviewer.Services
{
    internal sealed class WindowShellController
    {
        private readonly Window _window;
        private readonly Grid _rootGrid;
        private readonly UIElement _appTitleBar;
        private readonly MainToolbarControl _toolbar;
        private readonly UIElement _statusBarGrid;
        private readonly FrameworkElement _sidebarGrid;
        private readonly UIElement? _splitterGrid;
        private readonly ColumnDefinition _sidebarColumn;
        private readonly WindowStateManager _windowState;
        private readonly FullscreenOverlayManager _overlayManager;
        private readonly Action _saveWindowSettings;
        private readonly Action _invalidateThemeTargets;
        private const int IdcArrow = 32512;

        internal WindowShellController(
            Window window,
            Grid rootGrid,
            UIElement appTitleBar,
            MainToolbarControl toolbar,
            UIElement statusBarGrid,
            FrameworkElement sidebarGrid,
            UIElement? splitterGrid,
            ColumnDefinition sidebarColumn,
            WindowStateManager windowState,
            FullscreenOverlayManager overlayManager,
            Action saveWindowSettings,
            Action invalidateThemeTargets)
        {
            _window = window;
            _rootGrid = rootGrid;
            _appTitleBar = appTitleBar;
            _toolbar = toolbar;
            _statusBarGrid = statusBarGrid;
            _sidebarGrid = sidebarGrid;
            _splitterGrid = splitterGrid;
            _sidebarColumn = sidebarColumn;
            _windowState = windowState;
            _overlayManager = overlayManager;
            _saveWindowSettings = saveWindowSettings;
            _invalidateThemeTargets = invalidateThemeTargets;
        }

        internal ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        internal void ApplyInitialShellState()
        {
            if (!_windowState.IsSidebarVisible)
            {
                _sidebarGrid.Visibility = Visibility.Collapsed;
                if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
                _sidebarColumn.Width = new GridLength(0);
            }

            if (!_windowState.IsPinned)
            {
                _toolbar.SetPinState(false);
                _appTitleBar.Visibility = Visibility.Collapsed;
                SetCaptionButtonsVisibility(false);
                _toolbar.Visibility = Visibility.Collapsed;
                _statusBarGrid.Visibility = Visibility.Collapsed;
                _sidebarGrid.Visibility = Visibility.Collapsed;
                if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
                _sidebarColumn.Width = new GridLength(0);
            }
        }

        internal void HideToolbarUI()
        {
            if (_windowState.IsFullscreen || !_windowState.IsPinned)
            {
                _appTitleBar.Visibility = Visibility.Collapsed;
                if (!_windowState.IsFullscreen) SetCaptionButtonsVisibility(false);
                _toolbar.Visibility = Visibility.Collapsed;
                if (!_windowState.IsFullscreen) _statusBarGrid.Visibility = Visibility.Collapsed;
            }
        }

        internal void HideSidebarUI()
        {
            if (_windowState.IsFullscreen || !_windowState.IsPinned)
            {
                _sidebarGrid.Visibility = Visibility.Collapsed;
                _sidebarColumn.Width = new GridLength(0);
                if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
            }
        }

        internal void ToggleSidebar()
        {
            if (_windowState.IsSidebarVisible && !_windowState.IsFullscreen)
            {
                _windowState.SidebarWidth = (int)_sidebarColumn.Width.Value > 200
                    ? (int)_sidebarColumn.Width.Value
                    : 320;
            }

            if ((int)_sidebarColumn.Width.Value > 200)
            {
                _windowState.IsSidebarVisible = true;
            }

            _windowState.IsSidebarVisible = !_windowState.IsSidebarVisible;
            _sidebarGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_splitterGrid != null)
            {
                _splitterGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            _sidebarColumn.Width = _windowState.IsSidebarVisible
                ? new GridLength(_windowState.SidebarWidth)
                : new GridLength(0);
            _saveWindowSettings();
        }

        internal void CaptureSidebarResize()
        {
            if (_sidebarColumn.Width.IsAbsolute && _sidebarColumn.Width.Value > 200)
            {
                _windowState.SidebarWidth = (int)_sidebarColumn.Width.Value;
            }
        }

        internal void ToggleFullscreen()
        {
            _windowState.ToggleFullscreen();
            ApplyFullscreenUiState();
            _rootGrid.Focus(FocusState.Programmatic);
        }

        internal void ApplyFullscreenUiState()
        {
            if (!_windowState.IsFullscreen)
            {
                _overlayManager.StopAll();
                if (_windowState.IsPinned)
                {
                    _appTitleBar.Visibility = Visibility.Visible;
                    SetCaptionButtonsVisibility(true);
                    _toolbar.Visibility = Visibility.Visible;
                    _statusBarGrid.Visibility = Visibility.Visible;
                    if (_windowState.IsSidebarVisible)
                    {
                        _sidebarGrid.Visibility = Visibility.Visible;
                        if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Visible;
                        _sidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                    }
                    else if (_splitterGrid != null)
                    {
                        _splitterGrid.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    _appTitleBar.Visibility = Visibility.Collapsed;
                    SetCaptionButtonsVisibility(false);
                    _toolbar.Visibility = Visibility.Collapsed;
                    _statusBarGrid.Visibility = Visibility.Collapsed;
                    _sidebarGrid.Visibility = Visibility.Collapsed;
                    if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
                    _sidebarColumn.Width = new GridLength(0);
                }

                _toolbar.SetFullscreenState(false);
            }
            else
            {
                _appTitleBar.Visibility = Visibility.Collapsed;
                _toolbar.Visibility = Visibility.Collapsed;
                _statusBarGrid.Visibility = Visibility.Collapsed;
                _sidebarGrid.Visibility = Visibility.Collapsed;
                if (_windowState.IsSidebarVisible && (int)_sidebarColumn.Width.Value > 200)
                {
                    _windowState.SidebarWidth = (int)_sidebarColumn.Width.Value;
                }
                _sidebarColumn.Width = new GridLength(0);
                if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
                _toolbar.SetFullscreenState(true);
                _overlayManager.StopAll();
            }

            RefreshPointerCursor();
        }

        internal void RefreshPointerCursor()
        {
            SetArrowCursor();
            _rootGrid.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SetArrowCursor);
        }

        private static void SetArrowCursor()
        {
            try
            {
                var arrow = LoadCursor(IntPtr.Zero, IdcArrow);
                if (arrow != IntPtr.Zero)
                {
                    SetCursor(arrow);
                }
            }
            catch
            {
            }
        }

        internal void ToggleMaximizeRestore()
        {
            _windowState.ToggleMaximizeRestore();
            _rootGrid.Focus(FocusState.Programmatic);
        }

        internal void HandlePointerMoved(PointerRoutedEventArgs e)
        {
            if (!_windowState.IsFullscreen && _windowState.IsPinned) return;

            var pt = e.GetCurrentPoint(_rootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            bool inTopZone = y < FullscreenOverlayManager.TopHoverZone;
            if (_toolbar.Visibility == Visibility.Visible && y < _toolbar.ActualHeight)
            {
                inTopZone = true;
            }

            if (inTopZone)
            {
                if (_toolbar.Visibility != Visibility.Visible)
                {
                    _appTitleBar.Visibility = Visibility.Visible;
                    if (!_windowState.IsFullscreen) SetCaptionButtonsVisibility(true);
                    _toolbar.Visibility = Visibility.Visible;
                    if (!_windowState.IsFullscreen) _statusBarGrid.Visibility = Visibility.Visible;
                }
                _overlayManager.StopToolbarTimer();
            }
            else if (_toolbar.Visibility == Visibility.Visible && !_overlayManager.IsToolbarTimerRunning)
            {
                _overlayManager.StartToolbarTimer();
            }

            bool inLeftZone = _windowState.IsSidebarVisible && x < FullscreenOverlayManager.LeftHoverZone;
            if (_sidebarGrid.Visibility == Visibility.Visible && x < _windowState.SidebarWidth)
            {
                inLeftZone = true;
            }

            if (_windowState.IsSidebarVisible && inLeftZone)
            {
                if (_sidebarGrid.Visibility != Visibility.Visible)
                {
                    _sidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                    _sidebarGrid.Visibility = Visibility.Visible;
                }
                _overlayManager.StopSidebarTimer();
            }
            else if (_sidebarGrid.Visibility == Visibility.Visible && !_overlayManager.IsSidebarTimerRunning)
            {
                _overlayManager.StartSidebarTimer();
            }
        }

        internal void HandlePointerExited()
        {
            if (!_windowState.IsFullscreen && _windowState.IsPinned) return;

            if (_toolbar.Visibility == Visibility.Visible && !_overlayManager.IsToolbarTimerRunning)
            {
                _overlayManager.StartToolbarTimer();
            }

            if (_sidebarGrid.Visibility == Visibility.Visible && !_overlayManager.IsSidebarTimerRunning)
            {
                _overlayManager.StartSidebarTimer();
            }
        }

        internal void HandleSmartTouchNavigation(PointerRoutedEventArgs e, bool shouldInvertControls, Action prevAction, Action nextAction)
        {
            var pt = e.GetCurrentPoint(_rootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            if (_windowState.IsFullscreen)
            {
                if (y < FullscreenOverlayManager.TopHoverZone)
                {
                    if (_toolbar.Visibility != Visibility.Visible)
                    {
                        _toolbar.Visibility = Visibility.Visible;
                    }
                    _overlayManager.StartToolbarTimer();
                    return;
                }

                if (x < FullscreenOverlayManager.LeftHoverZone)
                {
                    if (_sidebarGrid.Visibility != Visibility.Visible)
                    {
                        _sidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                        _sidebarGrid.Visibility = Visibility.Visible;
                    }
                    _overlayManager.StartSidebarTimer();
                    return;
                }
            }

            if (x < _rootGrid.ActualWidth / 2)
            {
                if (shouldInvertControls) nextAction();
                else prevAction();
            }
            else
            {
                if (shouldInvertControls) prevAction();
                else nextAction();
            }
        }

        internal void TogglePin()
        {
            if (_windowState.IsFullscreen) return;

            _windowState.TogglePin();
            _toolbar.SetPinState(_windowState.IsPinned);

            if (_windowState.IsPinned)
            {
                _overlayManager.StopAll();

                _appTitleBar.Visibility = Visibility.Visible;
                SetCaptionButtonsVisibility(true);
                _toolbar.Visibility = Visibility.Visible;
                _statusBarGrid.Visibility = Visibility.Visible;
                if (_windowState.IsSidebarVisible)
                {
                    _sidebarGrid.Visibility = Visibility.Visible;
                    if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Visible;
                    _sidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                }
            }
            else
            {
                if (_windowState.IsSidebarVisible && (int)_sidebarColumn.Width.Value > 200)
                {
                    _windowState.SidebarWidth = (int)_sidebarColumn.Width.Value;
                }

                _appTitleBar.Visibility = Visibility.Collapsed;
                SetCaptionButtonsVisibility(false);
                _toolbar.Visibility = Visibility.Collapsed;
                _statusBarGrid.Visibility = Visibility.Collapsed;
                _sidebarGrid.Visibility = Visibility.Collapsed;
                if (_splitterGrid != null) _splitterGrid.Visibility = Visibility.Collapsed;
                _sidebarColumn.Width = new GridLength(0);
            }

            _saveWindowSettings();
        }

        internal void ToggleAlwaysOnTop()
        {
            _windowState.ToggleAlwaysOnTop();
            _toolbar.SetAlwaysOnTopState(_windowState.IsAlwaysOnTop);
            _saveWindowSettings();
        }

        internal void ToggleGlobalTheme()
        {
            bool wasFullscreen = _windowState.IsFullscreen;
            if (wasFullscreen)
            {
                ToggleFullscreen();
            }

            SetTheme(CurrentTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark);

            if (wasFullscreen)
            {
                ToggleFullscreen();
            }

            _saveWindowSettings();
        }

        internal void SetTheme(ElementTheme theme)
        {
            CurrentTheme = theme;

            if (_window.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }

            _rootGrid.RequestedTheme = theme;
            _rootGrid.Focus(FocusState.Programmatic);

            _toolbar.SetThemeState(CurrentTheme);

            UpdateThemeToggleButtonTooltip();
            UpdateTitleBarColors();
            _invalidateThemeTargets();
        }

        internal void UpdateTitleBarColors()
        {
            var appWindow = _window.AppWindow;
            if (appWindow == null || !AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var titleBar = appWindow.TitleBar;
            if (CurrentTheme == ElementTheme.Dark)
            {
                var darkBg = ColorHelper.FromArgb(255, 28, 28, 28);
                titleBar.BackgroundColor = darkBg;
                titleBar.InactiveBackgroundColor = darkBg;

                if (_window.ExtendsContentIntoTitleBar)
                {
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                }
                else
                {
                    titleBar.ButtonBackgroundColor = darkBg;
                    titleBar.ButtonInactiveBackgroundColor = darkBg;
                }

                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonInactiveForegroundColor = Colors.Gray;
                return;
            }

            var lightBg = ColorHelper.FromArgb(255, 243, 243, 243);
            titleBar.BackgroundColor = lightBg;
            titleBar.InactiveBackgroundColor = lightBg;

            if (_window.ExtendsContentIntoTitleBar)
            {
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
            else
            {
                titleBar.ButtonBackgroundColor = lightBg;
                titleBar.ButtonInactiveBackgroundColor = lightBg;
            }

            titleBar.ButtonForegroundColor = Colors.Black;
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x33, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedForegroundColor = Colors.Black;
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x66, 0x00, 0x00, 0x00);
            titleBar.ButtonInactiveForegroundColor = Colors.LightGray;
        }

        internal void UpdateThemeToggleButtonTooltip()
        {
            _toolbar.UpdateThemeToggleButtonTooltip(CurrentTheme);
        }

        internal void SetCaptionButtonsVisibility(bool isVisible)
        {
            _windowState.SetCaptionButtonsVisibility(isVisible);
        }
    }
}
