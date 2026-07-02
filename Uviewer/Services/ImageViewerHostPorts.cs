using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal interface IImageFastNavigationHost
    {
        List<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; }
        bool IsCurrentViewSideBySide { get; }

        CanvasControl MainCanvas { get; }
        FrameworkElement FastNavOverlay { get; }
        TextBlock FastNavText { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock ImageIndexText { get; }
        TextBlock TextProgressText { get; }

        FastNavigationService FastNavigationService { get; }

        void Signal7zJump();
    }

    internal interface IImageZoomHost
    {
        double ZoomLevel { get; }
        CanvasBitmap? CurrentBitmap { get; }
        bool IsCurrentViewSideBySide { get; }
        bool IsPdfMode { get; }

        CanvasControl MainCanvas { get; }
        CanvasControl LeftCanvas { get; }
        CanvasControl RightCanvas { get; }
        FrameworkElement ImageArea { get; }
        MainToolbarControl MainToolbar { get; }

        ZoomService ZoomService { get; }
        ImageViewportNavigationService ImageViewportNavigationService { get; }

        Task RerenderPdfCurrentPageAsync();
    }

    internal interface IImageInputHost
    {
        List<ImageEntry> ImageEntries { get; }
        double ZoomLevel { get; }
        CanvasBitmap? CurrentBitmap { get; }
        bool IsCurrentViewSideBySide { get; }
        bool IsPdfMode { get; }
        bool ShouldInvertControls { get; }
        double LastCanvasWidth { get; set; }

        CanvasControl MainCanvas { get; }
        FrameworkElement ImageArea { get; }
        Grid SideBySideGrid { get; }

        ImageViewportNavigationService ImageViewportNavigationService { get; }

        Task RerenderPdfCurrentPageAsync();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        void FocusRoot();
    }

    internal interface IImagePresentationHost
    {
        List<ImageEntry> ImageEntries { get; }
        bool SharpenEnabled { get; }
        bool IsCurrentViewSideBySide { get; }
        bool IsSideBySideMode { get; }
        bool NextImageOnRight { get; }
        bool IsPdfMode { get; }
        bool IsWebDavMode { get; }
        string? CurrentWebDavItemPath { get; }
        int CurrentIndex { get; }

        FrameworkElement EmptyStatePanel { get; }
        CanvasControl MainCanvas { get; }
        Grid SideBySideGrid { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock ImageIndexText { get; }
        TextBlock TextProgressText { get; }
        MainToolbarControl MainToolbar { get; }

        ArchiveSession ArchiveSession { get; }
        ImageStatusBarService ImageStatusBarService { get; }
    }

    internal interface IImageExplorerNavigationHost
    {
        List<ImageEntry> ImageEntries { get; }
        ObservableCollection<FileItem> FileItems { get; }
        int CurrentIndex { get; set; }
        bool IsWebDavMode { get; }
        bool IsExplorerGrid { get; }
        bool IsEpubMode { get; }
        bool IsTextMode { get; }
        string? CurrentWebDavItemPath { get; }
        string? CurrentEpubFilePath { get; }
        string? CurrentTextFilePath { get; }

        ListViewBase FileListView { get; }
        ListViewBase FileGridView { get; }

        ArchiveSession ArchiveSession { get; }

        Task AddToRecentAsync(bool immediate);
        Task HandleFileSelectionAsync(FileItem item);
        void FocusRoot();
    }

    internal interface IImagePreloadHost
    {
        List<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; }
        double ZoomLevel { get; }
        CanvasBitmap? CurrentBitmap { get; }
        CanvasBitmap? LeftBitmap { get; }
        CanvasBitmap? RightBitmap { get; }
        bool IsPdfMode { get; }
        bool SharpenEnabled { get; }

        CanvasControl MainCanvas { get; }

        PreloadManager PreloadManager { get; }
        ImageBitmapLoader ImageBitmapLoader { get; }
    }

    internal interface IImageBitmapLifetimeHost
    {
        CanvasBitmap? CurrentBitmap { get; set; }
        CanvasBitmap? LeftBitmap { get; set; }
        CanvasBitmap? RightBitmap { get; set; }
        bool IsAnimatedFrameActive { get; set; }
        CancellationTokenSource? ImageLoadingCts { get; }

        CanvasControl MainCanvas { get; }
        CanvasControl LeftCanvas { get; }
        CanvasControl RightCanvas { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock ImageIndexText { get; }

        ImageCacheManager ImageCache { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        PreloadManager PreloadManager { get; }
    }

    internal interface IImageDocumentEntryHost
    {
        bool IsTextMode { get; }
        bool IsEpubMode { get; }
        bool IsVerticalMode { get; }
        bool IsAozoraMode { get; }
        string? CurrentTextFilePath { get; }
        string? CurrentTextArchiveEntryKey { get; }
        string? CurrentEpubFilePath { get; }
        int CurrentEpubChapterIndex { get; }
        int PendingEpubChapterIndex { get; set; }
        int PendingEpubPageIndex { get; set; }
        int PendingEpubStartBlockIndex { get; set; }
        int AozoraPendingTargetLine { get; set; }

        ScrollViewer? TextScrollViewer { get; }
        EpubWin2DPage? CurrentEpubWin2DPage { get; }

        Task AddToRecentAsync(bool immediate);
        Task LoadTextEntryAsync(ImageEntry entry);
        Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine);
        Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token);
        Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1);
        void ShowEpubImagePage(EpubWin2DPage page);
        void InvalidateEpubTextCanvas();
        void InvalidateVerticalTextCanvas();
        void InvalidateAozoraTextCanvas();
    }

    internal interface IImageViewingOptionsHost
    {
        List<ImageEntry> ImageEntries { get; }
        CanvasBitmap? CurrentBitmap { get; }
        CanvasBitmap? LeftBitmap { get; }
        CanvasBitmap? RightBitmap { get; }
        bool SharpenEnabled { get; set; }
        bool IsPdfMode { get; }
        bool IsSideBySideMode { get; set; }
        bool NextImageOnRight { get; set; }
        bool IsVerticalMode { get; }
        bool IsEpubMode { get; }
        bool IsAozoraMode { get; }
        int CurrentVerticalStartLine { get; }
        int CurrentEpubChapterIndex { get; }
        int CurrentEpubPageIndex { get; }
        EpubWin2DPage? CurrentEpubWin2DPage { get; }

        ImageCacheManager ImageCache { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        ImageResourceService ImageResourceService { get; }

        Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1);
        Task PrepareVerticalTextAsync(int line);
        void SetEpubPageIndex(int index);
        void ShowEpubImagePage(EpubWin2DPage page);
        void InvalidateEpubTextCanvas();
        void InvalidateVerticalTextCanvas();
        void InvalidateAozoraTextCanvas();
        void SaveWindowSettings();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
    }

    internal interface IImageSideBySideDisplayHost
    {
        List<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; }
        bool NextImageOnRight { get; }
        double ZoomLevel { get; set; }
        CanvasBitmap? CurrentBitmap { get; set; }
        CanvasBitmap? LeftBitmap { get; set; }
        CanvasBitmap? RightBitmap { get; set; }

        CanvasControl LeftCanvas { get; }
        CanvasControl RightCanvas { get; }
        TextBlock FileNameText { get; }

        ImageCacheManager ImageCache { get; }
        SideBySideImageLoadService SideBySideImageLoadService { get; }
    }

    internal interface IImageSingleDisplayHost
    {
        List<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; }
        double ZoomLevel { get; set; }
        CanvasBitmap? CurrentBitmap { get; set; }
        bool IsAnimatedFrameActive { get; set; }
        bool SharpenEnabled { get; }

        CanvasControl MainCanvas { get; }
        TextBlock FileNameText { get; }
        DispatcherQueue DispatcherQueue { get; }

        ImageCacheManager ImageCache { get; }
        ImageViewportNavigationService ImageViewportNavigationService { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        ImageProcessingViewModel ImageOptions { get; }
    }

    internal interface IImagePdfPageDisplayHost
    {
        int CurrentIndex { get; }
        double ZoomLevel { get; }
        CanvasBitmap? CurrentBitmap { get; set; }
        CanvasBitmap? LeftBitmap { get; set; }
        CanvasBitmap? RightBitmap { get; set; }
        bool IsCurrentViewSideBySide { get; set; }
        bool IsSeamlessScroll { get; }

        CanvasControl MainCanvas { get; }

        ImageCacheManager ImageCache { get; }
        ImageViewportNavigationService ImageViewportNavigationService { get; }

        void SwitchToImageMode();
        Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false);
        Task AddToRecentAsync(bool immediate);
        Task RerenderPdfCurrentPageAsync();
        void FocusRoot();
    }

    internal interface IImageViewerRuntimeHost
    {
        List<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; set; }
        double ZoomLevel { get; set; }
        CanvasBitmap? CurrentBitmap { get; set; }
        CanvasBitmap? LeftBitmap { get; }
        CanvasBitmap? RightBitmap { get; }
        bool IsCurrentViewSideBySide { get; set; }
        bool IsSideBySideMode { get; }
        bool AutoDoublePageForArchive { get; }
        bool SharpenEnabled { get; }
        bool IsPdfMode { get; }
        bool IsWebDavMode { get; }
        CancellationTokenSource? ImageLoadingCts { get; set; }

        CanvasControl MainCanvas { get; }

        SevenZipExtractionCoordinator SevenZipExtraction { get; }
        ArchiveSession ArchiveSession { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        ImageCacheManager ImageCache { get; }
        ImageViewportNavigationService ImageViewportNavigationService { get; }
        ImageDoublePageDecisionService ImageDoublePageDecisionService { get; }
        PreloadManager PreloadManager { get; }
        ImageBitmapLoader ImageBitmapLoader { get; }
        ImageNavigationCoordinator ImageNavigationCoordinator { get; }
        WebDavService WebDavService { get; }

        void Signal7zJump();
        void SwitchToImageMode();
        Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false);
        Task AddToRecentAsync(bool immediate);
        SharpenParams CreateSharpenParams();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        void FocusRoot();
    }
}
