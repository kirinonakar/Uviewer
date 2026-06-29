using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : Window, IImageViewerHost
    {
        private ImageViewerController _imageViewerController = null!;

        private void UpdateFastNavigationUI() => _imageViewerController.UpdateFastNavigationUI();

        private Task ResetFastNavigation() => _imageViewerController.ResetFastNavigationAsync();

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomIn();

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOut();

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void ZoomActualButton_Click(object sender, RoutedEventArgs e) => _imageViewerController.ZoomActual();

        private void ZoomIn() => _imageViewerController.ZoomIn();

        private void ZoomOut() => _imageViewerController.ZoomOut();

        private void FitToWindow() => _imageViewerController.FitToWindow();

        private void ApplyZoom() => _imageViewerController.ApplyZoom();

        private Task DisplayCurrentImageAsync() => _imageViewerController.DisplayCurrentImageAsync();

        private void SyncSidebarSelection(ImageEntry entry) => _imageViewerController.SyncSidebarSelection(entry);

        private bool IsBitmapInCache(CanvasBitmap bitmap) => _imageViewerController.IsBitmapInCache(bitmap);

        private void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap) =>
            _imageViewerController.OnAnimatedWebpFrameUpdated(sender, newBitmap);

        private void OnAnimatedWebpAnimationStopped(object? sender, System.EventArgs e) =>
            _imageViewerController.OnAnimatedWebpAnimationStopped(sender, e);

        private async void SharpenButton_Click(object sender, RoutedEventArgs e) =>
            await _imageViewerController.ToggleSharpeningAsync();

        internal void UpdateSharpenButtonState() => _imageViewerController.UpdateSharpenButtonState();

        private void SideBySideButton_Click(object sender, RoutedEventArgs e) => _imageViewerController.ToggleSideBySide();

        private void NextImageSideButton_Click(object sender, RoutedEventArgs e) => _imageViewerController.ToggleNextImageSide();

        internal void UpdateSideBySideButtonState() => _imageViewerController.UpdateSideBySideButtonState();

        internal void UpdateNextImageSideButtonState() => _imageViewerController.UpdateNextImageSideButtonState();

        private void ShowImageUI() => _imageViewerController.ShowImageUI();

        private void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) =>
            _imageViewerController.UpdateStatusBar(entry, bitmap);

        private void ImageArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _imageViewerController.ImageAreaSizeChanged(e);

        private async void ImageArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            await _imageViewerController.HandlePointerWheelAsync(e);

        private void ImageArea_ManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e) =>
            _imageViewerController.ManipulationStarting(e);

        private async void ImageArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) =>
            await _imageViewerController.ManipulationDeltaAsync(e);

        private void ImageArea_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) =>
            _imageViewerController.ManipulationCompleted();

        private Task HandlePdfScrollAsync(double deltaX, double deltaY) =>
            _imageViewerController.HandlePdfScrollAsync(deltaX, deltaY);

        private async void ImageArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            await _imageViewerController.PointerPressedAsync(e);

        private void ClearImageResources() => _imageViewerController.ClearImageResources();

        private Task NavigateToPreviousAsync(bool isManualClick = false) =>
            _imageViewerController.NavigateToPreviousAsync(isManualClick);

        private async void OnSharpenParamsChanged() =>
            await _imageViewerController.OnSharpenParamsChangedAsync();

        private void SharpenParams_Reset_Click(object sender, RoutedEventArgs e) => ImageOptions.Reset();

        private Task NavigateToNextAsync(bool isManualClick = false) =>
            _imageViewerController.NavigateToNextAsync(isManualClick);

        private void StartImagePreload(bool prioritizeNext) =>
            _imageViewerController.StartImagePreload(prioritizeNext);

        private string? GetCurrentNavigatingPath() => _imageViewerController.GetCurrentNavigatingPath();

        private Task NavigateToFileAsync(bool isNext) => _imageViewerController.NavigateToFileAsync(isNext);

        private Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, CancellationToken token) =>
            _imageViewerController.LoadBitmapForPreloadAsync(entry, token);

        System.Collections.Generic.List<ImageEntry> IImageViewerHost.ImageEntries
        {
            get => _imageEntries;
            set => _imageEntries = value;
        }

        System.Collections.ObjectModel.ObservableCollection<FileItem> IImageViewerHost.FileItems => _fileItems;
        int IImageViewerHost.CurrentIndex { get => _currentIndex; set => _currentIndex = value; }
        double IImageViewerHost.ZoomLevel { get => _zoomLevel; set => _zoomLevel = value; }
        CanvasBitmap? IImageViewerHost.CurrentBitmap { get => _currentBitmap; set => _currentBitmap = value; }
        CanvasBitmap? IImageViewerHost.LeftBitmap { get => _leftBitmap; set => _leftBitmap = value; }
        CanvasBitmap? IImageViewerHost.RightBitmap { get => _rightBitmap; set => _rightBitmap = value; }
        bool IImageViewerHost.IsCurrentViewSideBySide { get => _isCurrentViewSideBySide; set => _isCurrentViewSideBySide = value; }
        bool IImageViewerHost.IsSideBySideMode { get => _isSideBySideMode; set => _isSideBySideMode = value; }
        bool IImageViewerHost.NextImageOnRight { get => _nextImageOnRight; set => _nextImageOnRight = value; }
        bool IImageViewerHost.AutoDoublePageForArchive => _autoDoublePageForArchive;
        bool IImageViewerHost.IsAnimatedFrameActive { get => _isAnimatedFrameActive; set => _isAnimatedFrameActive = value; }
        bool IImageViewerHost.SharpenEnabled { get => _sharpenEnabled; set => _sharpenEnabled = value; }
        bool IImageViewerHost.IsSeamlessScroll => _isSeamlessScroll;
        bool IImageViewerHost.IsPdfMode => _currentPdfDocument != null;
        bool IImageViewerHost.IsWebDavMode => _isWebDavMode;
        bool IImageViewerHost.IsTextMode => _isTextMode;
        bool IImageViewerHost.IsEpubMode => _isEpubMode;
        bool IImageViewerHost.IsVerticalMode => _isVerticalMode;
        bool IImageViewerHost.IsAozoraMode => _isAozoraMode;
        bool IImageViewerHost.IsExplorerGrid => _isExplorerGrid;
        bool IImageViewerHost.ShouldInvertControls => ShouldInvertControls;
        string? IImageViewerHost.CurrentTextFilePath => _currentTextFilePath;
        string? IImageViewerHost.CurrentTextArchiveEntryKey => _currentTextArchiveEntryKey;
        string? IImageViewerHost.CurrentEpubFilePath => _currentEpubFilePath;
        string? IImageViewerHost.CurrentWebDavItemPath => _currentWebDavItemPath;
        int IImageViewerHost.CurrentEpubChapterIndex => _currentEpubChapterIndex;
        int IImageViewerHost.CurrentEpubPageIndex => _currentEpubPageIndex;
        int IImageViewerHost.PendingEpubChapterIndex { get => PendingEpubChapterIndex; set => PendingEpubChapterIndex = value; }
        int IImageViewerHost.PendingEpubPageIndex { get => PendingEpubPageIndex; set => PendingEpubPageIndex = value; }
        int IImageViewerHost.PendingEpubStartBlockIndex { get => _pendingEpubStartBlockIndex; set => _pendingEpubStartBlockIndex = value; }
        int IImageViewerHost.AozoraPendingTargetLine { get => _aozoraPendingTargetLine; set => _aozoraPendingTargetLine = value; }
        int IImageViewerHost.CurrentVerticalStartLine => _currentVerticalPageInfo.StartLine;
        double IImageViewerHost.LastCanvasWidth { get => _lastCanvasWidth; set => _lastCanvasWidth = value; }
        CancellationTokenSource? IImageViewerHost.ImageLoadingCts { get => _imageLoadingCts; set => _imageLoadingCts = value; }

        CanvasControl IImageViewerHost.MainCanvas => MainCanvas;
        CanvasControl IImageViewerHost.LeftCanvas => LeftCanvas;
        CanvasControl IImageViewerHost.RightCanvas => RightCanvas;
        FrameworkElement IImageViewerHost.ImageArea => ImageArea;
        FrameworkElement IImageViewerHost.RootGrid => RootGrid;
        FrameworkElement IImageViewerHost.EmptyStatePanel => EmptyStatePanel;
        FrameworkElement IImageViewerHost.FastNavOverlay => FastNavOverlay;
        Grid IImageViewerHost.SideBySideGrid => SideBySideGrid;
        TextBlock IImageViewerHost.FastNavText => FastNavText;
        TextBlock IImageViewerHost.FileNameText => FileNameText;
        TextBlock IImageViewerHost.ImageInfoText => ImageInfoText;
        TextBlock IImageViewerHost.ImageIndexText => ImageIndexText;
        TextBlock IImageViewerHost.TextProgressText => TextProgressText;
        ListViewBase IImageViewerHost.FileListView => FileListView;
        ListViewBase IImageViewerHost.FileGridView => FileGridView;
        ScrollViewer? IImageViewerHost.TextScrollViewer => TextScrollViewer;
        CanvasControl? IImageViewerHost.VerticalTextCanvas => VerticalTextCanvas;
        CanvasControl? IImageViewerHost.AozoraTextCanvas => AozoraTextCanvas;
        CanvasControl? IImageViewerHost.EpubTextCanvas => EpubTextCanvas;
        Controls.MainToolbarControl IImageViewerHost.MainToolbar => MainToolbar;

        FastNavigationService IImageViewerHost.FastNavigationService => _fastNavigationService;
        SevenZipExtractionCoordinator IImageViewerHost.SevenZipExtraction => _sevenZipExtraction;
        ArchiveSession IImageViewerHost.ArchiveSession => _archiveSession;
        IAnimatedWebpService IImageViewerHost.AnimatedWebpService => _animatedWebpService;
        ImageCacheManager IImageViewerHost.ImageCache => _imageCache;
        ImageViewportNavigationService IImageViewerHost.ImageViewportNavigationService => _imageViewportNavigationService;
        ZoomService IImageViewerHost.ZoomService => _zoomService;
        ImageDoublePageDecisionService IImageViewerHost.ImageDoublePageDecisionService => _imageDoublePageDecisionService;
        SideBySideImageLoadService IImageViewerHost.SideBySideImageLoadService => _sideBySideImageLoadService;
        ImageStatusBarService IImageViewerHost.ImageStatusBarService => _imageStatusBarService;
        PreloadManager IImageViewerHost.PreloadManager => _preloadManager;
        ImageBitmapLoader IImageViewerHost.ImageBitmapLoader => _imageBitmapLoader;
        ImageNavigationCoordinator IImageViewerHost.ImageNavigationCoordinator => _imageNavigationCoordinator;
        ImageResourceService IImageViewerHost.ImageResourceService => _imageResourceService;
        WindowSettingsCoordinator IImageViewerHost.WindowSettingsCoordinator => _windowSettingsCoordinator;
        WebDavService IImageViewerHost.WebDavService => _webDavService;
        ImageProcessingViewModel IImageViewerHost.ImageOptions => ImageOptions;
        EpubWin2DPage? IImageViewerHost.CurrentEpubWin2DPage => CurrentEpubWin2DPage;

        void IImageViewerHost.Signal7zJump() => Signal7zJump();
        void IImageViewerHost.SwitchToImageMode() => SwitchToImageMode();
        Task<CanvasBitmap?> IImageViewerHost.LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token, bool isPreload) =>
            LoadPdfPageBitmapAsync(pageIndex, canvas, token, isPreload);

        Task IImageViewerHost.AddToRecentAsync(bool immediate) => AddToRecentAsync(immediate);
        Task IImageViewerHost.LoadTextEntryAsync(ImageEntry entry) => LoadTextEntryAsync(entry);
        Task IImageViewerHost.ReloadTextDisplayFromCacheAsync(string fileName, int targetLine) =>
            ReloadTextDisplayFromCacheAsync(fileName, targetLine);
        Task IImageViewerHost.LoadEpubEntryAsync(ImageEntry entry, CancellationToken token) => LoadEpubEntryAsync(entry, token);
        Task IImageViewerHost.LoadEpubChapterAsync(int index, int targetLine, int targetBlockIndex, int targetPage) =>
            LoadEpubChapterAsync(index, targetLine: targetLine, targetBlockIndex: targetBlockIndex, targetPage: targetPage);
        void IImageViewerHost.ShowEpubImagePage(EpubWin2DPage page) => ShowEpubImagePage(page);
        void IImageViewerHost.SetEpubPageIndex(int index) => SetEpubPageIndex(index);
        Task IImageViewerHost.PrepareVerticalTextAsync(int line) => PrepareVerticalTextAsync(line);
        SharpenParams IImageViewerHost.CreateSharpenParams() => CreateSharpenParams();
        Task IImageViewerHost.RerenderPdfCurrentPageAsync() => RerenderPdfCurrentPageAsync();
        void IImageViewerHost.ShowNotification(string message, string icon, string color) => ShowNotification(message, icon, color);
        Task IImageViewerHost.HandleFileSelectionAsync(FileItem item) => HandleFileSelectionAsync(item);
        void IImageViewerHost.FocusRoot() => RootGrid.Focus(FocusState.Programmatic);
        void IImageViewerHost.SaveWindowSettings() => _windowSettingsCoordinator.SaveWindowSettings();
        void IImageViewerHost.InvalidateEpubTextCanvas() => EpubTextCanvas?.Invalidate();
        void IImageViewerHost.InvalidateVerticalTextCanvas() => VerticalTextCanvas?.Invalidate();
        void IImageViewerHost.InvalidateAozoraTextCanvas() => AozoraTextCanvas?.Invalidate();
    }
}
