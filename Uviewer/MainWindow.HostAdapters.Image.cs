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
        private abstract class ImageWindowPort
        {
            protected readonly MainWindow Window;
            protected ImageViewerState ImageState => Window._imageViewerState;

            protected ImageWindowPort(MainWindow window)
            {
                Window = window;
            }
        }

        private sealed class ImageStatePort : ImageWindowPort, IImageViewerRuntimeHost, IImagePreloadHost
        {
            public ImageStatePort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public int CurrentIndex { get => ImageState.CurrentIndex; set => ImageState.CurrentIndex = value; }
            public double ZoomLevel { get => Window._zoomLevel; set => Window._zoomLevel = value; }
            public CanvasBitmap? CurrentBitmap { get => ImageState.CurrentBitmap; set => ImageState.CurrentBitmap = value; }
            public CanvasBitmap? LeftBitmap => ImageState.LeftBitmap;
            public CanvasBitmap? RightBitmap => ImageState.RightBitmap;
            public bool IsCurrentViewSideBySide { get => ImageState.IsCurrentViewSideBySide; set => ImageState.IsCurrentViewSideBySide = value; }
            public bool IsSideBySideMode => ImageState.IsSideBySideMode;
            public bool AutoDoublePageForArchive => ImageState.AutoDoublePageForArchive;
            public bool SharpenEnabled => ImageState.IsSharpenEnabled;
            public bool IsPdfMode => Window._currentPdfDocument != null;
            public bool IsWebDavMode => Window._isWebDavMode;
            public CancellationTokenSource? ImageLoadingCts { get => ImageState.ImageLoadingCts; set => ImageState.ImageLoadingCts = value; }

            public CanvasControl MainCanvas => Window.MainCanvas;

            public SevenZipExtractionCoordinator SevenZipExtraction => Window._sevenZipExtraction;
            public ArchiveSession ArchiveSession => Window._archiveSession;
            public IAnimatedWebpService AnimatedWebpService => Window._animatedWebpService;
            public ImageCacheManager ImageCache => Window._imageCache;
            public ImageViewportNavigationService ImageViewportNavigationService => Window._imageViewportNavigationService;
            public ImageDoublePageDecisionService ImageDoublePageDecisionService => Window._imageDoublePageDecisionService;
            public PreloadManager PreloadManager => Window._preloadManager;
            public ImageBitmapLoader ImageBitmapLoader => Window._imageBitmapLoader;
            public ImageNavigationCoordinator ImageNavigationCoordinator => Window._imageNavigationCoordinator;
            public WebDavService WebDavService => Window._webDavService;

            public void Signal7zJump() => Window.Signal7zJump();
            public void SwitchToImageMode() => Window.SwitchToImageMode();
            public Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false) =>
                Window._pdfDocumentController.LoadPageBitmapAsync(pageIndex, canvas, token, isPreload);

            public Task AddToRecentAsync(bool immediate) => Window._bookmarkInteractionController.AddCurrentRecentAsync(immediate);
            public SharpenParams CreateSharpenParams() => Window.CreateSharpenParams();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                Window.ShowNotification(message, icon, color);

            public void FocusRoot() => Window.RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private sealed class ImageBitmapLifetimePort : ImageWindowPort, IImageBitmapLifetimeHost
        {
            public ImageBitmapLifetimePort(MainWindow window)
                : base(window)
            {
            }

            public CanvasBitmap? CurrentBitmap { get => ImageState.CurrentBitmap; set => ImageState.CurrentBitmap = value; }
            public CanvasBitmap? LeftBitmap { get => ImageState.LeftBitmap; set => ImageState.LeftBitmap = value; }
            public CanvasBitmap? RightBitmap { get => ImageState.RightBitmap; set => ImageState.RightBitmap = value; }
            public bool IsAnimatedFrameActive { get => ImageState.IsAnimatedFrameActive; set => ImageState.IsAnimatedFrameActive = value; }
            public CancellationTokenSource? ImageLoadingCts => ImageState.ImageLoadingCts;

            public CanvasControl MainCanvas => Window.MainCanvas;
            public CanvasControl LeftCanvas => Window.LeftCanvas;
            public CanvasControl RightCanvas => Window.RightCanvas;
            public TextBlock FileNameText => Window.FileNameText;
            public TextBlock ImageInfoText => Window.ImageInfoText;
            public TextBlock ImageIndexText => Window.ImageIndexText;

            public ImageCacheManager ImageCache => Window._imageCache;
            public IAnimatedWebpService AnimatedWebpService => Window._animatedWebpService;
            public PreloadManager PreloadManager => Window._preloadManager;
        }

        private sealed class ImageInputPort : ImageWindowPort, IImageInputHost
        {
            public ImageInputPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public double ZoomLevel => Window._zoomLevel;
            public CanvasBitmap? CurrentBitmap => ImageState.CurrentBitmap;
            public bool IsCurrentViewSideBySide => ImageState.IsCurrentViewSideBySide;
            public bool IsPdfMode => Window._currentPdfDocument != null;
            public bool ShouldInvertControls => Window.ShouldInvertControls;
            public double LastCanvasWidth { get => ImageState.LastCanvasWidth; set => ImageState.LastCanvasWidth = value; }

            public CanvasControl MainCanvas => Window.MainCanvas;
            public FrameworkElement ImageArea => Window.ImageArea;
            public Grid SideBySideGrid => Window.SideBySideGrid;

            public ImageViewportNavigationService ImageViewportNavigationService => Window._imageViewportNavigationService;

            public Task RerenderPdfCurrentPageAsync() => Window._pdfDocumentController.RerenderCurrentPageAsync();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                Window.ShowNotification(message, icon, color);

            public void FocusRoot() => Window.RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private sealed class ImagePdfPageDisplayPort : ImageWindowPort, IImagePdfPageDisplayHost
        {
            public ImagePdfPageDisplayPort(MainWindow window)
                : base(window)
            {
            }

            public int CurrentIndex => ImageState.CurrentIndex;
            public double ZoomLevel => Window._zoomLevel;
            public CanvasBitmap? CurrentBitmap { get => ImageState.CurrentBitmap; set => ImageState.CurrentBitmap = value; }
            public CanvasBitmap? LeftBitmap { get => ImageState.LeftBitmap; set => ImageState.LeftBitmap = value; }
            public CanvasBitmap? RightBitmap { get => ImageState.RightBitmap; set => ImageState.RightBitmap = value; }
            public bool IsCurrentViewSideBySide { get => ImageState.IsCurrentViewSideBySide; set => ImageState.IsCurrentViewSideBySide = value; }
            public bool IsSeamlessScroll => ImageState.IsSeamlessScroll;

            public CanvasControl MainCanvas => Window.MainCanvas;

            public ImageCacheManager ImageCache => Window._imageCache;
            public ImageViewportNavigationService ImageViewportNavigationService => Window._imageViewportNavigationService;

            public void SwitchToImageMode() => Window.SwitchToImageMode();
            public Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false) =>
                Window._pdfDocumentController.LoadPageBitmapAsync(pageIndex, canvas, token, isPreload);

            public Task AddToRecentAsync(bool immediate) => Window._bookmarkInteractionController.AddCurrentRecentAsync(immediate);
            public Task RerenderPdfCurrentPageAsync() => Window._pdfDocumentController.RerenderCurrentPageAsync();
            public void FocusRoot() => Window.RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private sealed class ImageSideBySideDisplayPort : ImageWindowPort, IImageSideBySideDisplayHost
        {
            public ImageSideBySideDisplayPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public int CurrentIndex => ImageState.CurrentIndex;
            public bool NextImageOnRight => ImageState.NextImageOnRight;
            public double ZoomLevel { get => Window._zoomLevel; set => Window._zoomLevel = value; }
            public CanvasBitmap? CurrentBitmap { get => ImageState.CurrentBitmap; set => ImageState.CurrentBitmap = value; }
            public CanvasBitmap? LeftBitmap { get => ImageState.LeftBitmap; set => ImageState.LeftBitmap = value; }
            public CanvasBitmap? RightBitmap { get => ImageState.RightBitmap; set => ImageState.RightBitmap = value; }

            public CanvasControl LeftCanvas => Window.LeftCanvas;
            public CanvasControl RightCanvas => Window.RightCanvas;
            public TextBlock FileNameText => Window.FileNameText;

            public ImageCacheManager ImageCache => Window._imageCache;
            public SideBySideImageLoadService SideBySideImageLoadService => Window._sideBySideImageLoadService;
        }

        private sealed class ImageSingleDisplayPort : ImageWindowPort, IImageSingleDisplayHost
        {
            public ImageSingleDisplayPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public int CurrentIndex => ImageState.CurrentIndex;
            public double ZoomLevel { get => Window._zoomLevel; set => Window._zoomLevel = value; }
            public CanvasBitmap? CurrentBitmap { get => ImageState.CurrentBitmap; set => ImageState.CurrentBitmap = value; }
            public bool IsAnimatedFrameActive { get => ImageState.IsAnimatedFrameActive; set => ImageState.IsAnimatedFrameActive = value; }
            public bool SharpenEnabled => ImageState.IsSharpenEnabled;

            public CanvasControl MainCanvas => Window.MainCanvas;
            public TextBlock FileNameText => Window.FileNameText;
            public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue => Window.DispatcherQueue;

            public ImageCacheManager ImageCache => Window._imageCache;
            public ImageViewportNavigationService ImageViewportNavigationService => Window._imageViewportNavigationService;
            public IAnimatedWebpService AnimatedWebpService => Window._animatedWebpService;
            public ImageProcessingViewModel ImageOptions => Window.ImageOptions;
        }

        private sealed class ImageZoomPort : ImageWindowPort, IImageZoomHost
        {
            public ImageZoomPort(MainWindow window)
                : base(window)
            {
            }

            public double ZoomLevel => Window._zoomLevel;
            public CanvasBitmap? CurrentBitmap => ImageState.CurrentBitmap;
            public bool IsCurrentViewSideBySide => ImageState.IsCurrentViewSideBySide;
            public bool IsPdfMode => Window._currentPdfDocument != null;

            public CanvasControl MainCanvas => Window.MainCanvas;
            public CanvasControl LeftCanvas => Window.LeftCanvas;
            public CanvasControl RightCanvas => Window.RightCanvas;
            public FrameworkElement ImageArea => Window.ImageArea;
            public MainToolbarControl MainToolbar => Window.MainToolbar;

            public ZoomService ZoomService => Window._zoomService;
            public ImageViewportNavigationService ImageViewportNavigationService => Window._imageViewportNavigationService;

            public Task RerenderPdfCurrentPageAsync() => Window._pdfDocumentController.RerenderCurrentPageAsync();
        }

        private sealed class ImageNavigationPort : ImageWindowPort, IImageExplorerNavigationHost, IImageFastNavigationHost
        {
            public ImageNavigationPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public ObservableCollection<FileItem> FileItems => Window._fileItems;
            public int CurrentIndex { get => ImageState.CurrentIndex; set => ImageState.CurrentIndex = value; }
            public bool IsCurrentViewSideBySide => ImageState.IsCurrentViewSideBySide;
            public bool IsWebDavMode => Window._isWebDavMode;
            public bool IsExplorerGrid => Window._isExplorerGrid;
            public bool IsEpubMode => Window._isEpubMode;
            public bool IsTextMode => Window._isTextMode;
            public string? CurrentWebDavItemPath => Window._currentWebDavItemPath;
            public string? CurrentEpubFilePath => Window._currentEpubFilePath;
            public string? CurrentTextFilePath => Window._currentTextFilePath;

            public CanvasControl MainCanvas => Window.MainCanvas;
            public FrameworkElement FastNavOverlay => Window.FastNavOverlay;
            public TextBlock FastNavText => Window.FastNavText;
            public TextBlock FileNameText => Window.FileNameText;
            public TextBlock ImageInfoText => Window.ImageInfoText;
            public TextBlock ImageIndexText => Window.ImageIndexText;
            public TextBlock TextProgressText => Window.TextProgressText;
            public ListViewBase FileListView => Window.FileListView;
            public ListViewBase FileGridView => Window.FileGridView;

            public FastNavigationService FastNavigationService => Window._fastNavigationService;
            public ArchiveSession ArchiveSession => Window._archiveSession;

            public void Signal7zJump() => Window.Signal7zJump();
            public Task AddToRecentAsync(bool immediate) => Window._bookmarkInteractionController.AddCurrentRecentAsync(immediate);
            public Task HandleFileSelectionAsync(FileItem item) => Window.HandleFileSelectionAsync(item);
            public void FocusRoot() => Window.RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private sealed class ImageDocumentOpenPort : ImageWindowPort, IImageDocumentEntryHost
        {
            public ImageDocumentOpenPort(MainWindow window)
                : base(window)
            {
            }

            public bool IsTextMode => Window._isTextMode;
            public bool IsEpubMode => Window._isEpubMode;
            public bool IsVerticalMode => Window._isVerticalMode;
            public bool IsAozoraMode => Window._isAozoraMode;
            public string? CurrentTextFilePath => Window._currentTextFilePath;
            public string? CurrentTextArchiveEntryKey => Window._currentTextArchiveEntryKey;
            public string? CurrentEpubFilePath => Window._currentEpubFilePath;
            public int CurrentEpubChapterIndex => Window._currentEpubChapterIndex;
            public int PendingEpubChapterIndex { get => Window.PendingEpubChapterIndex; set => Window.PendingEpubChapterIndex = value; }
            public int PendingEpubPageIndex { get => Window.PendingEpubPageIndex; set => Window.PendingEpubPageIndex = value; }
            public int PendingEpubStartBlockIndex { get => Window._pendingEpubStartBlockIndex; set => Window._pendingEpubStartBlockIndex = value; }
            public int AozoraPendingTargetLine { get => Window._aozoraPendingTargetLine; set => Window._aozoraPendingTargetLine = value; }

            public ScrollViewer? TextScrollViewer => Window.TextScrollViewer;
            public EpubWin2DPage? CurrentEpubWin2DPage => Window.CurrentEpubWin2DPage;

            public Task AddToRecentAsync(bool immediate) => Window._bookmarkInteractionController.AddCurrentRecentAsync(immediate);
            public Task LoadTextEntryAsync(ImageEntry entry) => Window.LoadTextEntryAsync(entry);
            public Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine) =>
                Window.ReloadTextDisplayFromCacheAsync(fileName, targetLine);

            public Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token) => Window._epubReaderController.LoadEpubEntryAsync(entry, token);
            public Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1) =>
                Window._epubReaderController.LoadEpubChapterAsync(index, targetLine: targetLine, targetBlockIndex: targetBlockIndex, targetPage: targetPage);

            public void ShowEpubImagePage(EpubWin2DPage page) => Window._epubReaderController.ShowEpubImagePage(page);
            public void InvalidateEpubTextCanvas() => Window.EpubTextCanvas?.Invalidate();
            public void InvalidateVerticalTextCanvas() => Window.VerticalTextCanvas?.Invalidate();
            public void InvalidateAozoraTextCanvas() => Window.AozoraTextCanvas?.Invalidate();
        }

        private sealed class ImageUiPort : ImageWindowPort, IImagePresentationHost
        {
            public ImageUiPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public bool SharpenEnabled => ImageState.IsSharpenEnabled;
            public bool IsCurrentViewSideBySide => ImageState.IsCurrentViewSideBySide;
            public bool IsSideBySideMode => ImageState.IsSideBySideMode;
            public bool NextImageOnRight => ImageState.NextImageOnRight;
            public bool IsPdfMode => Window._currentPdfDocument != null;
            public bool IsWebDavMode => Window._isWebDavMode;
            public string? CurrentWebDavItemPath => Window._currentWebDavItemPath;
            public int CurrentIndex => ImageState.CurrentIndex;

            public FrameworkElement EmptyStatePanel => Window.EmptyStatePanel;
            public CanvasControl MainCanvas => Window.MainCanvas;
            public Grid SideBySideGrid => Window.SideBySideGrid;
            public TextBlock FileNameText => Window.FileNameText;
            public TextBlock ImageInfoText => Window.ImageInfoText;
            public TextBlock ImageIndexText => Window.ImageIndexText;
            public TextBlock TextProgressText => Window.TextProgressText;
            public MainToolbarControl MainToolbar => Window.MainToolbar;

            public ArchiveSession ArchiveSession => Window._archiveSession;
            public ImageStatusBarService ImageStatusBarService => Window._imageStatusBarService;
        }

        private sealed class ImageSettingsPort : ImageWindowPort, IImageViewingOptionsHost
        {
            public ImageSettingsPort(MainWindow window)
                : base(window)
            {
            }

            public List<ImageEntry> ImageEntries => ImageState.Entries;
            public CanvasBitmap? CurrentBitmap => ImageState.CurrentBitmap;
            public CanvasBitmap? LeftBitmap => ImageState.LeftBitmap;
            public CanvasBitmap? RightBitmap => ImageState.RightBitmap;
            public bool SharpenEnabled { get => ImageState.IsSharpenEnabled; set => ImageState.IsSharpenEnabled = value; }
            public bool IsPdfMode => Window._currentPdfDocument != null;
            public bool IsSideBySideMode { get => ImageState.IsSideBySideMode; set => ImageState.IsSideBySideMode = value; }
            public bool NextImageOnRight { get => ImageState.NextImageOnRight; set => ImageState.NextImageOnRight = value; }
            public bool IsVerticalMode => Window._isVerticalMode;
            public bool IsEpubMode => Window._isEpubMode;
            public bool IsAozoraMode => Window._isAozoraMode;
            public int CurrentVerticalStartLine => Window._currentVerticalPageInfo.StartLine;
            public int CurrentEpubChapterIndex => Window._currentEpubChapterIndex;
            public int CurrentEpubPageIndex => Window._currentEpubPageIndex;
            public EpubWin2DPage? CurrentEpubWin2DPage => Window.CurrentEpubWin2DPage;

            public ImageCacheManager ImageCache => Window._imageCache;
            public IAnimatedWebpService AnimatedWebpService => Window._animatedWebpService;
            public ImageResourceService ImageResourceService => Window._imageResourceService;

            public Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1) =>
                Window._epubReaderController.LoadEpubChapterAsync(index, targetLine: targetLine, targetBlockIndex: targetBlockIndex, targetPage: targetPage);

            public Task PrepareVerticalTextAsync(int line) => Window.PrepareVerticalTextAsync(line);
            public void SetEpubPageIndex(int index) => Window._epubReaderController.SetEpubPageIndex(index);
            public void ShowEpubImagePage(EpubWin2DPage page) => Window._epubReaderController.ShowEpubImagePage(page);
            public void InvalidateEpubTextCanvas() => Window.EpubTextCanvas?.Invalidate();
            public void InvalidateVerticalTextCanvas() => Window.VerticalTextCanvas?.Invalidate();
            public void InvalidateAozoraTextCanvas() => Window.AozoraTextCanvas?.Invalidate();
            public void SaveWindowSettings() => Window._windowSettingsCoordinator.SaveWindowSettings();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                Window.ShowNotification(message, icon, color);
        }
    }
}
