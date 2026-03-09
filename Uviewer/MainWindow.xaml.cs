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
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private List<ImageEntry> _imageEntries = new();
        private int _currentIndex = -1;
        private double _zoomLevel = 1.0;
        private CanvasBitmap? _currentBitmap;
        private const double ZoomStep = 0.25;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 10.0;

        // Folder explorer
        private string? _currentExplorerPath;
        private ObservableCollection<FileItem> _fileItems = new();
        private bool _isExplorerGrid = false;

        // Fullscreen
        private bool _isFullscreen = false;
        private bool _wasMaximizedBeforeFullscreen = false;
        private bool _isSidebarVisible = true;
        private const double FullscreenTopHoverZone = 80;
        private const double FullscreenLeftHoverZone = 60;
        private const double FullscreenSidebarKeepZoneRight = 300;
        private const int FullscreenHideDelayMs = 1000;
        private DispatcherQueueTimer? _fullscreenToolbarHideTimer;
        private DispatcherQueueTimer? _fullscreenSidebarHideTimer;
        private bool _toolbarHideTimerRunning = false;
        private bool _sidebarHideTimerRunning = false;
        private int _SidebarWidth = 320;

        private DispatcherQueueTimer? _notificationTimer;

        private Windows.Graphics.RectInt32 _lastNonMaximizedRect = new(100, 100, 1200, 800);

        // Side-by-side view settings
        private bool _isSideBySideMode = false;
        private bool _nextImageOnRight = true;
        private ElementTheme _currentTheme = ElementTheme.Default;
        private CanvasBitmap? _leftBitmap;
        private CanvasBitmap? _rightBitmap;
        private bool _matchControlDirection = false;
        private int _pendingPdfPageIndex = -1;
        private double _pdfPanY = 0;
        private double _pdfPanX = 0;
        private double _lastCanvasWidth = 0;
        private bool _isPdfTransitioning = false;
        private int _pdfScrollDirection = 1; // 1 for next (start top), -1 for prev (start bottom)
        private bool _isSeamlessScroll = false;
        private bool _allowMultipleInstances = true;
        private bool _isPinned = true; // Pin toggle: true = UI fixed, false = auto-hide
        private bool _isAlwaysOnTop = false;

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
        private readonly Dictionary<int, CanvasBitmap> _preloadedImages = new();
        private readonly HashSet<int> _loadingIndices = new();
        private const int PreloadCount = 5; // Number of images to preload ahead
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4); // Limit concurrent thumbnail loads (archives)
        private CancellationTokenSource? _preloadCts;

        private static readonly string[] SupportedImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".jxl", ".ico", ".tiff", ".tif"
        };

        private static readonly string[] SupportedTextExtensions =
        {
            ".txt", ".html", ".htm", ".md", ".xml"
        };

        private static readonly string[] SupportedArchiveExtensions =
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".cbz", ".cbr"
        };

        private static readonly string[] SupportedEpubExtensions =
        {
            ".epub"
        };

        private static readonly string[] SupportedPdfExtensions =
        {
            ".pdf"
        };

        private IEnumerable<string> SupportedFileExtensions =>
            SupportedImageExtensions.Concat(SupportedTextExtensions).Concat(SupportedArchiveExtensions).Concat(SupportedEpubExtensions).Concat(SupportedPdfExtensions);

        private bool IsTextEntry(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath);
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey);

            return !string.IsNullOrEmpty(ext) && SupportedTextExtensions.Contains(ext.ToLowerInvariant());
        }

        private bool IsEpubEntry(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath);
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey);

            return !string.IsNullOrEmpty(ext) && SupportedEpubExtensions.Contains(ext.ToLowerInvariant());
        }

        private bool IsPdfEntry(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath);
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey);

            return !string.IsNullOrEmpty(ext) && SupportedPdfExtensions.Contains(ext.ToLowerInvariant()) || entry.IsPdfEntry;
        }

        private bool IsImageEntry(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath);
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey);

            return !string.IsNullOrEmpty(ext) && SupportedImageExtensions.Contains(ext.ToLowerInvariant());
        }

        // File item for ListView
        public class FileItem : INotifyPropertyChanged
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public bool IsDirectory { get; set; }
            public bool IsArchive { get; set; }
            public bool IsImage { get; set; }
            public bool IsText { get; set; }
            public bool IsEpub { get; set; }
            public bool IsPdf { get; set; }
            public bool IsParentDirectory { get; set; }
            public bool IsWebDav { get; set; }
            public string? WebDavPath { get; set; }

            private ImageSource? _thumbnail;
            public ImageSource? Thumbnail
            {
                get => _thumbnail;
                set
                {
                    if (_thumbnail != value)
                    {
                        _thumbnail = value;
                        OnPropertyChanged();
                    }
                }
            }

            public string Icon => IsParentDirectory ? "\uE72B" :
                                  IsDirectory ? "\uE8B7" :
                                  IsArchive ? "\uE8D4" :
                                  IsEpub ? "\uE82D" : // Book icon
                                  IsPdf ? "\uEA90" : // PDF icon (Pdf icon glyph usually \uEA90 or Document \uE8A5)
                                  IsImage ? "\uE8B9" :
                                  IsText ? "\uE8C4" : "\uE7C3";

            public SolidColorBrush IconColor => IsDirectory || IsParentDirectory ?
                new SolidColorBrush(Colors.Gold) :
                IsArchive ? new SolidColorBrush(Colors.Orange) :
                IsEpub ? new SolidColorBrush(Colors.MediumPurple) :
                IsPdf ? new SolidColorBrush(Colors.IndianRed) :
                IsImage ? new SolidColorBrush(Colors.CornflowerBlue) :
                IsText ? new SolidColorBrush(Colors.LightGreen) :
                new SolidColorBrush(Colors.Gray);

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Represents an image entry (either file or archive entry)
        private class ImageEntry
        {
            public string DisplayName { get; set; } = "";
            public string? FilePath { get; set; }
            public string? ArchiveEntryKey { get; set; }
            public bool IsArchiveEntry => ArchiveEntryKey != null;
            public bool IsPdfEntry { get; set; } = false;
            public uint PdfPageIndex { get; set; } = 0;
            public string? WebDavPath { get; set; }
            public bool IsWebDavEntry => WebDavPath != null;
        }



        [System.Text.Json.Serialization.JsonSerializable(typeof(List<FavoriteItem>))]
        public partial class FavoritesContext : System.Text.Json.Serialization.JsonSerializerContext;

        [System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentItem>))]
        public partial class RecentContext : System.Text.Json.Serialization.JsonSerializerContext;

        public async Task InitializeAsync(string? launchFilePath = null)
        {
            try
            {
                // Always load metadata first to prevent race conditions and data loss
                await LoadFavorites();
                await LoadRecentItems();
                UpdateFavoritesMenu();
                UpdateRecentMenu();

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
                        if (SupportedArchiveExtensions.Contains(extension))
                        {
                            await LoadImagesFromArchiveAsync(launchFilePath);
                        }
                        else if (SupportedPdfExtensions.Contains(extension))
                        {
                            await LoadImagesFromPdfAsync(launchFilePath);
                        }
                        else if (SupportedEpubExtensions.Contains(extension))
                        {
                            var file = await StorageFile.GetFileFromPathAsync(launchFilePath);
                            await LoadEpubFileAsync(file);
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

                // Load saved window position, size and maximized state
                var appWindow2 = this.AppWindow;
                appWindow2.Changed += AppWindow_Changed;

                // Load saved window position, size and maximized state
                bool hasLoadedSettings = LoadWindowSettings(appWindow2);
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
                    _lastNonMaximizedRect = new Windows.Graphics.RectInt32(centerX, centerY, defaultSize.Width, defaultSize.Height);
                }

                // Initialize button states
                UpdateSideBySideButtonState();
                UpdateNextImageSideButtonState();
                UpdateSharpenButtonState();

                // Apply saved sidebar visibility state
                if (!_isSidebarVisible)
                {
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                }

                // Apply saved pin state
                if (!_isPinned)
                {
                    PinButton.IsChecked = false;
                    PinIcon.Glyph = "\uE77A"; // Unpin icon
                    AppTitleBar.Visibility = Visibility.Collapsed;
                    ToolbarGrid.Visibility = Visibility.Collapsed;
                    StatusBarGrid.Visibility = Visibility.Collapsed;
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                }

                // Enable keyboard shortcuts on the root content to ensure they catch everything
                if (this.Content is FrameworkElement fe)
                {
                    fe.PreviewKeyDown += RootGrid_PreviewKeyDown;
                    fe.KeyDown += RootGrid_KeyDown;
                }

                // Initialize file list
                FileListView.ItemsSource = _fileItems;
                FileGridView.ItemsSource = _fileItems;

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
                try
                {
                    // Stop all timers
                    _fullscreenToolbarHideTimer?.Stop();
                    _fullscreenSidebarHideTimer?.Stop();
                    _animatedWebpTimer?.Stop();
                    _fastNavOverlayTimer?.Stop();

                    // Cancel any ongoing operations
                    _imageLoadingCts?.Cancel();
                    _fastNavigationResetCts?.Cancel();

                    // Clean up archive resources
                    if (_currentArchive != null)
                    {
                        try
                        {
                            _currentArchive.Dispose();
                            _currentArchive = null;
                        }
                        catch { }
                    }

                    // Clean up cached bitmaps
                    _currentBitmap?.Dispose();
                    _currentBitmap = null;
                    _leftBitmap?.Dispose();
                    _leftBitmap = null;
                    _rightBitmap?.Dispose();
                    _rightBitmap = null;

                    lock (_preloadedImages)
                    {
                        foreach (var img in _preloadedImages.Values)
                        {
                            img?.Dispose();
                        }
                        _preloadedImages.Clear();
                    }

                    lock (_sharpenedImageCache)
                    {
                        foreach (var img in _sharpenedImageCache.Values)
                        {
                            img?.Dispose();
                        }
                        _sharpenedImageCache.Clear();
                    }

                    // Save settings
                    SaveWindowSettings();
                    // Save current position before closing
                    await AddToRecentAsync(true);

                    await SaveRecentItems();
                    await SaveFavorites();

                    // Dispose semaphores
                    _archiveLock.Dispose();
                    _thumbnailSemaphore.Dispose();

                    // Cleanup WebDAV
                    _webDavService?.Dispose();
                    _webDavCts?.Cancel();
                    _webDavCts?.Dispose();

                    // Dispose cancellation tokens
                    _imageLoadingCts?.Dispose();
                    _fastNavigationResetCts?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
                }
            };

            // Initialize fullscreen timers with proper settings
            _fullscreenToolbarHideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _fullscreenToolbarHideTimer.Interval = TimeSpan.FromMilliseconds(FullscreenHideDelayMs);
            _fullscreenToolbarHideTimer.IsRepeating = false;
            _fullscreenToolbarHideTimer.Tick += (s, e) =>
            {
                _toolbarHideTimerRunning = false;
                if (_isFullscreen || !_isPinned)
                {
                    AppTitleBar.Visibility = Visibility.Collapsed;
                    ToolbarGrid.Visibility = Visibility.Collapsed;
                    if (!_isFullscreen) StatusBarGrid.Visibility = Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine("✓ Titlebar and Toolbar hidden by timer");
                }
            };

            _fullscreenSidebarHideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _fullscreenSidebarHideTimer.Interval = TimeSpan.FromMilliseconds(FullscreenHideDelayMs);
            _fullscreenSidebarHideTimer.IsRepeating = false;
            _fullscreenSidebarHideTimer.Tick += (s, e) =>
            {
                _sidebarHideTimerRunning = false;
                if (_isFullscreen || !_isPinned)
                {
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine("✓ Sidebar hidden by timer");
                }
            };

            // Initialize animated WebP timer
            _animatedWebpTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _animatedWebpTimer.Interval = TimeSpan.FromMilliseconds(100);
            _animatedWebpTimer.Tick += AnimatedWebpTimer_Tick;

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

            UpdateLanguageMenuCheckmark();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                if (args.DidSizeChange) TriggerEpubResize();

                if (sender.Presenter is OverlappedPresenter overlapped)
                {
                    // [수정] Restored 상태여야 함은 물론, 시스템 좌표(-8, -8 등)가 튀는 것을 방지
                    if (overlapped.State == OverlappedPresenterState.Restored)
                    {
                        var pos = sender.Position;
                        var size = sender.Size;

                        // 비정상적인 크기나 위치는 무시 (최소 크기 가드)
                        if (size.Width >= 100 && size.Height >= 100)
                        {
                            // 최대화 시 발생하는 시스템 좌표(-8, -8)가 Restored 상태로 보고되는 경우가 있으므로 
                            // 해당 좌표가 실제 화면 영역 내에 유효하게 걸쳐있는지 최종 확인
                            var currentRect = new Windows.Graphics.RectInt32(pos.X, pos.Y, size.Width, size.Height);
                            var area = Microsoft.UI.Windowing.DisplayArea.GetFromRect(currentRect, Microsoft.UI.Windowing.DisplayAreaFallback.None);
                            
                            if (area != null)
                            {
                                // [수정] 현재 모니터의 WorkArea보다 살짝이라도 벗어나는(음수 좌표 등) 경우는 
                                // 시스템이 최대화/전환을 위해 일시적으로 설정한 좌표일 확률이 높으므로 저장하지 않습니다.
                                if (pos.X >= area.WorkArea.X && pos.Y >= area.WorkArea.Y &&
                                    size.Width <= area.WorkArea.Width && size.Height <= area.WorkArea.Height)
                                {
                                    _lastNonMaximizedRect = currentRect;
                                }
                            }
                        }
                    }
                }

                // [Important] Re-focus RootGrid after window state changes (Maximize/Restore/Resize)
                // This ensures keyboard shortcuts keep working without an extra click.
                RootGrid?.Focus(FocusState.Programmatic);
            }
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

        private void ToggleFullscreen()
        {
            var appWindow = this.AppWindow;

            if (_isFullscreen)
            {
                // Exit fullscreen
                appWindow.SetPresenter(AppWindowPresenterKind.Default);
                _isFullscreen = false;

                // Restore Always on Top state
                if (appWindow.Presenter is OverlappedPresenter op)
                {
                    op.IsAlwaysOnTop = _isAlwaysOnTop;
                }

                if (_isPinned)
                {
                    // 핀 고정 상태: UI 모두 복원
                    AppTitleBar.Visibility = Visibility.Visible;
                    ToolbarGrid.Visibility = Visibility.Visible;
                    StatusBarGrid.Visibility = Visibility.Visible;
                    if (_isSidebarVisible)
                    {
                        SidebarGrid.Visibility = Visibility.Visible;
                        SplitterGrid.Visibility = Visibility.Visible;
                        SidebarColumn.Width = new GridLength(_SidebarWidth);
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
                if (appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    _wasMaximizedBeforeFullscreen = overlapped.State == OverlappedPresenterState.Maximized;
                }
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                AppTitleBar.Visibility = Visibility.Collapsed;
                ToolbarGrid.Visibility = Visibility.Collapsed;
                StatusBarGrid.Visibility = Visibility.Collapsed;
                SidebarGrid.Visibility = Visibility.Collapsed;
                if (_isSidebarVisible && (int)SidebarColumn.Width.Value > 200)
                {
                    _SidebarWidth = (int)SidebarColumn.Width.Value; // Save current width
                }
                SidebarColumn.Width = new GridLength(0);
                SplitterGrid.Visibility = Visibility.Collapsed;  // Hide splitter in fullscreen
                FullscreenIcon.Glyph = "\uE73F"; // Exit fullscreen icon
                _isFullscreen = true;
                StopFullscreenHoverTimers();
            }

            // [Important] Re-focus RootGrid after window state change
            RootGrid?.Focus(FocusState.Programmatic);
        }

        private void StopFullscreenHoverTimers()
        {
            if (_toolbarHideTimerRunning)
            {
                _fullscreenToolbarHideTimer?.Stop();
                _toolbarHideTimerRunning = false;
                System.Diagnostics.Debug.WriteLine("■ Toolbar timer STOPPED");
            }

            if (_sidebarHideTimerRunning)
            {
                _fullscreenSidebarHideTimer?.Stop();
                _sidebarHideTimerRunning = false;
                System.Diagnostics.Debug.WriteLine("■ Sidebar timer STOPPED");
            }
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isFullscreen && _isPinned) return;

            var pt = e.GetCurrentPoint(RootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            bool inTopZone = y < FullscreenTopHoverZone;
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
                    ToolbarGrid.Visibility = Visibility.Visible;
                    if (!_isFullscreen) StatusBarGrid.Visibility = Visibility.Visible;
                    System.Diagnostics.Debug.WriteLine("Titlebar and Toolbar SHOWN (mouse in top zone)");
                }
                if (_toolbarHideTimerRunning)
                {
                    _fullscreenToolbarHideTimer?.Stop();
                    _toolbarHideTimerRunning = false;
                    System.Diagnostics.Debug.WriteLine("Toolbar timer STOPPED (mouse in top zone)");
                }
            }
            else
            {
                // Start hide timer only if not already running
                if (ToolbarGrid.Visibility == Visibility.Visible && !_toolbarHideTimerRunning)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempting to start toolbar timer (y={y})");
                    StartOrRestartFullscreenToolbarHideTimer();
                }
            }

            bool inLeftZone = _isSidebarVisible && x < FullscreenLeftHoverZone;
            if (SidebarGrid.Visibility == Visibility.Visible && x < _SidebarWidth)
            {
                inLeftZone = true;
            }

            if (_isSidebarVisible && inLeftZone)
            {
                // Show sidebar and stop hide timer while in hover zone
                if (SidebarGrid.Visibility != Visibility.Visible)
                {
                    SidebarColumn.Width = new GridLength(_SidebarWidth);
                    SidebarGrid.Visibility = Visibility.Visible;
                    System.Diagnostics.Debug.WriteLine("Sidebar SHOWN (mouse in left zone)");
                }
                if (_sidebarHideTimerRunning)
                {
                    _fullscreenSidebarHideTimer?.Stop();
                    _sidebarHideTimerRunning = false;
                    System.Diagnostics.Debug.WriteLine("Sidebar timer STOPPED (mouse in left zone)");
                }
            }
            else
            {
                // Start hide timer only if not already running
                if (SidebarGrid.Visibility == Visibility.Visible && !_sidebarHideTimerRunning)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempting to start sidebar timer (x={x})");
                    StartOrRestartFullscreenSidebarHideTimer();
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
            if (!_isFullscreen && _isPinned) return;

            if (ToolbarGrid.Visibility == Visibility.Visible && !_toolbarHideTimerRunning)
            {
                StartOrRestartFullscreenToolbarHideTimer();
            }

            if (SidebarGrid.Visibility == Visibility.Visible && !_sidebarHideTimerRunning)
            {
                StartOrRestartFullscreenSidebarHideTimer();
            }
        }

        private void StartOrRestartFullscreenToolbarHideTimer()
        {
            if (_toolbarHideTimerRunning)
            {
                System.Diagnostics.Debug.WriteLine("⚠ Toolbar timer already running, ignoring");
                return;
            }

            _toolbarHideTimerRunning = true;
            _fullscreenToolbarHideTimer?.Start();
            System.Diagnostics.Debug.WriteLine($"▶ Toolbar hide timer STARTED (will hide in {FullscreenHideDelayMs}ms)");
        }

        private void StartOrRestartFullscreenSidebarHideTimer()
        {
            if (_sidebarHideTimerRunning)
            {
                System.Diagnostics.Debug.WriteLine("⚠ Sidebar timer already running, ignoring");
                return;
            }

            _sidebarHideTimerRunning = true;
            _fullscreenSidebarHideTimer?.Start();
            System.Diagnostics.Debug.WriteLine($"▶ Sidebar hide timer STARTED (will hide in {FullscreenHideDelayMs}ms)");
        }

        // Unified Touch Handler for Text, Aozora, and Epub modes
        private void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction)
        {
            var pt = e.GetCurrentPoint(RootGrid);
            double x = pt.Position.X;
            double y = pt.Position.Y;

            // 1. Edge Detection (Fullscreen only)
            if (_isFullscreen)
            {
                // Top Edge -> Show Toolbar
                if (y < FullscreenTopHoverZone)
                {
                    if (ToolbarGrid.Visibility != Visibility.Visible)
                    {
                        ToolbarGrid.Visibility = Visibility.Visible;
                    }
                    StartOrRestartFullscreenToolbarHideTimer();
                    return;
                }

                // Left Edge -> Show Sidebar
                if (x < FullscreenLeftHoverZone)
                {
                    if (SidebarGrid.Visibility != Visibility.Visible)
                    {
                        SidebarColumn.Width = new GridLength(_SidebarWidth);
                        SidebarGrid.Visibility = Visibility.Visible;
                    }
                    StartOrRestartFullscreenSidebarHideTimer();
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
            if (_isFullscreen) return; // 전체화면에서는 핀 모드 불필요

            _isPinned = !_isPinned;
            PinButton.IsChecked = _isPinned;
            PinIcon.Glyph = _isPinned ? "\uE890" : "\uE890"; // Eye / EyeOff icon

            if (_isPinned)
            {
                // 핀 고정: UI 모두 표시
                _fullscreenToolbarHideTimer?.Stop();
                _toolbarHideTimerRunning = false;
                _fullscreenSidebarHideTimer?.Stop();
                _sidebarHideTimerRunning = false;

                AppTitleBar.Visibility = Visibility.Visible;
                ToolbarGrid.Visibility = Visibility.Visible;
                StatusBarGrid.Visibility = Visibility.Visible;
                if (_isSidebarVisible)
                {
                    SidebarGrid.Visibility = Visibility.Visible;
                    if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Visible;
                    SidebarColumn.Width = new GridLength(_SidebarWidth);
                }
            }
            else
            {
                // 핀 해제: UI 모두 숨김
                if (_isSidebarVisible && (int)SidebarColumn.Width.Value > 200)
                {
                    _SidebarWidth = (int)SidebarColumn.Width.Value;
                }

                AppTitleBar.Visibility = Visibility.Collapsed;
                ToolbarGrid.Visibility = Visibility.Collapsed;
                StatusBarGrid.Visibility = Visibility.Collapsed;
                SidebarGrid.Visibility = Visibility.Collapsed;
                if (SplitterGrid != null) SplitterGrid.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
            }

            SaveWindowSettings();
        }

        private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAlwaysOnTop();
        }

        private void ToggleAlwaysOnTop()
        {
            _isAlwaysOnTop = !_isAlwaysOnTop;
            if (AlwaysOnTopButton != null) AlwaysOnTopButton.IsChecked = _isAlwaysOnTop;

            var appWindow = this.AppWindow;
            if (appWindow != null && appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = _isAlwaysOnTop;
            }
            SaveWindowSettings();
        }

        private void GlobalThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTheme == ElementTheme.Dark)
            {
                SetTheme(ElementTheme.Light);
            }
            else
            {
                SetTheme(ElementTheme.Dark);
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
                _preloadCts?.Cancel();
                _preloadCts?.Dispose();
                _preloadCts = null;

                bool isFast = !isManualClick && DetectFastNavigation();

                if (isFast)
                {
                    if (_isSideBySideMode && _currentPdfDocument == null)
                        _currentIndex = Math.Max(0, _currentIndex - 2);
                    else
                        _currentIndex--;

                    ShowFilenameOnly();
                    return;
                }

                if (_isSideBySideMode && _currentPdfDocument == null)
                    _currentIndex = Math.Max(0, _currentIndex - 2);
                else
                    _currentIndex--;

                await DisplayCurrentImageAsync();
                await AddToRecentAsync(true);

                // Trigger preloading for previous images if navigating backwards
                if (_currentArchive != null || _currentPdfDocument != null)
                {
                    _preloadCts = new CancellationTokenSource();
                    var token = _preloadCts.Token;
                    _ = Task.Run(() => PreloadPreviousImagesAsync(token));
                }
            }
            RootGrid.Focus(FocusState.Programmatic);
        }

        private async Task NavigateToNextAsync(bool isManualClick = false)
        {
            _pdfScrollDirection = 1;
            if (_currentIndex < _imageEntries.Count - 1)
            {
                _preloadCts?.Cancel();
                _preloadCts?.Dispose();
                _preloadCts = null;

                bool isFast = !isManualClick && DetectFastNavigation();

                if (isFast)
                {
                    if (_isSideBySideMode && _currentPdfDocument == null)
                        _currentIndex = Math.Min(_imageEntries.Count - 1, _currentIndex + 2);
                    else
                        _currentIndex++;

                    ShowFilenameOnly();
                    return;
                }

                if (_isSideBySideMode && _currentPdfDocument == null)
                    _currentIndex = Math.Min(_imageEntries.Count - 1, _currentIndex + 2);
                else
                    _currentIndex++;

                await DisplayCurrentImageAsync();
                await AddToRecentAsync(true);

                if (_currentArchive != null || _currentPdfDocument != null)
                {
                    _preloadCts = new CancellationTokenSource();
                    var token = _preloadCts.Token;
                    _ = Task.Run(() => PreloadNextImagesAsync(token));
                }
            }
            RootGrid.Focus(FocusState.Programmatic);
        }

        private async Task NavigateToFileAsync(bool isNext)
        {
            // Save current position before navigating away
            await AddToRecentAsync(true);

            // Find current file/archive in the list
            string? currentPath = null;
            if (_isWebDavMode)
            {
                currentPath = _currentWebDavItemPath;
            }
            else if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
            {
                currentPath = _currentArchivePath;
            }
            else if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath))
            {
                currentPath = _currentEpubFilePath;
            }
            else if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath))
            {
                currentPath = _currentTextFilePath;
            }
            else if (_imageEntries != null && _imageEntries.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageEntries.Count)
            {
                currentPath = _imageEntries[_currentIndex].FilePath;
            }

            if (string.IsNullOrEmpty(currentPath))
                return;

            if (!_isWebDavMode)
            {
                // Ensure explorer path is set if missing (e.g. opened from Recent Files)
                if (string.IsNullOrEmpty(_currentExplorerPath))
                {
                    _currentExplorerPath = Path.GetDirectoryName(currentPath);
                }

                if (string.IsNullOrEmpty(_currentExplorerPath))
                    return;

                // Ensure file list is loaded and contains the current path
                // [Optimization] If file is not in list but we have an explorer path, it might be background loading.
                if (_fileItems.Count <= 1 && !string.IsNullOrEmpty(_currentExplorerPath))
                {
                    // Check if it's the initial fast-load single entry
                    if (_fileItems.Count == 0 || !_fileItems.Any(f => f.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        LoadExplorerFolder(_currentExplorerPath);
                    }
                }
            }

            if (string.IsNullOrEmpty(currentPath))
                return;

            var currentItemIndex = -1;
            for (int i = 0; i < _fileItems.Count; i++)
            {
                if (_isWebDavMode)
                {
                    if (_fileItems[i].WebDavPath == currentPath)
                    {
                        currentItemIndex = i;
                        break;
                    }
                }
                else
                {
                    if (_fileItems[i].FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        currentItemIndex = i;
                        break;
                    }
                }
            }

            if (currentItemIndex == -1) return;

            // Find next/prev navigable file
            int newIndex = currentItemIndex;
            while (true)
            {
                newIndex = isNext ? newIndex + 1 : newIndex - 1;

                if (newIndex < 0 || newIndex >= _fileItems.Count)
                    break; // End of list

                var item = _fileItems[newIndex];
                if (item.IsDirectory || item.IsParentDirectory)
                    continue; // Skip directories

                try
                {
                    if (_isWebDavMode && item.IsWebDav)
                    {
                        await AddToRecentAsync(true);
                        await HandleWebDavFileSelectionAsync(item);
                        SyncExplorerSelection(item);
                        return;
                    }
                    else if (item.IsArchive)
                    {
                        await AddToRecentAsync(true); // Save current before switching
                        await LoadImagesFromArchiveAsync(item.FullPath);
                        SyncExplorerSelection(item);
                        return;
                    }
                    else if (item.IsPdf)
                    {
                        await AddToRecentAsync(true);
                        await LoadImagesFromPdfAsync(item.FullPath);
                        SyncExplorerSelection(item);
                        return;
                    }
                    else if (item.IsImage || item.IsText || item.IsEpub)
                    {
                        await AddToRecentAsync(true); // Save current before switching
                        var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                        await LoadImageFromFileAsync(file);
                        SyncExplorerSelection(item);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error navigating to file: {ex.Message}");
                    // Continue to next file on error
                }
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

                            CanvasBitmap? prev = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(prevIdx, out prev); }
                            if (prev != null && prev.Device != null)
                            {
                                var pFit = Math.Min(canvasSize.Width / prev.Size.Width, canvasSize.Height / prev.Size.Height);
                                var pScaledSize = new Windows.Foundation.Size(prev.Size.Width * pFit * _zoomLevel, prev.Size.Height * pFit * _zoomLevel);
                                var pPos = new Windows.Foundation.Point((canvasSize.Width - pScaledSize.Width) / 2 + _pdfPanX, currentY_top - pScaledSize.Height - gap);
                                ds.DrawImage(prev, new Windows.Foundation.Rect(pPos, pScaledSize));
                                currentY_top = pPos.Y;

                                // Stop if even this page is way above screen
                                if (currentY_top + pScaledSize.Height < -500) break;
                            }
                            else break; // Missing preload, can't draw further
                        }

                        // Draw next pages (up to 5)
                        double currentY_bottom = position.Y + scaledSize.Height;
                        for (int i = 1; i <= 5; i++)
                        {
                            int nextIdx = _currentIndex + i;
                            if (nextIdx >= _imageEntries.Count) break;

                            CanvasBitmap? next = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(nextIdx, out next); }
                            if (next != null && next.Device != null)
                            {
                                var nFit = Math.Min(canvasSize.Width / next.Size.Width, canvasSize.Height / next.Size.Height);
                                var nScaledSize = new Windows.Foundation.Size(next.Size.Width * nFit * _zoomLevel, next.Size.Height * nFit * _zoomLevel);
                                var nPos = new Windows.Foundation.Point((canvasSize.Width - nScaledSize.Width) / 2 + _pdfPanX, currentY_bottom + gap);
                                ds.DrawImage(next, new Windows.Foundation.Rect(nPos, nScaledSize));
                                currentY_bottom = nPos.Y + nScaledSize.Height;

                                // Stop if even this page is way below screen
                                if (nPos.Y > canvasSize.Height + 500) break;
                            }
                            else break;
                        }
                    }
                    else
                    {
                        var destRect = new Windows.Foundation.Rect(position, scaledSize);
                        ds.DrawImage(_currentBitmap, destRect);
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
                            CanvasBitmap? prevLeft = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(_currentIndex - 2, out prevLeft); }
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
                            CanvasBitmap? nextLeft = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(_currentIndex + 2, out nextLeft); }
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
                            CanvasBitmap? prevRight = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(prevRightIndex, out prevRight); }
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
                            CanvasBitmap? nextRight = null;
                            lock (_preloadedImages) { _preloadedImages.TryGetValue(nextRightIndex, out nextRight); }
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
                    ds.DrawImage(_rightBitmap, destRect);
                }
                catch (Exception) { }
            }
        }

        #endregion

        #region Image Preloading

        private async Task PreloadNextImagesAsync(CancellationToken token)
        {
            try
            {
                if (_imageEntries.Count == 0) return;
                if (token.IsCancellationRequested) return;

                var startIndex = _currentPdfDocument != null ? Math.Max(0, _currentIndex - 5) : Math.Max(0, _currentIndex - 1);
                var endIndex = _currentPdfDocument != null ? Math.Min(_imageEntries.Count - 1, _currentIndex + 5) : Math.Min(_imageEntries.Count - 1, _currentIndex + PreloadCount);

                var tasks = new List<Task>();

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (token.IsCancellationRequested) break;

                    int index = i;
                    if (index == _currentIndex) continue;

                    // 1. 이미 캐시에 있거나, 2. 현재 로딩 중이라면 스킵
                    bool shouldSkip = false;
                    lock (_preloadedImages) { if (_preloadedImages.ContainsKey(index)) shouldSkip = true; }

                    if (!shouldSkip)
                    {
                        lock (_loadingIndices)
                        {
                            if (_loadingIndices.Contains(index)) shouldSkip = true;
                            else _loadingIndices.Add(index); // 로딩 시작 표시
                        }
                    }

                    if (shouldSkip) continue;

                    var entry = _imageEntries[index];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            CanvasBitmap? bitmap = null;
                            bool isPdf = entry.IsPdfEntry && _currentPdfDocument != null;

                            if (isPdf)
                            {
                                bitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token);
                            }

                            else if (entry.IsArchiveEntry && _currentArchive != null)
                            {
                                bitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, MainCanvas, token);
                            }
                            else if (!entry.IsArchiveEntry && entry.FilePath != null)
                            {
                                bitmap = await LoadImageFromPathAsync(entry.FilePath, MainCanvas);
                            }
                            else if (entry.IsWebDavEntry && _isWebDavMode && !token.IsCancellationRequested)
                            {
                                try
                                {
                                    var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                                    if (!string.IsNullOrEmpty(tempPath) && !token.IsCancellationRequested)
                                    {
                                        entry.FilePath = tempPath;
                                        bitmap = await LoadImageFromPathAsync(tempPath, MainCanvas);
                                    }
                                }
                                catch { }
                            }

                            if (token.IsCancellationRequested)
                            {
                                // 만들었는데 취소됐다면 즉시 폐기
                                bitmap?.Dispose();
                                return;
                            }

                            if (bitmap != null)
                            {
                                // 2. 샤픈 효과 적용 (PDF 제외)
                                if (_sharpenEnabled && !entry.IsPdfEntry)
                                {
                                    var sharpened = await ApplySharpenToBitmapAsync(bitmap, MainCanvas, skipUpscale: false);
                                    if (sharpened != null)
                                    {
                                        bitmap.Dispose(); // 원본은 버리고 샤픈된 것 사용
                                        bitmap = sharpened;
                                    }
                                }

                                lock (_preloadedImages)
                                {
                                    // 로딩 끝낸 사이에 이미 누가 넣었는지 더블 체크
                                    if (!_preloadedImages.ContainsKey(index))
                                    {
                                        _preloadedImages[index] = bitmap;
                                    }
                                    else
                                    {
                                        // 늦게 도착한 비트맵은 즉시 폐기
                                        bitmap.Dispose();
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            // 로딩 상태 해제
                            lock (_loadingIndices) { _loadingIndices.Remove(index); }
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                if (!token.IsCancellationRequested)
                {
                    // Clean up old preloaded images to save memory
                    CleanupOldPreloadedImages();
                    CleanupOldSharpenedImages();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preloading next images: {ex.Message}");
            }
        }

        private async Task PreloadPreviousImagesAsync(CancellationToken token)
        {
            try
            {
                if (_imageEntries.Count == 0) return;
                if (token.IsCancellationRequested) return;

                var startIndex = _currentPdfDocument != null ? Math.Max(0, _currentIndex - 5) : Math.Max(0, _currentIndex - PreloadCount);
                var endIndex = _currentPdfDocument != null ? Math.Min(_imageEntries.Count - 1, _currentIndex + 5) : _currentIndex - 1;

                var tasks = new List<Task>();

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (token.IsCancellationRequested) break;

                    int index = i;

                    // 1. 이미 캐시에 있거나, 2. 현재 로딩 중이라면 스킵
                    bool shouldSkip = false;
                    lock (_preloadedImages) { if (_preloadedImages.ContainsKey(index)) shouldSkip = true; }

                    if (!shouldSkip)
                    {
                        lock (_loadingIndices)
                        {
                            if (_loadingIndices.Contains(index)) shouldSkip = true;
                            else _loadingIndices.Add(index); // 로딩 시작 표시
                        }
                    }

                    if (shouldSkip) continue;

                    var entry = _imageEntries[index];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            CanvasBitmap? bitmap = null;
                            bool isPdf = entry.IsPdfEntry && _currentPdfDocument != null;

                            if (isPdf)
                            {
                                bitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token);
                            }
                            else if (entry.IsArchiveEntry && _currentArchive != null)
                            {
                                bitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, MainCanvas, token);
                            }
                            else if (!entry.IsArchiveEntry && entry.FilePath != null)
                            {
                                bitmap = await LoadImageFromPathAsync(entry.FilePath, MainCanvas);
                            }
                            else if (entry.IsWebDavEntry && _isWebDavMode && !token.IsCancellationRequested)
                            {
                                try
                                {
                                    var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                                    if (!string.IsNullOrEmpty(tempPath) && !token.IsCancellationRequested)
                                    {
                                        entry.FilePath = tempPath;
                                        bitmap = await LoadImageFromPathAsync(tempPath, MainCanvas);
                                    }
                                }
                                catch { }
                            }

                            if (token.IsCancellationRequested)
                            {
                                // 만들었는데 취소됐다면 즉시 폐기
                                bitmap?.Dispose();
                                return;
                            }

                            if (bitmap != null)
                            {
                                lock (_preloadedImages)
                                {
                                    if (!_preloadedImages.ContainsKey(index))
                                    {
                                        _preloadedImages[index] = bitmap;
                                    }
                                    else
                                    {
                                        // 늦게 도착한 것은 즉시 폐기
                                        bitmap.Dispose();
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            // 로딩 상태 해제
                            lock (_loadingIndices) { _loadingIndices.Remove(index); }
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                if (!token.IsCancellationRequested)
                {
                    // Clean up old preloaded images to save memory
                    CleanupOldPreloadedImages();
                    CleanupOldSharpenedImages();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preloading previous images: {ex.Message}");
            }
        }

        private void CleanupOldPreloadedImages()
        {
            lock (_preloadedImages)
            {
                // Keep only images within a reasonable range of current index
                var keepRange = _currentPdfDocument != null ? 6 : PreloadCount * 2;
                var keysToRemove = _preloadedImages.Keys
                    .Where(index => Math.Abs(index - _currentIndex) > keepRange)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_preloadedImages.TryGetValue(key, out var bitmap))
                    {
                        if (!IsBitmapInCache(bitmap))
                        {
                            bitmap?.Dispose();
                        }
                    }
                    _preloadedImages.Remove(key);
                }
            }
        }

        private void CleanupOldSharpenedImages()
        {
            lock (_sharpenedImageCache)
            {
                var keepRange = PreloadCount * 2;
                var keysToRemove = _sharpenedImageCache.Keys
                    .Where(index => Math.Abs(index - _currentIndex) > keepRange)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_sharpenedImageCache.TryGetValue(key, out var bitmap))
                    {
                        if (!IsBitmapInCache(bitmap))
                        {
                            bitmap?.Dispose();
                        }
                    }
                    _sharpenedImageCache.Remove(key);
                }
            }
        }

        #endregion



    }
}