using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class WindowSettingsHostAdapter : IWindowSettingsHost
        {
            private readonly MainWindow _window;

            public WindowSettingsHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public AppWindow AppWindow => _window.AppWindow;
            public WindowStateManager WindowState => _window._windowState;
            public ImageProcessingViewModel ImageOptions => _window.ImageOptions;

            public bool SharpenEnabled
            {
                get => _window._imageViewerState.IsSharpenEnabled;
                set => _window._imageViewerState.IsSharpenEnabled = value;
            }

            public bool SideBySideMode
            {
                get => _window._imageViewerState.IsSideBySideMode;
                set => _window._imageViewerState.IsSideBySideMode = value;
            }

            public bool NextImageOnRight
            {
                get => _window._imageViewerState.NextImageOnRight;
                set => _window._imageViewerState.NextImageOnRight = value;
            }

            public ElementTheme CurrentTheme => _window._windowShellController.CurrentTheme;

            public bool MatchControlDirection
            {
                get => _window._matchControlDirection;
                set => _window._matchControlDirection = value;
            }

            public bool AllowMultipleInstances
            {
                get => _window._allowMultipleInstances;
                set => _window._allowMultipleInstances = value;
            }

            public bool AutoDoublePageForArchive
            {
                get => _window._imageViewerState.AutoDoublePageForArchive;
                set => _window._imageViewerState.AutoDoublePageForArchive = value;
            }

            public bool IsRegistered
            {
                get => _window._isRegistered;
                set => _window._isRegistered = value;
            }

            public double ExplorerThumbnailSize
            {
                get => _window._explorerThumbnailSize;
                set => _window._explorerThumbnailSize = System.Math.Clamp(value, 64, 180);
            }

            public bool ShowFolderThumbnails
            {
                get => _window._showFolderThumbnails;
                set => _window._showFolderThumbnails = value;
            }

            public string ExternalProgramPath
            {
                get => _window._externalProgramPath;
                set => _window._externalProgramPath = value ?? string.Empty;
            }

            public void SetTheme(ElementTheme theme) => _window.SetTheme(theme);

            public void RestoreMaximizedWhenActivated()
            {
                _window.Activated += RestoreMaximizedStateOnce;
            }

            public void ApplyInitialWindowSettingsUiState()
            {
                _window._imageViewerController.UpdateSharpenButtonState();
                _window._imageViewerController.UpdateSideBySideButtonState();
                _window._imageViewerController.UpdateNextImageSideButtonState();

                _window.MainToolbar.SetWindowOptionStates(
                    _window._matchControlDirection,
                    _window._allowMultipleInstances,
                    _window._imageViewerState.AutoDoublePageForArchive,
                    _window._windowState.IsAlwaysOnTop);
                _window._explorerSidebarController.ApplyThumbnailSettingsToControls();
                _window.MainToolbar.SetExternalProgramPath(_window._externalProgramPath);

                if (_window.AppWindow.Presenter is OverlappedPresenter op)
                {
                    op.IsAlwaysOnTop = _window._windowState.IsAlwaysOnTop;
                }
            }

            private void RestoreMaximizedStateOnce(object sender, WindowActivatedEventArgs e)
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated)
                    return;

                _window.Activated -= RestoreMaximizedStateOnce;
                try
                {
                    if (_window._windowState.IsFullscreen) return;

                    var appWindow = _window.AppWindow;
                    if (appWindow.Presenter is OverlappedPresenter overlapped)
                    {
                        overlapped.Maximize();
                    }
                    else
                    {
                        appWindow.SetPresenter(OverlappedPresenter.Create());
                        (appWindow.Presenter as OverlappedPresenter)?.Maximize();
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error restoring maximized state: {ex.Message}");
                }
            }
        }
    }
}
