using Microsoft.UI.Windowing;
using System;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class WindowSettingsCoordinator
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly IWindowSettingsHost _host;

        public WindowSettingsCoordinator(IWindowSettingsHost host, AppSettingsService appSettingsService)
        {
            _host = host;
            _appSettingsService = appSettingsService;
        }

        public bool ApplyWindowSettings(AppWindow appWindow)
        {
            var primaryArea = DisplayArea.Primary;
            var hasPersistedSettings = _appSettingsService.HasPersistedSettings;
            var settings = _appSettingsService.LoadSettings(primaryArea);

            if (!hasPersistedSettings)
            {
                return false;
            }

            var windowState = _host.WindowState;
            windowState.LastNonMaximizedRect = settings.LastNonMaximizedRect;
            appWindow.MoveAndResize(windowState.LastNonMaximizedRect);

            if (settings.IsMaximized)
            {
               _host.RestoreMaximizedWhenActivated();
            }

            _host.SharpenEnabled = settings.SharpenEnabled;
            _host.SideBySideMode = settings.IsSideBySideMode;
            _host.NextImageOnRight = settings.NextImageOnRight;
            _host.SetTheme(settings.Theme);
            _host.MatchControlDirection = settings.MatchControlDirection;
            _host.AllowMultipleInstances = settings.AllowMultipleInstances;
            windowState.IsSidebarVisible = settings.IsSidebarVisible;
            windowState.IsPinned = settings.IsPinned;
            windowState.IsAlwaysOnTop = settings.IsAlwaysOnTop;
            _host.AutoDoublePageForArchive = settings.AutoDoublePageForArchive;
            _host.IsRegistered = settings.IsRegistered;
            _host.ExplorerThumbnailSize = settings.ExplorerThumbnailSize;
            _host.ShowFolderThumbnails = settings.ShowFolderThumbnails;
            _host.ExternalProgramPath = settings.ExternalProgramPath;
            _host.ToolbarSettings = settings.Toolbar;

            // Sharpen parameters via ImageOptions
            _host.ImageOptions.UpscaleFactor = settings.UpscaleFactor;
            _host.ImageOptions.SharpenAmount = settings.SharpenAmount;
            _host.ImageOptions.SharpenThreshold = settings.SharpenThreshold;
            _host.ImageOptions.UnsharpAmount = settings.UnsharpAmount;
            _host.ImageOptions.UnsharpRadius = settings.UnsharpRadius;

            _host.ApplyInitialWindowSettingsUiState();

            return true;
        }

        public void SaveWindowSettings()
        {
            var appWindow = _host.AppWindow;
            var windowState = _host.WindowState;
            bool isMaximized = false;

            if (windowState.IsFullscreen)
            {
                isMaximized = windowState.WasMaximizedBeforeFullscreen;
            }
            else if (appWindow.Presenter is OverlappedPresenter overlapped)
            {
                windowState.CaptureCurrentRestoreBounds();
                isMaximized = overlapped.State == OverlappedPresenterState.Maximized;
            }

            var settings = new AppSettings
            {
                LastNonMaximizedRect = windowState.LastNonMaximizedRect,
                IsMaximized = isMaximized,
                SharpenEnabled = _host.SharpenEnabled,
                IsSideBySideMode = _host.SideBySideMode,
                NextImageOnRight = _host.NextImageOnRight,
                Theme = _host.CurrentTheme,
                MatchControlDirection = _host.MatchControlDirection,
                AllowMultipleInstances = _host.AllowMultipleInstances,
                IsSidebarVisible = windowState.IsSidebarVisible,
                IsPinned = windowState.IsPinned,
                IsAlwaysOnTop = windowState.IsAlwaysOnTop,
                AutoDoublePageForArchive = _host.AutoDoublePageForArchive,
                IsRegistered = _host.IsRegistered,
                ExplorerThumbnailSize = _host.ExplorerThumbnailSize,
                ShowFolderThumbnails = _host.ShowFolderThumbnails,
                ExternalProgramPath = _host.ExternalProgramPath,
                Toolbar = _host.ToolbarSettings,
                UpscaleFactor = _host.ImageOptions.UpscaleFactor,
                SharpenAmount = _host.ImageOptions.SharpenAmount,
                SharpenThreshold = _host.ImageOptions.SharpenThreshold,
                UnsharpAmount = _host.ImageOptions.UnsharpAmount,
                UnsharpRadius = _host.ImageOptions.UnsharpRadius
            };

            _appSettingsService.SaveSettings(settings);
        }
    }
}
