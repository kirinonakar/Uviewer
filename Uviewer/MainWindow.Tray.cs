using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
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
                    () => _windowShellController.EndExternalPointerInteraction(),
                    BuildTrayWindowItems,
                    ActivateInstanceFromTray);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing tray icon: {ex.Message}");
                _trayIconService = null;
            }
        }

        private void UpdateTrayIconVisibility()
        {
            // 설정이 바뀐 직후일 수 있으므로 소유자 판정을 먼저 최신 상태로 갱신합니다.
            RefreshMultiInstanceState();

            // 다중 실행 중에는 대표 인스턴스 하나만 트레이 아이콘을 표시합니다.
            bool shouldShow = _keepInTray
                && !_trayExitRequested
                && !_isWindowClosing
                && IsTrayOwner();
            _trayIconService?.SetVisible(shouldShow);

            // 창이 모두 닫힌(트레이로 숨겨진) 상태에서는 트레이 소유자 하나만 남기고,
            // 소유자가 아닌 숨김 인스턴스는 유지하지 않습니다.
            if (!shouldShow
                && _allowMultipleInstances
                && _keepInTray
                && _isHiddenToTray
                && !_trayExitRequested
                && !_isWindowClosing
                && !_isWindowCloseCommitted
                && !IsTrayOwner())
            {
                _shutdownCoordinator.RequestClose(Close);
            }
        }

        private bool TryHideToTray()
        {
            if (!_keepInTray || _trayExitRequested || _isWindowClosing) return false;

            bool cursorTrackingSuspended = false;
            try
            {
                UpdateTrayIconVisibility();

                // 다중 실행 + 트레이에 유지: 트레이 아이콘을 소유한 인스턴스만 트레이로 숨깁니다.
                // 그 외 인스턴스는 숨기지 않고 창을 닫아 종료하며, 트레이 메뉴에서도 자동으로 제거됩니다.
                if (_allowMultipleInstances && !IsTrayOwner()) return false;

                // 트레이 아이콘을 소유한 인스턴스는 아이콘 생성에 성공해야 숨길 수 있습니다.
                if (_trayIconService?.IsVisible != true) return false;

                SaveWindowSettingsForShutdown();
                _windowShellController.BeginExternalPointerInteraction();
                cursorTrackingSuspended = true;

                // Capture the reading position before clearing the current image.
                // Saving it may yield; frame/resource release must not wait for I/O.
                Task savePositionTask = SavePositionBeforeHidingToTrayAsync();
                _imageSwapChainRenderer.ClearAndRelease(
                    ImageArea.Background is Microsoft.UI.Xaml.Media.SolidColorBrush background
                        ? background.Color : Microsoft.UI.Colors.Black);
                _imageViewerController.ClearImageResources();
                ImageViewer.ShowEmptyState();

                AppWindow.Hide();
                _isHiddenToTray = true;
                RefreshMultiInstanceState();
                _explorerSidebarController.ClearFilter(focusInput: false);
                _trayDocumentReleaseTask = ReleaseDocumentAfterHidingToTrayAsync(savePositionTask);
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
            RefreshMultiInstanceState();
            if (AppWindow.Presenter is OverlappedPresenter overlapped &&
                overlapped.State == OverlappedPresenterState.Minimized)
            {
                overlapped.Restore();
            }

            Activate();
            _windowShellController.EndExternalPointerInteraction();
        }

        private async Task SavePositionBeforeHidingToTrayAsync()
        {
            try
            {
                await _bookmarkInteractionController.AddCurrentRecentAsync(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving document position for tray: {ex.Message}");
            }
        }

        private async Task ReleaseDocumentAfterHidingToTrayAsync(Task savePositionTask)
        {
            // Frames and presentation buffers are already detached. Finish slower
            // document cleanup after hiding, without losing the captured position.
            await Task.Yield();
            await savePositionTask;

            try
            {
                await _explorerDocumentReleaseService.ReleaseCurrentDocumentAsync(reduceMemory: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error releasing document after hiding to tray: {ex.Message}");
            }
        }

        private void ExitFromTray()
        {
            _ = ExitApplicationAsync(closeOtherInstances: true);
        }

        private void ExitRequestedByOtherInstance()
        {
            _ = ExitApplicationAsync(closeOtherInstances: false);
        }

        private async Task ExitApplicationAsync(bool closeOtherInstances)
        {
            if (_trayExitRequested || _isWindowClosing) return;

            // Capture visible window bounds before Close() starts changing AppWindow state.
            // When already hidden, this keeps the bounds saved immediately before hiding.
            SaveWindowSettingsForShutdown();
            _trayExitRequested = true;

            // 트레이에서 종료하면 함께 실행 중인 다른 창도 정리합니다.
            if (closeOtherInstances)
            {
                _multiInstanceCoordinator?.RequestCloseOtherInstances();
            }

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

        private void InitializeMultiInstanceCoordinator()
        {
            try
            {
                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _multiInstanceCoordinator = new MultiInstanceCoordinator(
                    windowHandle,
                    DispatcherQueue,
                    ActivateFromTrayRequest,
                    ExitRequestedByOtherInstance);
                _multiInstanceCoordinator.Start();
                RefreshMultiInstanceState();

                // 다른 인스턴스가 종료되거나 문서가 바뀌면 트레이 아이콘 소유자와 목록을 갱신합니다.
                _multiInstanceTimer = DispatcherQueue.CreateTimer();
                _multiInstanceTimer.Interval = TimeSpan.FromSeconds(2);
                _multiInstanceTimer.IsRepeating = true;
                _multiInstanceTimer.Tick += (s, e) =>
                {
                    if (_isWindowClosing) return;
                    UpdateTrayIconVisibility();
                };
                _multiInstanceTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing the multi-instance coordinator: {ex.Message}");
                _multiInstanceCoordinator = null;
            }
        }

        private void DisposeMultiInstanceCoordinator()
        {
            try
            {
                _multiInstanceTimer?.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping the multi-instance timer: {ex.Message}");
            }

            _multiInstanceTimer = null;
            _multiInstanceCoordinator?.Dispose();
            _multiInstanceCoordinator = null;
        }

        /// <summary>이 창에 열려 있는 문서/폴더 이름을 트레이 목록 제목으로 변환합니다.</summary>
        private string GetInstanceDisplayTitle()
        {
            try
            {
                string? path = _currentPdfPath;
                if (string.IsNullOrEmpty(path)) path = _currentEpubFilePath;
                if (string.IsNullOrEmpty(path)) path = _currentTextFilePath;
                if (string.IsNullOrEmpty(path)) path = _currentWebDavItemPath;

                if (!string.IsNullOrEmpty(path))
                {
                    string fileName = Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(fileName)) return fileName;
                }

                var entries = _imageEntries;
                int index = _currentIndex;
                if (entries != null && index >= 0 && index < entries.Count)
                {
                    string? displayName = entries[index]?.DisplayName;
                    if (!string.IsNullOrEmpty(displayName)) return displayName;
                }

                string? folder = _currentExplorerPath;
                if (!string.IsNullOrEmpty(folder))
                {
                    string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string folderName = Path.GetFileName(trimmed);
                    if (!string.IsNullOrEmpty(folderName)) return folderName;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resolving the instance title: {ex.Message}");
            }

            return Strings.WindowTitle;
        }

        private void RefreshMultiInstanceState()
        {
            // 실제로 화면에 표시 중인 창만 트레이 목록에 노출되도록 상태를 함께 공유합니다.
            bool hasVisibleWindow = !_isHiddenToTray && !_isWindowClosing && !_isWindowCloseCommitted;
            _multiInstanceCoordinator?.UpdateSelf(
                GetInstanceDisplayTitle(),
                _keepInTray,
                _allowMultipleInstances,
                hasVisibleWindow);
        }

        private bool IsTrayOwner()
        {
            // 조정자가 없으면 단일 실행으로 간주하고 이 창이 트레이를 소유합니다.
            return _multiInstanceCoordinator?.IsTrayOwner ?? true;
        }

        /// <summary>트레이 우클릭 메뉴에 표시할 열린 창(파일) 목록을 만듭니다.</summary>
        private IReadOnlyList<TrayWindowItem> BuildTrayWindowItems()
        {
            var items = new List<TrayWindowItem>();
            var coordinator = _multiInstanceCoordinator;

            // 다중 실행 + 트레이에 유지일 때만 창 목록을 노출합니다.
            if (coordinator == null || !_allowMultipleInstances) return items;

            foreach (InstanceInfo instance in coordinator.GetLiveInstances())
            {
                // 트레이로 숨겨진(열린 창이 없는) 인스턴스는 목록에서 제외합니다.
                if (!instance.HasVisibleWindow) continue;

                items.Add(new TrayWindowItem
                {
                    Title = string.IsNullOrWhiteSpace(instance.Title) ? Strings.WindowTitle : instance.Title,
                    WindowHandle = instance.WindowHandle,
                    ProcessId = instance.ProcessId,
                    IsCurrent = instance.ProcessId == Environment.ProcessId
                });
            }

            items.Sort((left, right) => left.ProcessId.CompareTo(right.ProcessId));
            return items;
        }

        private void ActivateInstanceFromTray(long windowHandle)
        {
            _multiInstanceCoordinator?.RequestActivate(windowHandle);
        }

        private void ActivateFromTrayRequest()
        {
            _ = ActivateFromTrayAsync();
        }

        private async Task ActivateFromTrayAsync()
        {
            try
            {
                await RestoreFromTrayAsync();
                BringWindowToForeground();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error activating the window from the tray: {ex.Message}");
            }
        }

        private void BringWindowToForeground()
        {
            try
            {
                IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (handle != IntPtr.Zero)
                {
                    SetForegroundWindow(handle);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error bringing the window to the foreground: {ex.Message}");
            }
        }

        /// <summary>다중 실행을 해제할 때 현재 창을 제외한 다른 인스턴스를 닫습니다.</summary>
        private void CloseOtherInstances()
        {
            _multiInstanceCoordinator?.RequestCloseOtherInstances();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

    }
}
