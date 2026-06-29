using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Renderers;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window, IKeyboardShortcutActions
    {
        // Image preloading for faster navigation
        private Services.ImageCacheManager _imageCache = null!;
        private WindowStateManager _windowState = null!;
        private readonly Services.IThumbnailService _thumbnailService = new Services.ThumbnailService();
        private Services.PreloadManager _preloadManager = null!;
        private Services.ImageBitmapLoader _imageBitmapLoader = null!;
        private Services.ImageDoublePageDecisionService _imageDoublePageDecisionService = null!;
        private readonly Services.ImageStatusBarService _imageStatusBarService = new();
        private readonly Services.SideBySideImageLoadService _sideBySideImageLoadService = new();
        private Services.ImageViewportNavigationService _imageViewportNavigationService = null!;

        // Refactored Services
        private Services.WindowSettingsCoordinator _windowSettingsCoordinator = null!;
        private Services.ExplorerController _explorerController = null!;
        private Services.BookmarkPanelController _bookmarkPanelController = null!;
        private Services.FavoritesController _favoritesController = null!;
        private readonly Services.AppSettingsService _appSettingsService = new();
        private readonly Services.ZoomService _zoomService = new();
        private readonly Services.ISharpeningService _sharpeningService = new Services.SharpeningService();
        private Services.FastNavigationService _fastNavigationService = null!;
        private readonly IAnimatedWebpService _animatedWebpService = null!;
        private readonly IKeyboardShortcutService _keyboardShortcutService = new KeyboardShortcutService();
        private readonly Services.TocService _tocService = new();
        private readonly Services.DocumentSearchService _documentSearchService = new();
        private readonly Services.SearchHighlightService _searchHighlightService = new();
        private readonly Services.DocumentSearchCoordinatorService _documentSearchCoordinatorService = new();
        private Services.SearchOverlayService _searchOverlayService = null!;
        private readonly DocumentSearchState _documentSearchState = new();
        private readonly Services.DocumentSessionTracker _documentSessionTracker = new();
        private string? _activeSearchQuery => _documentSearchState.Query;
        private IReadOnlyList<PdfSearchHighlight> _activePdfSearchHighlights => _documentSearchState.PdfHighlights;
        private int _activePdfSearchPageIndex => _documentSearchState.PdfPageIndex;
        private int _activePdfSearchMatchIndex => _documentSearchState.PdfMatchIndex;
        private readonly Services.ImageResourceService _imageResourceService;
        private bool _isWindowClosing;
        private readonly Services.ShutdownCoordinator _shutdownCoordinator = new();
        private Services.LocalDocumentOpenCoordinator _localDocumentOpenCoordinator = null!;
        private Services.WebDavDocumentOpenCoordinator _webDavDocumentOpenCoordinator = null!;
        private Services.DocumentNavigationCoordinator _documentNavigationCoordinator = null!;
        private Services.ImageNavigationCoordinator _imageNavigationCoordinator = null!;

        // ImageResourceService를 _sharpeningService 다음에 생성해야 하므로
        // 필드 초기화 식 대신 생성자 내부에서 초기화합니다.

        // Loading and navigation state
        private CancellationTokenSource? _imageLoadingCts;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private readonly Services.SevenZipExtractionCoordinator _sevenZipExtraction = new();

        private void Signal7zJump()
        {
            _sevenZipExtraction.SignalJump(_currentIndex);
        }





        private Services.FavoritesService _favoritesService = new();
        private Services.RecentService _recentService = new();
        private bool _isColorPickerOpen;
        private Microsoft.UI.Xaml.Controls.ContentDialog? _aboutDialog;

        public async Task InitializeAsync(string? launchFilePath = null)
        {
            try
            {
                // Always load metadata first to prevent race conditions and data loss
                await _favoritesService.LoadFavoritesAsync();
                await _recentService.LoadRecentItemsAsync();
                UpdateFavoritesMenu();
                UpdateRecentMenu();

                // Clean up any stale/incomplete temp files at startup
                _sevenZipExtraction.CleanupZeroByteTempFiles();

                if (!string.IsNullOrEmpty(launchFilePath))
                {
                    // [Priority] Process launch path
                    await ProcessLaunchPathAsync(launchFilePath);
                }
                else
                {
                    // Load Pictures folder by default if no path provided
                    LoadExplorerFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Initialization Error: {ex}");
                if (FileNameText != null) FileNameText.Text = $"Error: {ex.Message}";
                MessageBox(IntPtr.Zero, $"Initialization Error:\n{ex.Message}\n{ex.StackTrace}", "Uviewer Init Error", 0x10);
            }
        }



        public async Task HandleNewInstanceFile(string? filePath)
        {
            try
            {
                // Bring window to front
                var appWindow = this.AppWindow;
                appWindow.Show();
                if (appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    if (overlapped.State == OverlappedPresenterState.Minimized)
                    {
                        overlapped.Restore();
                    }
                }

                // Re-activate window
                this.Activate();

                if (!string.IsNullOrEmpty(filePath))
                {
                    await ProcessLaunchPathAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling new instance file: {ex.Message}");
            }
        }

        private async Task ProcessLaunchPathAsync(string launchFilePath)
        {
            try
            {
                await _localDocumentOpenCoordinator.OpenLaunchPathAsync(launchFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing launch path: {ex.Message}");
            }
        }

        private void LoadExplorerFolderInBackground(string folderPath)
        {
            _ = Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() => LoadExplorerFolder(folderPath));
            });
        }

        public MainWindow(string? launchFilePath = null)
        {
            // _imageResourceService는 _sharpeningService에 의존하므로 생성자 시작 시 초기화
            _imageResourceService = new Services.ImageResourceService(_sharpeningService);

            InitializeComponent();
            _documentReaderController = new DocumentReaderController(this);
            _epubReaderController = new Services.EpubReaderController(this);
            _imageViewerController = new Services.ImageViewerController(this);
            _imageViewportNavigationService = new Services.ImageViewportNavigationService(
                DispatcherQueue,
                RerenderPdfCurrentPageAsync);
            MainToolbar.ImageOptions = ImageOptions;
            HookMainToolbarEvents();
            HookExtractedControlEvents();
            _searchOverlayService = new Services.SearchOverlayService(
                SearchCurrentDocumentAsync,
                NavigateToSearchMatchAsync,
                GetCurrentSearchPosition,
                SetActiveSearchQuery);
            LoadTextSettings();
            _documentNavigationCoordinator = new Services.DocumentNavigationCoordinator(new Services.DocumentNavigationHandlers
            {
                IsVerticalMode = () => _isVerticalMode,
                IsEpubMode = () => _isEpubMode,
                IsTextMode = () => _isTextMode,
                IsAozoraMode = () => _isAozoraMode,
                NavigateVerticalPage = NavigateVerticalPage,
                NavigateEpubAsync = NavigateEpubAsync,
                NavigateAozoraPage = NavigateAozoraPage,
                NavigateTextPage = NavigateTextPage,
                NavigatePreviousImageAsync = () => NavigateToPreviousAsync(),
                NavigateNextImageAsync = () => NavigateToNextAsync()
            });
            _localDocumentOpenCoordinator = new Services.LocalDocumentOpenCoordinator(new Services.LocalDocumentOpenHandlers
            {
                OpenArchiveAsync = LoadImagesFromArchiveAsync,
                OpenPdfAsync = LoadImagesFromPdfAsync,
                OpenStorageFileAsync = LoadImageFromFileAsync,
                OpenFolderAsync = LoadImagesFromFolderAsync,
                SaveCurrentPositionAsync = () => AddToRecentAsync(true),
                LoadExplorerFolder = LoadExplorerFolder,
                LoadExplorerFolderInBackground = LoadExplorerFolderInBackground,
                ShouldLoadExplorerFolder = folderPath =>
                    !string.Equals(folderPath, _currentExplorerPath, StringComparison.OrdinalIgnoreCase),
                HideEmptyState = () =>
                {
                    if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
                }
            });
            _webDavDocumentOpenCoordinator = new Services.WebDavDocumentOpenCoordinator(new Services.WebDavDocumentOpenHandlers
            {
                LoadFolderAsync = LoadWebDavFolderAsync,
                CloseCurrentPdfAsync = CloseCurrentPdfAsync,
                CloseCurrentEpubAsync = CloseCurrentEpubAsync,
                CloseCurrentArchiveAsync = CloseCurrentArchiveAsync,
                SetCurrentItemPath = path => _currentWebDavItemPath = path,
                ClearImageResources = ClearImageResources,
                SetStatusText = text => FileNameText.Text = text,
                CreateLoadingStatus = name => name + Strings.Loading,
                CreateDownloadFailedStatus = () => "다운로드 실패",
                CreateFileOpenFailedStatus = ex => $"파일 열기 실패: {ex.Message}",
                CreateArchiveOpenFailedStatus = ex => $"압축 파일 열기 실패: {ex.Message}",
                RestartOperation = _webDavState.RestartOperation,
                DownloadToTempFileAsync = _webDavService.DownloadToTempFileAsync,
                DownloadFileAsync = _webDavService.DownloadFileAsync,
                OpenLocalArchiveAsync = LoadImagesFromArchiveAsync,
                OpenLocalPdfAsync = LoadImagesFromPdfAsync,
                PrepareSequentialEntries = PrepareWebDavSequentialEntries,
                OpenEpubFileAsync = LoadEpubFileAsync,
                DisplayCurrentImageAsync = DisplayCurrentImageAsync,
                StartPreload = StartWebDavPreload,
                OpenArchiveStreamAsync = OpenWebDavArchiveStreamAsync,
                Log = message => System.Diagnostics.Debug.WriteLine(message)
            });

            // [추가] UI 크기 변경 이벤트 구독
            RootGrid.SizeChanged += RootGrid_SizeChanged;

            try
            {
                // Set window title
                Title = "Uviewer - Image & Text Viewer";

                // Custom Title Bar
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);

                // Set window icon (with fallback for single-file publish)
                try
                {
                    var appWindow = this.AppWindow;
                    var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Uviewer.ico");
                    if (!File.Exists(iconPath))
                    {
                        // Try alternative path for published apps
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

                _windowState = new WindowStateManager(this);

                // Load saved window position, size and maximized state
                var appWindow2 = this.AppWindow;
                appWindow2.Changed += AppWindow_Changed;

                _overlayManager = new FullscreenOverlayManager();
                _overlayManager.Initialize(DispatcherQueue);
                _windowChromeController = new Services.WindowChromeController(
                    this,
                    RootGrid,
                    AppTitleBar,
                    MainToolbar,
                    StatusBarGrid,
                    SidebarGrid,
                    SplitterGrid,
                    SidebarColumn,
                    _windowState,
                    _overlayManager,
                    () => _windowSettingsCoordinator.SaveWindowSettings(),
                    InvalidateThemeTargets);
                _overlayManager.HideToolbarRequested += (s, e) => _windowChromeController.HideToolbarUI();
                _overlayManager.HideSidebarRequested += (s, e) => _windowChromeController.HideSidebarUI();

                _fastNavigationService = new Services.FastNavigationService(DispatcherQueue);
                _imageNavigationCoordinator = new Services.ImageNavigationCoordinator(new Services.ImageNavigationHandlers
                {
                    GetImageEntries = () => _imageEntries,
                    GetCurrentIndex = () => _currentIndex,
                    SetCurrentIndex = value => _currentIndex = value,
                    IsCurrentViewSideBySide = () => _isCurrentViewSideBySide,
                    SetScrollDirection = value => _imageViewportNavigationService.ScrollDirection = value,
                    FastNavigationService = _fastNavigationService,
                    ResetFastNavigationAsync = ResetFastNavigation,
                    UpdateFastNavigationUi = UpdateFastNavigationUI,
                    DisplayCurrentImageAsync = DisplayCurrentImageAsync,
                    SaveCurrentPositionAsync = () => AddToRecentAsync(true),
                    ShouldPreloadAfterNavigate = () => _archiveSession.CurrentArchive != null || _currentPdfDocument != null,
                    StartPreload = StartImagePreload,
                    FocusViewer = () => RootGrid.Focus(FocusState.Programmatic)
                });
                _animatedWebpService = new Services.AnimatedWebpService(_sharpeningService, DispatcherQueue);
                _animatedWebpService.FrameUpdated += OnAnimatedWebpFrameUpdated;
                _animatedWebpService.AnimationStopped += OnAnimatedWebpAnimationStopped;

                _windowSettingsCoordinator = new Services.WindowSettingsCoordinator(this, _appSettingsService);
                appWindow2.Closing += AppWindow_Closing;
                _explorerController = new Services.ExplorerController(_explorerState, _thumbnailService, DispatcherQueue);
                _bookmarkPanelController = new Services.BookmarkPanelController(_bookmarkPanelState, _favoritesService, _recentService);
                _favoritesController = new Services.FavoritesController(_favoritesService, _bookmarkPanelController);
                
                // Load saved window position, size and maximized state
                bool hasLoadedSettings = _windowSettingsCoordinator.ApplyWindowSettings(appWindow2);
                if (!hasLoadedSettings)
                {
                    // 설정 파일이 없으면 기본 사이즈 적용 및 중앙 정렬
                    var primaryArea = Microsoft.UI.Windowing.DisplayArea.Primary;
                    var defaultSize = new Windows.Graphics.SizeInt32(1200, 800);
                    appWindow2.Resize(defaultSize);
                    
                    var centerX = (primaryArea.WorkArea.Width - defaultSize.Width) / 2;
                    var centerY = (primaryArea.WorkArea.Height - defaultSize.Height) / 2;
                    appWindow2.Move(new Windows.Graphics.PointInt32(centerX, centerY));

                    // 현재 위치와 크기를 초기값으로 저장
                    _windowState.LastNonMaximizedRect = new Windows.Graphics.RectInt32(centerX, centerY, defaultSize.Width, defaultSize.Height);
                }

                // Initialize button states
                UpdateSideBySideButtonState();
                UpdateNextImageSideButtonState();
                UpdateSharpenButtonState();

                _windowChromeController.ApplyInitialChromeState();

                // Enable keyboard shortcuts on the root content to ensure they catch everything
                if (this.Content is FrameworkElement fe)
                {
                    fe.PreviewKeyDown += async (s, e) => await _keyboardShortcutService.HandlePreviewKeyDownAsync(s, e, this);
                    fe.KeyDown += async (s, e) => await _keyboardShortcutService.HandleKeyDownAsync(s, e, this);
                }

                // Initialize file list
                FileListView.ItemsSource = _fileItems;
                FileGridView.ItemsSource = _fileItems;

                _imageCache = new Services.ImageCacheManager(DispatcherQueue);
                _preloadManager = new Services.PreloadManager(_imageCache, DispatcherQueue);
                _imageBitmapLoader = new Services.ImageBitmapLoader(_imageCache, _sharpeningService, DispatcherQueue);
                _imageDoublePageDecisionService = new Services.ImageDoublePageDecisionService(_imageCache);

                // Apply Localization
                ApplyLocalization();
                MainToolbar.SetExternalProgramPath(_externalProgramPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing MainWindow: {ex.Message}");
            }

            // 화면 UI(RootGrid)가 로드된 후에 초기화 작업을 시작합니다.
            RootGrid.Loaded += async (s, e) =>
            {
                _windowChromeController.UpdateTitleBarColors();
                RootGrid.Focus(FocusState.Programmatic);
                // Win2D 캔버스 디바이스가 초기화될 시간을 아주 잠깐 확보 (안전장치)
                await Task.Delay(50);
                // 이전 비정상 종료 시 남은 WebDAV 임시 파일 정리
                WebDavService.CleanupTempFiles();
                await InitializeAsync(launchFilePath);
            };

            this.Activated += (s, e) =>
            {
                if (e.WindowActivationState != WindowActivationState.Deactivated)
                {
                    // Ensure focus is restored to the root grid on activation
                    RootGrid.Focus(FocusState.Programmatic);
                }
            };


            // Subscribe to window closed event to save settings
            this.Closed += async (s, e) =>
            {
                _isWindowClosing = true;
                bool wasPdfOpen = _currentPdfDocument != null;
                await _shutdownCoordinator.ShutdownAsync(CreateShutdownContext(wasPdfOpen));
            };

            // Initialize notification timer
            _notificationTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _notificationTimer.Interval = TimeSpan.FromSeconds(2);
            _notificationTimer.IsRepeating = false;
            _notificationTimer.Tick += (s, e) =>
            {
                NotificationOverlay.Visibility = Visibility.Collapsed;
            };

            // Subscribe to sharpening parameter changes
            ImageOptions.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null && !e.PropertyName.EndsWith("Text"))
                {
                    OnSharpenParamsChanged();
                }
            };
        }

        private Services.ShutdownContext CreateShutdownContext(bool wasPdfOpen) => new()
        {
            WasPdfOpen = wasPdfOpen,
            SaveCurrentPositionAsync = () => AddToRecentAsync(true),
            SaveWindowSettings = SaveWindowSettingsForShutdown,
            StopNotificationTimer = () => _notificationTimer?.Stop(),
            StopVerticalResizeTimer = () => _documentReaderController.StopVerticalResizeTimer(),
            StopOverlayTimers = () => _overlayManager?.StopAll(),
            PreloadManager = _preloadManager,
            SearchOverlayService = _searchOverlayService,
            ImageLoadingCts = _imageLoadingCts,
            TextReaderState = _textReaderState,
            DocumentSearchState = _documentSearchState,
            ShutdownPdfResources = ShutdownPdfResources,
            ShutdownEpubResources = ShutdownEpubResources,
            FastNavigationService = _fastNavigationService,
            ImageViewportNavigationService = _imageViewportNavigationService,
            ArchiveSession = _archiveSession,
            EpubSession = _epubSession,
            ImageViewerState = _imageViewerState,
            ImageCache = _imageCache,
            ImageEntries = _imageEntries,
            SevenZipExtraction = _sevenZipExtraction,
            CleanupWebDavTempFiles = WebDavService.CleanupTempFiles,
            RecentService = _recentService,
            FavoritesService = _favoritesService,
            WebDavService = _webDavService,
            WebDavState = _webDavState,
            AnimatedWebpService = _animatedWebpService,
            RequestApplicationExit = () => Application.Current?.Exit()
        };

        public void ShowNotification(string message, string icon = "\uE735", string color = "Gold")
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NotificationText.Text = message;
                NotificationIcon.Glyph = icon;

                if (color == "Red")
                    NotificationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                else if (color == "Gold")
                    NotificationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gold);
                else
                    NotificationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

                NotificationOverlay.Visibility = Visibility.Visible;
                _notificationTimer?.Stop();
                _notificationTimer?.Start();
            });
        }

        private void ApplyLocalization()
        {
            MainToolbar.ApplyLocalization();

            // Tooltips
            UpdateToggleViewButtonTooltip();
            ToolTipService.SetToolTip(ParentFolderButton, Strings.ParentFolderTooltip);
            ToolTipService.SetToolTip(SidebarFavoritesButton, Strings.FavoritesTooltip);
            ToolTipService.SetToolTip(SidebarRecentButton, Strings.RecentTooltip);
            ToolTipService.SetToolTip(BrowseFolderButton, Strings.BrowseFolderTooltip);
            ToolTipService.SetToolTip(WebDavButton, Strings.WebDavTooltip);
            ToolTipService.SetToolTip(FileNameText, Strings.ExifStatusBarTooltip);
            if (AddWebDavButton != null) AddWebDavButton.Content = Strings.AddWebDavServer;
            UpdateThemeToggleButtonTooltip();
            _searchOverlayService?.ApplyLocalization();

            // Texts
            // Only show placeholders when nothing is opened yet.
            // If a folder/WebDAV path or file is already active, keep the current text.
            if (string.IsNullOrEmpty(_currentExplorerPath) && string.IsNullOrEmpty(_currentWebDavPath))
            {
                CurrentPathText.Text = Strings.CurrentPathPlaceholder;
            }

            if ((_imageEntries == null || _imageEntries.Count == 0) || _currentIndex < 0)
            {
                FileNameText.Text = Strings.FileSelectPlaceholder;
            }

            // Empty State
            if (EmptyStatePanel.Children.Count >= 3)
            {
                if (EmptyStatePanel.Children[1] is TextBlock tb1) tb1.Text = Strings.EmptyStateDrag;
                if (EmptyStatePanel.Children[2] is TextBlock tb2) tb2.Text = Strings.EmptyStateClick;
                if (EmptyStatePanel.Children.Count >= 4 && EmptyStatePanel.Children[3] is Button btn) btn.Content = Strings.EmptyStateButton;
            }

            // Overlay Texts
            FastNavText.Text = Strings.FastNavText;
            TextFastNavText.Text = Strings.TextFastNavText;

            // Menus
            if (SidebarAddToFavoritesButton != null) SidebarAddToFavoritesButton.Content = Strings.AddToFavorites;
            InitializeExplorerContextMenus();

            // Favorites Pivot Headers
            if (SidebarFileFavoritesPivotItem != null) SidebarFileFavoritesPivotItem.Header = Strings.FavoritesFiles;
            if (SidebarFolderFavoritesPivotItem != null) SidebarFolderFavoritesPivotItem.Header = Strings.FavoritesFolders;

            // Clear and re-populate favorites to refresh tooltips
            UpdateFavoritesMenu();

            // Sort Menu & Tooltip
            UpdateSortIcon();

            UpdateLanguageMenuCheckmark();

            if (ThumbnailSettingsTitleText != null) ThumbnailSettingsTitleText.Text = Strings.ThumbnailSettingsTitle;
            if (ThumbnailSizeLabel != null) ThumbnailSizeLabel.Text = Strings.ThumbnailSizeLabel;
            if (FolderThumbnailsCheckBox != null) FolderThumbnailsCheckBox.Content = Strings.ShowFolderThumbnailsLabel;
            ApplyThumbnailSettingsToControls();

            UpdateFontSettingsMenu();

            // Trigger x:Bind Refresh
            this.Bindings.Update();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // 복잡한 위치/크기 추적은 매니저에게 위임합니다.
            _windowState.HandleAppWindowChanged(args);

            if (_windowState.SyncFullscreenStateFromPresenter())
            {
                ApplyFullscreenUiState();
            }

            if (args.DidPositionChange || args.DidSizeChange)
            {
                if (args.DidSizeChange)
                {
                    TriggerEpubResize();
                    TextWindowLayoutService.EnforceMinWindowSize(sender, _windowState, _isTextMode || _isEpubMode);
                }

                // [Important] Re-focus RootGrid after window state changes (Maximize/Restore/Resize)
                // This ensures keyboard shortcuts keep working without an extra click.
                RootGrid?.Focus(FocusState.Programmatic);
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            SaveWindowSettingsForShutdown();
        }

        private void SaveWindowSettingsForShutdown()
        {
            try
            {
                _windowSettingsCoordinator?.SaveWindowSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings: {ex.Message}");
            }
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TextWindowLayoutService.ConstrainSidebarForTextMode(
                e.NewSize,
                _isTextMode || _isEpubMode,
                _windowState.IsSidebarVisible,
                SidebarColumn);
        }

        // [추가] 텍스트를 열 때 창 크기가 작으면 800x600 크기 이상으로 강제로 늘리는 메서드
        private void EnsureMinWindowSizeForText()
        {
            TextWindowLayoutService.EnsureMinWindowSizeForText(
                AppWindow,
                RootGrid,
                _windowState,
                SidebarColumn);
        }

        #region Image Resource Helpers

        /// <summary>
        /// 현재 MainWindow 필드 값을 기반으로 ViewingContext 스냅샷을 생성합니다.
        /// 이미지 로딩·존재 확인 메서드 호출 직전에 사용합니다.
        /// </summary>
        private Services.ViewingContext CreateViewingContext() => new(
            IsEpubMode:               _isEpubMode,
            IsWebDavMode:             _isWebDavMode,
            EpubSession:              _epubSession,
            CurrentTextFilePath:      _currentTextFilePath,
            CurrentTextArchiveEntryKey: _currentTextArchiveEntryKey,
            ArchiveSession:           _archiveSession,
            CurrentWebDavItemPath:    _currentWebDavItemPath,
            ImageEntries:             _imageEntries,
            ResolveWebDavImagePath:   ResolveWebDavImagePath,
            WebDavService:            _webDavService
        );

        /// <summary>
        /// 현재 ImageOptions 설정을 SharpenParams 레코드로 변환합니다.
        /// </summary>
        private Services.SharpenParams CreateSharpenParams() => new(
            UpscaleFactor:   (float)ImageOptions.UpscaleFactor,
            SharpenAmount:   (float)ImageOptions.SharpenAmount,
            SharpenThreshold:(float)ImageOptions.SharpenThreshold,
            UnsharpAmount:   (float)ImageOptions.UnsharpAmount,
            UnsharpRadius:   (float)ImageOptions.UnsharpRadius
        );

        private async Task LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing = null,
            Func<bool>? shouldKeepLoadedBitmap = null)
        {
            var bitmap = await _imageResourceService.LoadAsync(
                cacheKey,
                resourcePath,
                device,
                CreateViewingContext(),
                _sharpenEnabled,
                CreateSharpenParams(),
                shouldKeepLoadedBitmap);

            if (bitmap != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (shouldKeepLoadedBitmap != null && !shouldKeepLoadedBitmap()) return;
                    invalidate();
                });
            }
            else
            {
                onMissing?.Invoke();
            }
        }

        #endregion

        #region Fullscreen

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            RequestWindowClose();
        }

        private void RequestWindowClose()
        {
            _shutdownCoordinator.RequestClose(Close);
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowAboutDialog();
        }

        private async Task ShowAboutDialog()
        {
            try
            {
                var dialog = new AboutDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    RequestedTheme = RootGrid.ActualTheme
                };

                _aboutDialog = dialog;
                await dialog.ShowAsync();
                _aboutDialog = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing About dialog: {ex.Message}");
            }
        }

        private void ToggleFullscreen()
        {
            _windowChromeController.ToggleFullscreen();
        }

        private void ApplyFullscreenUiState()
        {
            _windowChromeController.ApplyFullscreenUiState();
        }

        private void ToggleMaximizeRestore()
        {
            _windowChromeController.ToggleMaximizeRestore();
        }


        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _windowChromeController.HandlePointerMoved(e);
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Focus RootGrid whenever it is clicked directly or via transparent child
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _windowChromeController.HandlePointerExited();
        }


        // Unified Touch Handler for Text, Aozora, and Epub modes
        private void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction)
        {
            _windowChromeController.HandleSmartTouchNavigation(e, ShouldInvertControls, prevAction, nextAction);
        }

        #endregion


        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePin();
        }

        private void TogglePin()
        {
            _windowChromeController.TogglePin();
        }

        private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAlwaysOnTop();
        }

        private void ToggleAlwaysOnTop()
        {
            _windowChromeController.ToggleAlwaysOnTop();
        }

        private void GlobalThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _windowChromeController.ToggleGlobalTheme();
        }

        internal void SetTheme(ElementTheme theme)
        {
            _windowChromeController.SetTheme(theme);
        }

        private void InvalidateThemeTargets()
        {
            if (_isVerticalMode && VerticalTextCanvas != null) VerticalTextCanvas.Invalidate();
            if (_isEpubMode && EpubTextCanvas != null) EpubTextCanvas.Invalidate();
            if (_isAozoraMode && AozoraTextCanvas != null) AozoraTextCanvas.Invalidate();
            if (MainCanvas != null) MainCanvas.Invalidate();
        }

        private void UpdateThemeToggleButtonTooltip()
        {
            _windowChromeController.UpdateThemeToggleButtonTooltip();
        }



        #region Win2D Canvas Event Handlers

        private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            double panY = _imageViewportNavigationService.PanY;
            ImageCanvasRenderer.DrawMainCanvas(
                sender,
                args,
                _currentBitmap,
                _imageEntries,
                _imageCache,
                _currentIndex,
                _zoomLevel,
                _currentPdfDocument != null,
                _isCurrentViewSideBySide,
                _sharpenEnabled,
                _isAnimatedFrameActive,
                _imageViewportNavigationService.PanX,
                ref panY);
            _imageViewportNavigationService.PanY = panY;

            PdfSearchHighlightRenderer.Draw(
                sender,
                args,
                _currentBitmap,
                _currentPdfDocument != null,
                _currentIndex,
                _zoomLevel,
                _imageViewportNavigationService.PanX,
                _imageViewportNavigationService.PanY,
                _activePdfSearchPageIndex,
                _activePdfSearchHighlights,
                _activePdfSearchMatchIndex);
        }

        private void LeftCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void LeftCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            ImageCanvasRenderer.DrawSideCanvas(sender, args, _leftBitmap, _zoomLevel, alignRight: true);
        }

        private void RightCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void RightCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            ImageCanvasRenderer.DrawSideCanvas(sender, args, _rightBitmap, _zoomLevel, alignRight: false);
        }

        #endregion

    }
}
