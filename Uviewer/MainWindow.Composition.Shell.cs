using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class ShellComposition
            {
                public static void InitializeToolbar(MainWindow window)
                {
                    window.MainToolbar.ImageOptions = window.ImageOptions;
                    window.HookMainToolbarEvents();
                    window.HookExtractedControlEvents();
                }

                public static void InitializeWindowShell(MainWindow window)
                {
                    window.Title = "Uviewer - Image & Text Viewer";
                    window.ExtendsContentIntoTitleBar = true;
                    window.SetTitleBar(window.AppTitleBar);

                    try
                    {
                        var appWindow = window.AppWindow;
                        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Uviewer.ico");
                        if (!File.Exists(iconPath))
                        {
                            iconPath = Path.Combine(AppContext.BaseDirectory, "Uviewer.ico");
                        }

                        if (File.Exists(iconPath))
                        {
                            appWindow.SetIcon(iconPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting window icon: {ex.Message}");
                    }
                }

                public static void InitializeWindowControllers(MainWindow window)
                {
                    window._windowState = new WindowStateManager(window);
                    var appWindow = window.AppWindow;
                    appWindow.Changed += window.AppWindow_Changed;

                    window._overlayManager = new FullscreenOverlayManager();
                    window._overlayManager.Initialize(window.DispatcherQueue);

                    window._windowChromeController = new WindowChromeController(
                        window,
                        window.RootGrid,
                        window.AppTitleBar,
                        window.MainToolbar,
                        window.StatusBarGrid,
                        window.SidebarGrid,
                        window.SplitterGrid,
                        window.SidebarColumn,
                        window._windowState,
                        window._overlayManager,
                        () => window._windowSettingsCoordinator.SaveWindowSettings(),
                        window.InvalidateThemeTargets);

                    window._overlayManager.HideToolbarRequested += (s, e) => window._windowChromeController.HideToolbarUI();
                    window._overlayManager.HideSidebarRequested += (s, e) => window._windowChromeController.HideSidebarUI();
                }

                public static void InitializeWindowSettings(MainWindow window)
                {
                    var appWindow = window.AppWindow;
                    window._windowSettingsCoordinator = new WindowSettingsCoordinator(window, window._appSettingsService);
                    appWindow.Closing += window.AppWindow_Closing;
                }

                public static void InitializeExplorerAndBookmarks(MainWindow window)
                {
                    window._explorerController = new ExplorerController(window._explorerState, window._thumbnailService, window.DispatcherQueue);
                    window._bookmarkPanelController = new BookmarkPanelController(window._bookmarkPanelState, window._favoritesService, window._recentService);
                    window._favoritesController = new FavoritesController(window._favoritesService, window._bookmarkPanelController);
                }

                public static void ApplyInitialWindowLayout(MainWindow window)
                {
                    var appWindow = window.AppWindow;
                    bool hasLoadedSettings = window._windowSettingsCoordinator.ApplyWindowSettings(appWindow);
                    if (!hasLoadedSettings)
                    {
                        var primaryArea = DisplayArea.Primary;
                        var defaultSize = new Windows.Graphics.SizeInt32(1200, 800);
                        appWindow.Resize(defaultSize);

                        var centerX = (primaryArea.WorkArea.Width - defaultSize.Width) / 2;
                        var centerY = (primaryArea.WorkArea.Height - defaultSize.Height) / 2;
                        appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));

                        window._windowState.LastNonMaximizedRect =
                            new Windows.Graphics.RectInt32(centerX, centerY, defaultSize.Width, defaultSize.Height);
                    }

                    window.UpdateSideBySideButtonState();
                    window.UpdateNextImageSideButtonState();
                    window.UpdateSharpenButtonState();
                    window._windowChromeController.ApplyInitialChromeState();
                }

                public static void InitializeRootInput(MainWindow window)
                {
                    if (window.Content is FrameworkElement fe)
                    {
                        fe.PreviewKeyDown += async (s, e) =>
                            await window._keyboardShortcutService.HandlePreviewKeyDownAsync(s, e, window);
                        fe.KeyDown += async (s, e) =>
                            await window._keyboardShortcutService.HandleKeyDownAsync(s, e, window);
                    }
                }

                public static void InitializeExplorerLists(MainWindow window)
                {
                    window.FileListView.ItemsSource = window._fileItems;
                    window.FileGridView.ItemsSource = window._fileItems;
                }

                public static void WireLifecycleEvents(MainWindow window, string? launchFilePath)
                {
                    window.RootGrid.Loaded += async (s, e) =>
                    {
                        window._windowChromeController.UpdateTitleBarColors();
                        window.RootGrid.Focus(FocusState.Programmatic);
                        await Task.Delay(50);
                        WebDavService.CleanupTempFiles();
                        await window.InitializeAsync(launchFilePath);
                    };

                    window.Activated += (s, e) =>
                    {
                        if (e.WindowActivationState != WindowActivationState.Deactivated)
                        {
                            window.RootGrid.Focus(FocusState.Programmatic);
                        }
                    };

                    window.Closed += async (s, e) =>
                    {
                        window._isWindowClosing = true;
                        bool wasPdfOpen = window._currentPdfDocument != null;
                        await window._shutdownCoordinator.ShutdownAsync(window.CreateShutdownContext(wasPdfOpen));
                    };
                }

                public static void InitializeNotificationTimer(MainWindow window)
                {
                    window._notificationTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                    window._notificationTimer.Interval = TimeSpan.FromSeconds(2);
                    window._notificationTimer.IsRepeating = false;
                    window._notificationTimer.Tick += (s, e) =>
                    {
                        window.NotificationOverlay.Visibility = Visibility.Collapsed;
                    };
                }
            }
        }
    }
}
