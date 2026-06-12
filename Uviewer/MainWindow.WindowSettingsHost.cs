using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : IWindowSettingsHost
    {
        AppWindow IWindowSettingsHost.AppWindow => this.AppWindow;
        WindowStateManager IWindowSettingsHost.WindowState => _windowState;
        ImageProcessingViewModel IWindowSettingsHost.ImageOptions => ImageOptions;

        bool IWindowSettingsHost.SharpenEnabled
        {
            get => _sharpenEnabled;
            set => _sharpenEnabled = value;
        }

        bool IWindowSettingsHost.SideBySideMode
        {
            get => _isSideBySideMode;
            set => _isSideBySideMode = value;
        }

        bool IWindowSettingsHost.NextImageOnRight
        {
            get => _nextImageOnRight;
            set => _nextImageOnRight = value;
        }

        ElementTheme IWindowSettingsHost.CurrentTheme => _windowChromeController.CurrentTheme;

        bool IWindowSettingsHost.MatchControlDirection
        {
            get => _matchControlDirection;
            set => _matchControlDirection = value;
        }

        bool IWindowSettingsHost.AllowMultipleInstances
        {
            get => _allowMultipleInstances;
            set => _allowMultipleInstances = value;
        }

        bool IWindowSettingsHost.AutoDoublePageForArchive
        {
            get => _autoDoublePageForArchive;
            set => _autoDoublePageForArchive = value;
        }

        bool IWindowSettingsHost.IsRegistered
        {
            get => _isRegistered;
            set => _isRegistered = value;
        }

        double IWindowSettingsHost.ExplorerThumbnailSize
        {
            get => _explorerThumbnailSize;
            set => _explorerThumbnailSize = System.Math.Clamp(value, 64, 180);
        }

        bool IWindowSettingsHost.ShowFolderThumbnails
        {
            get => _showFolderThumbnails;
            set => _showFolderThumbnails = value;
        }

        string IWindowSettingsHost.ExternalProgramPath
        {
            get => _externalProgramPath;
            set => _externalProgramPath = value ?? string.Empty;
        }

        void IWindowSettingsHost.SetTheme(ElementTheme theme) => SetTheme(theme);

        void IWindowSettingsHost.RestoreMaximizedWhenActivated()
        {
            Activated += RestoreMaximizedStateOnce;
        }

        void IWindowSettingsHost.ApplyInitialWindowSettingsUiState()
        {
            UpdateSharpenButtonState();
            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();

            MainToolbar.SetWindowOptionStates(
                _matchControlDirection,
                _allowMultipleInstances,
                _autoDoublePageForArchive,
                _windowState.IsAlwaysOnTop);
            ApplyThumbnailSettingsToControls();
            MainToolbar.SetExternalProgramPath(_externalProgramPath);

            if (AppWindow.Presenter is OverlappedPresenter op)
            {
                op.IsAlwaysOnTop = _windowState.IsAlwaysOnTop;
            }
        }

        private void RestoreMaximizedStateOnce(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                return;

            Activated -= RestoreMaximizedStateOnce;
            try
            {
                if (_windowState.IsFullscreen) return;

                var appWindow = AppWindow;
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
