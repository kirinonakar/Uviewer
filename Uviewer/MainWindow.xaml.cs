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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private readonly Services.AozoraBlockMeasurer _aozoraBlockMeasurer = new();
        private readonly Services.AozoraBlockPaginator _aozoraBlockPaginator;
        private readonly Services.AozoraPageMapCalculator _aozoraPageMapCalculator;
        private readonly Services.AozoraPreviousPageCache _aozoraPreviousPageCache;
        private readonly Services.EpubPageFlowService _epubPageFlowService = new();
        private readonly Services.TextBlockDocumentService _textBlockDocumentService = new();
        private readonly Services.ReadingProgressService _readingProgressService = new();
        private readonly Services.ImageResourceService _imageResourceService;
        private bool _isWindowClosing;

        // ImageResourceService를 _sharpeningService 다음에 생성해야 하므로
        // 필드 초기화 식 대신 생성자 내부에서 초기화합니다.

        // Loading and navigation state
        private CancellationTokenSource? _imageLoadingCts;

        private static bool TryGetBitmapSize([NotNullWhen(true)] CanvasBitmap? bitmap, out Windows.Foundation.Size size)
        {
            size = default;

            if (bitmap == null) return false;

            try
            {
                if (bitmap.Device == null) return false;
                size = bitmap.Size;
                return size.Width > 0 && size.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCanvasBitmapUsable(CanvasBitmap? bitmap)
        {
            return TryGetBitmapSize(bitmap, out _);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private readonly Services.SevenZipExtractionCoordinator _sevenZipExtraction = new();

        private void Signal7zJump()
        {
            _sevenZipExtraction.SignalJump(_currentIndex);
        }





        private Services.FavoritesService _favoritesService = new();
        private Services.RecentService _recentService = new();
        private Services.TextDialogService _textDialogService = null!;
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
                launchFilePath = launchFilePath.Trim('\"');
                launchFilePath = Path.GetFullPath(launchFilePath);
                if (File.Exists(launchFilePath))
                {
                    // Hide empty state immediately
                    if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;

                    var fileFolder = Path.GetDirectoryName(launchFilePath);
                    if (!string.IsNullOrEmpty(fileFolder) && Directory.Exists(fileFolder))
                    {
                        var extension = Path.GetExtension(launchFilePath).ToLowerInvariant();

                        // [Step 1] Priority Load: Load the file first
                        if (FileExplorerService.SupportedArchiveExtensions.Contains(extension))
                        {
                            await LoadImagesFromArchiveAsync(launchFilePath);
                        }
                        else if (FileExplorerService.SupportedPdfExtensions.Contains(extension))
                        {
                            await LoadImagesFromPdfAsync(launchFilePath);
                        }
                        else if (FileExplorerService.SupportedEpubExtensions.Contains(extension))
                        {
                            var file = await StorageFile.GetFileFromPathAsync(launchFilePath);
                            await LoadImageFromFileAsync(file, true); // Use fast initial load
                        }
                        else
                        {
                            var file = await StorageFile.GetFileFromPathAsync(launchFilePath);
                            await LoadImageFromFileAsync(file, true); // Use fast initial load
                        }

                        // [Step 2] Background: Load explorer folder
                        _ = Task.Run(() =>
                        {
                            DispatcherQueue.TryEnqueue(() => LoadExplorerFolder(fileFolder));
                        });
                    }
                }
                else if (Directory.Exists(launchFilePath))
                {
                    LoadExplorerFolder(launchFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing launch path: {ex.Message}");
            }
        }

        public MainWindow(string? launchFilePath = null)
        {
            // _imageResourceService는 _sharpeningService에 의존하므로 생성자 시작 시 초기화
            _imageResourceService = new Services.ImageResourceService(_sharpeningService);
            _aozoraBlockPaginator = new Services.AozoraBlockPaginator(_aozoraBlockMeasurer);
            _aozoraPageMapCalculator = new Services.AozoraPageMapCalculator(_aozoraBlockMeasurer);
            _aozoraPreviousPageCache = new Services.AozoraPreviousPageCache(_aozoraBlockMeasurer, _aozoraBlockPaginator);

            InitializeComponent();
            _textDialogService = new Services.TextDialogService(RootGrid);
            LoadTextSettings();

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

                _fastNavigationService = new Services.FastNavigationService(DispatcherQueue);
                _animatedWebpService = new Services.AnimatedWebpService(_sharpeningService, DispatcherQueue);
                _animatedWebpService.FrameUpdated += OnAnimatedWebpFrameUpdated;
                _animatedWebpService.AnimationStopped += OnAnimatedWebpAnimationStopped;

                _windowSettingsCoordinator = new Services.WindowSettingsCoordinator(this, _appSettingsService);
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

                _overlayManager = new FullscreenOverlayManager();
                _overlayManager.Initialize(DispatcherQueue);

                // 타이머가 만료되었을 때 실행할 UI 숨김 로직 연결
                _overlayManager.HideToolbarRequested += (s, e) => HideToolbarUI();
                _overlayManager.HideSidebarRequested += (s, e) => HideSidebarUI();

                // Initialize button states
                UpdateSideBySideButtonState();
                UpdateNextImageSideButtonState();
                UpdateSharpenButtonState();

                // Apply saved sidebar visibility state
                if (!_windowState.IsSidebarVisible)
                {
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                }

                // Apply saved pin state
                if (!_windowState.IsPinned)
                {
                    PinButton.IsChecked = false;
                    PinIcon.Glyph = "\uE77A"; // Unpin icon
                    AppTitleBar.Visibility = Visibility.Collapsed;
                    SetCaptionButtonsVisibility(false);
                    ToolbarGrid.Visibility = Visibility.Collapsed;
                    StatusBarGrid.Visibility = Visibility.Collapsed;
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                }

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

                // Apply Localization
                ApplyLocalization();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing MainWindow: {ex.Message}");
            }

            // 화면 UI(RootGrid)가 로드된 후에 초기화 작업을 시작합니다.
            RootGrid.Loaded += async (s, e) =>
            {
                UpdateTitleBarColors();
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
                _preloadManager?.Dispose();
                try
                {
                    // Stop all timers
                    _overlayManager.StopAll();
                    _animatedWebpService.Stop();


                    // Cancel any ongoing operations
                    _imageLoadingCts?.Cancel();
                    ShutdownPdfResources();
                    // Clean up fast navigation timer
                    _fastNavigationService?.Dispose();

                    // Clean up archive resources
                    if (_currentArchive != null)
                    {
                        try { _currentArchive.Dispose(); _currentArchive = null; } catch { }
                    }
                    if (_current7zArchive != null)
                    {
                        try { _current7zArchive.Dispose(); _current7zArchive = null; } catch { }
                    }
                    _imageViewerState.ClearBitmaps();

                    if (!wasPdfOpen)
                    {
                        _imageCache?.Dispose();
                    }

                    // 이미지 엔트리의 파일 경로 참조 해제
                    if (_imageEntries != null)
                    {
                        foreach (var entry in _imageEntries) entry.FilePath = null;
                    }

                    if (!wasPdfOpen)
                    {
                        // Native 리소스와 파일 핸들을 즉시 해제하기 위해 GC 강제 실행
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    _sevenZipExtraction.CleanupTempData(immediate: true);
                    WebDavService.CleanupTempFiles();

                    // Save settings
                    _windowSettingsCoordinator.SaveWindowSettings();
                    // Save current position before closing
                    await AddToRecentAsync(true);

                    await _recentService.SaveRecentItemsAsync();
                    await _favoritesService.SaveFavoritesAsync();

                    // Dispose semaphores
                    _archiveLock.Dispose();

                    // Cleanup WebDAV
                    _webDavService?.Dispose();
                    _webDavState.Dispose();

                    // Dispose cancellation tokens
                    _imageLoadingCts?.Dispose();
                    _animatedWebpService.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
                }
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
            // Tooltips
            ToolTipService.SetToolTip(ToggleSidebarButton, Strings.ToggleSidebarTooltip);
            ToolTipService.SetToolTip(OpenFileButton, Strings.OpenFileTooltip);
            ToolTipService.SetToolTip(OpenFolderButton, Strings.OpenFolderTooltip);
            ToolTipService.SetToolTip(ZoomOutButton, Strings.ZoomOutTooltip);
            ToolTipService.SetToolTip(ZoomInButton, Strings.ZoomInTooltip);
            ToolTipService.SetToolTip(ZoomFitButton, Strings.ZoomFitTooltip);
            ToolTipService.SetToolTip(ZoomActualButton, Strings.ZoomActualTooltip);
            ToolTipService.SetToolTip(SharpenButton, Strings.SharpenTooltip);
            ToolTipService.SetToolTip(SideBySideButton, Strings.SideBySideTooltip);
            ToolTipService.SetToolTip(NextImageSideButton, Strings.NextImageSideTooltip);
            ToolTipService.SetToolTip(AozoraToggleButton, Strings.AozoraTooltip);
            ToolTipService.SetToolTip(VerticalToggleButton, Strings.VerticalTooltip);
            ToolTipService.SetToolTip(FontToggleButton, Strings.FontTooltip);
            ToolTipService.SetToolTip(GoToPageButton, Strings.GoToPageTooltip);
            ToolTipService.SetToolTip(TextSizeDownButton, Strings.TextSizeDownTooltip);
            ToolTipService.SetToolTip(TextSizeUpButton, Strings.TextSizeUpTooltip);
            ToolTipService.SetToolTip(ThemeToggleButton, Strings.ThemeTooltip);
            ToolTipService.SetToolTip(FullscreenButton, Strings.FullscreenTooltip);
            ToolTipService.SetToolTip(CloseWindowButton, Strings.CloseWindowTooltip);
            ToolTipService.SetToolTip(ToggleViewButton, Strings.ToggleViewTooltip);
            ToolTipService.SetToolTip(ParentFolderButton, Strings.ParentFolderTooltip);
            ToolTipService.SetToolTip(RecentButton, Strings.RecentTooltip);
            ToolTipService.SetToolTip(SidebarFavoritesButton, Strings.FavoritesTooltip);
            ToolTipService.SetToolTip(SidebarRecentButton, Strings.RecentTooltip);
            ToolTipService.SetToolTip(FavoritesButton, Strings.FavoritesTooltip);
            ToolTipService.SetToolTip(BrowseFolderButton, Strings.BrowseFolderTooltip);
            ToolTipService.SetToolTip(WebDavButton, Strings.WebDavTooltip);
            if (AddWebDavButton != null) AddWebDavButton.Content = Strings.AddWebDavServer;
            ToolTipService.SetToolTip(TocButton, Strings.TocTooltip);
            ToolTipService.SetToolTip(PdfTocButton, Strings.TocTooltip);
            ToolTipService.SetToolTip(PdfGoToPageButton, Strings.PdfGoToPageTooltip);
            ToolTipService.SetToolTip(SettingsButton, Strings.SettingsTooltip);
            ToolTipService.SetToolTip(PinButton, Strings.PinTooltip);
            ToolTipService.SetToolTip(AlwaysOnTopButton, Strings.AlwaysOnTopTooltip);
            UpdateThemeToggleButtonTooltip();
            ToolTipService.SetToolTip(PrevFileButton, Strings.PrevFileTooltip);
            ToolTipService.SetToolTip(NextFileButton, Strings.NextFileTooltip);
            ToolTipService.SetToolTip(PrevPageButton, Strings.PrevPageTooltip);
            ToolTipService.SetToolTip(NextPageButton, Strings.NextPageTooltip);

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
            if (AddToFavoritesButton != null) AddToFavoritesButton.Content = Strings.AddToFavorites;
            if (SidebarAddToFavoritesButton != null) SidebarAddToFavoritesButton.Content = Strings.AddToFavorites;
            if (ChangeFontMenuItem != null) ChangeFontMenuItem.Text = Strings.ChangeFont;
            if (ChangeUiFontMenuItem != null) ChangeUiFontMenuItem.Text = Strings.ChangeUiFont;
            if (EncodingMenuItem != null) EncodingMenuItem.Text = Strings.EncodingMenu;
            if (EncAutoItem != null) EncAutoItem.Text = Strings.EncAuto;
            if (EncUtf8Item != null) EncUtf8Item.Text = Strings.EncUtf8;
            if (EncEucKrItem != null) EncEucKrItem.Text = Strings.EncEucKr;
            if (EncSjisItem != null) EncSjisItem.Text = Strings.EncSjis;
            if (EncJohabItem != null) EncJohabItem.Text = Strings.EncJohab;
            if (ChangeColorsMenuItem != null) ChangeColorsMenuItem.Text = Strings.ChangeColors;
            if (MatchControlDirectionMenuItem != null)
            {
                MatchControlDirectionMenuItem.Text = Strings.MatchControlDirection;
                ToolTipService.SetToolTip(MatchControlDirectionMenuItem, Strings.MatchControlDirectionTooltip);
            }
            if (AllowMultipleInstancesMenuItem != null)
            {
                AllowMultipleInstancesMenuItem.Text = Strings.AllowMultipleInstances;
                ToolTipService.SetToolTip(AllowMultipleInstancesMenuItem, Strings.AllowMultipleInstancesTooltip);
            }
            if (AutoDoublePageForArchiveMenuItem != null)
            {
                AutoDoublePageForArchiveMenuItem.Text = Strings.AutoDoublePageForArchive;
            }
            if (AboutMenuItem != null) AboutMenuItem.Text = Strings.About;

            if (LanguageMenuItem != null) LanguageMenuItem.Text = Strings.LanguageSelection;
            if (LangAutoItem != null) LangAutoItem.Text = Strings.LanguageAuto;
            if (LangKoItem != null) LangKoItem.Text = Strings.LanguageKorean;
            if (LangEnItem != null) LangEnItem.Text = Strings.LanguageEnglish;
            if (LangJaItem != null) LangJaItem.Text = Strings.LanguageJapanese;
            if (LangZhHansItem != null) LangZhHansItem.Text = Strings.LanguageChineseSimplified;
            if (LangZhHantItem != null) LangZhHantItem.Text = Strings.LanguageChineseTraditional;
            if (LangViItem != null) LangViItem.Text = Strings.LanguageVietnamese;

            // Favorites Pivot Headers
            if (FileFavoritesPivotItem != null) FileFavoritesPivotItem.Header = Strings.FavoritesFiles;
            if (FolderFavoritesPivotItem != null) FolderFavoritesPivotItem.Header = Strings.FavoritesFolders;
            if (SidebarFileFavoritesPivotItem != null) SidebarFileFavoritesPivotItem.Header = Strings.FavoritesFiles;
            if (SidebarFolderFavoritesPivotItem != null) SidebarFolderFavoritesPivotItem.Header = Strings.FavoritesFolders;

            // Clear and re-populate favorites to refresh tooltips
            UpdateFavoritesMenu();

            // Sort Menu & Tooltip
            UpdateSortIcon();
            if (SortByNameMenu != null) SortByNameMenu.Text = Strings.SortByNameTooltip;
            if (SortByDateDescMenu != null) SortByDateDescMenu.Text = Strings.SortByDateDescTooltip;
            if (SortByDateAscMenu != null) SortByDateAscMenu.Text = Strings.SortByDateAscTooltip;

            UpdateLanguageMenuCheckmark();

            // Sharpen & Upscale Flyout
            if (SharpenSettingsTitleText != null) SharpenSettingsTitleText.Text = Strings.SharpenSettingsTitle;
            if (UpscaleLabel != null) UpscaleLabel.Text = Strings.UpscaleFactorLabel;
            if (SharpenAmountLabel != null) SharpenAmountLabel.Text = Strings.SharpenAmountLabel;
            if (SharpenThresholdLabel != null) SharpenThresholdLabel.Text = Strings.SharpenThresholdLabel;
            if (UnsharpAmountLabel != null) UnsharpAmountLabel.Text = Strings.UnsharpAmountLabel;
            if (UnsharpRadiusLabel != null) UnsharpRadiusLabel.Text = Strings.UnsharpRadiusLabel;
            if (SharpenParamsResetButton != null) SharpenParamsResetButton.Content = Strings.ResetButton;
            
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

                    // [수정] 텍스트 또는 EPUB 모드일 때 창 크기가 800x600 밑으로 내려가지 않도록 방지
                    if ((_isTextMode || _isEpubMode) && !_windowState.IsFullscreen)
                    {
                        if (sender.Size.Width < 800 || sender.Size.Height < 600)
                        {
                            sender.Resize(new Windows.Graphics.SizeInt32(
                                Math.Max(sender.Size.Width, 800),
                                Math.Max(sender.Size.Height, 600)
                            ));
                        }
                    }
                }

                // [Important] Re-focus RootGrid after window state changes (Maximize/Restore/Resize)
                // This ensures keyboard shortcuts keep working without an extra click.
                RootGrid?.Focus(FocusState.Programmatic);
            }
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if ((_isTextMode || _isEpubMode) && _windowState.IsSidebarVisible)
            {
                // 텍스트/EPUB 영역에 최소 500픽셀은 절대적으로 보장 (오류 방지 및 사이드바 억제)
                double minTextContentWidth = 500;
                double maxSidebarWidth = e.NewSize.Width - minTextContentWidth;

                if (maxSidebarWidth < 200)
                {
                    maxSidebarWidth = 200;
                }

                // LayoutCycleException 방지
                if (Math.Abs(SidebarColumn.MaxWidth - maxSidebarWidth) > 1.0)
                {
                    SidebarColumn.MaxWidth = maxSidebarWidth;
                }

                if (SidebarColumn.Width.IsAbsolute && SidebarColumn.Width.Value > maxSidebarWidth)
                {
                    SidebarColumn.Width = new GridLength(maxSidebarWidth);
                }
            }
            else
            {
                if (SidebarColumn.MaxWidth != double.PositiveInfinity)
                {
                    SidebarColumn.MaxWidth = double.PositiveInfinity;
                }
            }
        }

        // [추가] 텍스트를 열 때 창 크기가 작으면 800x600 크기 이상으로 강제로 늘리는 메서드
        private void EnsureMinWindowSizeForText()
        {
            if (_windowState.IsFullscreen) return;

            var currentSize = this.AppWindow.Size;
            bool needsResize = false;
            int newWidth = currentSize.Width;
            int newHeight = currentSize.Height;

            // 절대적인 최소 창 크기를 800x600으로 강제
            if (currentSize.Width < 800)
            {
                newWidth = 800;
                needsResize = true;
            }
            if (currentSize.Height < 600)
            {
                newHeight = 600;
                needsResize = true;
            }

            if (needsResize)
            {
                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
            }

            // 창이 800일 때 사이드바가 화면을 너무 많이 덮지 않도록 제한
            if (_windowState.IsSidebarVisible)
            {
                // Resize가 비동기적으로 먹힐 수 있으므로, 보더 여백(~16px)을 뺀 예상 클라이언트 너비 사용
                double estimatedClientWidth = needsResize ? newWidth - 16 : RootGrid.ActualWidth;
                if (estimatedClientWidth <= 0) estimatedClientWidth = newWidth;

                double minTextWidth = 500; // 800 기준에 맞춰 텍스트 최소 너비 강력히 확보
                double maxAllowedSidebar = estimatedClientWidth - minTextWidth;

                if (maxAllowedSidebar < 200) maxAllowedSidebar = 200; // 사이드바 최소치

                if (SidebarColumn.ActualWidth > maxAllowedSidebar || SidebarColumn.Width.Value > maxAllowedSidebar)
                {
                    SidebarColumn.Width = new GridLength(maxAllowedSidebar);
                }
                SidebarColumn.MaxWidth = maxAllowedSidebar;
            }
        }



        private void HideToolbarUI()
        {
            if (_windowState.IsFullscreen || !_windowState.IsPinned)
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
                if (!_windowState.IsFullscreen) _windowState.SetCaptionButtonsVisibility(false);
                ToolbarGrid.Visibility = Visibility.Collapsed;
                if (!_windowState.IsFullscreen) StatusBarGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void HideSidebarUI()
        {
            if (_windowState.IsFullscreen || !_windowState.IsPinned)
            {
                SidebarGrid.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
                if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
            }
        }

        #region Image Resource Helpers

        /// <summary>
        /// 현재 MainWindow 필드 값을 기반으로 ViewingContext 스냅샷을 생성합니다.
        /// 이미지 로딩·존재 확인 메서드 호출 직전에 사용합니다.
        /// </summary>
        private Services.ViewingContext CreateViewingContext() => new(
            IsEpubMode:               _isEpubMode,
            IsWebDavMode:             _isWebDavMode,
            EpubArchive:              _currentEpubArchive,
            EpubArchiveLock:          _epubArchiveLock,
            CurrentTextFilePath:      _currentTextFilePath,
            CurrentTextArchiveEntryKey: _currentTextArchiveEntryKey,
            CurrentArchive:           _currentArchive,
            Current7zArchive:         _current7zArchive,
            ArchiveLock:              _archiveLock,
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

        private static bool IsAutoDoublePageTallCandidate(double width, double height)
        {
            if (width <= 0 || height <= 0) return false;
            return height >= width * 1.2 && height <= width * 3.0;
        }

        #region Fullscreen

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Exit();
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
            // 1. 매니저에게 창 상태 전환 지시
            _windowState.ToggleFullscreen();

            // 2. 바뀐 상태에 따라 UI 컨트롤(Grid 등) 표시/숨김 처리
            ApplyFullscreenUiState();

            // [Important] Re-focus RootGrid after window state change
            RootGrid?.Focus(FocusState.Programmatic);
        }

        private void ApplyFullscreenUiState()
        {
            if (!_windowState.IsFullscreen)
            {
                // Exit fullscreen
                _overlayManager.StopAll();
                if (_windowState.IsPinned)
                {
                    // 핀 고정 상태: UI 모두 복원
                    AppTitleBar.Visibility = Visibility.Visible;
                    _windowState.SetCaptionButtonsVisibility(true);
                    ToolbarGrid.Visibility = Visibility.Visible;
                    StatusBarGrid.Visibility = Visibility.Visible;
                    if (_windowState.IsSidebarVisible)
                    {
                        SidebarGrid.Visibility = Visibility.Visible;
                        SplitterGrid.Visibility = Visibility.Visible;
                        SidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                    }
                    else
                    {
                        SplitterGrid.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // 핀 해제 상태: UI 숨긴 채 유지
                    AppTitleBar.Visibility = Visibility.Collapsed;
                    _windowState.SetCaptionButtonsVisibility(false);
                    ToolbarGrid.Visibility = Visibility.Collapsed;
                    StatusBarGrid.Visibility = Visibility.Collapsed;
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    SplitterGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                }
                FullscreenIcon.Glyph = "\uE740"; // Fullscreen icon
            }
            else
            {
                // Enter fullscreen
                AppTitleBar.Visibility = Visibility.Collapsed;
                ToolbarGrid.Visibility = Visibility.Collapsed;
                StatusBarGrid.Visibility = Visibility.Collapsed;
                SidebarGrid.Visibility = Visibility.Collapsed;
                if (_windowState.IsSidebarVisible && (int)SidebarColumn.Width.Value > 200)
                {
                    _windowState.SidebarWidth = (int)SidebarColumn.Width.Value; // Save current width
                }
                SidebarColumn.Width = new GridLength(0);
                SplitterGrid.Visibility = Visibility.Collapsed;  // Hide splitter in fullscreen
                FullscreenIcon.Glyph = "\uE73F"; // Exit fullscreen icon
                _overlayManager.StopAll();
            }
        }

        private void ToggleMaximizeRestore()
        {
            _windowState.ToggleMaximizeRestore();
            RootGrid?.Focus(FocusState.Programmatic);
        }


        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_windowState.IsFullscreen && _windowState.IsPinned) return;

            var pt = e.GetCurrentPoint(RootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            bool inTopZone = y < FullscreenOverlayManager.TopHoverZone;
            if (ToolbarGrid.Visibility == Visibility.Visible && y < ToolbarGrid.ActualHeight)
            {
                inTopZone = true;
            }

            if (inTopZone)
            {
                // Show toolbar and stop hide timer while in hover zone
                if (ToolbarGrid.Visibility != Visibility.Visible)
                {
                    AppTitleBar.Visibility = Visibility.Visible;
                    if (!_windowState.IsFullscreen) _windowState.SetCaptionButtonsVisibility(true);
                    ToolbarGrid.Visibility = Visibility.Visible;
                    if (!_windowState.IsFullscreen) StatusBarGrid.Visibility = Visibility.Visible;
                }
                _overlayManager.StopToolbarTimer();
            }
            else
            {
                // Start hide timer only if not already running
                if (ToolbarGrid.Visibility == Visibility.Visible && !_overlayManager.IsToolbarTimerRunning)
                {
                    _overlayManager.StartToolbarTimer();
                }
            }

            bool inLeftZone = _windowState.IsSidebarVisible && x < FullscreenOverlayManager.LeftHoverZone;
            if (SidebarGrid.Visibility == Visibility.Visible && x < _windowState.SidebarWidth)
            {
                inLeftZone = true;
            }

            if (_windowState.IsSidebarVisible && inLeftZone)
            {
                // Show sidebar and stop hide timer while in hover zone
                if (SidebarGrid.Visibility != Visibility.Visible)
                {
                    SidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                    SidebarGrid.Visibility = Visibility.Visible;
                }
                _overlayManager.StopSidebarTimer();
            }
            else
            {
                // Start hide timer only if not already running
                if (SidebarGrid.Visibility == Visibility.Visible && !_overlayManager.IsSidebarTimerRunning)
                {
                    _overlayManager.StartSidebarTimer();
                }
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Focus RootGrid whenever it is clicked directly or via transparent child
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_windowState.IsFullscreen && _windowState.IsPinned) return;

            if (ToolbarGrid.Visibility == Visibility.Visible && !_overlayManager.IsToolbarTimerRunning)
            {
                _overlayManager.StartToolbarTimer();
            }

            if (SidebarGrid.Visibility == Visibility.Visible && !_overlayManager.IsSidebarTimerRunning)
            {
                _overlayManager.StartSidebarTimer();
            }
        }


        // Unified Touch Handler for Text, Aozora, and Epub modes
        private void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction)
        {
            var pt = e.GetCurrentPoint(RootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            // 1. Edge Detection (Fullscreen only)
            if (_windowState.IsFullscreen)
            {
                // Top Edge -> Show Toolbar
                if (y < FullscreenOverlayManager.TopHoverZone)
                {
                    if (ToolbarGrid.Visibility != Visibility.Visible)
                    {
                        ToolbarGrid.Visibility = Visibility.Visible;
                    }
                    _overlayManager.StartToolbarTimer();
                    return;
                }

                // Left Edge -> Show Sidebar
                if (x < FullscreenOverlayManager.LeftHoverZone)
                {
                    if (SidebarGrid.Visibility != Visibility.Visible)
                    {
                        SidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                        SidebarGrid.Visibility = Visibility.Visible;
                    }
                    _overlayManager.StartSidebarTimer();
                    return;
                }
            }

            // 2. Navigation Zones (Screen Half)
            if (x < RootGrid.ActualWidth / 2)
            {
                if (ShouldInvertControls) nextAction?.Invoke();
                else prevAction?.Invoke();
            }
            else
            {
                if (ShouldInvertControls) prevAction?.Invoke();
                else nextAction?.Invoke();
            }
        }

        #endregion


        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePin();
        }

        private void TogglePin()
        {
            if (_windowState.IsFullscreen) return; // 전체화면에서는 핀 모드 불필요

            _windowState.TogglePin();
            PinButton.IsChecked = _windowState.IsPinned;
            PinIcon.Glyph = _windowState.IsPinned ? "\uE890" : "\uE890"; // Eye / EyeOff icon

            if (_windowState.IsPinned)
            {
                // 핀 고정: UI 모두 표시
                _overlayManager.StopAll();

                AppTitleBar.Visibility = Visibility.Visible;
                _windowState.SetCaptionButtonsVisibility(true);
                ToolbarGrid.Visibility = Visibility.Visible;
                StatusBarGrid.Visibility = Visibility.Visible;
                if (_windowState.IsSidebarVisible)
                {
                    SidebarGrid.Visibility = Visibility.Visible;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Visible;
                    SidebarColumn.Width = new GridLength(_windowState.SidebarWidth);
                }
            }
            else
            {
                // 핀 해제: UI 모두 숨김
                if (_windowState.IsSidebarVisible && (int)SidebarColumn.Width.Value > 200)
                {
                    _windowState.SidebarWidth = (int)SidebarColumn.Width.Value;
                }

                AppTitleBar.Visibility = Visibility.Collapsed;
                _windowState.SetCaptionButtonsVisibility(false);
                ToolbarGrid.Visibility = Visibility.Collapsed;
                StatusBarGrid.Visibility = Visibility.Collapsed;
                SidebarGrid.Visibility = Visibility.Collapsed;
                if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
            }

            _windowSettingsCoordinator.SaveWindowSettings();
        }

        private void SetCaptionButtonsVisibility(bool isVisible)
        {
            _windowState.SetCaptionButtonsVisibility(isVisible);
        }

        private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAlwaysOnTop();
        }

        private void ToggleAlwaysOnTop()
        {
            _windowState.ToggleAlwaysOnTop();
            if (AlwaysOnTopButton != null) AlwaysOnTopButton.IsChecked = _windowState.IsAlwaysOnTop;
            _windowSettingsCoordinator.SaveWindowSettings();
        }

        private void GlobalThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // If in fullscreen, exit first, change theme, then re-enter
            bool wasFullscreen = _windowState.IsFullscreen;
            if (wasFullscreen)
            {
                ToggleFullscreen();
            }

            if (_currentTheme == ElementTheme.Dark)
            {
                SetTheme(ElementTheme.Light);
            }
            else
            {
                SetTheme(ElementTheme.Dark);
            }

            if (wasFullscreen)
            {
                ToggleFullscreen();
            }
        }

        internal void SetTheme(ElementTheme theme)
        {
            _currentTheme = theme;

            // Set theme on the root content to ensure all theme resources (including Mica) update
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }

            if (RootGrid != null)
            {
                // Force child elements to re-evaluate their theme resources
                RootGrid.RequestedTheme = theme;
                // [Important] Changing theme can cause focus loss; re-focus to keep shortcuts working
                RootGrid.Focus(FocusState.Programmatic);
            }

            // Update icon
            if (ThemeIcon != null)
            {
                ThemeIcon.Glyph = _currentTheme == ElementTheme.Dark ? "\uE706" : "\uE708"; // Sun if dark (to switch to light), Moon if light (to switch to dark)
            }

            // Update ToggleButton state
            if (GlobalThemeToggleButton != null)
            {
                GlobalThemeToggleButton.IsChecked = _currentTheme == ElementTheme.Dark;
            }

            UpdateThemeToggleButtonTooltip();
            UpdateTitleBarColors();

            // [추가] 테마 변경 시 Win2D 기반 캔버스들을 즉시 다시 그려 배경색 등을 반영합니다.
            if (_isVerticalMode && VerticalTextCanvas != null) VerticalTextCanvas.Invalidate();
            if (_isEpubMode && EpubTextCanvas != null) EpubTextCanvas.Invalidate();
            if (_isAozoraMode && AozoraTextCanvas != null) AozoraTextCanvas.Invalidate();
            if (MainCanvas != null) MainCanvas.Invalidate();
        }

        private void UpdateTitleBarColors()
        {
            var appWindow = this.AppWindow;
            if (appWindow != null && AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;

                if (_currentTheme == ElementTheme.Dark)
                {
                    // Use solid dark color to prevent white flicker during maximize/fullscreen
                    var darkBg = ColorHelper.FromArgb(255, 28, 28, 28);
                    titleBar.BackgroundColor = darkBg;
                    titleBar.InactiveBackgroundColor = darkBg;

                    // When extended, we want transparent buttons to blend with our custom UI.
                    // When NOT extended (e.g. in Fullscreen), we want solid background to match the system title bar.
                    if (ExtendsContentIntoTitleBar)
                    {
                        titleBar.ButtonBackgroundColor = Colors.Transparent;
                        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    }
                    else
                    {
                        titleBar.ButtonBackgroundColor = darkBg;
                        titleBar.ButtonInactiveBackgroundColor = darkBg;
                    }

                    titleBar.ButtonForegroundColor = Colors.White;
                    titleBar.ButtonHoverForegroundColor = Colors.White;
                    titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
                    titleBar.ButtonPressedForegroundColor = Colors.White;
                    titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
                    titleBar.ButtonInactiveForegroundColor = Colors.Gray;
                }
                else
                {
                    // Use solid light color for light theme
                    var lightBg = ColorHelper.FromArgb(255, 243, 243, 243);
                    titleBar.BackgroundColor = lightBg;
                    titleBar.InactiveBackgroundColor = lightBg;

                    if (ExtendsContentIntoTitleBar)
                    {
                        titleBar.ButtonBackgroundColor = Colors.Transparent;
                        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    }
                    else
                    {
                        titleBar.ButtonBackgroundColor = lightBg;
                        titleBar.ButtonInactiveBackgroundColor = lightBg;
                    }

                    titleBar.ButtonForegroundColor = Colors.Black;
                    titleBar.ButtonHoverForegroundColor = Colors.Black;
                    titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x33, 0x00, 0x00, 0x00);
                    titleBar.ButtonPressedForegroundColor = Colors.Black;
                    titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x66, 0x00, 0x00, 0x00);
                    titleBar.ButtonInactiveForegroundColor = Colors.LightGray;
                }
            }
        }

        private void UpdateThemeToggleButtonTooltip()
        {
            if (GlobalThemeToggleButton != null)
            {
                ToolTipService.SetToolTip(GlobalThemeToggleButton, _currentTheme == ElementTheme.Dark ? Strings.LightModeTooltip : Strings.DarkModeTooltip);
            }
        }



        #region Win2D Canvas Event Handlers

        private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
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
                _pdfPanX,
                ref _pdfPanY);
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
