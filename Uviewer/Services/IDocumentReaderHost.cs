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
    internal interface IDocumentReaderHost
    {
        bool IsWindowClosing { get; }
        bool IsWebDavMode { get; }
        bool IsEpubMode { get; set; }
        bool IsSideBySideMode { get; set; }
        bool AutoDoublePageForArchive { get; }
        bool NextImageOnRight { get; }
        bool IsNavigatingRecent { get; set; }
        bool SharpenEnabled { get; }
        bool IsColorPickerOpen { get; set; }
        bool ShouldInvertControls { get; }
        int CurrentIndex { get; set; }
        string WindowTitle { get; set; }
        List<ImageEntry> ImageEntries { get; set; }
        CanvasBitmap? CurrentBitmap { get; }
        PdfDocument? CurrentPdfDocument { get; }
        string? ActiveSearchQuery { get; }

        int CurrentEpubChapterIndex { get; set; }
        int CurrentEpubPageIndex { get; set; }
        IReadOnlyList<string> EpubSpine { get; }
        List<EpubWin2DPage> EpubWin2DPages { get; }
        Dictionary<int, List<EpubWin2DPage>> EpubPreloadCache { get; }

        DispatcherQueue DispatcherQueue { get; }
        AppWindow AppWindow { get; }
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

        ArchiveSession ArchiveSession { get; }
        RecentService RecentService { get; }
        FavoritesService FavoritesService { get; }
        TocService TocService { get; }
        DocumentSearchService DocumentSearchService { get; }
        SearchHighlightService SearchHighlightService { get; }
        ImageResourceService ImageResourceService { get; }
        WindowChromeController WindowChromeController { get; }

        Task AddToRecentAsync(bool immediate);
        void SyncSidebarSelection(ImageEntry entry);
        void EnsureMinWindowSizeForText();
        void UpdateSideBySideButtonState();
        void UpdateNextImageSideButtonState();
        void UpdateFavoritesMenu();
        void UpdateRecentMenu();
        void UpdateWebDavServerList();
        void ApplyLocalization();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        string GetTextSettingsFilePath();
        void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap);
        Task DisplayCurrentImageAsync();
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
        void ToggleSidebar();
        void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction);
        void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber);
        DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind);
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
}
