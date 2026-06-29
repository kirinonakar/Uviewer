using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class ImageViewerHostAdapter : IImageViewerHost
        {
            private readonly MainWindow _window;

            public ImageViewerHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public List<ImageEntry> ImageEntries
            {
                get => _window._imageEntries;
                set => _window._imageEntries = value;
            }

            public ObservableCollection<FileItem> FileItems => _window._fileItems;
            public int CurrentIndex { get => _window._currentIndex; set => _window._currentIndex = value; }
            public double ZoomLevel { get => _window._zoomLevel; set => _window._zoomLevel = value; }
            public CanvasBitmap? CurrentBitmap { get => _window._currentBitmap; set => _window._currentBitmap = value; }
            public CanvasBitmap? LeftBitmap { get => _window._leftBitmap; set => _window._leftBitmap = value; }
            public CanvasBitmap? RightBitmap { get => _window._rightBitmap; set => _window._rightBitmap = value; }
            public bool IsCurrentViewSideBySide { get => _window._isCurrentViewSideBySide; set => _window._isCurrentViewSideBySide = value; }
            public bool IsSideBySideMode { get => _window._isSideBySideMode; set => _window._isSideBySideMode = value; }
            public bool NextImageOnRight { get => _window._nextImageOnRight; set => _window._nextImageOnRight = value; }
            public bool AutoDoublePageForArchive => _window._autoDoublePageForArchive;
            public bool IsAnimatedFrameActive { get => _window._isAnimatedFrameActive; set => _window._isAnimatedFrameActive = value; }
            public bool SharpenEnabled { get => _window._sharpenEnabled; set => _window._sharpenEnabled = value; }
            public bool IsSeamlessScroll => _window._isSeamlessScroll;
            public bool IsPdfMode => _window._currentPdfDocument != null;
            public bool IsWebDavMode => _window._isWebDavMode;
            public bool IsTextMode => _window._isTextMode;
            public bool IsEpubMode => _window._isEpubMode;
            public bool IsVerticalMode => _window._isVerticalMode;
            public bool IsAozoraMode => _window._isAozoraMode;
            public bool IsExplorerGrid => _window._isExplorerGrid;
            public bool ShouldInvertControls => _window.ShouldInvertControls;
            public string? CurrentTextFilePath => _window._currentTextFilePath;
            public string? CurrentTextArchiveEntryKey => _window._currentTextArchiveEntryKey;
            public string? CurrentEpubFilePath => _window._currentEpubFilePath;
            public string? CurrentWebDavItemPath => _window._currentWebDavItemPath;
            public int CurrentEpubChapterIndex => _window._currentEpubChapterIndex;
            public int CurrentEpubPageIndex => _window._currentEpubPageIndex;
            public int PendingEpubChapterIndex { get => _window.PendingEpubChapterIndex; set => _window.PendingEpubChapterIndex = value; }
            public int PendingEpubPageIndex { get => _window.PendingEpubPageIndex; set => _window.PendingEpubPageIndex = value; }
            public int PendingEpubStartBlockIndex { get => _window._pendingEpubStartBlockIndex; set => _window._pendingEpubStartBlockIndex = value; }
            public int AozoraPendingTargetLine { get => _window._aozoraPendingTargetLine; set => _window._aozoraPendingTargetLine = value; }
            public int CurrentVerticalStartLine => _window._currentVerticalPageInfo.StartLine;
            public double LastCanvasWidth { get => _window._lastCanvasWidth; set => _window._lastCanvasWidth = value; }
            public CancellationTokenSource? ImageLoadingCts { get => _window._imageLoadingCts; set => _window._imageLoadingCts = value; }

            public CanvasControl MainCanvas => _window.MainCanvas;
            public CanvasControl LeftCanvas => _window.LeftCanvas;
            public CanvasControl RightCanvas => _window.RightCanvas;
            public FrameworkElement ImageArea => _window.ImageArea;
            public FrameworkElement RootGrid => _window.RootGrid;
            public FrameworkElement EmptyStatePanel => _window.EmptyStatePanel;
            public FrameworkElement FastNavOverlay => _window.FastNavOverlay;
            public Grid SideBySideGrid => _window.SideBySideGrid;
            public TextBlock FastNavText => _window.FastNavText;
            public TextBlock FileNameText => _window.FileNameText;
            public TextBlock ImageInfoText => _window.ImageInfoText;
            public TextBlock ImageIndexText => _window.ImageIndexText;
            public TextBlock TextProgressText => _window.TextProgressText;
            public ListViewBase FileListView => _window.FileListView;
            public ListViewBase FileGridView => _window.FileGridView;
            public ScrollViewer? TextScrollViewer => _window.TextScrollViewer;
            public CanvasControl? VerticalTextCanvas => _window.VerticalTextCanvas;
            public CanvasControl? AozoraTextCanvas => _window.AozoraTextCanvas;
            public CanvasControl? EpubTextCanvas => _window.EpubTextCanvas;
            public MainToolbarControl MainToolbar => _window.MainToolbar;
            public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue => _window.DispatcherQueue;

            public FastNavigationService FastNavigationService => _window._fastNavigationService;
            public SevenZipExtractionCoordinator SevenZipExtraction => _window._sevenZipExtraction;
            public ArchiveSession ArchiveSession => _window._archiveSession;
            public IAnimatedWebpService AnimatedWebpService => _window._animatedWebpService;
            public ImageCacheManager ImageCache => _window._imageCache;
            public ImageViewportNavigationService ImageViewportNavigationService => _window._imageViewportNavigationService;
            public ZoomService ZoomService => _window._zoomService;
            public ImageDoublePageDecisionService ImageDoublePageDecisionService => _window._imageDoublePageDecisionService;
            public SideBySideImageLoadService SideBySideImageLoadService => _window._sideBySideImageLoadService;
            public ImageStatusBarService ImageStatusBarService => _window._imageStatusBarService;
            public PreloadManager PreloadManager => _window._preloadManager;
            public ImageBitmapLoader ImageBitmapLoader => _window._imageBitmapLoader;
            public ImageNavigationCoordinator ImageNavigationCoordinator => _window._imageNavigationCoordinator;
            public ImageResourceService ImageResourceService => _window._imageResourceService;
            public WindowSettingsCoordinator WindowSettingsCoordinator => _window._windowSettingsCoordinator;
            public WebDavService WebDavService => _window._webDavService;
            public ImageProcessingViewModel ImageOptions => _window.ImageOptions;
            public EpubWin2DPage? CurrentEpubWin2DPage => _window.CurrentEpubWin2DPage;

            public void Signal7zJump() => _window.Signal7zJump();
            public void SwitchToImageMode() => _window.SwitchToImageMode();
            public Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false) =>
                _window.LoadPdfPageBitmapAsync(pageIndex, canvas, token, isPreload);

            public Task AddToRecentAsync(bool immediate) => _window.AddToRecentAsync(immediate);
            public Task LoadTextEntryAsync(ImageEntry entry) => _window.LoadTextEntryAsync(entry);
            public Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine) =>
                _window.ReloadTextDisplayFromCacheAsync(fileName, targetLine);
            public Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token) => _window.LoadEpubEntryAsync(entry, token);
            public Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1) =>
                _window.LoadEpubChapterAsync(index, targetLine: targetLine, targetBlockIndex: targetBlockIndex, targetPage: targetPage);
            public void ShowEpubImagePage(EpubWin2DPage page) => _window.ShowEpubImagePage(page);
            public void SetEpubPageIndex(int index) => _window.SetEpubPageIndex(index);
            public Task PrepareVerticalTextAsync(int line) => _window.PrepareVerticalTextAsync(line);
            public SharpenParams CreateSharpenParams() => _window.CreateSharpenParams();
            public Task RerenderPdfCurrentPageAsync() => _window.RerenderPdfCurrentPageAsync();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") => _window.ShowNotification(message, icon, color);
            public Task HandleFileSelectionAsync(FileItem item) => _window.HandleFileSelectionAsync(item);
            public void FocusRoot() => _window.RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            public void SaveWindowSettings() => _window._windowSettingsCoordinator.SaveWindowSettings();
            public void InvalidateEpubTextCanvas() => _window.EpubTextCanvas?.Invalidate();
            public void InvalidateVerticalTextCanvas() => _window.VerticalTextCanvas?.Invalidate();
            public void InvalidateAozoraTextCanvas() => _window.AozoraTextCanvas?.Invalidate();
        }
    }
}
