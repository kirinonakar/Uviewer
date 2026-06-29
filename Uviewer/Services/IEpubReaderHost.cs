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
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer.Services
{
    internal interface IEpubReaderHost
    {
        bool IsWindowClosing { get; }
        bool IsWebDavMode { get; }
        bool IsTextMode { get; set; }
        bool IsAozoraMode { get; set; }
        bool IsMarkdownRenderMode { get; set; }
        bool IsVerticalMode { get; set; }
        bool IsSideBySideMode { get; }
        bool AutoDoublePageForArchive { get; }
        bool NextImageOnRight { get; }
        bool IsNavigatingRecent { get; set; }
        int CurrentIndex { get; set; }
        int AozoraPendingTargetLine { get; set; }
        int TextTotalLineCountInSource { get; set; }
        string CurrentTextContent { get; set; }
        string WindowTitle { get; set; }
        List<ImageEntry> ImageEntries { get; set; }
        List<AozoraBindingModel> AozoraBlocks { get; }
        string? ActiveSearchQuery { get; }

        DispatcherQueue DispatcherQueue { get; }
        AppWindow AppWindow { get; }
        Grid RootGrid { get; }
        Grid ImageArea { get; }
        Grid TextArea { get; }
        Grid EpubArea { get; }
        Grid EpubImageHost { get; }
        Grid EpubTouchOverlay { get; }
        ScrollViewer TextScrollViewer { get; }
        CanvasControl VerticalTextCanvas { get; }
        CanvasControl AozoraTextCanvas { get; }
        CanvasControl EpubTextCanvas { get; }
        CanvasControl EpubCanvasDisplay { get; }
        CanvasControl EpubCanvasDisplayLeft { get; }
        CanvasControl EpubCanvasDisplayRight { get; }
        ColumnDefinition EpubImageLeftColumn { get; }
        ColumnDefinition EpubImageRightColumn { get; }
        TextBlock FileNameText { get; }
        TextBlock ImageInfoText { get; }
        TextBlock TextProgressText { get; }
        TextBlock ImageIndexText { get; }
        MainToolbarControl MainToolbar { get; }

        TextSettingsManager SettingsManager { get; }
        ReaderLayoutService ReaderLayoutService { get; }
        TextBlockDocumentService TextBlockDocumentService { get; }
        TextStatusBarService TextStatusBarService { get; }
        TextDialogService TextDialogService { get; }
        ImageResourceService ImageResourceService { get; }
        IAnimatedWebpService AnimatedWebpService { get; }
        WebDavService WebDavService { get; }
        TocService TocService { get; }

        Task AddToRecentAsync(bool immediate);
        Task<bool> CloseCurrentArchiveAsync();
        Task<bool> CloseCurrentPdfAsync();
        void CancelAndResetGlobalTextCts();
        void LoadTextSettings();
        void SaveTextSettings();
        void EnsureMinWindowSizeForText();
        void UpdateSideBySideButtonState();
        void UpdateNextImageSideButtonState();
        void SyncSidebarSelection(ImageEntry entry);
        void ClearBackwardCache();
        void ClearVerticalDisplayState();
        void AttachVerticalPreviewKeyIfNeeded();
        Task PrepareVerticalTextAsync(int line);
        void ShowNotification(string message, string icon, string color);
        FontWeight GetFontWeightForFamily(string fontFamily);
        Color GetVerticalBackgroundColor();
        Color GetVerticalTextColor();
        DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind);
        List<AozoraBindingModel> PaginateVerticalAozoraPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            float availableWidth,
            float availableHeight,
            CanvasDevice? device = null);
        List<AozoraBindingModel> PaginateHorizontalAozoraPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            float availableWidth,
            float availableHeight,
            CanvasDevice? device = null);
        int FindPreviousPageStart(
            int targetIdx,
            List<AozoraBindingModel> blocks,
            float maxWidth,
            float availHeight,
            ICanvasResourceCreator device,
            bool isVertical);
        Task LoadImageResourceAndInvalidateAsync(
            string resourcePath,
            string cacheKey,
            CanvasDevice device,
            Action invalidate,
            Action? onMissing = null,
            Func<bool>? shouldKeepLoadedBitmap = null);
    }
}
