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
    internal interface IImageViewerHost
    {
        List<ImageEntry> ImageEntries { get; set; }
        ObservableCollection<FileItem> FileItems { get; }
        int CurrentIndex { get; set; }
        double ZoomLevel { get; set; }
        CanvasBitmap? CurrentBitmap { get; set; }
        CanvasBitmap? LeftBitmap { get; set; }
        CanvasBitmap? RightBitmap { get; set; }
        bool IsCurrentViewSideBySide { get; set; }
        bool IsSideBySideMode { get; set; }
        bool NextImageOnRight { get; set; }
        bool AutoDoublePageForArchive { get; }
        bool IsAnimatedFrameActive { get; set; }
        bool SharpenEnabled { get; set; }
        bool IsSeamlessScroll { get; }
        bool IsPdfMode { get; }
        bool IsWebDavMode { get; }
        bool IsTextMode { get; }
        bool IsEpubMode { get; }
        bool IsVerticalMode { get; }
        bool IsAozoraMode { get; }
        bool IsExplorerGrid { get; }
        bool ShouldInvertControls { get; }
        string? CurrentTextFilePath { get; }
        string? CurrentTextArchiveEntryKey { get; }
        string? CurrentEpubFilePath { get; }
        string? CurrentWebDavItemPath { get; }
        int CurrentEpubChapterIndex { get; }
        int CurrentEpubPageIndex { get; }
        int PendingEpubChapterIndex { get; set; }
        int PendingEpubPageIndex { get; set; }
        int PendingEpubStartBlockIndex { get; set; }
        int AozoraPendingTargetLine { get; set; }
        int CurrentVerticalStartLine { get; }
        double LastCanvasWidth { get; set; }
        CancellationTokenSource? ImageLoadingCts { get; set; }

        CanvasControl MainCanvas { get; }
        CanvasControl LeftCanvas { get; }
        CanvasControl RightCanvas { get; }
        FrameworkElement ImageArea { get; }
        FrameworkElement RootGrid { get; }
        FrameworkElement EmptyStatePanel { get; }
        FrameworkElement FastNavOverlay { get; }
        Grid SideBySideGrid { get; }
        TextBlock FastNavText { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock ImageIndexText { get; }
        TextBlock TextProgressText { get; }
        ListViewBase FileListView { get; }
        ListViewBase FileGridView { get; }
        ScrollViewer? TextScrollViewer { get; }
        CanvasControl? VerticalTextCanvas { get; }
        CanvasControl? AozoraTextCanvas { get; }
        CanvasControl? EpubTextCanvas { get; }
        MainToolbarControl MainToolbar { get; }
        DispatcherQueue DispatcherQueue { get; }

        FastNavigationService FastNavigationService { get; }
        SevenZipExtractionCoordinator SevenZipExtraction { get; }
        ArchiveSession ArchiveSession { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        ImageCacheManager ImageCache { get; }
        ImageViewportNavigationService ImageViewportNavigationService { get; }
        ZoomService ZoomService { get; }
        ImageDoublePageDecisionService ImageDoublePageDecisionService { get; }
        SideBySideImageLoadService SideBySideImageLoadService { get; }
        ImageStatusBarService ImageStatusBarService { get; }
        PreloadManager PreloadManager { get; }
        ImageBitmapLoader ImageBitmapLoader { get; }
        ImageNavigationCoordinator ImageNavigationCoordinator { get; }
        ImageResourceService ImageResourceService { get; }
        WindowSettingsCoordinator WindowSettingsCoordinator { get; }
        WebDavService WebDavService { get; }
        ImageProcessingViewModel ImageOptions { get; }

        EpubWin2DPage? CurrentEpubWin2DPage { get; }

        void Signal7zJump();
        void SwitchToImageMode();
        Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default, bool isPreload = false);
        Task AddToRecentAsync(bool immediate);
        Task LoadTextEntryAsync(ImageEntry entry);
        Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine);
        Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token);
        Task LoadEpubChapterAsync(int index, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1);
        void ShowEpubImagePage(EpubWin2DPage page);
        void SetEpubPageIndex(int index);
        Task PrepareVerticalTextAsync(int line);
        SharpenParams CreateSharpenParams();
        Task RerenderPdfCurrentPageAsync();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        Task HandleFileSelectionAsync(FileItem item);
        void FocusRoot();
        void SaveWindowSettings();
        void InvalidateEpubTextCanvas();
        void InvalidateVerticalTextCanvas();
        void InvalidateAozoraTextCanvas();
    }
}
