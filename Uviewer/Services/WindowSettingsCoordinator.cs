using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer.Services
{
    public class WindowSettingsCoordinator
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly MainWindow _mainWindow;

        public WindowSettingsCoordinator(MainWindow mainWindow, AppSettingsService appSettingsService)
        {
            _mainWindow = mainWindow;
            _appSettingsService = appSettingsService;
        }

        public bool ApplyWindowSettings(AppWindow appWindow)
        {
            var primaryArea = DisplayArea.Primary;
            var settings = _appSettingsService.LoadSettings(primaryArea);

            // Check if settings file actually had data
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "window_settings.txt");
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            var windowState = _mainWindow.GetWindowState();
            windowState.LastNonMaximizedRect = settings.LastNonMaximizedRect;
            appWindow.MoveAndResize(windowState.LastNonMaximizedRect);

            if (settings.IsMaximized)
            {
               _mainWindow.Activated += RestoreMaximizedStateOnce;
            }

            _mainWindow.SetSharpenEnabled(settings.SharpenEnabled);
            _mainWindow.SetSideBySideMode(settings.IsSideBySideMode);
            _mainWindow.SetNextImageOnRight(settings.NextImageOnRight);
            _mainWindow.SetTheme(settings.Theme);
            _mainWindow.SetMatchControlDirection(settings.MatchControlDirection);
            _mainWindow.SetAllowMultipleInstances(settings.AllowMultipleInstances);
            windowState.IsSidebarVisible = settings.IsSidebarVisible;
            windowState.IsPinned = settings.IsPinned;
            windowState.IsAlwaysOnTop = settings.IsAlwaysOnTop;
            _mainWindow.SetAutoDoublePageForArchive(settings.AutoDoublePageForArchive);
            _mainWindow.SetIsRegistered(settings.IsRegistered);

            // Sharpen parameters via ImageOptions
            _mainWindow.ImageOptions.UpscaleFactor = settings.UpscaleFactor;
            _mainWindow.ImageOptions.SharpenAmount = settings.SharpenAmount;
            _mainWindow.ImageOptions.SharpenThreshold = settings.SharpenThreshold;
            _mainWindow.ImageOptions.UnsharpAmount = settings.UnsharpAmount;
            _mainWindow.ImageOptions.UnsharpRadius = settings.UnsharpRadius;

            _mainWindow.ApplyInitialUIState();

            return true;
        }

        private void RestoreMaximizedStateOnce(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                return;

            _mainWindow.Activated -= RestoreMaximizedStateOnce;
            try
            {
                var windowState = _mainWindow.GetWindowState();
                if (windowState.IsFullscreen) return;
                
                var appWindow = _mainWindow.AppWindow;
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring maximized state: {ex.Message}");
            }
        }

        public void SaveWindowSettings()
        {
            var appWindow = _mainWindow.AppWindow;
            var windowState = _mainWindow.GetWindowState();
            bool isMaximized = false;

            if (windowState.IsFullscreen)
            {
                isMaximized = windowState.WasMaximizedBeforeFullscreen;
            }
            else if (appWindow.Presenter is OverlappedPresenter overlapped)
            {
                isMaximized = overlapped.State == OverlappedPresenterState.Maximized;
            }

            var settings = new AppSettings
            {
                LastNonMaximizedRect = windowState.LastNonMaximizedRect,
                IsMaximized = isMaximized,
                SharpenEnabled = _mainWindow.IsSharpenEnabled(),
                IsSideBySideMode = _mainWindow.IsSideBySideMode(),
                NextImageOnRight = _mainWindow.IsNextImageOnRight(),
                Theme = _mainWindow.GetCurrentTheme(),
                MatchControlDirection = _mainWindow.IsMatchControlDirection(),
                AllowMultipleInstances = _mainWindow.IsAllowMultipleInstances(),
                IsSidebarVisible = windowState.IsSidebarVisible,
                IsPinned = windowState.IsPinned,
                IsAlwaysOnTop = windowState.IsAlwaysOnTop,
                AutoDoublePageForArchive = _mainWindow.IsAutoDoublePageForArchive(),
                IsRegistered = _mainWindow.IsRegistered(),
                UpscaleFactor = _mainWindow.ImageOptions.UpscaleFactor,
                SharpenAmount = _mainWindow.ImageOptions.SharpenAmount,
                SharpenThreshold = _mainWindow.ImageOptions.SharpenThreshold,
                UnsharpAmount = _mainWindow.ImageOptions.UnsharpAmount,
                UnsharpRadius = _mainWindow.ImageOptions.UnsharpRadius
            };

            _appSettingsService.SaveSettings(settings);
        }
    }
}
