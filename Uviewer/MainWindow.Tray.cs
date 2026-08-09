using Microsoft.UI.Windowing;
using System;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private void InitializeTrayIcon()
        {
            try
            {
                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _trayIconService = new TrayIconService(
                    windowHandle,
                    DispatcherQueue,
                    () => Strings.TrayOpen,
                    () => Strings.TrayExit,
                    RestoreFromTray,
                    ExitFromTray);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing tray icon: {ex.Message}");
                _trayIconService = null;
            }
        }

        private void UpdateTrayIconVisibility()
        {
            _trayIconService?.SetVisible(_keepInTray && !_trayExitRequested);
        }

        private bool TryHideToTray()
        {
            if (!_keepInTray || _trayExitRequested || _isWindowClosing) return false;

            try
            {
                _trayIconService?.SetVisible(true);
                if (_trayIconService?.IsVisible != true) return false;

                SaveWindowSettingsForShutdown();
                AppWindow.Hide();
                _isHiddenToTray = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding window to tray: {ex.Message}");
                return false;
            }
        }

        private void RestoreFromTray()
        {
            if (_trayExitRequested || _isWindowClosing) return;

            AppWindow.Show();
            _isHiddenToTray = false;
            if (AppWindow.Presenter is OverlappedPresenter overlapped &&
                overlapped.State == OverlappedPresenterState.Minimized)
            {
                overlapped.Restore();
            }

            Activate();
        }

        private void ExitFromTray()
        {
            if (_trayExitRequested || _isWindowClosing) return;

            _trayExitRequested = true;
            _trayIconService?.Dispose();
            _trayIconService = null;
            _shutdownCoordinator.RequestClose(Close);
        }

        private void DisposeTrayIcon()
        {
            _trayIconService?.Dispose();
            _trayIconService = null;
        }
    }
}
