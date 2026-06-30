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
        private readonly IReaderAppStateHost _appStateHost;
        private readonly ITextReaderViewHost _viewHost;
        private readonly IImageNavigationHost _imageNavigationHost;
        private readonly IEpubNavigationHost _epubNavigationHost;
        private readonly IDocumentSearchHost _searchHost;
        private readonly IReaderLibraryHost _libraryHost;

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
        internal readonly ReadingProgressController _readingProgressController;
        internal readonly TextReaderSettingsController _textReaderSettingsController;
        internal readonly TextDialogService _textDialogService;

        internal DocumentReaderController(DocumentReaderDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            _appStateHost = dependencies.AppStateHost;
            _viewHost = dependencies.ViewHost;
            _imageNavigationHost = dependencies.ImageNavigationHost;
            _epubNavigationHost = dependencies.EpubNavigationHost;
            _searchHost = dependencies.SearchHost;
            _libraryHost = dependencies.LibraryHost;
            _aozoraBlockPaginator = new AozoraBlockPaginator(_aozoraBlockMeasurer);
            _aozoraPageMapCalculator = new AozoraPageMapCalculator(_aozoraBlockMeasurer);
            _aozoraPreviousPageCache = new AozoraPreviousPageCache(_aozoraBlockMeasurer, _aozoraBlockPaginator);
            _textLinePresenterService = new TextLinePresenterService(_textLineLayoutService);
            _textSearchHighlightPresenterService = new TextSearchHighlightPresenterService(_searchHost.SearchHighlightService);
            _textStatusBarService = new TextStatusBarService(_readingProgressService);
            _textLineLoadService = new TextLineLoadService(_textLineLayoutService);
            _textDocumentLoadService = new TextDocumentLoadService(_libraryHost.ArchiveSession);
            _textDocumentSearchService = new TextDocumentSearchService(_searchHost.DocumentSearchService);
            _textDialogService = new TextDialogService(RootGrid);
            _readingProgressController = new ReadingProgressController(
                _textStatusBarService,
                _viewHost,
                () => AddToRecentAsync(true));
            _textReaderSettingsController = new TextReaderSettingsController(
                () => _settingsManager,
                _textUiSettingsService,
                _textDialogService,
                _viewHost,
                SaveTextSettings,
                () => RefreshTextDisplay(),
                CreateUiFontApplyTargets,
                ShowNotification);
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

        internal bool _isEpubMode { get => _appStateHost.IsEpubMode; set => _appStateHost.IsEpubMode = value; }
        internal bool _isWindowClosing => _appStateHost.IsWindowClosing;
        internal bool _isWebDavMode => _appStateHost.IsWebDavMode;
        internal bool _isSideBySideMode { get => _imageNavigationHost.IsSideBySideMode; set => _imageNavigationHost.IsSideBySideMode = value; }
        internal bool _autoDoublePageForArchive => _imageNavigationHost.AutoDoublePageForArchive;
        internal bool _nextImageOnRight => _imageNavigationHost.NextImageOnRight;
        internal bool _isNavigatingRecent { get => _appStateHost.IsNavigatingRecent; set => _appStateHost.IsNavigatingRecent = value; }
        internal bool _sharpenEnabled => _imageNavigationHost.SharpenEnabled;
        internal bool _isColorPickerOpen { get => _appStateHost.IsColorPickerOpen; set => _appStateHost.IsColorPickerOpen = value; }
        internal bool ShouldInvertControls => _appStateHost.ShouldInvertControls;
        internal int _currentIndex { get => _imageNavigationHost.CurrentIndex; set => _imageNavigationHost.CurrentIndex = value; }
        internal List<ImageEntry> _imageEntries { get => _imageNavigationHost.ImageEntries; set => _imageNavigationHost.ImageEntries = value; }
        internal CanvasBitmap? _currentBitmap => _imageNavigationHost.CurrentBitmap;
        internal PdfDocument? _currentPdfDocument => _imageNavigationHost.CurrentPdfDocument;
        internal string? _activeSearchQuery => _searchHost.ActiveSearchQuery;
        internal int _currentEpubChapterIndex { get => _epubNavigationHost.CurrentEpubChapterIndex; set => _epubNavigationHost.CurrentEpubChapterIndex = value; }
        internal int _currentEpubPageIndex { get => _epubNavigationHost.CurrentEpubPageIndex; set => _epubNavigationHost.CurrentEpubPageIndex = value; }
        internal IReadOnlyList<string> _epubSpine => _epubNavigationHost.EpubSpine;
        internal List<EpubWin2DPage> _epubWin2DPages => _epubNavigationHost.EpubWin2DPages;
        internal Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache => _epubNavigationHost.EpubPreloadCache;
        internal ArchiveSession _archiveSession => _libraryHost.ArchiveSession;
        internal RecentService _recentService => _libraryHost.RecentService;
        internal FavoritesService _favoritesService => _libraryHost.FavoritesService;
        internal TocService _tocService => _libraryHost.TocService;
        internal ImageResourceService _imageResourceService => _libraryHost.ImageResourceService;
        internal WindowChromeController _windowChromeController => _appStateHost.WindowChromeController;
        internal string Title { get => _appStateHost.WindowTitle; set => _appStateHost.WindowTitle = value; }

        internal DispatcherQueue DispatcherQueue => _appStateHost.DispatcherQueue;
        internal AppWindow AppWindow => _appStateHost.AppWindow;
        internal Grid RootGrid => _viewHost.RootGrid;
        internal Grid ImageArea => _viewHost.ImageArea;
        internal Grid TextArea => _viewHost.TextArea;
        internal Grid EpubArea => _viewHost.EpubArea;
        internal ScrollViewer TextScrollViewer => _viewHost.TextScrollViewer;
        internal ItemsRepeater TextItemsRepeater => _viewHost.TextItemsRepeater;
        internal CanvasControl MainCanvas => _viewHost.MainCanvas;
        internal CanvasControl AozoraTextCanvas => _viewHost.AozoraTextCanvas;
        internal CanvasControl VerticalTextCanvas => _viewHost.VerticalTextCanvas;
        internal FrameworkElement EmptyStatePanel => _viewHost.EmptyStatePanel;
        internal Grid TextFastNavOverlay => _viewHost.TextFastNavOverlay;
        internal ContentControl RootFontControl => _viewHost.RootFontControl;
        internal ListView FileListView => _viewHost.FileListView;
        internal GridView FileGridView => _viewHost.FileGridView;
        internal Pivot SidebarFavoritesPivot => _viewHost.SidebarFavoritesPivot;
        internal TextBlock CurrentPathText => _viewHost.CurrentPathText;
        internal TextBlock NotificationText => _viewHost.NotificationText;
        internal TextBlock FileNameText => _viewHost.FileNameText;
        internal TextBlock ImageInfoText => _viewHost.ImageInfoText;
        internal TextBlock TextProgressText => _viewHost.TextProgressText;
        internal TextBlock ImageIndexText => _viewHost.ImageIndexText;
        internal MainToolbarControl MainToolbar => _viewHost.MainToolbar;

        internal Task AddToRecentAsync(bool immediate) => _imageNavigationHost.AddToRecentAsync(immediate);
        internal void SyncSidebarSelection(ImageEntry entry) => _imageNavigationHost.SyncSidebarSelection(entry);
        internal void EnsureMinWindowSizeForText() => _appStateHost.EnsureMinWindowSizeForText();
        internal void UpdateSideBySideButtonState() => _imageNavigationHost.UpdateSideBySideButtonState();
        internal void UpdateNextImageSideButtonState() => _imageNavigationHost.UpdateNextImageSideButtonState();
        internal void UpdateFavoritesMenu() => _libraryHost.UpdateFavoritesMenu();
        internal void UpdateRecentMenu() => _libraryHost.UpdateRecentMenu();
        internal void UpdateWebDavServerList() => _libraryHost.UpdateWebDavServerList();
        internal void ApplyLocalization() => _appStateHost.ApplyLocalization();
        internal void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
            _appStateHost.ShowNotification(message, icon, color);
        internal string GetTextSettingsFilePath() => _viewHost.GetTextSettingsFilePath();
        internal void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) => _imageNavigationHost.UpdateStatusBar(entry, bitmap);
        internal Task DisplayCurrentImageAsync() => _imageNavigationHost.DisplayCurrentImageAsync();
        internal Task NavigateEpubAsync(int direction) => _epubNavigationHost.NavigateEpubAsync(direction);
        internal Task LoadEpubChapterAsync(
            int index,
            bool fromEnd = false,
            int targetLine = -1,
            int targetBlockIndex = -1,
            int targetPage = -1,
            double? progress = null,
            CancellationToken token = default) =>
            _epubNavigationHost.LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);
        internal void JumpToEpubTocItem(EpubTocItem item) => _epubNavigationHost.JumpToEpubTocItem(item);
        internal void UpdateEpubStatus() => _epubNavigationHost.UpdateEpubStatus();
        internal void TriggerEpubResize() => _epubNavigationHost.TriggerEpubResize();
        internal void ToggleSidebar() => _appStateHost.ToggleSidebar();
        internal void HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction) =>
            _appStateHost.HandleSmartTouchNavigation(e, prevAction, nextAction);
        internal void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
            _searchHost.ApplySearchHighlightsToTextBlock(textBlock, content, lineNumber);
        internal DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) =>
            _searchHost.GetActiveSearchMatchFor(kind);
        internal ViewingContext CreateViewingContext() => _imageNavigationHost.CreateViewingContext();
        internal SharpenParams CreateSharpenParams() => _imageNavigationHost.CreateSharpenParams();
        internal Task LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing = null,
            Func<bool>? shouldKeepLoadedBitmap = null) =>
            _imageNavigationHost.LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);
    }
}
