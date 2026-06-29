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
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer
{
    public sealed partial class MainWindow : IDocumentReaderHost
    {
        private DocumentReaderController _documentReaderController = null!;

        private TextReaderState _textReaderState => _documentReaderController.TextReaderState;
        private List<TextLine> _textLines
        {
            get => _documentReaderController.TextLines;
            set => _documentReaderController.TextLines = value;
        }

        private string _currentTextContent
        {
            get => _documentReaderController.CurrentTextContent;
            set => _documentReaderController.CurrentTextContent = value;
        }

        private TextSettingsManager _settingsManager => _documentReaderController.SettingsManager;
        private bool _isTextMode
        {
            get => _documentReaderController.IsTextMode;
            set => _documentReaderController.IsTextMode = value;
        }

        private int _textTotalLineCountInSource
        {
            get => _documentReaderController.TextTotalLineCountInSource;
            set => _documentReaderController.TextTotalLineCountInSource = value;
        }

        private int _lastRecentSaveLine
        {
            get => _documentReaderController._lastRecentSaveLine;
            set => _documentReaderController._lastRecentSaveLine = value;
        }

        private CancellationTokenSource? _globalTextCts => _documentReaderController.GlobalTextCts;
        private string? _currentTextFilePath
        {
            get => _documentReaderController.CurrentTextFilePath;
            set => _documentReaderController.CurrentTextFilePath = value;
        }

        private string? _currentTextArchiveEntryKey
        {
            get => _documentReaderController.CurrentTextArchiveEntryKey;
            set => _documentReaderController.CurrentTextArchiveEntryKey = value;
        }

        private bool _isAozoraMode
        {
            get => _documentReaderController.IsAozoraMode;
            set => _documentReaderController.IsAozoraMode = value;
        }

        private bool _isMarkdownRenderMode
        {
            get => _documentReaderController.IsMarkdownRenderMode;
            set => _documentReaderController.IsMarkdownRenderMode = value;
        }

        private List<AozoraBindingModel> _aozoraBlocks => _documentReaderController.AozoraBlocks;
        private int _aozoraTotalLineCountInSource => _documentReaderController.AozoraTotalLineCountInSource;
        private ReaderPageInfo _currentAozoraPageInfo => _documentReaderController.CurrentAozoraPageInfo;
        private int _currentAozoraStartBlockIndex => _documentReaderController.CurrentAozoraStartBlockIndex;
        private int _aozoraPendingTargetLine
        {
            get => _documentReaderController.AozoraPendingTargetLine;
            set => _documentReaderController.AozoraPendingTargetLine = value;
        }

        private bool _isVerticalMode
        {
            get => _documentReaderController.IsVerticalMode;
            set => _documentReaderController.IsVerticalMode = value;
        }

        private ReaderPageInfo _currentVerticalPageInfo => _documentReaderController.CurrentVerticalPageInfo;
        private int _currentVerticalStartBlockIndex => _documentReaderController.CurrentVerticalStartBlockIndex;

        private TextDocumentSearchService _textDocumentSearchService => _documentReaderController.TextDocumentSearchService;
        private TextSearchHighlightPresenterService _textSearchHighlightPresenterService => _documentReaderController.TextSearchHighlightPresenterService;
        private TextBlockDocumentService _textBlockDocumentService => _documentReaderController.TextBlockDocumentService;
        private ReaderLayoutService _readerLayoutService => _documentReaderController.ReaderLayoutService;
        private TextStatusBarService _textStatusBarService => _documentReaderController.TextStatusBarService;
        private TextDialogService _textDialogService => _documentReaderController.TextDialogService;

        private void EncodingItem_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.EncodingItem_Click(sender, e);
        private Task ApplyEncodingSelectionAsync(string tag) =>
            _documentReaderController.ApplyEncodingSelectionAsync(tag);
        private void InitializeText() => _documentReaderController.InitializeText();
        private void CancelAndResetGlobalTextCts() => _documentReaderController.CancelAndResetGlobalTextCts();
        private Task LoadTextFileAsync(StorageFile file) => _documentReaderController.LoadTextFileAsync(file);
        private Task LoadTextEntryAsync(ImageEntry entry) => _documentReaderController.LoadTextEntryAsync(entry);
        private Task LoadTextFromArchiveEntryAsync(ImageEntry entry) => _documentReaderController.LoadTextFromArchiveEntryAsync(entry);
        private Task DisplayLoadedText(string content, string name, string? uniquePath = null, CancellationToken token = default) =>
            _documentReaderController.DisplayLoadedText(content, name, uniquePath, token);
        private void SwitchToTextMode() => _documentReaderController.SwitchToTextMode();
        private void LoadTextSettings() => _documentReaderController.LoadTextSettings();
        private void SaveTextSettings() => _documentReaderController.SaveTextSettings();
        private void SwitchToImageMode() => _documentReaderController.SwitchToImageMode();
        private void DisableVerticalModeForImageDocument() => _documentReaderController.DisableVerticalModeForImageDocument();
        private void CloseCurrentText() => _documentReaderController.CloseCurrentText();
        private Task RefreshTextDisplay(bool resetScroll = false) => _documentReaderController.RefreshTextDisplay(resetScroll);
        private void ColorsMenu_Click(object sender, RoutedEventArgs e) => _documentReaderController.ColorsMenu_Click(sender, e);
        private void LanguageItem_Click(object sender, RoutedEventArgs e) => _documentReaderController.LanguageItem_Click(sender, e);
        private Task ApplyLanguageSelectionAsync(string lang) => _documentReaderController.ApplyLanguageSelectionAsync(lang);
        private void UpdateLanguageMenuCheckmark() => _documentReaderController.UpdateLanguageMenuCheckmark();
        private void FontToggleButton_Click(object sender, RoutedEventArgs e) => _documentReaderController.FontToggleButton_Click(sender, e);
        private void FontMenu_Click(object sender, RoutedEventArgs e) => _documentReaderController.FontMenu_Click(sender, e);
        private void UiFontMenu_Click(object sender, RoutedEventArgs e) => _documentReaderController.UiFontMenu_Click(sender, e);
        private void ToggleFont() => _documentReaderController.ToggleFont();
        private void UpdateFontSettingsMenu() => _documentReaderController.UpdateFontSettingsMenu();
        private void SetDefaultFont1MenuItem_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.SetDefaultFont1MenuItem_Click(sender, e);
        private void SetDefaultFont2MenuItem_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.SetDefaultFont2MenuItem_Click(sender, e);
        private void ResetDefaultFontsMenuItem_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.ResetDefaultFontsMenuItem_Click(sender, e);
        private void TextSizeUpButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.TextSizeUpButton_Click(sender, e);
        private void IncreaseTextSize() => _documentReaderController.IncreaseTextSize();
        private void TextSizeDownButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.TextSizeDownButton_Click(sender, e);
        private void DecreaseTextSize() => _documentReaderController.DecreaseTextSize();
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.ThemeToggleButton_Click(sender, e);
        private void ToggleTheme() => _documentReaderController.ToggleTheme();
        private void GoToPageButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.GoToPageButton_Click(sender, e);
        private Task ShowGoToLineDialog() => _documentReaderController.ShowGoToLineDialog();
        private Task GoToLine(string lineText) => _documentReaderController.GoToLine(lineText);
        private void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) =>
            _documentReaderController.TextItemsRepeater_ElementPrepared(sender, args);
        private void TextArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.TextArea_PointerPressed(sender, e);
        private void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.TextArea_PointerWheelChanged(sender, e);
        private void NavigateTextPage(int direction) => _documentReaderController.NavigateTextPage(direction);
        private void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null) =>
            _documentReaderController.UpdateTextStatusBar(fileName, totalLines, currentPage);
        private void TextScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            _documentReaderController.TextScrollViewer_ViewChanged(sender, e);
        private void TextScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _documentReaderController.TextScrollViewer_SizeChanged(sender, e);
        private void TextArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _documentReaderController.TextArea_SizeChanged(sender, e);
        private int GetTopVisibleLineIndex() => _documentReaderController.GetTopVisibleLineIndex();
        private void ScrollToLine(int line) => _documentReaderController.ScrollToLine(line);

        private void AozoraToggleButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.AozoraToggleButton_Click(sender, e);
        private void ToggleAozoraMode() => _documentReaderController.ToggleAozoraMode();
        private Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine) =>
            _documentReaderController.ReloadTextDisplayFromCacheAsync(fileName, targetLine);
        private Task PrepareAozoraDisplayAsync(string rawContent, int targetLine = 1, int targetBlockIndex = -1, CancellationToken token = default) =>
            _documentReaderController.PrepareAozoraDisplayAsync(rawContent, targetLine, targetBlockIndex, token);
        private void AozoraTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _documentReaderController.AozoraTextCanvas_SizeChanged(sender, e);
        private Task RenderAozoraDynamicPage(int startIdx) => _documentReaderController.RenderAozoraDynamicPage(startIdx);
        private void StartAozoraPageCalculationAsync() => _documentReaderController.StartAozoraPageCalculationAsync();
        private List<AozoraBindingModel> PaginateHorizontalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null) =>
            _documentReaderController.PaginateHorizontalAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        private void AozoraTextCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            _documentReaderController.AozoraTextCanvas_CreateResources(sender, args);
        private void AozoraTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _documentReaderController.AozoraTextCanvas_Draw(sender, args);
        private void AozoraTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.AozoraTextCanvas_PointerPressed(sender, e);
        private void AozoraTextCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.AozoraTextCanvas_PointerWheelChanged(sender, e);
        private void NavigateAozoraPage(int direction) => _documentReaderController.NavigateAozoraPage(direction);
        private void UpdateAozoraStatusBar() => _documentReaderController.UpdateAozoraStatusBar();
        public void JumpToAozoraLine(int targetLine) => _documentReaderController.JumpToAozoraLine(targetLine);
        private void TocButton_Click(object sender, RoutedEventArgs e) => _documentReaderController.TocButton_Click(sender, e);
        private void TocListView_ItemClick(object sender, ItemClickEventArgs e) =>
            _documentReaderController.TocListView_ItemClick(sender, e);

        public void TriggerVerticalResize() => _documentReaderController.TriggerVerticalResize();
        private void ClearVerticalDisplayState() => _documentReaderController.ClearVerticalDisplayState();
        private void VerticalToggleButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.VerticalToggleButton_Click(sender, e);
        private void ToggleVerticalMode() => _documentReaderController.ToggleVerticalMode();
        private Task PrepareVerticalTextAsync(int targetLine = 1, int targetBlockIndex = -1, CancellationToken externalToken = default) =>
            _documentReaderController.PrepareVerticalTextAsync(targetLine, targetBlockIndex, externalToken);
        private Task RenderVerticalDynamicPageAsync(int startIdx, CancellationToken token = default) =>
            _documentReaderController.RenderVerticalDynamicPageAsync(startIdx, token);
        private void StartVerticalPageCalculationAsync() => _documentReaderController.StartVerticalPageCalculationAsync();
        private List<AozoraBindingModel> PaginateAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null) =>
            _documentReaderController.PaginateAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        private void VerticalTextCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            _documentReaderController.VerticalTextCanvas_CreateResources(sender, args);
        private void VerticalTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _documentReaderController.VerticalTextCanvas_Draw(sender, args);
        private Color GetVerticalTextColor() => _documentReaderController.GetVerticalTextColor();
        private Color GetVerticalBackgroundColor() => _documentReaderController.GetVerticalBackgroundColor();
        private void VerticalTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.VerticalTextCanvas_PointerPressed(sender, e);
        private void VerticalTextCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            _documentReaderController.VerticalTextCanvas_PointerWheelChanged(sender, e);
        private void NavigateVerticalPage(int direction) => _documentReaderController.NavigateVerticalPage(direction);
        private void UpdateVerticalStatusBar() => _documentReaderController.UpdateVerticalStatusBar();
        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _documentReaderController.VerticalTextCanvas_SizeChanged(sender, e);
        private void ClearBackwardCache() => _documentReaderController.ClearBackwardCache();
        private int FindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, ICanvasResourceCreator device, bool isVertical) =>
            _documentReaderController.FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical);
        private FontWeight GetFontWeightForFamily(string fontFamily) =>
            _documentReaderController.GetFontWeightForFamily(fontFamily);

        bool IDocumentReaderHost.IsWindowClosing => _isWindowClosing;
        bool IDocumentReaderHost.IsWebDavMode => _isWebDavMode;
        bool IDocumentReaderHost.IsEpubMode { get => _isEpubMode; set => _isEpubMode = value; }
        bool IDocumentReaderHost.IsSideBySideMode { get => _isSideBySideMode; set => _isSideBySideMode = value; }
        bool IDocumentReaderHost.AutoDoublePageForArchive => _autoDoublePageForArchive;
        bool IDocumentReaderHost.NextImageOnRight => _nextImageOnRight;
        bool IDocumentReaderHost.IsNavigatingRecent { get => _isNavigatingRecent; set => _isNavigatingRecent = value; }
        bool IDocumentReaderHost.SharpenEnabled => _sharpenEnabled;
        bool IDocumentReaderHost.IsColorPickerOpen { get => _isColorPickerOpen; set => _isColorPickerOpen = value; }
        bool IDocumentReaderHost.ShouldInvertControls => ShouldInvertControls;
        int IDocumentReaderHost.CurrentIndex { get => _currentIndex; set => _currentIndex = value; }
        string IDocumentReaderHost.WindowTitle { get => Title; set => Title = value; }
        List<ImageEntry> IDocumentReaderHost.ImageEntries { get => _imageEntries; set => _imageEntries = value; }
        CanvasBitmap? IDocumentReaderHost.CurrentBitmap => _currentBitmap;
        PdfDocument? IDocumentReaderHost.CurrentPdfDocument => _currentPdfDocument;
        string? IDocumentReaderHost.ActiveSearchQuery => _activeSearchQuery;

        int IDocumentReaderHost.CurrentEpubChapterIndex { get => _currentEpubChapterIndex; set => _currentEpubChapterIndex = value; }
        int IDocumentReaderHost.CurrentEpubPageIndex { get => _currentEpubPageIndex; set => _currentEpubPageIndex = value; }
        IReadOnlyList<string> IDocumentReaderHost.EpubSpine => _epubSpine;
        List<EpubWin2DPage> IDocumentReaderHost.EpubWin2DPages => _epubWin2DPages;
        Dictionary<int, List<EpubWin2DPage>> IDocumentReaderHost.EpubPreloadCache => _epubPreloadCache;

        DispatcherQueue IDocumentReaderHost.DispatcherQueue => DispatcherQueue;
        AppWindow IDocumentReaderHost.AppWindow => AppWindow;
        Grid IDocumentReaderHost.RootGrid => RootGrid;
        Grid IDocumentReaderHost.ImageArea => ImageArea;
        Grid IDocumentReaderHost.TextArea => TextArea;
        Grid IDocumentReaderHost.EpubArea => EpubArea;
        ScrollViewer IDocumentReaderHost.TextScrollViewer => TextScrollViewer;
        ItemsRepeater IDocumentReaderHost.TextItemsRepeater => TextItemsRepeater;
        CanvasControl IDocumentReaderHost.MainCanvas => MainCanvas;
        CanvasControl IDocumentReaderHost.AozoraTextCanvas => AozoraTextCanvas;
        CanvasControl IDocumentReaderHost.VerticalTextCanvas => VerticalTextCanvas;
        FrameworkElement IDocumentReaderHost.EmptyStatePanel => EmptyStatePanel;
        Grid IDocumentReaderHost.TextFastNavOverlay => TextFastNavOverlay;
        ContentControl IDocumentReaderHost.RootFontControl => RootFontControl;
        ListView IDocumentReaderHost.FileListView => FileListView;
        GridView IDocumentReaderHost.FileGridView => FileGridView;
        Pivot IDocumentReaderHost.SidebarFavoritesPivot => SidebarFavoritesPivot;
        TextBlock IDocumentReaderHost.CurrentPathText => CurrentPathText;
        TextBlock IDocumentReaderHost.NotificationText => NotificationText;
        TextBlock IDocumentReaderHost.FileNameText => FileNameText;
        TextBlock IDocumentReaderHost.ImageInfoText => ImageInfoText;
        TextBlock IDocumentReaderHost.TextProgressText => TextProgressText;
        TextBlock IDocumentReaderHost.ImageIndexText => ImageIndexText;
        MainToolbarControl IDocumentReaderHost.MainToolbar => MainToolbar;

        ArchiveSession IDocumentReaderHost.ArchiveSession => _archiveSession;
        RecentService IDocumentReaderHost.RecentService => _recentService;
        FavoritesService IDocumentReaderHost.FavoritesService => _favoritesService;
        TocService IDocumentReaderHost.TocService => _tocService;
        DocumentSearchService IDocumentReaderHost.DocumentSearchService => _documentSearchService;
        SearchHighlightService IDocumentReaderHost.SearchHighlightService => _searchHighlightService;
        ImageResourceService IDocumentReaderHost.ImageResourceService => _imageResourceService;
        WindowChromeController IDocumentReaderHost.WindowChromeController => _windowChromeController;

        Task IDocumentReaderHost.AddToRecentAsync(bool immediate) => AddToRecentAsync(immediate);
        void IDocumentReaderHost.SyncSidebarSelection(ImageEntry entry) => SyncSidebarSelection(entry);
        void IDocumentReaderHost.EnsureMinWindowSizeForText() => EnsureMinWindowSizeForText();
        void IDocumentReaderHost.UpdateSideBySideButtonState() => UpdateSideBySideButtonState();
        void IDocumentReaderHost.UpdateNextImageSideButtonState() => UpdateNextImageSideButtonState();
        void IDocumentReaderHost.UpdateFavoritesMenu() => UpdateFavoritesMenu();
        void IDocumentReaderHost.UpdateRecentMenu() => UpdateRecentMenu();
        void IDocumentReaderHost.UpdateWebDavServerList() => UpdateWebDavServerList();
        void IDocumentReaderHost.ApplyLocalization() => ApplyLocalization();
        void IDocumentReaderHost.ShowNotification(string message, string icon, string color) => ShowNotification(message, icon, color);
        string IDocumentReaderHost.GetTextSettingsFilePath() => GetTextSettingsFilePath();
        void IDocumentReaderHost.UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) => UpdateStatusBar(entry, bitmap);
        Task IDocumentReaderHost.DisplayCurrentImageAsync() => DisplayCurrentImageAsync();
        Task IDocumentReaderHost.NavigateEpubAsync(int direction) => NavigateEpubAsync(direction);
        Task IDocumentReaderHost.LoadEpubChapterAsync(int index, bool fromEnd, int targetLine, int targetBlockIndex, int targetPage, double? progress, CancellationToken token) =>
            LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);
        void IDocumentReaderHost.JumpToEpubTocItem(EpubTocItem item) => JumpToEpubTocItem(item);
        void IDocumentReaderHost.UpdateEpubStatus() => UpdateEpubStatus();
        void IDocumentReaderHost.TriggerEpubResize() => TriggerEpubResize();
        void IDocumentReaderHost.ToggleSidebar() => ToggleSidebar();
        void IDocumentReaderHost.HandleSmartTouchNavigation(PointerRoutedEventArgs e, Action prevAction, Action nextAction) =>
            HandleSmartTouchNavigation(e, prevAction, nextAction);
        void IDocumentReaderHost.ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
            ApplySearchHighlightsToTextBlock(textBlock, content, lineNumber);
        DocumentSearchMatch? IDocumentReaderHost.GetActiveSearchMatchFor(DocumentSearchKind kind) =>
            GetActiveSearchMatchFor(kind);
        ViewingContext IDocumentReaderHost.CreateViewingContext() => CreateViewingContext();
        SharpenParams IDocumentReaderHost.CreateSharpenParams() => CreateSharpenParams();
        Task IDocumentReaderHost.LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing,
            Func<bool>? shouldKeepLoadedBitmap) =>
            LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);
    }
}
