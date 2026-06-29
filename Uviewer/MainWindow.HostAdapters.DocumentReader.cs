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
using Uviewer.Services;
using Windows.Data.Pdf;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class DocumentReaderHostAdapter : IDocumentReaderHost
        {
            private readonly MainWindow _window;

            public DocumentReaderHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsWindowClosing => _window._isWindowClosing;
            public bool IsWebDavMode => _window._isWebDavMode;
            public bool IsEpubMode { get => _window._isEpubMode; set => _window._isEpubMode = value; }
            public bool IsSideBySideMode { get => _window._isSideBySideMode; set => _window._isSideBySideMode = value; }
            public bool AutoDoublePageForArchive => _window._autoDoublePageForArchive;
            public bool NextImageOnRight => _window._nextImageOnRight;
            public bool IsNavigatingRecent { get => _window._isNavigatingRecent; set => _window._isNavigatingRecent = value; }
            public bool SharpenEnabled => _window._sharpenEnabled;
            public bool IsColorPickerOpen { get => _window._isColorPickerOpen; set => _window._isColorPickerOpen = value; }
            public bool ShouldInvertControls => _window.ShouldInvertControls;
            public int CurrentIndex { get => _window._currentIndex; set => _window._currentIndex = value; }
            public string WindowTitle { get => _window.Title; set => _window.Title = value; }
            public List<ImageEntry> ImageEntries { get => _window._imageEntries; set => _window._imageEntries = value; }
            public CanvasBitmap? CurrentBitmap => _window._currentBitmap;
            public PdfDocument? CurrentPdfDocument => _window._currentPdfDocument;
            public string? ActiveSearchQuery => _window._activeSearchQuery;

            public int CurrentEpubChapterIndex { get => _window._currentEpubChapterIndex; set => _window._currentEpubChapterIndex = value; }
            public int CurrentEpubPageIndex { get => _window._currentEpubPageIndex; set => _window._currentEpubPageIndex = value; }
            public IReadOnlyList<string> EpubSpine => _window._epubSpine;
            public List<EpubWin2DPage> EpubWin2DPages => _window._epubWin2DPages;
            public Dictionary<int, List<EpubWin2DPage>> EpubPreloadCache => _window._epubPreloadCache;

            public DispatcherQueue DispatcherQueue => _window.DispatcherQueue;
            public AppWindow AppWindow => _window.AppWindow;
            public Grid RootGrid => _window.RootGrid;
            public Grid ImageArea => _window.ImageArea;
            public Grid TextArea => _window.TextArea;
            public Grid EpubArea => _window.EpubArea;
            public ScrollViewer TextScrollViewer => _window.TextScrollViewer;
            public ItemsRepeater TextItemsRepeater => _window.TextItemsRepeater;
            public CanvasControl MainCanvas => _window.MainCanvas;
            public CanvasControl AozoraTextCanvas => _window.AozoraTextCanvas;
            public CanvasControl VerticalTextCanvas => _window.VerticalTextCanvas;
            public FrameworkElement EmptyStatePanel => _window.EmptyStatePanel;
            public Grid TextFastNavOverlay => _window.TextFastNavOverlay;
            public ContentControl RootFontControl => _window.RootFontControl;
            public ListView FileListView => _window.FileListView;
            public GridView FileGridView => _window.FileGridView;
            public Pivot SidebarFavoritesPivot => _window.SidebarFavoritesPivot;
            public TextBlock CurrentPathText => _window.CurrentPathText;
            public TextBlock NotificationText => _window.NotificationText;
            public TextBlock FileNameText => _window.FileNameText;
            public TextBlock ImageInfoText => _window.ImageInfoText;
            public TextBlock TextProgressText => _window.TextProgressText;
            public TextBlock ImageIndexText => _window.ImageIndexText;
            public MainToolbarControl MainToolbar => _window.MainToolbar;

            public ArchiveSession ArchiveSession => _window._archiveSession;
            public RecentService RecentService => _window._recentService;
            public FavoritesService FavoritesService => _window._favoritesService;
            public TocService TocService => _window._tocService;
            public DocumentSearchService DocumentSearchService => _window._documentSearchService;
            public SearchHighlightService SearchHighlightService => _window._searchHighlightService;
            public ImageResourceService ImageResourceService => _window._imageResourceService;
            public WindowChromeController WindowChromeController => _window._windowChromeController;

            public Task AddToRecentAsync(bool immediate) => _window.AddToRecentAsync(immediate);
            public void SyncSidebarSelection(ImageEntry entry) => _window.SyncSidebarSelection(entry);
            public void EnsureMinWindowSizeForText() => _window.EnsureMinWindowSizeForText();
            public void UpdateSideBySideButtonState() => _window.UpdateSideBySideButtonState();
            public void UpdateNextImageSideButtonState() => _window.UpdateNextImageSideButtonState();
            public void UpdateFavoritesMenu() => _window.UpdateFavoritesMenu();
            public void UpdateRecentMenu() => _window.UpdateRecentMenu();
            public void UpdateWebDavServerList() => _window.UpdateWebDavServerList();
            public void ApplyLocalization() => _window.ApplyLocalization();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                _window.ShowNotification(message, icon, color);
            public string GetTextSettingsFilePath() => _window.GetTextSettingsFilePath();
            public void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) => _window.UpdateStatusBar(entry, bitmap);
            public Task DisplayCurrentImageAsync() => _window.DisplayCurrentImageAsync();
            public Task NavigateEpubAsync(int direction) => _window.NavigateEpubAsync(direction);
            public Task LoadEpubChapterAsync(
                int index,
                bool fromEnd = false,
                int targetLine = -1,
                int targetBlockIndex = -1,
                int targetPage = -1,
                double? progress = null,
                CancellationToken token = default) =>
                _window.LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);
            public void JumpToEpubTocItem(EpubTocItem item) => _window.JumpToEpubTocItem(item);
            public void UpdateEpubStatus() => _window.UpdateEpubStatus();
            public void TriggerEpubResize() => _window.TriggerEpubResize();
            public void ToggleSidebar() => _window.ToggleSidebar();
            public void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction) =>
                _window.HandleSmartTouchNavigation(e, prevAction, nextAction);
            public void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
                _window.ApplySearchHighlightsToTextBlock(textBlock, content, lineNumber);
            public DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) =>
                _window.GetActiveSearchMatchFor(kind);
            public ViewingContext CreateViewingContext() => _window.CreateViewingContext();
            public SharpenParams CreateSharpenParams() => _window.CreateSharpenParams();
            public Task LoadImageResourceAndInvalidateAsync(
                string resourcePath,
                string cacheKey,
                CanvasDevice device,
                Action invalidate,
                Action? onMissing = null,
                Func<bool>? shouldKeepLoadedBitmap = null) =>
                _window.LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);
        }
    }
}
