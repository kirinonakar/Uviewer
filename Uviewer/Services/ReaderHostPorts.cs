using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;
using Windows.Data.Pdf;

namespace Uviewer.Services
{
    internal interface IReaderAppStateHost
    {
        bool IsWindowClosing { get; }
        bool IsWebDavMode { get; }
        bool IsEpubMode { get; set; }
        bool IsNavigatingRecent { get; set; }
        bool IsColorPickerOpen { get; set; }
        bool ShouldInvertControls { get; }
        string WindowTitle { get; set; }

        DispatcherQueue DispatcherQueue { get; }
        AppWindow AppWindow { get; }
        WindowShellController WindowShellController { get; }

        void EnsureMinWindowSizeForText();
        void ApplyLocalization();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        void ToggleSidebar();
        void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction);
    }

    internal interface ITextReaderViewHost
    {
        Grid RootGrid { get; }
        Grid ImageArea { get; }
        Grid TextArea { get; }
        Grid EpubArea { get; }
        ScrollViewer TextScrollViewer { get; }
        ItemsRepeater TextItemsRepeater { get; }
        CanvasControl MainCanvas { get; }
        CanvasControl AozoraTextCanvas { get; }
        CanvasControl VerticalTextCanvas { get; }
        FrameworkElement EmptyStatePanel { get; }
        Grid TextFastNavOverlay { get; }
        ContentControl RootFontControl { get; }
        ListView FileListView { get; }
        GridView FileGridView { get; }
        Pivot SidebarFavoritesPivot { get; }
        TextBlock CurrentPathText { get; }
        TextBlock NotificationText { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock TextProgressText { get; }
        TextBlock ImageIndexText { get; }
        MainToolbarControl MainToolbar { get; }

        string GetTextSettingsFilePath();
    }

    internal interface IImageNavigationHost
    {
        bool IsSideBySideMode { get; set; }
        bool AutoDoublePageForArchive { get; }
        bool NextImageOnRight { get; }
        bool SharpenEnabled { get; }
        int CurrentIndex { get; set; }
        List<ImageEntry> ImageEntries { get; set; }
        CanvasBitmap? CurrentBitmap { get; }
        PdfDocument? CurrentPdfDocument { get; }

        Task AddToRecentAsync(bool immediate);
        void SyncSidebarSelection(ImageEntry entry);
        void UpdateSideBySideButtonState();
        void UpdateNextImageSideButtonState();
        void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap);
        Task DisplayCurrentImageAsync();
        ViewingContext CreateViewingContext();
        SharpenParams CreateSharpenParams();
        Task LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing = null,
            Func<bool>? shouldKeepLoadedBitmap = null);
    }

    internal interface IEpubNavigationHost
    {
        int CurrentEpubChapterIndex { get; set; }
        int CurrentEpubPageIndex { get; set; }
        IReadOnlyList<string> EpubSpine { get; }
        List<EpubWin2DPage> EpubWin2DPages { get; }
        Dictionary<int, List<EpubWin2DPage>> EpubPreloadCache { get; }

        Task NavigateEpubAsync(int direction);
        Task LoadEpubChapterAsync(
            int index,
            bool fromEnd = false,
            int targetLine = -1,
            int targetBlockIndex = -1,
            int targetPage = -1,
            double? progress = null,
            CancellationToken token = default);
        void JumpToEpubTocItem(EpubTocItem item);
        void UpdateEpubStatus();
        void TriggerEpubResize();
    }

    internal interface IDocumentSearchHost
    {
        string? ActiveSearchQuery { get; }
        DocumentSearchService DocumentSearchService { get; }
        SearchHighlightService SearchHighlightService { get; }

        void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber);
        DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind);
    }

    internal interface IReaderLibraryHost
    {
        ArchiveSession ArchiveSession { get; }
        RecentService RecentService { get; }
        FavoritesService FavoritesService { get; }
        TocService TocService { get; }
        ImageResourceService ImageResourceService { get; }

        void UpdateFavoritesMenu();
        void UpdateRecentMenu();
        void UpdateWebDavServerList();
    }
}
