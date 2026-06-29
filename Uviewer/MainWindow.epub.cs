using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer
{
    public sealed partial class MainWindow : IEpubReaderHost
    {
        private EpubReaderController _epubReaderController = null!;

        private EpubSession _epubSession => _epubReaderController.Session;
        private EpubDocumentService _epubDocumentService => _epubReaderController.DocumentService;
        private EpubPageFlowService _epubPageFlowService => _epubReaderController.PageFlowService;
        private IReadOnlyList<string> _epubSpine => _epubReaderController.Spine;
        private Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache => _epubReaderController.PreloadCache;
        private Dictionary<int, bool> _epubChapterHasText => _epubReaderController.ChapterHasText;

        private bool _isEpubMode
        {
            get => _epubReaderController.IsEpubMode;
            set => _epubReaderController.IsEpubMode = value;
        }

        private int _currentEpubChapterIndex
        {
            get => _epubReaderController.CurrentChapterIndex;
            set => _epubReaderController.CurrentChapterIndex = value;
        }

        private int _currentEpubPageIndex
        {
            get => _epubReaderController.CurrentPageIndex;
            set => _epubReaderController.CurrentPageIndex = value;
        }

        private List<EpubWin2DPage> _epubWin2DPages
        {
            get => _epubReaderController.Pages;
            set => _epubReaderController.Pages = value;
        }

        private int _pendingEpubStartBlockIndex
        {
            get => _epubReaderController.PendingStartBlockIndex;
            set => _epubReaderController.PendingStartBlockIndex = value;
        }

        public int PendingEpubChapterIndex
        {
            get => _epubReaderController.PendingChapterIndex;
            set => _epubReaderController.PendingChapterIndex = value;
        }

        public int PendingEpubPageIndex
        {
            get => _epubReaderController.PendingPageIndex;
            set => _epubReaderController.PendingPageIndex = value;
        }

        public int CurrentEpubChapterIndex => _epubReaderController.CurrentChapterIndex;
        public int CurrentEpubPageIndex => _epubReaderController.CurrentPageIndex;

        private string? _currentEpubFilePath => _epubReaderController.CurrentFilePath;
        private string? _currentEpubDisplayName => _epubReaderController.CurrentDisplayName;
        private EpubWin2DPage? CurrentEpubWin2DPage => _epubReaderController.CurrentPage;
        private object? EpubSelectedItem => _epubReaderController.SelectedItem;
        private List<UIElement> _epubPages => _epubReaderController.PageElements;

        public void TriggerEpubResize() => _epubReaderController.TriggerEpubResize();
        public Task RestoreEpubStateAsync(int chapterIndex, int pageIndex) =>
            _epubReaderController.RestoreEpubStateAsync(chapterIndex, pageIndex);
        public Task NavigateEpubAsync(int direction) => _epubReaderController.NavigateEpubAsync(direction);
        public void JumpToEpubTocItem(EpubTocItem item) => _epubReaderController.JumpToEpubTocItem(item);
        public void ClearEpubCache() => _epubReaderController.ClearEpubCache();

        private Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token = default) =>
            _epubReaderController.LoadEpubEntryAsync(entry, token);
        private Task LoadEpubFileAsync(StorageFile file, ImageEntry? entry = null, CancellationToken token = default) =>
            _epubReaderController.LoadEpubFileAsync(file, entry, token);
        private Task<bool> CloseCurrentEpubAsync() => _epubReaderController.CloseCurrentEpubAsync();
        private void ShutdownEpubResources() => _epubReaderController.ShutdownEpubResources();
        private void CloseCurrentEpub() => _epubReaderController.CloseCurrentEpub();
        private void UpdateEpubStatus() => _epubReaderController.UpdateEpubStatus();
        private void ShowEpubImagePage(EpubWin2DPage page) => _epubReaderController.ShowEpubImagePage(page);
        private Task ShowEpubGoToLineDialog() => _epubReaderController.ShowEpubGoToLineDialog();
        private Task GoToEpubLineAsync(int targetLine) => _epubReaderController.GoToEpubLineAsync(targetLine);
        private void SetEpubPageIndex(int index) => _epubReaderController.SetEpubPageIndex(index);
        private void UpdateEpubVisuals() => _epubReaderController.UpdateEpubVisuals();
        private Task<List<EpubWin2DPage>> RenderEpubPagesAsync(string html, string currentPath, int pinBlockIndex = -1) =>
            _epubReaderController.RenderEpubPagesAsync(html, currentPath, pinBlockIndex);
        private EpubWin2DPage? GetEpubWin2DPage(int chapterIndex, int pageIndex) =>
            _epubReaderController.GetEpubWin2DPage(chapterIndex, pageIndex);
        private EpubImageSize? GetCachedEpubImageSize(string imagePath) =>
            _epubReaderController.GetCachedEpubImageSize(imagePath);
        private Task<List<AozoraBindingModel>> GetEpubChapterAsAozoraBlocksAsync(int index) =>
            _epubReaderController.GetEpubChapterAsAozoraBlocksAsync(index);

        private Task LoadEpubChapterAsync(
            int index,
            bool fromEnd = false,
            int targetLine = -1,
            int targetBlockIndex = -1,
            int targetPage = -1,
            double? progress = null,
            CancellationToken token = default) =>
            _epubReaderController.LoadEpubChapterAsync(index, fromEnd, targetLine, targetBlockIndex, targetPage, progress, token);

        private void EpubArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _epubReaderController.EpubArea_SizeChanged(sender, e);
        private void EpubTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) =>
            _epubReaderController.EpubTextCanvas_CreateResources(sender, args);
        private void EpubTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            _epubReaderController.EpubTextCanvas_SizeChanged(sender, e);
        private void EpubCanvasDisplay_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _epubReaderController.EpubCanvasDisplay_Draw(sender, args);
        private void EpubCanvasDisplayLeft_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _epubReaderController.EpubCanvasDisplayLeft_Draw(sender, args);
        private void EpubCanvasDisplayRight_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _epubReaderController.EpubCanvasDisplayRight_Draw(sender, args);
        private void EpubTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            _epubReaderController.EpubTextCanvas_Draw(sender, args);
        private void EpubTouchOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            _epubReaderController.EpubTouchOverlay_PointerPressed(sender, e);
        private void EpubTouchOverlay_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            _epubReaderController.EpubTouchOverlay_PointerWheelChanged(sender, e);

        bool IEpubReaderHost.IsWindowClosing => _isWindowClosing;
        bool IEpubReaderHost.IsWebDavMode => _isWebDavMode;
        bool IEpubReaderHost.IsTextMode { get => _isTextMode; set => _isTextMode = value; }
        bool IEpubReaderHost.IsAozoraMode { get => _isAozoraMode; set => _isAozoraMode = value; }
        bool IEpubReaderHost.IsMarkdownRenderMode { get => _isMarkdownRenderMode; set => _isMarkdownRenderMode = value; }
        bool IEpubReaderHost.IsVerticalMode { get => _isVerticalMode; set => _isVerticalMode = value; }
        bool IEpubReaderHost.IsSideBySideMode => _isSideBySideMode;
        bool IEpubReaderHost.AutoDoublePageForArchive => _autoDoublePageForArchive;
        bool IEpubReaderHost.NextImageOnRight => _nextImageOnRight;
        bool IEpubReaderHost.IsNavigatingRecent { get => _isNavigatingRecent; set => _isNavigatingRecent = value; }
        int IEpubReaderHost.CurrentIndex { get => _currentIndex; set => _currentIndex = value; }
        int IEpubReaderHost.AozoraPendingTargetLine { get => _aozoraPendingTargetLine; set => _aozoraPendingTargetLine = value; }
        int IEpubReaderHost.TextTotalLineCountInSource { get => _textTotalLineCountInSource; set => _textTotalLineCountInSource = value; }
        string IEpubReaderHost.CurrentTextContent { get => _currentTextContent; set => _currentTextContent = value; }
        string IEpubReaderHost.WindowTitle { get => Title; set => Title = value; }
        List<ImageEntry> IEpubReaderHost.ImageEntries { get => _imageEntries; set => _imageEntries = value; }
        List<AozoraBindingModel> IEpubReaderHost.AozoraBlocks => _aozoraBlocks;
        string? IEpubReaderHost.ActiveSearchQuery => _activeSearchQuery;

        Microsoft.UI.Dispatching.DispatcherQueue IEpubReaderHost.DispatcherQueue => DispatcherQueue;
        Microsoft.UI.Windowing.AppWindow IEpubReaderHost.AppWindow => AppWindow;
        Grid IEpubReaderHost.RootGrid => RootGrid;
        Grid IEpubReaderHost.ImageArea => ImageArea;
        Grid IEpubReaderHost.TextArea => TextArea;
        Grid IEpubReaderHost.EpubArea => EpubArea;
        Grid IEpubReaderHost.EpubImageHost => EpubImageHost;
        Grid IEpubReaderHost.EpubTouchOverlay => EpubTouchOverlay;
        ScrollViewer IEpubReaderHost.TextScrollViewer => TextScrollViewer;
        CanvasControl IEpubReaderHost.VerticalTextCanvas => VerticalTextCanvas;
        CanvasControl IEpubReaderHost.AozoraTextCanvas => AozoraTextCanvas;
        CanvasControl IEpubReaderHost.EpubTextCanvas => EpubTextCanvas;
        CanvasControl IEpubReaderHost.EpubCanvasDisplay => EpubCanvasDisplay;
        CanvasControl IEpubReaderHost.EpubCanvasDisplayLeft => EpubCanvasDisplayLeft;
        CanvasControl IEpubReaderHost.EpubCanvasDisplayRight => EpubCanvasDisplayRight;
        ColumnDefinition IEpubReaderHost.EpubImageLeftColumn => EpubImageLeftColumn;
        ColumnDefinition IEpubReaderHost.EpubImageRightColumn => EpubImageRightColumn;
        TextBlock IEpubReaderHost.FileNameText => FileNameText;
        TextBlock IEpubReaderHost.ImageInfoText => ImageInfoText;
        TextBlock IEpubReaderHost.TextProgressText => TextProgressText;
        TextBlock IEpubReaderHost.ImageIndexText => ImageIndexText;
        MainToolbarControl IEpubReaderHost.MainToolbar => MainToolbar;

        TextSettingsManager IEpubReaderHost.SettingsManager => _settingsManager;
        ReaderLayoutService IEpubReaderHost.ReaderLayoutService => _readerLayoutService;
        TextBlockDocumentService IEpubReaderHost.TextBlockDocumentService => _textBlockDocumentService;
        TextStatusBarService IEpubReaderHost.TextStatusBarService => _textStatusBarService;
        TextDialogService IEpubReaderHost.TextDialogService => _textDialogService;
        ImageResourceService IEpubReaderHost.ImageResourceService => _imageResourceService;
        IAnimatedWebpService IEpubReaderHost.AnimatedWebpService => _animatedWebpService;
        WebDavService IEpubReaderHost.WebDavService => _webDavService;
        TocService IEpubReaderHost.TocService => _tocService;

        Task IEpubReaderHost.AddToRecentAsync(bool immediate) => AddToRecentAsync(immediate);
        Task<bool> IEpubReaderHost.CloseCurrentArchiveAsync() => CloseCurrentArchiveAsync();
        Task<bool> IEpubReaderHost.CloseCurrentPdfAsync() => CloseCurrentPdfAsync();
        void IEpubReaderHost.CancelAndResetGlobalTextCts() => CancelAndResetGlobalTextCts();
        void IEpubReaderHost.LoadTextSettings() => LoadTextSettings();
        void IEpubReaderHost.SaveTextSettings() => SaveTextSettings();
        void IEpubReaderHost.EnsureMinWindowSizeForText() => EnsureMinWindowSizeForText();
        void IEpubReaderHost.UpdateSideBySideButtonState() => UpdateSideBySideButtonState();
        void IEpubReaderHost.UpdateNextImageSideButtonState() => UpdateNextImageSideButtonState();
        void IEpubReaderHost.SyncSidebarSelection(ImageEntry entry) => SyncSidebarSelection(entry);
        void IEpubReaderHost.ClearBackwardCache() => ClearBackwardCache();
        void IEpubReaderHost.ClearVerticalDisplayState() => ClearVerticalDisplayState();
        Task IEpubReaderHost.PrepareVerticalTextAsync(int line) => PrepareVerticalTextAsync(line);
        void IEpubReaderHost.ShowNotification(string message, string icon, string color) => ShowNotification(message, icon, color);
        FontWeight IEpubReaderHost.GetFontWeightForFamily(string fontFamily) => GetFontWeightForFamily(fontFamily);
        Color IEpubReaderHost.GetVerticalBackgroundColor() => GetVerticalBackgroundColor();
        Color IEpubReaderHost.GetVerticalTextColor() => GetVerticalTextColor();
        DocumentSearchMatch? IEpubReaderHost.GetActiveSearchMatchFor(DocumentSearchKind kind) => GetActiveSearchMatchFor(kind);
        List<AozoraBindingModel> IEpubReaderHost.PaginateVerticalAozoraPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            float availableWidth,
            float availableHeight,
            CanvasDevice? device) =>
            PaginateAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        List<AozoraBindingModel> IEpubReaderHost.PaginateHorizontalAozoraPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            float availableWidth,
            float availableHeight,
            CanvasDevice? device) =>
            PaginateHorizontalAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        int IEpubReaderHost.FindPreviousPageStart(
            int targetIdx,
            List<AozoraBindingModel> blocks,
            float maxWidth,
            float availHeight,
            ICanvasResourceCreator device,
            bool isVertical) =>
            FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical);
        Task IEpubReaderHost.LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing,
            Func<bool>? shouldKeepLoadedBitmap) =>
            LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);

        void IEpubReaderHost.AttachVerticalPreviewKeyIfNeeded()
        {
            if (!_verticalKeyAttached && RootGrid != null)
            {
                RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                _verticalKeyAttached = true;
            }
        }
    }
}
