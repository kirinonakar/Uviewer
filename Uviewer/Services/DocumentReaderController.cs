using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
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
    internal sealed partial class DocumentReaderController
    {
        private readonly IDocumentReaderHost _host;

        internal readonly AozoraBlockMeasurer _aozoraBlockMeasurer = new();
        internal readonly AozoraBlockPaginator _aozoraBlockPaginator;
        internal readonly AozoraPageMapCalculator _aozoraPageMapCalculator;
        internal readonly ReaderPageMapCalculationService _readerPageMapCalculationService = new();
        internal readonly ReaderPageNavigationService _readerPageNavigationService = new();
        internal readonly AozoraPreviousPageCache _aozoraPreviousPageCache;
        internal readonly ReaderLayoutService _readerLayoutService = new();
        internal readonly TextBlockDocumentService _textBlockDocumentService = new();
        internal readonly TextDocumentLoadService _textDocumentLoadService;
        internal readonly TextDocumentSearchService _textDocumentSearchService;
        internal readonly TextDisplayPreparationService _textDisplayPreparationService = new();
        internal readonly TextLineLayoutService _textLineLayoutService = new();
        internal readonly TextLineLoadService _textLineLoadService;
        internal readonly TextLinePresenterService _textLinePresenterService;
        internal readonly TextSearchHighlightPresenterService _textSearchHighlightPresenterService;
        internal readonly TextViewportService _textViewportService = new();
        internal readonly TextPageCalculationService _textPageCalculationService = new();
        internal readonly TextResumeService _textResumeService = new();
        internal readonly TextUiSettingsService _textUiSettingsService = new();
        internal readonly ReadingProgressService _readingProgressService = new();
        internal readonly TextStatusBarService _textStatusBarService;
        internal readonly TextDialogService _textDialogService;

        internal DocumentReaderController(IDocumentReaderHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _aozoraBlockPaginator = new AozoraBlockPaginator(_aozoraBlockMeasurer);
            _aozoraPageMapCalculator = new AozoraPageMapCalculator(_aozoraBlockMeasurer);
            _aozoraPreviousPageCache = new AozoraPreviousPageCache(_aozoraBlockMeasurer, _aozoraBlockPaginator);
            _textLinePresenterService = new TextLinePresenterService(_textLineLayoutService);
            _textSearchHighlightPresenterService = new TextSearchHighlightPresenterService(_host.SearchHighlightService);
            _textStatusBarService = new TextStatusBarService(_readingProgressService);
            _textLineLoadService = new TextLineLoadService(_textLineLayoutService);
            _textDocumentLoadService = new TextDocumentLoadService(_host.ArchiveSession);
            _textDocumentSearchService = new TextDocumentSearchService(_host.DocumentSearchService);
            _textDialogService = new TextDialogService(RootGrid);
        }

        internal TextReaderState TextReaderState => _textReaderState;
        internal List<TextLine> TextLines
        {
            get => _textLines;
            set => _textLines = value;
        }

        internal string CurrentTextContent
        {
            get => _currentTextContent;
            set => _currentTextContent = value;
        }

        internal string? CurrentTextFilePath
        {
            get => _currentTextFilePath;
            set => _currentTextFilePath = value;
        }

        internal string? CurrentTextArchiveEntryKey
        {
            get => _currentTextArchiveEntryKey;
            set => _currentTextArchiveEntryKey = value;
        }

        internal TextSettingsManager SettingsManager => _settingsManager;
        internal TextDocumentSearchService TextDocumentSearchService => _textDocumentSearchService;
        internal TextSearchHighlightPresenterService TextSearchHighlightPresenterService => _textSearchHighlightPresenterService;
        internal TextBlockDocumentService TextBlockDocumentService => _textBlockDocumentService;
        internal ReaderLayoutService ReaderLayoutService => _readerLayoutService;
        internal TextStatusBarService TextStatusBarService => _textStatusBarService;
        internal TextDialogService TextDialogService => _textDialogService;
        internal bool IsTextMode { get => _isTextMode; set => _isTextMode = value; }
        internal bool IsAozoraMode { get => _isAozoraMode; set => _isAozoraMode = value; }
        internal bool IsMarkdownRenderMode { get => _isMarkdownRenderMode; set => _isMarkdownRenderMode = value; }
        internal bool IsVerticalMode { get => _isVerticalMode; set => _isVerticalMode = value; }
        internal int AozoraPendingTargetLine { get => _aozoraPendingTargetLine; set => _aozoraPendingTargetLine = value; }
        internal int TextTotalLineCountInSource { get => _textTotalLineCountInSource; set => _textTotalLineCountInSource = value; }
        internal int AozoraTotalLineCountInSource => _aozoraTotalLineCountInSource;
        internal List<AozoraBindingModel> AozoraBlocks => _aozoraBlocks;
        internal ReaderPageInfo CurrentAozoraPageInfo => _currentAozoraPageInfo;
        internal int CurrentAozoraStartBlockIndex => _currentAozoraStartBlockIndex;
        internal ReaderPageInfo CurrentVerticalPageInfo => _currentVerticalPageInfo;
        internal int CurrentVerticalStartBlockIndex => _currentVerticalStartBlockIndex;
        internal int CurrentVerticalStartLine => _currentVerticalPageInfo.StartLine;
        internal CancellationTokenSource? GlobalTextCts => _globalTextCts;
        internal bool VerticalKeyAttached => _verticalKeyAttached;

        internal void StopVerticalResizeTimer() => _verticalResizeTimer?.Stop();

        internal void AttachVerticalPreviewKeyIfNeeded()
        {
            if (!_verticalKeyAttached && RootGrid != null)
            {
                RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                _verticalKeyAttached = true;
            }
        }

        internal void InvalidateAozoraTextCanvas() => AozoraTextCanvas?.Invalidate();
        internal void InvalidateVerticalTextCanvas() => VerticalTextCanvas?.Invalidate();

        internal bool _isEpubMode { get => _host.IsEpubMode; set => _host.IsEpubMode = value; }
        internal bool _isWindowClosing => _host.IsWindowClosing;
        internal bool _isWebDavMode => _host.IsWebDavMode;
        internal bool _isSideBySideMode { get => _host.IsSideBySideMode; set => _host.IsSideBySideMode = value; }
        internal bool _autoDoublePageForArchive => _host.AutoDoublePageForArchive;
        internal bool _nextImageOnRight => _host.NextImageOnRight;
        internal bool _isNavigatingRecent { get => _host.IsNavigatingRecent; set => _host.IsNavigatingRecent = value; }
        internal bool _sharpenEnabled => _host.SharpenEnabled;
        internal bool _isColorPickerOpen { get => _host.IsColorPickerOpen; set => _host.IsColorPickerOpen = value; }
        internal bool ShouldInvertControls => _host.ShouldInvertControls;
        internal int _currentIndex { get => _host.CurrentIndex; set => _host.CurrentIndex = value; }
        internal List<ImageEntry> _imageEntries { get => _host.ImageEntries; set => _host.ImageEntries = value; }
        internal CanvasBitmap? _currentBitmap => _host.CurrentBitmap;
        internal PdfDocument? _currentPdfDocument => _host.CurrentPdfDocument;
        internal string? _activeSearchQuery => _host.ActiveSearchQuery;
        internal int _currentEpubChapterIndex { get => _host.CurrentEpubChapterIndex; set => _host.CurrentEpubChapterIndex = value; }
        internal int _currentEpubPageIndex { get => _host.CurrentEpubPageIndex; set => _host.CurrentEpubPageIndex = value; }
        internal IReadOnlyList<string> _epubSpine => _host.EpubSpine;
        internal List<EpubWin2DPage> _epubWin2DPages => _host.EpubWin2DPages;
        internal Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache => _host.EpubPreloadCache;
        internal ArchiveSession _archiveSession => _host.ArchiveSession;
        internal RecentService _recentService => _host.RecentService;
        internal FavoritesService _favoritesService => _host.FavoritesService;
        internal TocService _tocService => _host.TocService;
        internal ImageResourceService _imageResourceService => _host.ImageResourceService;
        internal WindowChromeController _windowChromeController => _host.WindowChromeController;
        internal string Title { get => _host.WindowTitle; set => _host.WindowTitle = value; }

        internal DispatcherQueue DispatcherQueue => _host.DispatcherQueue;
        internal AppWindow AppWindow => _host.AppWindow;
        internal Grid RootGrid => _host.RootGrid;
        internal Grid ImageArea => _host.ImageArea;
        internal Grid TextArea => _host.TextArea;
        internal Grid EpubArea => _host.EpubArea;
        internal ScrollViewer TextScrollViewer => _host.TextScrollViewer;
        internal ItemsRepeater TextItemsRepeater => _host.TextItemsRepeater;
        internal CanvasControl MainCanvas => _host.MainCanvas;
        internal CanvasControl AozoraTextCanvas => _host.AozoraTextCanvas;
        internal CanvasControl VerticalTextCanvas => _host.VerticalTextCanvas;
        internal FrameworkElement EmptyStatePanel => _host.EmptyStatePanel;
        internal Grid TextFastNavOverlay => _host.TextFastNavOverlay;
        internal ContentControl RootFontControl => _host.RootFontControl;
        internal ListView FileListView => _host.FileListView;
        internal GridView FileGridView => _host.FileGridView;
        internal Pivot SidebarFavoritesPivot => _host.SidebarFavoritesPivot;
        internal TextBlock CurrentPathText => _host.CurrentPathText;
        internal TextBlock NotificationText => _host.NotificationText;
        internal TextBlock FileNameText => _host.FileNameText;
        internal TextBlock ImageInfoText => _host.ImageInfoText;
        internal TextBlock TextProgressText => _host.TextProgressText;
        internal TextBlock ImageIndexText => _host.ImageIndexText;
        internal MainToolbarControl MainToolbar => _host.MainToolbar;

        internal Task AddToRecentAsync(bool immediate) => _host.AddToRecentAsync(immediate);
        internal void SyncSidebarSelection(ImageEntry entry) => _host.SyncSidebarSelection(entry);
        internal void EnsureMinWindowSizeForText() => _host.EnsureMinWindowSizeForText();
        internal void UpdateSideBySideButtonState() => _host.UpdateSideBySideButtonState();
        internal void UpdateNextImageSideButtonState() => _host.UpdateNextImageSideButtonState();
        internal void UpdateFavoritesMenu() => _host.UpdateFavoritesMenu();
        internal void UpdateRecentMenu() => _host.UpdateRecentMenu();
        internal void UpdateWebDavServerList() => _host.UpdateWebDavServerList();
        internal void ApplyLocalization() => _host.ApplyLocalization();
        internal void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
            _host.ShowNotification(message, icon, color);
        internal string GetTextSettingsFilePath() => _host.GetTextSettingsFilePath();
        internal void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) => _host.UpdateStatusBar(entry, bitmap);
        internal Task DisplayCurrentImageAsync() => _host.DisplayCurrentImageAsync();
        internal Task NavigateEpubAsync(int direction) => _host.NavigateEpubAsync(direction);
        internal Task LoadEpubChapterAsync(
            int index,
            bool fromEnd = false,
            int targetLine = -1,
            int targetBlockIndex = -1,
            int targetPage = -1,
            double? progress = null,
            CancellationToken token = default) =>
            _host.LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);
        internal void JumpToEpubTocItem(EpubTocItem item) => _host.JumpToEpubTocItem(item);
        internal void UpdateEpubStatus() => _host.UpdateEpubStatus();
        internal void TriggerEpubResize() => _host.TriggerEpubResize();
        internal void ToggleSidebar() => _host.ToggleSidebar();
        internal void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction) =>
            _host.HandleSmartTouchNavigation(e, prevAction, nextAction);
        internal void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
            _host.ApplySearchHighlightsToTextBlock(textBlock, content, lineNumber);
        internal DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) =>
            _host.GetActiveSearchMatchFor(kind);
        internal ViewingContext CreateViewingContext() => _host.CreateViewingContext();
        internal SharpenParams CreateSharpenParams() => _host.CreateSharpenParams();
        internal Task LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing = null,
            Func<bool>? shouldKeepLoadedBitmap = null) =>
            _host.LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);
    }
}
