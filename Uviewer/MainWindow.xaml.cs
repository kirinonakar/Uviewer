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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window, IKeyboardShortcutActions
    {
        private List<ImageEntry> _imageEntries = new();
        private int _currentIndex = -1;
        private double _zoomLevel { get => _zoomService.Level; set => _zoomService.SetLevel(value); }
        private CanvasBitmap? _currentBitmap;

        // Folder explorer
        private string? _currentExplorerPath;
        private ObservableCollection<FileItem> _fileItems = new();
        private bool _isExplorerGrid = false;
        private ExplorerSortMode _explorerSortMode = ExplorerSortMode.Name;

        // Fullscreen
        private FullscreenOverlayManager _overlayManager = null!;

        private DispatcherQueueTimer? _notificationTimer;

        // Side-by-side view settings
        private bool _isSideBySideMode = false;
        private bool _nextImageOnRight = true;
        private bool _autoDoublePageForArchive = false;
        private bool _isCurrentViewSideBySide = false;
        private ElementTheme _currentTheme = ElementTheme.Default;
        private CanvasBitmap? _leftBitmap;
        private CanvasBitmap? _rightBitmap;
        private bool _matchControlDirection = false;
        private int _pendingPdfPageIndex = -1;

        // Sharpen & Upscale Parameters
        private bool _sharpenEnabled;
        private float _upscaleFactor = 2.0f;
        private float _sharpenAmountParam = 1.0f;
        private float _sharpenThresholdParam = 0.01f;
        private float _unsharpAmount = 2.0f;
        private float _unsharpRadius = 1.0f;

        private double _pdfPanY = 0;
        private double _pdfPanX = 0;
        private double _lastCanvasWidth = 0;
        private bool _isPdfTransitioning = false;
        private int _pdfScrollDirection = 1; // 1 for next (start top), -1 for prev (start bottom)
        private bool _isSeamlessScroll = false;
        private bool _allowMultipleInstances = true;
        private bool _isRegistered = false;

        // 컨트롤 반전 로직 수정:
        // - Match Control Direction이 켜져 있고 Next Image가 왼쪽인 경우
        // - 이미지 모드(한장보기/두장보기)와 epub 이미지의 경우 반전 적용
        // - 텍스트 모드는 어떤 경우에도 반전되지 않음
        private bool ShouldInvertControls
        {
            get
            {
                if (_currentPdfDocument != null) return false;
                if (!_matchControlDirection || _nextImageOnRight) return false;
                if (_isTextMode) return false;

                if (_isEpubMode)
                {
                    // 1. 현재 선택된 페이지가 텍스트인 경우 반전 안 함
                    if (EpubSelectedItem is Grid g && !(g.Tag is EpubImageTag)) return false;

                    // 2. 현재 챕터에 텍스트가 포함된 경우 (이미지 페이지라도) 반전 안 함
                    if (_epubChapterHasText.TryGetValue(_currentEpubChapterIndex, out var curHasText) && curHasText) return false;

                    // 3. 현재 챕터가 이미지만 있더라도, 전후 챕터가 모두 텍스트인 경우 반전 안 함 (소설 내 삽화 등)
                    bool prevHasText = _currentEpubChapterIndex > 0 &&
                                      _epubChapterHasText.TryGetValue(_currentEpubChapterIndex - 1, out var pHT) && pHT;
                    bool nextHasText = _currentEpubChapterIndex < _epubSpine.Count - 1 &&
                                      _epubChapterHasText.TryGetValue(_currentEpubChapterIndex + 1, out var nHT) && nHT;

                    if (prevHasText && nextHasText) return false;
                }

                return true;
            }
        }

        // Image preloading for faster navigation
        private Services.ImageCacheManager _imageCache = null!;
        private WindowStateManager _windowState = null!;
        private readonly Services.IThumbnailService _thumbnailService = new Services.ThumbnailService();
        private Services.PreloadManager _preloadManager = null!;

        // Refactored Services
        private readonly Services.AppSettingsService _appSettingsService = new();
        private readonly Services.ZoomService _zoomService = new();
        private readonly Services.ISharpeningService _sharpeningService = new Services.SharpeningService();
        private Services.FastNavigationService _fastNavigationService = null!;
        private readonly IAnimatedWebpService _animatedWebpService = null!;
        private readonly IKeyboardShortcutService _keyboardShortcutService = new KeyboardShortcutService();
        private readonly Services.TocService _tocService = new();

        // Loading and navigation state
        private CancellationTokenSource? _imageLoadingCts;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        // 7z background extraction
        private string? _current7zTempFolder;
        private CancellationTokenSource? _7zExtractCts;
        private CancellationTokenSource _7zJumpCts = new();
        private int _lastIndexFor7zJump = -1;

        private void Signal7zJump()
        {
            try
            {
                _lastIndexFor7zJump = _currentIndex;
                var old = _7zJumpCts;
                _7zJumpCts = new CancellationTokenSource();
                old.Cancel();
                old.Dispose();
            }
            catch { }
        }





        private Services.FavoritesService _favoritesService = new();
        private Services.RecentService _recentService = new();
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
                CleanupZeroByteTempFiles();

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
            InitializeComponent();
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

                // Load saved window position, size and maximized state
                bool hasLoadedSettings = ApplyWindowSettings(appWindow2);
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
                _preloadManager?.Dispose();
                try
                {
                    // Stop all timers
                    _overlayManager.StopAll();
                    _animatedWebpService.Stop();


                    // Cancel any ongoing operations
                    _imageLoadingCts?.Cancel();
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
                    _currentBitmap = null;
                    _leftBitmap = null;
                    _rightBitmap = null;

                    _imageCache?.Dispose();

                    // 이미지 엔트리의 파일 경로 참조 해제
                    if (_imageEntries != null)
                    {
                        foreach (var entry in _imageEntries) entry.FilePath = null;
                    }

                    // Native 리소스와 파일 핸들을 즉시 해제하기 위해 GC 강제 실행
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    Cleanup7zTempData(immediate: true);
                    WebDavService.CleanupTempFiles();

                    // Save settings
                    SaveWindowSettings();
                    // Save current position before closing
                    await AddToRecentAsync(true);

                    await _recentService.SaveRecentItemsAsync();
                    await _favoritesService.SaveFavoritesAsync();

                    // Dispose semaphores
                    _archiveLock.Dispose();

                    // Cleanup WebDAV
                    _webDavService?.Dispose();
                    _webDavCts?.Cancel();
                    _webDavCts?.Dispose();

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
            if (NotificationText != null) NotificationText.Text = Strings.AddedToFavoritesNotification;

            if (LanguageMenuItem != null) LanguageMenuItem.Text = Strings.LanguageSelection;
            if (LangAutoItem != null) LangAutoItem.Text = Strings.LanguageAuto;
            if (LangKoItem != null) LangKoItem.Text = Strings.LanguageKorean;
            if (LangEnItem != null) LangEnItem.Text = Strings.LanguageEnglish;
            if (LangJaItem != null) LangJaItem.Text = Strings.LanguageJapanese;

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

            // Trigger x:Bind Refresh
            this.Bindings.Update();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // 복잡한 위치/크기 추적은 매니저에게 위임합니다.
            _windowState.HandleAppWindowChanged(args);

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

        #region Window Settings
        private bool ApplyWindowSettings(AppWindow appWindow)
        {
            var primaryArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var settings = _appSettingsService.LoadSettings(primaryArea);
            
            // Check if settings file actually had data (assuming fallback values if file missing)
            if (settings.LastNonMaximizedRect.Width == (int)(primaryArea.WorkArea.Width * 0.7) && 
                settings.LastNonMaximizedRect.Height == (int)(primaryArea.WorkArea.Height * 0.7) &&
                !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "window_settings.txt")))
            {
                return false;
            }

            _windowState.LastNonMaximizedRect = settings.LastNonMaximizedRect;
            appWindow.MoveAndResize(_windowState.LastNonMaximizedRect);

            if (settings.IsMaximized)
            {
                Activated += RestoreMaximizedStateOnce;
            }

            _sharpenEnabled = settings.SharpenEnabled;
            _isSideBySideMode = settings.IsSideBySideMode;
            _nextImageOnRight = settings.NextImageOnRight;
            SetTheme(settings.Theme);
            _matchControlDirection = settings.MatchControlDirection;
            _allowMultipleInstances = settings.AllowMultipleInstances;
            _windowState.IsSidebarVisible = settings.IsSidebarVisible;
            _windowState.IsPinned = settings.IsPinned;
            _windowState.IsAlwaysOnTop = settings.IsAlwaysOnTop;
            _autoDoublePageForArchive = settings.AutoDoublePageForArchive;
            _isRegistered = settings.IsRegistered;

            // Load Sharpen & Upscale Parameters
            _upscaleFactor = settings.UpscaleFactor;
            _sharpenAmountParam = settings.SharpenAmount;
            _sharpenThresholdParam = settings.SharpenThreshold;
            _unsharpAmount = settings.UnsharpAmount;
            _unsharpRadius = settings.UnsharpRadius;

            // Sync Sliders and Text
            if (UpscaleSlider != null) UpscaleSlider.Value = _upscaleFactor;
            if (SharpenSlider != null) SharpenSlider.Value = _sharpenAmountParam;
            if (SharpenThresholdSlider != null) SharpenThresholdSlider.Value = _sharpenThresholdParam;
            if (UnsharpAmountSlider != null) UnsharpAmountSlider.Value = _unsharpAmount;
            if (UnsharpRadiusSlider != null) UnsharpRadiusSlider.Value = _unsharpRadius;
            
            if (UpscaleValueText != null) UpscaleValueText.Text = $"{_upscaleFactor:F1}x";
            if (SharpenValueText != null) SharpenValueText.Text = $"{_sharpenAmountParam:F1}";
            if (SharpenThresholdValueText != null) SharpenThresholdValueText.Text = $"{_sharpenThresholdParam:F3}";
            if (UnsharpAmountValueText != null) UnsharpAmountValueText.Text = $"{_unsharpAmount:F1}";
            if (UnsharpRadiusValueText != null) UnsharpRadiusValueText.Text = $"{_unsharpRadius:F1}";

            // Attach events after setting initial values
            if (UpscaleSlider != null) UpscaleSlider.ValueChanged += SharpenParams_ValueChanged;
            if (SharpenSlider != null) SharpenSlider.ValueChanged += SharpenParams_ValueChanged;
            if (SharpenThresholdSlider != null) SharpenThresholdSlider.ValueChanged += SharpenParams_ValueChanged;
            if (UnsharpAmountSlider != null) UnsharpAmountSlider.ValueChanged += SharpenParams_ValueChanged;
            if (UnsharpRadiusSlider != null) UnsharpRadiusSlider.ValueChanged += SharpenParams_ValueChanged;

            if (MatchControlDirectionMenuItem != null) MatchControlDirectionMenuItem.IsChecked = _matchControlDirection;
            if (AllowMultipleInstancesMenuItem != null) AllowMultipleInstancesMenuItem.IsChecked = _allowMultipleInstances;
            if (AutoDoublePageForArchiveMenuItem != null) AutoDoublePageForArchiveMenuItem.IsChecked = _autoDoublePageForArchive;
            if (AlwaysOnTopButton != null) AlwaysOnTopButton.IsChecked = _windowState.IsAlwaysOnTop;
            if (appWindow != null && appWindow.Presenter is OverlappedPresenter op)
            {
                op.IsAlwaysOnTop = _windowState.IsAlwaysOnTop;
            }
            UpdateSharpenButtonState();
            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();

            return true;
        }

        private void RestoreMaximizedStateOnce(object sender, WindowActivatedEventArgs e)
        {
            // Only restore when window is activated (not deactivated)
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                return;

            Activated -= RestoreMaximizedStateOnce;
            try
            {
                if (_windowState.IsFullscreen) return;
                var appWindow = this.AppWindow;
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

        private void SaveWindowSettings()
        {
            var appWindow = this.AppWindow;
            bool isMaximized = false;

            if (_windowState.IsFullscreen)
            {
                isMaximized = _windowState.WasMaximizedBeforeFullscreen;
            }
            else if (appWindow.Presenter is OverlappedPresenter overlapped)
            {
                isMaximized = overlapped.State == OverlappedPresenterState.Maximized;
            }

            var settings = new AppSettings
            {
                LastNonMaximizedRect = _windowState.LastNonMaximizedRect,
                IsMaximized = isMaximized,
                SharpenEnabled = _sharpenEnabled,
                IsSideBySideMode = _isSideBySideMode,
                NextImageOnRight = _nextImageOnRight,
                Theme = _currentTheme,
                MatchControlDirection = _matchControlDirection,
                AllowMultipleInstances = _allowMultipleInstances,
                IsSidebarVisible = _windowState.IsSidebarVisible,
                IsPinned = _windowState.IsPinned,
                IsAlwaysOnTop = _windowState.IsAlwaysOnTop,
                AutoDoublePageForArchive = _autoDoublePageForArchive,
                IsRegistered = _isRegistered,
                UpscaleFactor = _upscaleFactor,
                SharpenAmount = _sharpenAmountParam,
                SharpenThreshold = _sharpenThresholdParam,
                UnsharpAmount = _unsharpAmount,
                UnsharpRadius = _unsharpRadius
            };

            _appSettingsService.SaveSettings(settings);
        }
        #endregion

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
            if (!_windowState.IsFullscreen)
            {
                // Exit fullscreen
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

            // [Important] Re-focus RootGrid after window state change
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

        public void ShowNotification(string message)
        {
            if (NotificationOverlay == null || NotificationText == null) return;

            NotificationText.Text = message;
            NotificationOverlay.Visibility = Visibility.Visible;
            _notificationTimer?.Stop();
            _notificationTimer?.Start();
        }

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

            SaveWindowSettings();
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
            SaveWindowSettings();
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

        private void SetTheme(ElementTheme theme)
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


        #region Navigation

        private async Task NavigateToPreviousAsync(bool isManualClick = false)
        {
            _pdfScrollDirection = -1;
            if (_currentIndex > 0)
            {
                bool isFast = !isManualClick && _fastNavigationService.DetectFastNavigation(ResetFastNavigation);

                int step = (_isCurrentViewSideBySide && _currentPdfDocument == null) ? 2 : 1;
                _currentIndex = FileExplorerService.GetNextImageIndex(_imageEntries, _currentIndex, step, false);

                if (isFast)
                {
                    UpdateFastNavigationUI();
                    return;
                }

                await DisplayCurrentImageAsync();

                // [최적화] 캐시된 저해상도 페이지라면 백그라운드에서 풀 해상도로 자동 업그레이드
                if (_currentPdfDocument != null)
                {
                    _preloadManager.ScheduleCurrentPageUpgradeIfNeeded(
                        _currentIndex,
                        () => _currentIndex,
                        _imageEntries, _zoomLevel, _currentBitmap,
                        (entry, token) => LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token, isPreload: false, isPreview: false),
                        () => MainCanvas?.Invalidate()
                    );
                }

                await AddToRecentAsync(true);

                // [최적화] 프리로드 재시작을 100ms 지연(디바운스)
                if (_currentArchive != null || _currentPdfDocument != null)
                {
                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: false);
                }
            }
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void SharpenParams_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (UpscaleSlider == null) return; // UI가 완전히 초기화되기 전이면 무시

            // 슬라이더 값 동기화
            _upscaleFactor = (float)UpscaleSlider.Value;
            _sharpenAmountParam = (float)SharpenSlider.Value;
            _sharpenThresholdParam = (float)SharpenThresholdSlider.Value;
            _unsharpAmount = (float)UnsharpAmountSlider.Value;
            _unsharpRadius = (float)UnsharpRadiusSlider.Value;

            // 텍스트 업데이트
            if (UpscaleValueText != null) UpscaleValueText.Text = $"{_upscaleFactor:F1}x";
            if (SharpenValueText != null) SharpenValueText.Text = $"{_sharpenAmountParam:F1}";
            if (SharpenThresholdValueText != null) SharpenThresholdValueText.Text = $"{_sharpenThresholdParam:F3}";
            if (UnsharpAmountValueText != null) UnsharpAmountValueText.Text = $"{_unsharpAmount:F1}";
            if (UnsharpRadiusValueText != null) UnsharpRadiusValueText.Text = $"{_unsharpRadius:F1}";

            // 샤프닝이 켜져있다면 즉시 캐시 지우고 화면 리렌더링
            if (_sharpenEnabled)
            {
                _imageCache?.ClearSharpenedCache(_currentBitmap, _leftBitmap, _rightBitmap);
                
            _animatedWebpService.Stop();

                // EPUB 및 텍스트 모드 이미지 캐시 초기화
                foreach (var bmp in _epubImageCache.Values)
                    if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
                _epubImageCache.Clear();

                foreach (var bmp in _verticalImageCache.Values)
                    if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
                _verticalImageCache.Clear();

                foreach (var bmp in _aozoraImageCache.Values)
                    if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
                _aozoraImageCache.Clear();
                
                if (_isEpubMode)
                {
                    if (CurrentEpubWin2DPage?.IsImagePage == true)
                    {
                        ShowEpubImagePage(CurrentEpubWin2DPage);
                    }
                    else
                    {
                        EpubTextCanvas?.Invalidate();
                    }
                }
                
                if (_isVerticalMode) VerticalTextCanvas?.Invalidate();
                if (_isAozoraMode) AozoraTextCanvas?.Invalidate();

                // 현재 이미지 다시 그리기
                _ = DisplayCurrentImageAsync();
            }
            
            // 변경사항 저장
            SaveWindowSettings();
        }

        private void SharpenParams_Reset_Click(object sender, RoutedEventArgs e)
        {
            if (UpscaleSlider != null) UpscaleSlider.Value = 2.0;
            if (SharpenSlider != null) SharpenSlider.Value = 1.0;
            if (SharpenThresholdSlider != null) SharpenThresholdSlider.Value = 0.01;
            if (UnsharpAmountSlider != null) UnsharpAmountSlider.Value = 2.0;
            if (UnsharpRadiusSlider != null) UnsharpRadiusSlider.Value = 1.0;
        }

        private async Task NavigateToNextAsync(bool isManualClick = false)
        {
            _pdfScrollDirection = 1;
            if (_currentIndex < _imageEntries.Count - 1)
            {
                bool isFast = !isManualClick && _fastNavigationService.DetectFastNavigation(ResetFastNavigation);

                int step = (_isCurrentViewSideBySide && _currentPdfDocument == null) ? 2 : 1;
                _currentIndex = FileExplorerService.GetNextImageIndex(_imageEntries, _currentIndex, step, true);

                if (isFast)
                {
                    UpdateFastNavigationUI();
                    return;
                }

                await DisplayCurrentImageAsync();

                // [최적화] 캐시된 저해상도 페이지라면 백그라운드에서 풀 해상도로 자동 업그레이드
                if (_currentPdfDocument != null)
                {
                    _preloadManager.ScheduleCurrentPageUpgradeIfNeeded(
                        _currentIndex,
                        () => _currentIndex,
                        _imageEntries, _zoomLevel, _currentBitmap,
                        (entry, token) => LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token, isPreload: false, isPreview: false),
                        () => MainCanvas?.Invalidate()
                    );
                }

                await AddToRecentAsync(true);

                // [최적화] 프리로드 재시작 디바운스 (PDF 한정)
                if (_currentArchive != null || _currentPdfDocument != null)
                {
                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: true);
                }
            }
            RootGrid.Focus(FocusState.Programmatic);
        }

        private string? GetCurrentNavigatingPath()
        {
            if (_isWebDavMode) return _currentWebDavItemPath;
            if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentArchivePath)) return _currentArchivePath;
            if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath)) return _currentEpubFilePath;
            if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath)) return _currentTextFilePath;
            if (_imageEntries != null && _imageEntries.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageEntries.Count) return _imageEntries[_currentIndex].FilePath;
            return null;
        }

        private async Task NavigateToFileAsync(bool isNext)
        {
            await AddToRecentAsync(true);
            string? currentPath = GetCurrentNavigatingPath();
            if (string.IsNullOrEmpty(currentPath)) return;

            var nextItem = FileExplorerService.GetNextNavigableFile(_fileItems, currentPath, isNext, _isWebDavMode);
            
            if (nextItem != null)
            {
                await HandleFileSelectionAsync(nextItem);
                SyncExplorerSelection(nextItem);
            }
            
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void SyncExplorerSelection(FileItem item)
        {
            if (_isExplorerGrid)
            {
                FileGridView.SelectedItem = item;
                FileGridView.ScrollIntoView(item);
            }
            else
            {
                FileListView.SelectedItem = item;
                FileListView.ScrollIntoView(item);
            }
        }

        private async Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, bool isPreview, CancellationToken token)
        {
            if (entry.IsPdfEntry && _currentPdfDocument != null)
            {
                return await LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token, isPreload: true, isPreview: isPreview);
            }
            else if (entry.FilePath != null)
            {
                return await LoadImageFromPathAsync(entry.FilePath, MainCanvas);
            }
            else if (entry.IsArchiveEntry && (_currentArchive != null || _current7zArchive != null))
            {
                return await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, MainCanvas, token);
            }
            else if (entry.IsWebDavEntry && _isWebDavMode && !token.IsCancellationRequested)
            {
                try
                {
                    var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                    if (!string.IsNullOrEmpty(tempPath) && !token.IsCancellationRequested)
                    {
                        entry.FilePath = tempPath;
                        return await LoadImageFromPathAsync(tempPath, MainCanvas);
                    }
                }
                catch { }
            }
            return null;
        }

        #endregion



        #region Win2D Canvas Event Handlers

        private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_currentBitmap != null)
            {
                try
                {
                    if (_currentBitmap.Device == null) return; // Disposed 체크

                    var ds = args.DrawingSession;
                    var canvasSize = sender.Size;
                    var imageSize = _currentBitmap.Size;

                    // Calculate fit ratio
                    var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);

                    // Apply zoom level on top of fit
                    var scaledSize = new Windows.Foundation.Size(imageSize.Width * fitRatio * _zoomLevel, imageSize.Height * fitRatio * _zoomLevel);

                    // Center the image
                    var position = new Windows.Foundation.Point(
                        (canvasSize.Width - scaledSize.Width) / 2,
                        (canvasSize.Height - scaledSize.Height) / 2);

                    if (_currentPdfDocument != null)
                    {
                        // PDF는 항상 연속 스크롤 모드 지원
                        double maxPan = Math.Max(0, (scaledSize.Height - canvasSize.Height) / 2);
                        double clampMargin = canvasSize.Height + 500; // 화면 높이 기반으로 제한 값 확장
                        if (_pdfPanY > maxPan + clampMargin) _pdfPanY = maxPan + clampMargin;
                        if (_pdfPanY < -maxPan - clampMargin) _pdfPanY = -maxPan - clampMargin;


                        // PDF Drawing with pan (X, Y)
                        position.X = (canvasSize.Width - scaledSize.Width) / 2 + _pdfPanX;
                        position.Y = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;
                        var destRect = new Windows.Foundation.Rect(position, scaledSize);

                        if (_currentBitmap.Device != null)
                        {
                            ds.DrawImage(_currentBitmap, destRect);
                        }

                        double gap = 20 * _zoomLevel;

                        // Draw previous pages (up to 5)
                        double currentY_top = position.Y;
                        for (int i = 1; i <= 5; i++)
                        {
                            int prevIdx = _currentIndex - i;
                            if (prevIdx < 0) break;

                            CanvasBitmap? prev = _imageCache.GetPreloadedImage(prevIdx);
                            if (prev != null && prev.Device != null && prev != _currentBitmap)
                            {
                                var pFit = Math.Min(canvasSize.Width / prev.Size.Width, canvasSize.Height / prev.Size.Height);
                                var pScaledSize = new Windows.Foundation.Size(prev.Size.Width * pFit * _zoomLevel, prev.Size.Height * pFit * _zoomLevel);
                                var pPos = new Windows.Foundation.Point((canvasSize.Width - pScaledSize.Width) / 2 + _pdfPanX, currentY_top - pScaledSize.Height - gap);
                                ds.DrawImage(prev, new Windows.Foundation.Rect(pPos, pScaledSize));
                                currentY_top = pPos.Y;

                                // Stop if even this page is way above screen
                                if (currentY_top + pScaledSize.Height < -500) break;
                            }
                            else if (prev == _currentBitmap) continue; // Skip duplicates
                            else break; // Missing preload, can't draw further
                        }

                        // Draw next pages (up to 5)
                        double currentY_bottom = position.Y + scaledSize.Height;
                        for (int i = 1; i <= 5; i++)
                        {
                            int nextIdx = _currentIndex + i;
                            if (nextIdx >= _imageEntries.Count) break;

                            CanvasBitmap? next = _imageCache.GetPreloadedImage(nextIdx);
                            if (next != null && next.Device != null && next != _currentBitmap)
                            {
                                var nFit = Math.Min(canvasSize.Width / next.Size.Width, canvasSize.Height / next.Size.Height);
                                var nScaledSize = new Windows.Foundation.Size(next.Size.Width * nFit * _zoomLevel, next.Size.Height * nFit * _zoomLevel);
                                var nPos = new Windows.Foundation.Point((canvasSize.Width - nScaledSize.Width) / 2 + _pdfPanX, currentY_bottom + gap);
                                ds.DrawImage(next, new Windows.Foundation.Rect(nPos, nScaledSize));
                                currentY_bottom = nPos.Y + nScaledSize.Height;

                                // Stop if even this page is way below screen
                                if (nPos.Y > canvasSize.Height + 500) break;
                            }
                            else if (next == _currentBitmap) continue; // Skip duplicates
                            else break;
                        }
                    }
                    else
                    {
                        var destRect = new Windows.Foundation.Rect(position, scaledSize);
                        ds.DrawImage(_currentBitmap, destRect, _currentBitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
                    }
                }
                catch (Exception)
                {
                    // 그리는 도중 이미지가 해제되면 무시
                }
            }
        }

        private void LeftCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void LeftCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_leftBitmap != null)
            {
                try
                {
                    if (_leftBitmap.Device == null) return; // Disposed 체크

                    var ds = args.DrawingSession;
                    var canvasSize = sender.Size;
                    var imageSize = _leftBitmap.Size;

                    // Calculate fit ratio for full canvas width (each canvas is already half the screen)
                    var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);

                    // Apply zoom level on top of fit
                    var scaledSize = new Windows.Foundation.Size(imageSize.Width * fitRatio * _zoomLevel, imageSize.Height * fitRatio * _zoomLevel);

                    // Align to RIGHT edge (to make images touch in the center)
                    var position = new Windows.Foundation.Point(
                        canvasSize.Width - scaledSize.Width,
                        (canvasSize.Height - scaledSize.Height) / 2);

                    if (_currentPdfDocument != null && scaledSize.Height > canvasSize.Height)
                    {
                        double maxPan = (scaledSize.Height - canvasSize.Height) / 2;
                        if (_pdfPanY > maxPan + 350) _pdfPanY = maxPan + 350;
                        if (_pdfPanY < -maxPan - 350) _pdfPanY = -maxPan - 350;
                        position.Y = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;

                        // Seamless adjacent pages in side-by-side mode (Left Canvas: draw previous if exists)
                        double gap = 20 * _zoomLevel;
                        if (position.Y > 0 && _currentIndex >= 2)
                        {
                            CanvasBitmap? prevLeft = _imageCache.GetPreloadedImage(_currentIndex - 2);
                            if (prevLeft != null)
                            {
                                var pFit = Math.Min(canvasSize.Width / prevLeft.Size.Width, canvasSize.Height / prevLeft.Size.Height);
                                var pScaledH = prevLeft.Size.Height * pFit * _zoomLevel;
                                var pPos = new Windows.Foundation.Point(canvasSize.Width - (prevLeft.Size.Width * pFit * _zoomLevel), position.Y - pScaledH - gap);
                                ds.DrawImage(prevLeft, new Windows.Foundation.Rect(pPos, new Windows.Foundation.Size(prevLeft.Size.Width * pFit * _zoomLevel, pScaledH)));
                            }
                        }
                        if (position.Y + scaledSize.Height < canvasSize.Height && _currentIndex + 2 < _imageEntries.Count)
                        {
                            CanvasBitmap? nextLeft = _imageCache.GetPreloadedImage(_currentIndex + 2);
                            if (nextLeft != null)
                            {
                                var nFit = Math.Min(canvasSize.Width / nextLeft.Size.Width, canvasSize.Height / nextLeft.Size.Height);
                                var nScaledH = nextLeft.Size.Height * nFit * _zoomLevel;
                                var nPos = new Windows.Foundation.Point(canvasSize.Width - (nextLeft.Size.Width * nFit * _zoomLevel), position.Y + scaledSize.Height + gap);
                                ds.DrawImage(nextLeft, new Windows.Foundation.Rect(nPos, new Windows.Foundation.Size(nextLeft.Size.Width * nFit * _zoomLevel, nScaledH)));
                            }
                        }
                    }

                    var destRect = new Windows.Foundation.Rect(position, scaledSize);
                    if (_currentPdfDocument == null)
                        ds.DrawImage(_leftBitmap, destRect, _leftBitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
                    else
                        ds.DrawImage(_leftBitmap, destRect);
                }
                catch (Exception) { }
            }
        }

        private void RightCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            // Resources will be created as needed
        }

        private void RightCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_rightBitmap != null)
            {
                try
                {
                    if (_rightBitmap.Device == null) return; // Disposed 체크

                    var ds = args.DrawingSession;
                    var canvasSize = sender.Size;
                    var imageSize = _rightBitmap.Size;

                    // Calculate fit ratio for full canvas width (each canvas is already half the screen)
                    var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);

                    // Apply zoom level on top of fit
                    var scaledSize = new Windows.Foundation.Size(imageSize.Width * fitRatio * _zoomLevel, imageSize.Height * fitRatio * _zoomLevel);

                    // Align to LEFT edge (to make images touch in the center)
                    var position = new Windows.Foundation.Point(
                        0,
                        (canvasSize.Height - scaledSize.Height) / 2);

                    if (_currentPdfDocument != null && scaledSize.Height > canvasSize.Height)
                    {
                        double maxPan = (scaledSize.Height - canvasSize.Height) / 2;
                        if (_pdfPanY > maxPan + 350) _pdfPanY = maxPan + 350;
                        if (_pdfPanY < -maxPan - 350) _pdfPanY = -maxPan - 350;
                        position.Y = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;
                        double gap = 20 * _zoomLevel;
                        int prevRightIndex = _currentIndex - 1;

                        if (position.Y > 0 && prevRightIndex >= 0)
                        {
                            CanvasBitmap? prevRight = _imageCache.GetPreloadedImage(prevRightIndex);
                            if (prevRight != null)
                            {
                                var pFit = Math.Min(canvasSize.Width / prevRight.Size.Width, canvasSize.Height / prevRight.Size.Height);
                                var pScaledH = prevRight.Size.Height * pFit * _zoomLevel;
                                var pPos = new Windows.Foundation.Point(0, position.Y - pScaledH - gap);
                                ds.DrawImage(prevRight, new Windows.Foundation.Rect(pPos, new Windows.Foundation.Size(prevRight.Size.Width * pFit * _zoomLevel, pScaledH)));
                            }
                        }

                        int nextRightIndex = _currentIndex + 3;
                        if (position.Y + scaledSize.Height < canvasSize.Height && nextRightIndex < _imageEntries.Count)
                        {
                            CanvasBitmap? nextRight = _imageCache.GetPreloadedImage(nextRightIndex);
                            if (nextRight != null)
                            {
                                var nFit = Math.Min(canvasSize.Width / nextRight.Size.Width, canvasSize.Height / nextRight.Size.Height);
                                var nScaledH = nextRight.Size.Height * nFit * _zoomLevel;
                                var nPos = new Windows.Foundation.Point(0, position.Y + scaledSize.Height + gap);
                                ds.DrawImage(nextRight, new Windows.Foundation.Rect(nPos, new Windows.Foundation.Size(nextRight.Size.Width * nFit * _zoomLevel, nScaledH)));
                            }
                        }
                    }

                    var destRect = new Windows.Foundation.Rect(position, scaledSize);
                    if (_currentPdfDocument == null)
                        ds.DrawImage(_rightBitmap, destRect, _rightBitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
                    else
                        ds.DrawImage(_rightBitmap, destRect);
                }
                catch (Exception) { }
            }
        }

        #endregion

        #region IKeyboardShortcutActions Implementation

        bool IKeyboardShortcutActions.IsColorPickerOpen => _isColorPickerOpen;
        bool IKeyboardShortcutActions.IsFullscreen => _windowState.IsFullscreen;
        bool IKeyboardShortcutActions.IsEpubMode => _isEpubMode;
        bool IKeyboardShortcutActions.IsVerticalMode 
        { 
            get => _isVerticalMode; 
            set 
            { 
                _isVerticalMode = value;
                if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = value;
            }
        }
        bool IKeyboardShortcutActions.IsTextMode => _isTextMode;
        bool IKeyboardShortcutActions.IsAozoraMode => _isAozoraMode;
        bool IKeyboardShortcutActions.ShouldInvertControls => this.ShouldInvertControls;
        int IKeyboardShortcutActions.CurrentEpubChapterIndex 
        { 
            get => _currentEpubChapterIndex; 
            set => _currentEpubChapterIndex = value; 
        }
        int IKeyboardShortcutActions.EpubSpineCount => _epubSpine.Count;
        int IKeyboardShortcutActions.CurrentImageIndex 
        { 
            get => _currentIndex; 
            set => _currentIndex = value; 
        }
        int IKeyboardShortcutActions.ImageEntriesCount => _imageEntries.Count;
        bool IKeyboardShortcutActions.HasPdfDocument => _currentPdfDocument != null;
        bool IKeyboardShortcutActions.IsSharpenEnabled 
        { 
            get => _sharpenEnabled; 
            set => _sharpenEnabled = value; 
        }
        bool IKeyboardShortcutActions.IsAboutDialogActive => _aboutDialog != null;

        void IKeyboardShortcutActions.ToggleFullscreen() => ToggleFullscreen();
        void IKeyboardShortcutActions.CloseApp() => CloseWindowButton_Click(CloseWindowButton, new RoutedEventArgs());
        void IKeyboardShortcutActions.NavigateVerticalPage(int offset) => NavigateVerticalPage(offset);
        Task IKeyboardShortcutActions.NavigateEpubAsync(int offset) => NavigateEpubAsync(offset);
        Task IKeyboardShortcutActions.ShowEpubGoToLineDialog() => ShowEpubGoToLineDialog();
        void IKeyboardShortcutActions.ToggleFont() => ToggleFont();
        void IKeyboardShortcutActions.ToggleVerticalMode()
        {
            _isVerticalMode = !_isVerticalMode;
            if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = _isVerticalMode;
            SaveTextSettings();
            ToggleVerticalMode();
        }
        void IKeyboardShortcutActions.SaveTextSettings() => SaveTextSettings();
        void IKeyboardShortcutActions.DecreaseTextSize() => DecreaseTextSize();
        void IKeyboardShortcutActions.IncreaseTextSize() => IncreaseTextSize();
        void IKeyboardShortcutActions.ToggleSidebar() => ToggleSidebar();
        void IKeyboardShortcutActions.ToggleTheme() => ToggleTheme();
        Task IKeyboardShortcutActions.LoadEpubChapterAsync(int index) => LoadEpubChapterAsync(index);
        void IKeyboardShortcutActions.ToggleSideBySide() => SideBySideButton_Click(SideBySideButton, new RoutedEventArgs());
        Task IKeyboardShortcutActions.NavigateToNextAsync(bool handled) => NavigateToNextAsync(handled);
        Task IKeyboardShortcutActions.NavigateToPreviousAsync(bool handled) => NavigateToPreviousAsync(handled);
        Task IKeyboardShortcutActions.DisplayCurrentImageAsync() => DisplayCurrentImageAsync();
        Task IKeyboardShortcutActions.NavigateToFileAsync(bool forward) => NavigateToFileAsync(forward);
        Task IKeyboardShortcutActions.AddToFavoritesAsync() => AddToFavoritesAsync();
        void IKeyboardShortcutActions.ToggleSharpening()
        {
            SharpenButton.IsChecked = !(SharpenButton.IsChecked ?? false);
            SharpenButton_Click(SharpenButton, new RoutedEventArgs());
        }
        Task IKeyboardShortcutActions.ShowGoToLineDialog() => ShowGoToLineDialog();
        Task IKeyboardShortcutActions.NavigateToParentFolderAsync() => NavigateToParentFolderAsync();
        Task IKeyboardShortcutActions.OpenFileAsync() => OpenFileAsync();
        void IKeyboardShortcutActions.ZoomIn() => ZoomIn();
        void IKeyboardShortcutActions.ZoomOut() => ZoomOut();
        void IKeyboardShortcutActions.FitToWindow() => FitToWindow();
        void IKeyboardShortcutActions.ZoomActual() => ZoomActualButton_Click(ZoomActualButton, new RoutedEventArgs());
        void IKeyboardShortcutActions.ToggleAlwaysOnTop() => ToggleAlwaysOnTop();
        void IKeyboardShortcutActions.ToggleGlobalTheme() => GlobalThemeToggleButton_Click(GlobalThemeToggleButton, new RoutedEventArgs());
        void IKeyboardShortcutActions.TogglePin() => TogglePin();
        void IKeyboardShortcutActions.HideAboutDialog()
        {
            if (_aboutDialog != null)
            {
                _aboutDialog.Hide();
                _aboutDialog = null;
            }
        }

        #endregion
    }
}
