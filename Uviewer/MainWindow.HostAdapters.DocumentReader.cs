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
        private sealed class ReaderAppStateHostAdapter : IReaderAppStateHost
        {
            private readonly MainWindow _window;

            public ReaderAppStateHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsWindowClosing => _window._isWindowClosing;
            public bool IsWebDavMode => _window._isWebDavMode;
            public bool IsEpubMode { get => _window._isEpubMode; set => _window._isEpubMode = value; }
            public bool IsNavigatingRecent { get => _window._isNavigatingRecent; set => _window._isNavigatingRecent = value; }
            public bool IsColorPickerOpen { get => _window._isColorPickerOpen; set => _window._isColorPickerOpen = value; }
            public bool ShouldInvertControls => _window.ShouldInvertControls;
            public string WindowTitle { get => _window.Title; set => _window.Title = value; }

            public DispatcherQueue DispatcherQueue => _window.DispatcherQueue;
            public AppWindow AppWindow => _window.AppWindow;
            public WindowShellController WindowShellController => _window._windowShellController;

            public void EnsureMinWindowSizeForText() => _window.EnsureMinWindowSizeForText();
            public void ApplyLocalization() => _window.ApplyLocalization();
            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                _window.ShowNotification(message, icon, color);
            public void ToggleSidebar() => _window._windowShellController.ToggleSidebar();
            public void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction) =>
                _window.HandleSmartTouchNavigation(e, prevAction, nextAction);
        }

        private sealed class TextReaderViewHostAdapter : ITextReaderViewHost
        {
            private readonly MainWindow _window;

            public TextReaderViewHostAdapter(MainWindow window)
            {
                _window = window;
            }

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

            public string GetTextSettingsFilePath() => _window.GetTextSettingsFilePath();
        }

        private sealed class ReaderImageNavigationHostAdapter : IImageNavigationHost
        {
            private readonly MainWindow _window;

            public ReaderImageNavigationHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsSideBySideMode { get => _window._imageViewerState.IsSideBySideMode; set => _window._imageViewerState.IsSideBySideMode = value; }
            public bool AutoDoublePageForArchive => _window._imageViewerState.AutoDoublePageForArchive;
            public bool NextImageOnRight => _window._imageViewerState.NextImageOnRight;
            public bool SharpenEnabled => _window._imageViewerState.IsSharpenEnabled;
            public int CurrentIndex { get => _window._imageViewerState.CurrentIndex; set => _window._imageViewerState.CurrentIndex = value; }
            public List<ImageEntry> ImageEntries { get => _window._imageViewerState.Entries; set => _window._imageViewerState.Entries = value ?? new List<ImageEntry>(); }
            public CanvasBitmap? CurrentBitmap => _window._imageViewerState.CurrentBitmap;
            public PdfDocument? CurrentPdfDocument => _window._currentPdfDocument;

            public Task AddToRecentAsync(bool immediate) => _window._bookmarkInteractionController.AddCurrentRecentAsync(immediate);
            public void SyncSidebarSelection(ImageEntry entry) => _window._imageViewerController.SyncSidebarSelection(entry);
            public void UpdateSideBySideButtonState() => _window._imageViewerController.UpdateSideBySideButtonState();
            public void UpdateNextImageSideButtonState() => _window._imageViewerController.UpdateNextImageSideButtonState();
            public void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) => _window._imageViewerController.UpdateStatusBar(entry, bitmap);
            public Task DisplayCurrentImageAsync() => _window._imageViewerController.DisplayCurrentImageAsync();
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

        private sealed class ReaderEpubNavigationHostAdapter : IEpubNavigationHost
        {
            private readonly MainWindow _window;

            public ReaderEpubNavigationHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public int CurrentEpubChapterIndex { get => _window._currentEpubChapterIndex; set => _window._currentEpubChapterIndex = value; }
            public int CurrentEpubPageIndex { get => _window._currentEpubPageIndex; set => _window._currentEpubPageIndex = value; }
            public IReadOnlyList<string> EpubSpine => _window._epubSpine;
            public List<EpubWin2DPage> EpubWin2DPages => _window._epubWin2DPages;
            public Dictionary<int, List<EpubWin2DPage>> EpubPreloadCache => _window._epubPreloadCache;

            public Task NavigateEpubAsync(int direction) => _window._epubReaderController.NavigateEpubAsync(direction);
            public Task LoadEpubChapterAsync(
                int index,
                bool fromEnd = false,
                int targetLine = -1,
                int targetBlockIndex = -1,
                int targetPage = -1,
                double? progress = null,
                CancellationToken token = default) =>
                _window._epubReaderController.LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);
            public void JumpToEpubTocItem(EpubTocItem item) => _window._epubReaderController.JumpToEpubTocItem(item);
            public void UpdateEpubStatus() => _window._epubReaderController.UpdateEpubStatus();
            public void TriggerEpubResize() => _window._epubReaderController.TriggerEpubResize();
        }

        private sealed class DocumentSearchHostAdapter : IDocumentSearchHost
        {
            private readonly MainWindow _window;

            public DocumentSearchHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public string? ActiveSearchQuery => _window._searchController.ActiveSearchQuery;
            public DocumentSearchService DocumentSearchService => _window._documentSearchService;
            public SearchHighlightService SearchHighlightService => _window._searchHighlightService;

            public void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
                _window._searchController.ApplyHighlightsToTextBlock(textBlock, content, lineNumber);
            public DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) =>
                _window._searchController.GetActiveMatchFor(kind);
        }

        private sealed class ReaderLibraryHostAdapter : IReaderLibraryHost
        {
            private readonly MainWindow _window;

            public ReaderLibraryHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public ArchiveSession ArchiveSession => _window._archiveSession;
            public RecentService RecentService => _window._recentService;
            public FavoritesService FavoritesService => _window._favoritesService;
            public TocService TocService => _window._tocService;
            public ImageResourceService ImageResourceService => _window._imageResourceService;

            public void UpdateFavoritesMenu() =>
                _window._bookmarkInteractionController.UpdateFavoritesMenu(_window._fileFavoriteItems, _window._folderFavoriteItems);
            public void UpdateRecentMenu() =>
                _window._bookmarkInteractionController.UpdateRecentMenu(_window._recentItemsList);
            public void UpdateWebDavServerList() => _window.UpdateWebDavServerList();
        }
    }
}
