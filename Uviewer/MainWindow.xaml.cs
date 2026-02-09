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
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
        private CanvasBitmap? _leftBitmap;
        private CanvasBitmap? _rightBitmap;

        // Image preloading for faster navigation
        private readonly Dictionary<int, CanvasBitmap> _preloadedImages = new();
        private const int PreloadCount = 3; // Number of images to preload ahead

        private static readonly string[] SupportedImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".ico", ".tiff", ".tif"
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
        
        private IEnumerable<string> SupportedFileExtensions => 
            SupportedImageExtensions.Concat(SupportedTextExtensions).Concat(SupportedArchiveExtensions).Concat(SupportedEpubExtensions);

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
            public bool IsParentDirectory { get; set; }

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
                                  IsImage ? "\uE8B9" : 
                                  IsText ? "\uE8C4" : "\uE7C3";

            public SolidColorBrush IconColor => IsDirectory || IsParentDirectory ?
                new SolidColorBrush(Colors.Gold) :
                IsArchive ? new SolidColorBrush(Colors.Orange) :
                IsEpub ? new SolidColorBrush(Colors.MediumPurple) :
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
        }



        [System.Text.Json.Serialization.JsonSerializable(typeof(List<FavoriteItem>))]
        public partial class FavoritesContext : System.Text.Json.Serialization.JsonSerializerContext;

        [System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentItem>))]
        public partial class RecentContext : System.Text.Json.Serialization.JsonSerializerContext;

        public async Task InitializeAsync(string? launchFilePath = null)
        {
            try
            {
                // Subscribe to global PreviewKeyDown to intercept navigation keys
                if (RootGrid != null)
                {
                    RootGrid.PreviewKeyDown += RootGrid_Global_PreviewKeyDown;
                }

                // Load favorites
                await LoadFavorites();
                UpdateFavoritesMenu();

                // Load recent items
                await LoadRecentItems();
                UpdateRecentMenu();

            // Handle launch file path if provided
            if (!string.IsNullOrEmpty(launchFilePath))
            {
                try
                {
                    if (File.Exists(launchFilePath))
                    {
                        // First navigate to the folder
                        var fileFolder = Path.GetDirectoryName(launchFilePath);
                        if (!string.IsNullOrEmpty(fileFolder) && Directory.Exists(fileFolder))
                        {
                            LoadExplorerFolder(fileFolder);
                            System.Diagnostics.Debug.WriteLine($"Loaded file folder: {fileFolder}");

                            // Then try to open the specific file
                            try
                            {
                                // Check if it's an archive file
                                var extension = Path.GetExtension(launchFilePath).ToLowerInvariant();
                                var archiveExtensions = new[] { ".zip", ".rar", ".7z", ".tar", ".gz" };
                                var epubExtensions = new[] { ".epub" };

                                if (archiveExtensions.Contains(extension))
                                {
                                    // For archive files, load the archive
                                    await LoadImagesFromArchiveAsync(launchFilePath);
                                    System.Diagnostics.Debug.WriteLine($"Loaded archive file: {launchFilePath}");
                                }
                                else if (epubExtensions.Contains(extension))
                                {
                                     // For EPUB files
                                     var file = await StorageFile.GetFileFromPathAsync(launchFilePath);
                                     await LoadEpubFileAsync(file);
                                     System.Diagnostics.Debug.WriteLine($"Loaded epub file: {launchFilePath}");
                                }
                                else
                                {
                                    // For regular image files, load the image
                                    var file = await StorageFile.GetFileFromPathAsync(launchFilePath);
                                    await LoadImageFromFileAsync(file);
                                    System.Diagnostics.Debug.WriteLine($"Loaded launch file: {launchFilePath}");
                                }
                            }
                            catch (Exception fileEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Could not load file directly, but folder is loaded: {fileEx.Message}");
                            }
                        }
                        else
                        {
                            // Fallback to Pictures folder
                            LoadExplorerFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                        }
                    }
                    else if (Directory.Exists(launchFilePath))
                    {
                        LoadExplorerFolder(launchFilePath);
                        System.Diagnostics.Debug.WriteLine($"Loaded launch folder: {launchFilePath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Launch path does not exist: {launchFilePath}");
                        // Load Pictures folder by default first
                        LoadExplorerFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading launch path: {launchFilePath}, Error: {ex.Message}");
                    // Load Pictures folder by default first
                    LoadExplorerFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                }
            }
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Initialization Error: {ex}");
                 if (FileNameText != null) FileNameText.Text = $"Error: {ex.Message}";
                 MessageBox(IntPtr.Zero, $"Initialization Error:\n{ex.Message}\n{ex.StackTrace}", "Uviewer Init Error", 0x10);
            }
        }



        public MainWindow(string? launchFilePath = null)
        {
            InitializeComponent();

            try
            {
                // Set window title
                Title = "Uviewer - Image & Text Viewer";

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
                    // 설정 파일이 없으면 기본 사이즈 적용 및 변수 초기화
                    var defaultSize = new Windows.Graphics.SizeInt32(1200, 800);
                    appWindow2.Resize(defaultSize);

                    // 현재 위치와 크기를 초기값으로 저장
                    _lastNonMaximizedRect = new Windows.Graphics.RectInt32(
                        appWindow2.Position.X, appWindow2.Position.Y,
                        defaultSize.Width, defaultSize.Height);
                }

                // Initialize button states
                UpdateSideBySideButtonState();
                UpdateNextImageSideButtonState();
                UpdateSharpenButtonState();

                // Enable keyboard shortcuts
                RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;
                RootGrid.KeyDown += RootGrid_KeyDown;

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
                // Win2D 캔버스 디바이스가 초기화될 시간을 아주 잠깐 확보 (안전장치)
                await Task.Delay(50);
                await InitializeAsync(launchFilePath);
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
                    
                    // Dispose semaphore
                    _archiveLock.Dispose();

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
                if (_isFullscreen)
                {
                    ToolbarGrid.Visibility = Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine("✓ Toolbar hidden by timer");
                }
            };

            _fullscreenSidebarHideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _fullscreenSidebarHideTimer.Interval = TimeSpan.FromMilliseconds(FullscreenHideDelayMs);
            _fullscreenSidebarHideTimer.IsRepeating = false;
            _fullscreenSidebarHideTimer.Tick += (s, e) =>
            {
                _sidebarHideTimerRunning = false;
                if (_isFullscreen)
                {
                    SidebarGrid.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
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
            ToolTipService.SetToolTip(TocButton, Strings.TocTooltip);
            ToolTipService.SetToolTip(SettingsButton, Strings.SettingsTooltip);
            ToolTipService.SetToolTip(PrevFileButton, Strings.PrevFileTooltip);
            ToolTipService.SetToolTip(NextFileButton, Strings.NextFileTooltip);
            ToolTipService.SetToolTip(PrevPageButton, Strings.PrevPageTooltip);
            ToolTipService.SetToolTip(NextPageButton, Strings.NextPageTooltip);

            // Texts
            CurrentPathText.Text = Strings.CurrentPathPlaceholder;
            FileNameText.Text = Strings.FileSelectPlaceholder;
            
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
            EpubFastNavText.Text = Strings.EpubFastNavText;
            
            // Menus
            if (AddToFavoritesButton != null) AddToFavoritesButton.Content = Strings.AddToFavorites;
            if (SidebarAddToFavoritesButton != null) SidebarAddToFavoritesButton.Content = Strings.AddToFavorites;
            if (ChangeFontMenuItem != null) ChangeFontMenuItem.Text = Strings.ChangeFont;
            if (NotificationText != null) NotificationText.Text = Strings.AddedToFavoritesNotification;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                if (args.DidSizeChange) TriggerEpubResize();

                if (sender.Presenter is OverlappedPresenter overlapped)
                {
                    // [수정] Restored 상태여야 함은 물론, 
                    // 시스템이 최대화를 위해 창을 옮기는 특이 좌표를 무시해야 합니다.
                    if (overlapped.State == OverlappedPresenterState.Restored)
                    {
                        // 보통 일반적인 창은 화면 구석에 딱 붙여도 -8(베젤 두께) 이하로 내려가지 않습니다.
                        // 최대화 시 발생하는 시스템 좌표(예: -8, -8)와 겹치지 않게 가드라인을 칩니다.
                        var pos = sender.Position;
                        var size = sender.Size;

                        // 너무 비정상적인 위치나 크기는 저장하지 않도록 제한
                        if (size.Width > 0 && size.Height > 0)
                        {
                            // [핵심] 현재 위치/크기를 바로 저장하지 않고 
                            // 실제 "사용자가 마우스로 조절 중인" 상태인지 체크하는 효과
                            _lastNonMaximizedRect = new Windows.Graphics.RectInt32(pos.X, pos.Y, size.Width, size.Height);
                        }
                    }
                }
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
                FullscreenIcon.Glyph = "\uE740"; // Fullscreen icon
                _isFullscreen = false;
            }
            else
            {
                // Enter fullscreen
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                ToolbarGrid.Visibility = Visibility.Collapsed;
                StatusBarGrid.Visibility = Visibility.Collapsed;
                SidebarGrid.Visibility = Visibility.Collapsed;
                if (_isSidebarVisible)
                {
                    _SidebarWidth = (int)SidebarColumn.Width.Value; // Save current width
                }
                SidebarColumn.Width = new GridLength(0);
                SplitterGrid.Visibility = Visibility.Collapsed;  // Hide splitter in fullscreen
                FullscreenIcon.Glyph = "\uE73F"; // Exit fullscreen icon
                _isFullscreen = true;
                StopFullscreenHoverTimers();
            }
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
            if (!_isFullscreen) return;

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
                    ToolbarGrid.Visibility = Visibility.Visible;
                    System.Diagnostics.Debug.WriteLine("Toolbar SHOWN (mouse in top zone)");
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

            bool inLeftZone = x < FullscreenLeftHoverZone;
            if (SidebarGrid.Visibility == Visibility.Visible && x < _SidebarWidth)
            {
                inLeftZone = true;
            }

            if (inLeftZone)
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

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isFullscreen) return;

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
                prevAction?.Invoke();
            }
            else
            {
                nextAction?.Invoke();
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


        #region Image Display

        private async Task DisplayCurrentImageAsync()
        {
            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count)
                return;

            // 이전 로딩 작업 취소
            _imageLoadingCts?.Cancel();
            _imageLoadingCts = new CancellationTokenSource();
            var token = _imageLoadingCts.Token; // <-- 이 토큰을 전달해야 함

            StopAnimatedWebp();

            var entry = _imageEntries[_currentIndex];
            if (IsTextEntry(entry))
            {
                await LoadTextEntryAsync(entry);
                await AddToRecentAsync(false);
                return;
            }
            else if (IsEpubEntry(entry))
            {
                await LoadEpubEntryAsync(entry);
                await AddToRecentAsync(false);
                return;
            }

            SwitchToImageMode(); // Ensure Image Mode is active


            if (_isSideBySideMode)
            {
                await DisplaySideBySideImagesAsync(token); // <-- token 전달
            }
            else
            {
                await DisplaySingleImageAsync(token); // <-- token 전달
            }

            await AddToRecentAsync(false);
        }

        private void SyncSidebarSelection(ImageEntry entry)
        {
            try
            {
                if (_fileItems == null || _fileItems.Count == 0) return;

                string targetPath = entry.IsArchiveEntry ? (_currentArchivePath ?? "") : (entry.FilePath ?? "");
                if (string.IsNullOrEmpty(targetPath)) return;

                // Find item in file list
                var item = _fileItems.FirstOrDefault(f => f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    if (_isExplorerGrid)
                    {
                        if (FileGridView.SelectedItem != item)
                        {
                            FileGridView.SelectedItem = item;
                            FileGridView.ScrollIntoView(item);
                        }
                    }
                    else
                    {
                        if (FileListView.SelectedItem != item)
                        {
                            FileListView.SelectedItem = item;
                            FileListView.ScrollIntoView(item);
                        }
                    }
                }
            }
            catch { }
        }

        // 매개변수 추가
        private async Task DisplaySingleImageAsync(CancellationToken token)
        {
            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count) return;

            var entry = _imageEntries[_currentIndex];
            StopAnimatedWebp(); // 기존 애니메이션 중단

            try
            {
                if (token.IsCancellationRequested) return;

                // 1. 이미지 로드 (캐시 또는 파일에서져옴)
                var bitmap = await LoadImageBitmapAsync(entry, MainCanvas, token);

                if (token.IsCancellationRequested)
                {
                    // 여기서 bitmap을 Dispose하지 않는 이유는 LoadImageBitmapAsync가 
                    // 캐시된 객체를 반환했을 수도 있기 때문입니다.
                    return;
                }

                if (bitmap != null)
                {
                    // [추가] 기존 비트맵이 캐시(프리로딩 등)에 없는 임시 비트맵(애니메이션 프레임 등)이면 명시적으로 해제
                    if (_currentBitmap != null && !IsBitmapInCache(_currentBitmap))
                    {
                        _currentBitmap.Dispose();
                    }

                    // [중요 수정] 이제 위에서 직접 Dispose를 호출할 수 있습니다 (캐시 여부 확인 후)
                    _currentBitmap = bitmap;

                    // UI 즉시 갱신
                    _zoomLevel = 1.0;
                    FitToWindow();
                    ShowImageUI();
                    UpdateStatusBar(entry, _currentBitmap);
                    UpdateSharpenButtonState();
                    MainCanvas.Invalidate();
                    
                    // Sync sidebar selection (using our new safe method)
                    SyncSidebarSelection(entry);
                }
                else
                {
                    FileNameText.Text = Strings.LoadImageError;
                    return;
                }

                // [애니메이션 WebP 처리 부분] 헤더만 읽어서 애니메이션 여부 확인 후 필요시 로드
                if (IsAnimationSupported(entry))
                {
                    // 로딩 표시 추가
                    FileNameText.Text += Strings.Loading;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            byte[]? imageBytes = null;
                            if (entry.IsArchiveEntry && entry.ArchiveEntryKey != null)
                            {
                                imageBytes = await LoadBytesFromArchiveEntryAsync(entry.ArchiveEntryKey, token);
                            }
                            else if (entry.FilePath != null)
                            {
                                imageBytes = await File.ReadAllBytesAsync(entry.FilePath, token);
                            }

                            if (imageBytes == null || token.IsCancellationRequested) return;

                            // 애니메이션 프레임 로드 (내부에서 애니메이션 여부 자동 확인)
                            var (framePixels, delays, width, height) = await TryLoadAnimatedImageFramesAsync(imageBytes);

                            if (token.IsCancellationRequested) return;

                            if (framePixels != null && framePixels.Count > 1 && width > 0 && height > 0)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    
                                    // 현재 인덱스가 일치하는지 확인 (다른 파일로 넘어갔을 경우 방지)
                                    bool isStillCurrent = false;
                                    if (entry.IsArchiveEntry) 
                                        isStillCurrent = _currentIndex < _imageEntries.Count && _imageEntries[_currentIndex].ArchiveEntryKey == entry.ArchiveEntryKey;
                                    else
                                        isStillCurrent = _currentIndex < _imageEntries.Count && _imageEntries[_currentIndex].FilePath == entry.FilePath;

                                    if (isStillCurrent)
                                    {
                                        _animatedWebpFramePixels = framePixels;
                                        _animatedWebpDelaysMs = delays ?? Enumerable.Repeat(DefaultWebpFrameDelayMs, framePixels.Count).ToList();
                                        _animatedWebpFrameIndex = 0;
                                        _animatedWebpWidth = width;
                                        _animatedWebpHeight = height;
                                        
                                        // 로딩 완료 후 상태바 복구
                                        UpdateStatusBar(entry, _currentBitmap!);
                                        StartAnimatedWebpTimer();
                                    }
                                });
                            }
                            else
                            {
                                // 애니메이션이 아니거나 로드 실패 시 상태바 복구
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    UpdateStatusBar(entry, _currentBitmap!);
                                });
                            }
                        }
                        catch 
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (token.IsCancellationRequested) return;
                                UpdateStatusBar(entry, _currentBitmap!);
                            });
                        }
                    }, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    FileNameText.Text = $"이미지 로드 오류: {ex.Message}";
            }
        }

        private bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            lock (_preloadedImages)
            {
                if (_preloadedImages.ContainsValue(bitmap)) return true;
            }
            lock (_sharpenedImageCache)
            {
                if (_sharpenedImageCache.ContainsValue(bitmap)) return true;
            }
            lock (_animatedWebpSharpenedCache)
            {
                if (_animatedWebpSharpenedCache.ContainsValue(bitmap)) return true;
            }
            return false;
        }

        private async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            try
            {
                CanvasBitmap? leftBitmap, rightBitmap;
                ImageEntry leftEntry, rightEntry;

                if (_nextImageOnRight)
                {
                    // → direction: current image on left, next image on right
                    leftEntry = _imageEntries[_currentIndex];
                    leftBitmap = await LoadImageBitmapAsync(leftEntry, LeftCanvas, token);

                    if (token.IsCancellationRequested) return; // 취소 확인

                    if (_currentIndex + 1 < _imageEntries.Count)
                    {
                        rightEntry = _imageEntries[_currentIndex + 1];
                        rightBitmap = await LoadImageBitmapAsync(rightEntry, RightCanvas, token);
                    }
                    else
                    {
                        rightEntry = leftEntry; // Use same entry if no next image
                        rightBitmap = null;
                    }
                }
                else
                {
                    // ← direction: next image (n+1) on left, current image (n) on right
                    if (_currentIndex + 1 < _imageEntries.Count)
                    {
                        leftEntry = _imageEntries[_currentIndex + 1];
                        leftBitmap = await LoadImageBitmapAsync(leftEntry, LeftCanvas, token);
                    }
                    else
                    {
                        leftEntry = _imageEntries[_currentIndex];
                        leftBitmap = null;
                    }

                    if (token.IsCancellationRequested) return; // 취소 확인

                    rightEntry = _imageEntries[_currentIndex];
                    rightBitmap = await LoadImageBitmapAsync(rightEntry, RightCanvas, token);
                }

                if (token.IsCancellationRequested) return;

                // [추가] 기존 비트맵들 정리
                if (_leftBitmap != null && !IsBitmapInCache(_leftBitmap)) _leftBitmap.Dispose();
                if (_rightBitmap != null && !IsBitmapInCache(_rightBitmap)) _rightBitmap.Dispose();

                _leftBitmap = leftBitmap;
                _rightBitmap = rightBitmap;
                _currentBitmap = rightBitmap ?? leftBitmap; // For zoom calculations

                _zoomLevel = 1.0;
                FitToWindow();
                ShowImageUI();
                UpdateStatusBar(rightEntry, _currentBitmap!);
                SyncSidebarSelection(rightEntry); // Sync to the "primary" image
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"이미지 로드 실패: {ex.Message}";
            }
        }

        private async Task<CanvasBitmap?> LoadImageBitmapAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token = default)
        {
            try
            {
                if (token.IsCancellationRequested) return null;
                // 1. 캐시 확인 (압축 파일 프리로딩 등)
                if (entry.IsArchiveEntry && _currentArchive != null)
                {
                    var entryIndex = _imageEntries.IndexOf(entry);
                    CanvasBitmap? preloadedBitmap = null;

                    lock (_preloadedImages)
                    {
                        if (_preloadedImages.TryGetValue(entryIndex, out var bitmap))
                        {
                            preloadedBitmap = bitmap;
                        }
                    }

                    if (preloadedBitmap != null)
                    {
                        if (_sharpenEnabled)
                        {
                            // 샤픈 캐시 확인 및 적용 로직...
                            lock (_sharpenedImageCache)
                            {
                                if (_sharpenedImageCache.TryGetValue(entryIndex, out var sharpenedBitmap))
                                    return sharpenedBitmap;
                            }
                            var sharpened = await ApplySharpenToBitmapAsync(preloadedBitmap, canvas, skipUpscale: false);
                            if (sharpened != null)
                            {
                                CacheSharpenedImage(entryIndex, sharpened);
                                return sharpened;
                            }
                        }
                        return preloadedBitmap;
                    }
                }

                CanvasBitmap? originalBitmap = null;

                // 2. 이미지 소스에 따라 로드
                if (entry.IsArchiveEntry && _currentArchive != null)
                {
                    // [중요] 압축 파일 내 이미지는 WebP 여부 상관없이 여기서 로드 (Win2D LoadAsync 사용)
                    originalBitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, canvas, token);
                }
                else if (entry.FilePath != null)
                {
                    // 로컬 파일 (애니메이션 WebP가 아닌 경우 여기로 옴)
                    originalBitmap = await LoadImageFromPathAsync(entry.FilePath, canvas);
                }

                // 3. 로드 실패 시 null 반환
                if (originalBitmap == null) return null;

                // 4. 샤픈 효과 적용
                if (_sharpenEnabled)
                {
                    var entryIndex = _imageEntries.IndexOf(entry);
                    lock (_sharpenedImageCache)
                    {
                        if (_sharpenedImageCache.TryGetValue(entryIndex, out var cached))
                            return cached;
                    }

                    var sharpened = await ApplySharpenToBitmapAsync(originalBitmap, canvas, skipUpscale: false);
                    if (sharpened != null)
                    {
                        CacheSharpenedImage(entryIndex, sharpened);
                        return sharpened;
                    }
                }

                return originalBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image bitmap: {ex.Message}");
                return null;
            }
        }

        private void SharpenButton_Click(object sender, RoutedEventArgs e)
        {
            _sharpenEnabled = !_sharpenEnabled;

            // [추가] 샤픈 옵션을 바꿀 때 캐시를 초기화하여 충돌 방지
            lock (_sharpenedImageCache)
            {
                foreach (var bmp in _sharpenedImageCache.Values)
                {
                    // 현재 화면에 떠있는 이미지가 아닐 때만 Dispose
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        bmp.Dispose();
                    }
                }
                _sharpenedImageCache.Clear();
            }

            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values)
                {
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        bmp.Dispose();
                    }
                }
                _animatedWebpSharpenedCache.Clear();
            }

            UpdateSharpenButtonState();
            SaveWindowSettings();

            // 이미지 다시 로드
            _ = DisplayCurrentImageAsync();
        }

        private void UpdateSharpenButtonState()
        {
            // UI 동기화
            SharpenButton.IsChecked = _sharpenEnabled;

            // 내부 변수 기준으로 UI 스타일 변경
            if (_sharpenEnabled)
            {
                SharpenIcon.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) &&
                    accent is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    SharpenButton.Foreground = brush;
                }
            }
            else
            {
                SharpenIcon.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                SharpenButton.ClearValue(Control.ForegroundProperty);
            }
        }

        private void SideBySideButton_Click(object sender, RoutedEventArgs e)
        {
            _isSideBySideMode = !_isSideBySideMode;
            UpdateSideBySideButtonState();
            SaveWindowSettings();
            _ = DisplayCurrentImageAsync();
        }

        private void NextImageSideButton_Click(object sender, RoutedEventArgs e)
        {
            _nextImageOnRight = !_nextImageOnRight;
            UpdateNextImageSideButtonState();
            SaveWindowSettings();
            if (_isSideBySideMode)
            {
                _ = DisplayCurrentImageAsync();
            }
        }

        private void UpdateSideBySideButtonState()
        {
            if (_isSideBySideMode)
            {
                SideBySideText.Text = "2";
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Microsoft.UI.Xaml.Media.Brush brush)
                    SideBySideButton.Foreground = brush;
            }
            else
            {
                SideBySideText.Text = "1";
                SideBySideButton.ClearValue(Button.ForegroundProperty);
            }
        }

        private void UpdateNextImageSideButtonState()
        {
            if (_nextImageOnRight)
            {
                NextImageSideText.Text = "→"; // Right arrow (left to right)
            }
            else
            {
                NextImageSideText.Text = "←"; // Left arrow (right to left)
            }
        }

        private async Task<CanvasBitmap?> LoadImageFromPathAsync(string filePath, CanvasControl canvas)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                return await CanvasBitmap.LoadAsync(canvas, stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image from path: {ex.Message}");
                return null;
            }
        }

        private async Task<CanvasBitmap?> LoadImageFromArchiveEntryAsync(string entryKey, CanvasControl canvas, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            using var memoryStream = new MemoryStream();

            // 1. [Lock 구간] 아카이브에서 데이터만 빠르게 메모리로 복사
            await _archiveLock.WaitAsync();
            try
            {
                if (_currentArchive == null) return null;
                var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                if (archiveEntry == null) return null;

                using var entryStream = archiveEntry.OpenEntryStream();
                // [핵심 수정] CopyToAsync에 토큰을 전달하여 스트림 복사 강제 중단
                await entryStream.CopyToAsync(memoryStream, token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Archive Stream Error: {ex.Message}");
                return null;
            }
            finally
            {
                _archiveLock.Release();
            }

            memoryStream.Position = 0;
            if (token.IsCancellationRequested) return null;

            // 2. [Lock 해제 후] 디코딩 수행 (여기가 CPU를 많이 쓰므로 락 밖에서 해야 함)
            try
            {
                return await CanvasBitmap.LoadAsync(canvas, memoryStream.AsRandomAccessStream());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win2D Load Error: {ex.Message}");
                return null;
            }
        }


        private async Task<byte[]?> LoadBytesFromArchiveEntryAsync(string entryKey, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            await _archiveLock.WaitAsync();
            try
            {
                if (_currentArchive == null) return null;
                var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                if (archiveEntry == null) return null;

                using var entryStream = archiveEntry.OpenEntryStream();
                using var memoryStream = new MemoryStream();
                await entryStream.CopyToAsync(memoryStream, token);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Archive Byte Load Error: {ex.Message}");
                return null;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        private void ShowImageUI()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;

            if (_isSideBySideMode)
            {
                MainCanvas.Visibility = Visibility.Collapsed;
                SideBySideGrid.Visibility = Visibility.Visible;
            }
            else
            {
                MainCanvas.Visibility = Visibility.Visible;
                SideBySideGrid.Visibility = Visibility.Collapsed;
            }
        }

        private string GetFormattedDisplayName(string displayName, bool isArchiveEntry)
        {
            if (isArchiveEntry && !string.IsNullOrEmpty(_currentArchivePath))
            {
                string archiveName = Path.GetFileName(_currentArchivePath);
                return $"{archiveName} - {displayName}";
            }
            return displayName;
        }

        private void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap)
        {
            FileNameText.Text = GetFormattedDisplayName(entry.DisplayName, entry.IsArchiveEntry);
            ImageInfoText.Text = $"{(int)bitmap.Size.Width} × {(int)bitmap.Size.Height}";
            TextProgressText.Text = ""; // Clear for image mode

            if (_isSideBySideMode)
            {
                int displayIndex = (_currentIndex / 2) + 1;
                int totalPairs = (_imageEntries.Count + 1) / 2;
                ImageIndexText.Text = $"{displayIndex} / {totalPairs} (B)";
            }
            else
            {
                ImageIndexText.Text = $"{_currentIndex + 1} / {_imageEntries.Count}";
            }
        }

        private void ImageArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-apply fit when window is resized
            if (_currentBitmap != null &&
                (MainCanvas.Visibility == Visibility.Visible || SideBySideGrid.Visibility == Visibility.Visible))
            {
                ApplyZoom();
            }
        }

        private async void ImageArea_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint(ImageArea).Properties;
            var wheelDelta = properties.MouseWheelDelta;

            // Scroll down = next image, Scroll up = previous image
            if (wheelDelta < 0)
            {
                // Scroll down - next image
                await NavigateToNextAsync();
            }
            else if (wheelDelta > 0)
            {
                // Scroll up - previous image
                await NavigateToPreviousAsync();
            }

            e.Handled = true;
        }

        private async void ImageArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_imageEntries.Count <= 1)
                return;

            var pt = e.GetCurrentPoint(ImageArea);
            if (!pt.Properties.IsLeftButtonPressed)
                return;

            double half = ImageArea.ActualWidth * 0.5;
            if (pt.Position.X < half)
            {
                await NavigateToPreviousAsync();
            }
            else
            {
                await NavigateToNextAsync();
            }

            e.Handled = true;
        }

        #endregion

        #region Navigation

        private async Task NavigateToPreviousAsync()
        {
            if (_currentIndex > 0)
            {
                DetectFastNavigation();

                if (_isSideBySideMode)
                {
                    _currentIndex = Math.Max(0, _currentIndex - 2);
                }
                else
                {
                    _currentIndex--;
                }

                if (_isFastNavigation)
                {
                    ShowFilenameOnly();
                }
                else
                {
                    await DisplayCurrentImageAsync();
                }

                _ = AddToRecentAsync(true);

                // Trigger preloading for previous images if navigating backwards
                if (_currentArchive != null)
                {
                    _ = Task.Run(PreloadPreviousImagesAsync);
                }
            }
        }

        private async Task NavigateToNextAsync()
        {
            if (_currentIndex < _imageEntries.Count - 1)
            {
                bool isFast = DetectFastNavigation();

                if (_isSideBySideMode)
                    _currentIndex = Math.Min(_imageEntries.Count - 1, _currentIndex + 2);
                else
                    _currentIndex++;

                if (isFast)
                {
                    // 빠른 탐색 중에는 텍스트만 업데이트하고 실제 로드는 타이머에 맡김
                    ShowFilenameOnly();
                }
                else
                {
                    // 일반적인 속도의 클릭/입력은 즉시 이미지 로드
                    await DisplayCurrentImageAsync();
                }

                _ = AddToRecentAsync(true);

                if (_currentArchive != null)
                    _ = Task.Run(PreloadNextImagesAsync);
            }
        }

        private async Task NavigateToFileAsync(bool isNext)
        {
            // Save current position before navigating away
            await AddToRecentAsync(true);

            // Find current file/archive in the list
            string? currentPath = null;
            if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
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

            // Ensure explorer path is set if missing (e.g. opened from Recent Files)
            if (string.IsNullOrEmpty(_currentExplorerPath))
            {
                _currentExplorerPath = Path.GetDirectoryName(currentPath);
            }

            if (string.IsNullOrEmpty(_currentExplorerPath))
                return;

            // Ensure file list is loaded and contains the current path
            if (_fileItems.Count == 0 || !_fileItems.Any(f => f.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase)))
            {
                LoadExplorerFolder(_currentExplorerPath);
            }

            if (string.IsNullOrEmpty(currentPath))
                return;

            var currentItemIndex = -1;
            for (int i = 0; i < _fileItems.Count; i++)
            {
                if (_fileItems[i].FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentItemIndex = i;
                    break;
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
                    if (item.IsArchive)
                    {
                        await AddToRecentAsync(true); // Save current before switching
                        await LoadImagesFromArchiveAsync(item.FullPath);
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
                    // If error, continue to next file
                }
            }
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
            if (_currentBitmap != null && !_isFastNavigation)
            {
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

                var destRect = new Windows.Foundation.Rect(position, scaledSize);
                ds.DrawImage(_currentBitmap, destRect);
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

                var destRect = new Windows.Foundation.Rect(position, scaledSize);
                ds.DrawImage(_leftBitmap, destRect);
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

                var destRect = new Windows.Foundation.Rect(position, scaledSize);
                ds.DrawImage(_rightBitmap, destRect);
            }
        }

        #endregion

        #region Image Preloading

        private async Task PreloadNextImagesAsync()
        {
            try
            {
                if (_imageEntries.Count == 0) return;

                var startIndex = _currentIndex + 1;
                var endIndex = Math.Min(_imageEntries.Count - 1, startIndex + PreloadCount);

                var tasks = new List<Task>();

                for (int i = startIndex; i <= endIndex; i++)
                {
                    int index = i;
                    lock (_preloadedImages) { if (_preloadedImages.ContainsKey(index)) continue; }

                    var entry = _imageEntries[index];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            CanvasBitmap? bitmap = null;
                            if (entry.IsArchiveEntry && _currentArchive != null)
                            {
                                bitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, MainCanvas, CancellationToken.None);
                            }
                            else if (!entry.IsArchiveEntry && entry.FilePath != null)
                            {
                                bitmap = await LoadImageFromPathAsync(entry.FilePath, MainCanvas);
                            }

                            if (bitmap != null)
                            {
                                lock (_preloadedImages) { _preloadedImages[index] = bitmap; }
                            }
                        }
                        catch { }
                    }));
                }

                await Task.WhenAll(tasks);

                // Clean up old preloaded images to save memory
                CleanupOldPreloadedImages();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preloading next images: {ex.Message}");
            }
        }

        private async Task PreloadPreviousImagesAsync()
        {
            try
            {
                if (_imageEntries.Count == 0) return;

                var startIndex = Math.Max(0, _currentIndex - PreloadCount);
                var endIndex = _currentIndex - 1;

                var tasks = new List<Task>();

                for (int i = startIndex; i <= endIndex; i++)
                {
                    int index = i;
                    lock (_preloadedImages) { if (_preloadedImages.ContainsKey(index)) continue; }

                    var entry = _imageEntries[index];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            CanvasBitmap? bitmap = null;
                            if (entry.IsArchiveEntry && _currentArchive != null)
                            {
                                bitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, MainCanvas, CancellationToken.None);
                            }
                            else if (!entry.IsArchiveEntry && entry.FilePath != null)
                            {
                                bitmap = await LoadImageFromPathAsync(entry.FilePath, MainCanvas);
                            }

                            if (bitmap != null)
                            {
                                lock (_preloadedImages) { _preloadedImages[index] = bitmap; }
                            }
                        }
                        catch { }
                    }));
                }

                await Task.WhenAll(tasks);

                // Clean up old preloaded images to save memory
                CleanupOldPreloadedImages();
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
                var keepRange = PreloadCount * 2;
                var keysToRemove = _preloadedImages.Keys
                    .Where(index => Math.Abs(index - _currentIndex) > keepRange)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _preloadedImages.Remove(key);
                }
            }
        }

        #endregion



    }
}