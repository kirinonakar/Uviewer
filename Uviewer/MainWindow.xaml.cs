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
    public sealed partial class MainWindow : Window
    {
        // Image preloading for faster navigation
        private Services.ImageCacheManager _imageCache = null!;
        private WindowStateManager _windowState = null!;
        private Services.IThumbnailService _thumbnailService = null!;
        private Services.PreloadManager _preloadManager = null!;
        private Services.ImageBitmapLoader _imageBitmapLoader = null!;
        private Services.ImageDoublePageDecisionService _imageDoublePageDecisionService = null!;
        private Services.ImageStatusBarService _imageStatusBarService = null!;
        private Services.SideBySideImageLoadService _sideBySideImageLoadService = null!;
        private Services.ImageViewportNavigationService _imageViewportNavigationService = null!;

        // Refactored Services
        private Services.WindowSettingsCoordinator _windowSettingsCoordinator = null!;
        private Services.ExplorerController _explorerController = null!;
        private Services.ExplorerSidebarController _explorerSidebarController = null!;
        private Services.BookmarkPanelController _bookmarkPanelController = null!;
        private Services.FavoritesController _favoritesController = null!;
        private Services.RecentController _recentController = null!;
        private Services.BookmarkInteractionController _bookmarkInteractionController = null!;
        private Services.IBookmarkNavigationHost _bookmarkNavigationHost = null!;
        private Services.AppSettingsService _appSettingsService = null!;
        private Services.ZoomService _zoomService = null!;
        private Services.ISharpeningService _sharpeningService = null!;
        private Services.FastNavigationService _fastNavigationService = null!;
        private IAnimatedWebpService _animatedWebpService = null!;
        private IKeyboardShortcutService _keyboardShortcutService = null!;
        private Services.TocService _tocService = null!;
        private Services.DocumentSearchService _documentSearchService = null!;
        private Services.SearchHighlightService _searchHighlightService = null!;
        private Services.DocumentSearchCoordinatorService _documentSearchCoordinatorService = null!;
        private Services.SearchOverlayService _searchOverlayService = null!;
        private DocumentSearchState _documentSearchState = null!;
        private Services.DocumentSessionTracker _documentSessionTracker = null!;
        private Services.ExplorerItemLaunchService _explorerItemLaunchService = null!;
        private string? _activeSearchQuery => _documentSearchState.Query;
        private IReadOnlyList<PdfSearchHighlight> _activePdfSearchHighlights => _documentSearchState.PdfHighlights;
        private int _activePdfSearchPageIndex => _documentSearchState.PdfPageIndex;
        private int _activePdfSearchMatchIndex => _documentSearchState.PdfMatchIndex;
        private Services.ImageResourceService _imageResourceService = null!;
        private bool _isWindowClosing;
        private Services.ShutdownCoordinator _shutdownCoordinator = null!;
        private Services.LocalDocumentOpenCoordinator _localDocumentOpenCoordinator = null!;
        private Services.WebDavDocumentOpenCoordinator _webDavDocumentOpenCoordinator = null!;
        private Services.DocumentNavigationCoordinator _documentNavigationCoordinator = null!;
        private Services.ImageNavigationCoordinator _imageNavigationCoordinator = null!;
        private Services.LocalImageDocumentController _localImageDocumentController = null!;
        private Services.PdfDocumentController _pdfDocumentController = null!;
        private Services.ArchiveDocumentController _archiveDocumentController = null!;
        private Services.FileOpenController _fileOpenController = null!;
        private Services.ExternalProgramSettingsController _externalProgramSettingsController = null!;
        private Services.ExplorerItemOperationController _explorerItemOperationController = null!;
        private Services.DocumentOpenStateQuery _documentOpenStateQuery = null!;

        // ImageResourceService를 _sharpeningService 다음에 생성해야 하므로
        // 필드 초기화 식 대신 생성자 내부에서 초기화합니다.

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private Services.SevenZipExtractionCoordinator _sevenZipExtraction = null!;

        private void Signal7zJump()
        {
            _sevenZipExtraction.SignalJump(_currentIndex);
        }





        private Services.FavoritesService _favoritesService = null!;
        private Services.RecentService _recentService = null!;
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
            MainWindowComposition.Initialize(this, launchFilePath);
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
