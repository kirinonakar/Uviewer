using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;
using Uviewer.Services;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class EpubReaderHostAdapter : IEpubReaderHost
        {
            private readonly MainWindow _window;

            public EpubReaderHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsWindowClosing => _window._isWindowClosing;
            public bool IsWebDavMode => _window._isWebDavMode;
            public bool IsTextMode { get => _window._isTextMode; set => _window._isTextMode = value; }
            public bool IsAozoraMode { get => _window._isAozoraMode; set => _window._isAozoraMode = value; }
            public bool IsMarkdownRenderMode { get => _window._isMarkdownRenderMode; set => _window._isMarkdownRenderMode = value; }
            public bool IsVerticalMode { get => _window._isVerticalMode; set => _window._isVerticalMode = value; }
            public bool IsSideBySideMode => _window._isSideBySideMode;
            public bool AutoDoublePageForArchive => _window._autoDoublePageForArchive;
            public bool NextImageOnRight => _window._nextImageOnRight;
            public bool IsNavigatingRecent { get => _window._isNavigatingRecent; set => _window._isNavigatingRecent = value; }
            public int CurrentIndex { get => _window._currentIndex; set => _window._currentIndex = value; }
            public int AozoraPendingTargetLine { get => _window._aozoraPendingTargetLine; set => _window._aozoraPendingTargetLine = value; }
            public int TextTotalLineCountInSource { get => _window._textTotalLineCountInSource; set => _window._textTotalLineCountInSource = value; }
            public string CurrentTextContent { get => _window._currentTextContent; set => _window._currentTextContent = value; }
            public string WindowTitle { get => _window.Title; set => _window.Title = value; }
            public List<ImageEntry> ImageEntries { get => _window._imageEntries; set => _window._imageEntries = value; }
            public List<AozoraBindingModel> AozoraBlocks => _window._aozoraBlocks;
            public string? ActiveSearchQuery => _window._activeSearchQuery;

            public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue => _window.DispatcherQueue;
            public AppWindow AppWindow => _window.AppWindow;
            public Grid RootGrid => _window.RootGrid;
            public Grid ImageArea => _window.ImageArea;
            public Grid TextArea => _window.TextArea;
            public Grid EpubArea => _window.EpubArea;
            public Grid EpubImageHost => _window.EpubImageHost;
            public Grid EpubTouchOverlay => _window.EpubTouchOverlay;
            public ScrollViewer TextScrollViewer => _window.TextScrollViewer;
            public CanvasControl VerticalTextCanvas => _window.VerticalTextCanvas;
            public CanvasControl AozoraTextCanvas => _window.AozoraTextCanvas;
            public CanvasControl EpubTextCanvas => _window.EpubTextCanvas;
            public CanvasControl EpubCanvasDisplay => _window.EpubCanvasDisplay;
            public CanvasControl EpubCanvasDisplayLeft => _window.EpubCanvasDisplayLeft;
            public CanvasControl EpubCanvasDisplayRight => _window.EpubCanvasDisplayRight;
            public ColumnDefinition EpubImageLeftColumn => _window.EpubImageLeftColumn;
            public ColumnDefinition EpubImageRightColumn => _window.EpubImageRightColumn;
            public TextBlock FileNameText => _window.FileNameText;
            public TextBlock ImageInfoText => _window.ImageInfoText;
            public TextBlock TextProgressText => _window.TextProgressText;
            public TextBlock ImageIndexText => _window.ImageIndexText;
            public MainToolbarControl MainToolbar => _window.MainToolbar;

            public TextSettingsManager SettingsManager => _window._settingsManager;
            public ReaderLayoutService ReaderLayoutService => _window._readerLayoutService;
            public TextBlockDocumentService TextBlockDocumentService => _window._textBlockDocumentService;
            public TextStatusBarService TextStatusBarService => _window._textStatusBarService;
            public TextDialogService TextDialogService => _window._textDialogService;
            public ImageResourceService ImageResourceService => _window._imageResourceService;
            public IAnimatedWebpService AnimatedWebpService => _window._animatedWebpService;
            public WebDavService WebDavService => _window._webDavService;
            public TocService TocService => _window._tocService;

            public Task AddToRecentAsync(bool immediate) => _window.AddToRecentAsync(immediate);
            public Task<bool> CloseCurrentArchiveAsync() => _window.CloseCurrentArchiveAsync();
            public Task<bool> CloseCurrentPdfAsync() => _window.CloseCurrentPdfAsync();
            public void CancelAndResetGlobalTextCts() => _window.CancelAndResetGlobalTextCts();
            public void LoadTextSettings() => _window.LoadTextSettings();
            public void SaveTextSettings() => _window.SaveTextSettings();
            public void EnsureMinWindowSizeForText() => _window.EnsureMinWindowSizeForText();
            public void UpdateSideBySideButtonState() => _window.UpdateSideBySideButtonState();
            public void UpdateNextImageSideButtonState() => _window.UpdateNextImageSideButtonState();
            public void SyncSidebarSelection(ImageEntry entry) => _window.SyncSidebarSelection(entry);
            public void ClearBackwardCache() => _window.ClearBackwardCache();
            public void ClearVerticalDisplayState() => _window.ClearVerticalDisplayState();
            public Task PrepareVerticalTextAsync(int line) => _window.PrepareVerticalTextAsync(line);
            public void ShowNotification(string message, string icon, string color) => _window.ShowNotification(message, icon, color);
            public FontWeight GetFontWeightForFamily(string fontFamily) => _window.GetFontWeightForFamily(fontFamily);
            public Color GetVerticalBackgroundColor() => _window.GetVerticalBackgroundColor();
            public Color GetVerticalTextColor() => _window.GetVerticalTextColor();
            public DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) => _window.GetActiveSearchMatchFor(kind);
            public List<AozoraBindingModel> PaginateVerticalAozoraPage(
                ref int index,
                List<AozoraBindingModel> blocks,
                float availableWidth,
                float availableHeight,
                CanvasDevice? device = null) =>
                _window.PaginateAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
            public List<AozoraBindingModel> PaginateHorizontalAozoraPage(
                ref int index,
                List<AozoraBindingModel> blocks,
                float availableWidth,
                float availableHeight,
                CanvasDevice? device = null) =>
                _window.PaginateHorizontalAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
            public int FindPreviousPageStart(
                int targetIdx,
                List<AozoraBindingModel> blocks,
                float maxWidth,
                float availHeight,
                ICanvasResourceCreator device,
                bool isVertical) =>
                _window.FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical);
            public Task LoadImageResourceAndInvalidateAsync(
                string resourcePath,
                string cacheKey,
                CanvasDevice device,
                Action invalidate,
                Action? onMissing = null,
                Func<bool>? shouldKeepLoadedBitmap = null) =>
                _window.LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);

            public void AttachVerticalPreviewKeyIfNeeded()
            {
                _window._documentReaderController.AttachVerticalPreviewKeyIfNeeded();
            }
        }
    }
}
