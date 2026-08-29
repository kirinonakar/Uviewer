using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private Task _trayDocumentReleaseTask = Task.CompletedTask;

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
                    ExitFromTray,
                    () => _windowShellController.BeginExternalPointerInteraction(),
                    () => _windowShellController.EndExternalPointerInteraction());
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

            bool cursorTrackingSuspended = false;
            try
            {
                _trayIconService?.SetVisible(true);
                if (_trayIconService?.IsVisible != true) return false;

                SaveWindowSettingsForShutdown();
                _windowShellController.BeginExternalPointerInteraction();
                cursorTrackingSuspended = true;
                AppWindow.Hide();
                _isHiddenToTray = true;
                _trayDocumentReleaseTask = ReleaseDocumentAfterHidingToTrayAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (cursorTrackingSuspended)
                {
                    _windowShellController.EndExternalPointerInteraction();
                }

                System.Diagnostics.Debug.WriteLine($"Error hiding window to tray: {ex.Message}");
                return false;
            }
        }

        private void RestoreFromTray()
        {
            _ = RestoreFromTrayAsync();
        }

        private async Task RestoreFromTrayAsync()
        {
            await _trayDocumentReleaseTask;
            if (_trayExitRequested || _isWindowClosing) return;

            AppWindow.Show();
            _isHiddenToTray = false;
            if (AppWindow.Presenter is OverlappedPresenter overlapped &&
                overlapped.State == OverlappedPresenterState.Minimized)
            {
                overlapped.Restore();
            }

            Activate();
            _windowShellController.EndExternalPointerInteraction();
        }

        private async Task ReleaseDocumentAfterHidingToTrayAsync()
        {
            // Let the hide request complete before starting UI-bound document cleanup.
            await Task.Yield();

            try
            {
                await _bookmarkInteractionController.AddCurrentRecentAsync(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving document position after hiding to tray: {ex.Message}");
            }

            try
            {
                await _explorerDocumentReleaseService.ReleaseCurrentDocumentAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error releasing document after hiding to tray: {ex.Message}");
            }
        }

        private void ExitFromTray()
        {
            _ = ExitFromTrayAsync();
        }

        private async Task ExitFromTrayAsync()
        {
            if (_trayExitRequested || _isWindowClosing) return;

            // Capture visible window bounds before Close() starts changing AppWindow state.
            // When already hidden, this keeps the bounds saved immediately before hiding.
            SaveWindowSettingsForShutdown();
            _trayExitRequested = true;
            _trayIconService?.Dispose();
            _trayIconService = null;
            await _trayDocumentReleaseTask;
            _shutdownCoordinator.RequestClose(Close);
        }

        private void DisposeTrayIcon()
        {
            _trayIconService?.Dispose();
            _trayIconService = null;
        }

        private async Task ShowKeepInTrayRestartDialogAsync()
        {
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    RequestedTheme = RootGrid.ActualTheme,
                    Title = Strings.KeepInTrayRestartTitle,
                    Content = Strings.KeepInTrayRestartMessage,
                    PrimaryButtonText = Strings.KeepInTrayRestartNow,
                    CloseButtonText = Strings.KeepInTrayRestartCancel,
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    RestartApplication();
                }
                else
                {
                    // 취소/닫기: 트레이에 유지를 해제하고 이전 다중실행 상태를 복원합니다.
                    _keepInTray = false;
                    _allowMultipleInstances = _previousAllowMultipleInstances;
                    MainToolbar.SetKeepInTray(false);
                    MainToolbar.SetAllowMultipleInstances(_allowMultipleInstances);
                    UpdateTrayIconVisibility();
                    _windowSettingsCoordinator.SaveWindowSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing keep-in-tray restart dialog: {ex.Message}");
            }
        }

        private static void RestartApplication()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                // 다른 실행 중인 인스턴스를 먼저 모두 종료합니다.
                string processName = Process.GetCurrentProcess().ProcessName;
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try { process.Kill(); } catch { }
                    process.Dispose();
                }

                // 새 인스턴스를 재시작 마커와 함께 실행합니다.
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                    Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(a => $"\"{a}\""))
                };
                psi.Environment["UVIEWER_RESTARTING"] = "1";
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restarting application: {ex.Message}");
                return;
            }

            Environment.Exit(0);
        }
    }
}
