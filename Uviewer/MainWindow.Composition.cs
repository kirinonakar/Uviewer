using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static class MainWindowComposition
        {
            public static void Initialize(MainWindow window, string? launchFilePath)
            {
                window.ApplyCoreServices(CreateCoreServices());

                window.InitializeComponent();
                InitializeControllers(window);
                InitializeToolbarAndSearch(window);
                InitializeDocumentOpenCoordinators(window);

                window.RootGrid.SizeChanged += window.RootGrid_SizeChanged;

                try
                {
                    InitializeWindowShell(window);
                    InitializeWindowControllers(window);
                    InitializeDocumentNavigation(window);
                    InitializeWindowSettings(window);
                    InitializeExplorerAndBookmarks(window);
                    ApplyInitialWindowLayout(window);
                    InitializeRootInput(window);
                    InitializeExplorerLists(window);
                    InitializeImagePipeline(window);

                    window.ApplyLocalization();
                    window.MainToolbar.SetExternalProgramPath(window._externalProgramPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error initializing MainWindow: {ex.Message}");
                }

                WireLifecycleEvents(window, launchFilePath);
                InitializeNotificationTimer(window);
                WireImageOptions(window);
            }

            private static MainWindowServices CreateCoreServices()
            {
                var sharpening = new SharpeningService();

                return new MainWindowServices(
                    new AppSettingsService(),
                    new ZoomService(),
                    sharpening,
                    new ThumbnailService(),
                    new ImageStatusBarService(),
                    new SideBySideImageLoadService(),
                    new KeyboardShortcutService(),
                    new TocService(),
                    new DocumentSearchService(),
                    new SearchHighlightService(),
                    new DocumentSearchCoordinatorService(),
                    new Uviewer.Models.DocumentSearchState(),
                    new DocumentSessionTracker(),
                    new ImageResourceService(sharpening),
                    new ShutdownCoordinator(),
                    new SevenZipExtractionCoordinator(),
                    new ArchiveSession(),
                    new FavoritesService(),
                    new RecentService());
            }

            private static void InitializeControllers(MainWindow window)
            {
                window._documentReaderController = new DocumentReaderController(window);
                window._epubReaderController = new EpubReaderController(window);
                window._imageViewerController = new ImageViewerController(window);
                window._imageViewportNavigationService = new ImageViewportNavigationService(
                    window.DispatcherQueue,
                    window.RerenderPdfCurrentPageAsync);
            }

            private static void InitializeToolbarAndSearch(MainWindow window)
            {
                window.MainToolbar.ImageOptions = window.ImageOptions;
                window.HookMainToolbarEvents();
                window.HookExtractedControlEvents();
                window._searchOverlayService = new SearchOverlayService(
                    window.SearchCurrentDocumentAsync,
                    window.NavigateToSearchMatchAsync,
                    window.GetCurrentSearchPosition,
                    window.SetActiveSearchQuery);
                window.LoadTextSettings();
            }

            private static void InitializeDocumentOpenCoordinators(MainWindow window)
            {
                window._localDocumentOpenCoordinator = new LocalDocumentOpenCoordinator(new LocalDocumentOpenHandlers
                {
                    OpenArchiveAsync = window.LoadImagesFromArchiveAsync,
                    OpenPdfAsync = window.LoadImagesFromPdfAsync,
                    OpenStorageFileAsync = window.LoadImageFromFileAsync,
                    OpenFolderAsync = window.LoadImagesFromFolderAsync,
                    SaveCurrentPositionAsync = () => window.AddToRecentAsync(true),
                    LoadExplorerFolder = window.LoadExplorerFolder,
                    LoadExplorerFolderInBackground = window.LoadExplorerFolderInBackground,
                    ShouldLoadExplorerFolder = folderPath =>
                        !string.Equals(folderPath, window._currentExplorerPath, StringComparison.OrdinalIgnoreCase),
                    HideEmptyState = () =>
                    {
                        if (window.EmptyStatePanel != null) window.EmptyStatePanel.Visibility = Visibility.Collapsed;
                    }
                });

                window._webDavDocumentOpenCoordinator = new WebDavDocumentOpenCoordinator(new WebDavDocumentOpenHandlers
                {
                    LoadFolderAsync = window.LoadWebDavFolderAsync,
                    CloseCurrentPdfAsync = window.CloseCurrentPdfAsync,
                    CloseCurrentEpubAsync = window.CloseCurrentEpubAsync,
                    CloseCurrentArchiveAsync = window.CloseCurrentArchiveAsync,
                    SetCurrentItemPath = path => window._currentWebDavItemPath = path,
                    ClearImageResources = window.ClearImageResources,
                    SetStatusText = text => window.FileNameText.Text = text,
                    CreateLoadingStatus = name => name + Strings.Loading,
                    CreateDownloadFailedStatus = () => "다운로드 실패",
                    CreateFileOpenFailedStatus = ex => $"파일 열기 실패: {ex.Message}",
                    CreateArchiveOpenFailedStatus = ex => $"압축 파일 열기 실패: {ex.Message}",
                    RestartOperation = window._webDavState.RestartOperation,
                    DownloadToTempFileAsync = window._webDavService.DownloadToTempFileAsync,
                    DownloadFileAsync = window._webDavService.DownloadFileAsync,
                    OpenLocalArchiveAsync = window.LoadImagesFromArchiveAsync,
                    OpenLocalPdfAsync = window.LoadImagesFromPdfAsync,
                    PrepareSequentialEntries = window.PrepareWebDavSequentialEntries,
                    OpenEpubFileAsync = window.LoadEpubFileAsync,
                    DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                    StartPreload = window.StartWebDavPreload,
                    OpenArchiveStreamAsync = window.OpenWebDavArchiveStreamAsync,
                    Log = message => System.Diagnostics.Debug.WriteLine(message)
                });
            }

            private static void InitializeWindowShell(MainWindow window)
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

            private static void InitializeWindowControllers(MainWindow window)
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

            private static void InitializeDocumentNavigation(MainWindow window)
            {
                window._documentNavigationCoordinator = new DocumentNavigationCoordinator(new DocumentNavigationHandlers
                {
                    IsVerticalMode = () => window._isVerticalMode,
                    IsEpubMode = () => window._isEpubMode,
                    IsTextMode = () => window._isTextMode,
                    IsAozoraMode = () => window._isAozoraMode,
                    NavigateVerticalPage = window.NavigateVerticalPage,
                    NavigateEpubAsync = window.NavigateEpubAsync,
                    NavigateAozoraPage = window.NavigateAozoraPage,
                    NavigateTextPage = window.NavigateTextPage,
                    NavigatePreviousImageAsync = () => window.NavigateToPreviousAsync(),
                    NavigateNextImageAsync = () => window.NavigateToNextAsync()
                });

                window._fastNavigationService = new FastNavigationService(window.DispatcherQueue);
                window._imageNavigationCoordinator = new ImageNavigationCoordinator(new ImageNavigationHandlers
                {
                    GetImageEntries = () => window._imageEntries,
                    GetCurrentIndex = () => window._currentIndex,
                    SetCurrentIndex = value => window._currentIndex = value,
                    IsCurrentViewSideBySide = () => window._isCurrentViewSideBySide,
                    SetScrollDirection = value => window._imageViewportNavigationService.ScrollDirection = value,
                    FastNavigationService = window._fastNavigationService,
                    ResetFastNavigationAsync = window.ResetFastNavigation,
                    UpdateFastNavigationUi = window.UpdateFastNavigationUI,
                    DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                    SaveCurrentPositionAsync = () => window.AddToRecentAsync(true),
                    ShouldPreloadAfterNavigate = () => window._archiveSession.CurrentArchive != null || window._currentPdfDocument != null,
                    StartPreload = window.StartImagePreload,
                    FocusViewer = () => window.RootGrid.Focus(FocusState.Programmatic)
                });

                window._animatedWebpService = new AnimatedWebpService(window._sharpeningService, window.DispatcherQueue);
                window._animatedWebpService.FrameUpdated += window.OnAnimatedWebpFrameUpdated;
                window._animatedWebpService.AnimationStopped += window.OnAnimatedWebpAnimationStopped;
            }

            private static void InitializeWindowSettings(MainWindow window)
            {
                var appWindow = window.AppWindow;
                window._windowSettingsCoordinator = new WindowSettingsCoordinator(window, window._appSettingsService);
                appWindow.Closing += window.AppWindow_Closing;
            }

            private static void InitializeExplorerAndBookmarks(MainWindow window)
            {
                window._explorerController = new ExplorerController(window._explorerState, window._thumbnailService, window.DispatcherQueue);
                window._bookmarkPanelController = new BookmarkPanelController(window._bookmarkPanelState, window._favoritesService, window._recentService);
                window._favoritesController = new FavoritesController(window._favoritesService, window._bookmarkPanelController);
            }

            private static void ApplyInitialWindowLayout(MainWindow window)
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

            private static void InitializeRootInput(MainWindow window)
            {
                if (window.Content is FrameworkElement fe)
                {
                    fe.PreviewKeyDown += async (s, e) =>
                        await window._keyboardShortcutService.HandlePreviewKeyDownAsync(s, e, window);
                    fe.KeyDown += async (s, e) =>
                        await window._keyboardShortcutService.HandleKeyDownAsync(s, e, window);
                }
            }

            private static void InitializeExplorerLists(MainWindow window)
            {
                window.FileListView.ItemsSource = window._fileItems;
                window.FileGridView.ItemsSource = window._fileItems;
            }

            private static void InitializeImagePipeline(MainWindow window)
            {
                window._imageCache = new ImageCacheManager(window.DispatcherQueue);
                window._preloadManager = new PreloadManager(window._imageCache, window.DispatcherQueue);
                window._imageBitmapLoader = new ImageBitmapLoader(window._imageCache, window._sharpeningService, window.DispatcherQueue);
                window._imageDoublePageDecisionService = new ImageDoublePageDecisionService(window._imageCache);
            }

            private static void WireLifecycleEvents(MainWindow window, string? launchFilePath)
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

            private static void InitializeNotificationTimer(MainWindow window)
            {
                window._notificationTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                window._notificationTimer.Interval = TimeSpan.FromSeconds(2);
                window._notificationTimer.IsRepeating = false;
                window._notificationTimer.Tick += (s, e) =>
                {
                    window.NotificationOverlay.Visibility = Visibility.Collapsed;
                };
            }

            private static void WireImageOptions(MainWindow window)
            {
                window.ImageOptions.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null && !e.PropertyName.EndsWith("Text"))
                    {
                        window.OnSharpenParamsChanged();
                    }
                };
            }
        }

        private void ApplyCoreServices(MainWindowServices services)
        {
            _appSettingsService = services.AppSettings;
            _zoomService = services.Zoom;
            _sharpeningService = services.Sharpening;
            _thumbnailService = services.Thumbnail;
            _imageStatusBarService = services.ImageStatusBar;
            _sideBySideImageLoadService = services.SideBySideImageLoad;
            _keyboardShortcutService = services.KeyboardShortcut;
            _tocService = services.Toc;
            _documentSearchService = services.DocumentSearch;
            _searchHighlightService = services.SearchHighlight;
            _documentSearchCoordinatorService = services.DocumentSearchCoordinator;
            _documentSearchState = services.DocumentSearchState;
            _documentSessionTracker = services.DocumentSessionTracker;
            _imageResourceService = services.ImageResource;
            _shutdownCoordinator = services.Shutdown;
            _sevenZipExtraction = services.SevenZipExtraction;
            _archiveSession = services.ArchiveSession;
            _favoritesService = services.Favorites;
            _recentService = services.Recent;
        }
    }
}
