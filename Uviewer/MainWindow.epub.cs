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
    public sealed partial class MainWindow
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

    }
}
