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
    public sealed partial class MainWindow
    {
        private const string TextSettingsFilePath = "text_settings.json";
        private string GetTextSettingsFilePath() => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Uviewer",
            TextSettingsFilePath);

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
        private bool _isAozoraParsePartial => _documentReaderController.IsAozoraParsePartial;
        private ReaderPageInfo _currentAozoraPageInfo => _documentReaderController.CurrentAozoraPageInfo;
        private int _currentAozoraStartBlockIndex => _documentReaderController.CurrentAozoraStartBlockIndex;
        private int _aozoraPendingTargetLine
        {
            get => _documentReaderController.AozoraPendingTargetLine;
            set => _documentReaderController.AozoraPendingTargetLine = value;
        }
        private int _aozoraPendingTargetBlockIndex
        {
            get => _documentReaderController.AozoraPendingTargetBlockIndex;
            set => _documentReaderController.AozoraPendingTargetBlockIndex = value;
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
        private void LanguageItem_Click(object sender, RoutedEventArgs e) => _documentReaderController.LanguageItem_Click(sender, e);
        private void UpdateLanguageMenuCheckmark() => _documentReaderController.UpdateLanguageMenuCheckmark();
        private void FontToggleButton_Click(object sender, RoutedEventArgs e) => _documentReaderController.FontToggleButton_Click(sender, e);
        private void UpdateFontSettingsMenu() => _documentReaderController.UpdateFontSettingsMenu();
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) =>
            _documentReaderController.ThemeToggleButton_Click(sender, e);
        private Task GoToLine(string lineText) => _documentReaderController.GoToLine(lineText);
        private void NavigateTextPage(int direction) => _documentReaderController.NavigateTextPage(direction);
        private void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null) =>
            _documentReaderController.UpdateTextStatusBar(fileName, totalLines, currentPage);
        private int GetTopVisibleLineIndex() => _documentReaderController.GetTopVisibleLineIndex();
        private void ScrollToLine(int line) => _documentReaderController.ScrollToLine(line);

        private void ToggleAozoraMode() => _documentReaderController.ToggleAozoraMode();
        private Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine) =>
            _documentReaderController.ReloadTextDisplayFromCacheAsync(fileName, targetLine);
        private Task PrepareAozoraDisplayAsync(string rawContent, int targetLine = 1, int targetBlockIndex = -1, CancellationToken token = default) =>
            _documentReaderController.PrepareAozoraDisplayAsync(rawContent, targetLine, targetBlockIndex, token);
        private Task RenderAozoraDynamicPage(int startIdx) => _documentReaderController.RenderAozoraDynamicPage(startIdx);
        private void StartAozoraPageCalculationAsync() => _documentReaderController.StartAozoraPageCalculationAsync();
        private List<AozoraBindingModel> PaginateHorizontalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null, CancellationToken token = default) =>
            _documentReaderController.PaginateHorizontalAozoraPage(ref index, blocks, availableWidth, availableHeight, device, token);
        private void NavigateAozoraPage(int direction) => _documentReaderController.NavigateAozoraPage(direction);
        private void UpdateAozoraStatusBar() => _documentReaderController.UpdateAozoraStatusBar();
        public void JumpToAozoraLine(int targetLine) => _documentReaderController.JumpToAozoraLine(targetLine);
        public void TriggerVerticalResize() => _documentReaderController.TriggerVerticalResize();
        private void ClearVerticalDisplayState() => _documentReaderController.ClearVerticalDisplayState();
        private Task PrepareVerticalTextAsync(int targetLine = 1, int targetBlockIndex = -1, CancellationToken externalToken = default) =>
            _documentReaderController.PrepareVerticalTextAsync(targetLine, targetBlockIndex, externalToken);
        private Task RenderVerticalDynamicPageAsync(int startIdx, CancellationToken token = default) =>
            _documentReaderController.RenderVerticalDynamicPageAsync(startIdx, token);
        private void StartVerticalPageCalculationAsync() => _documentReaderController.StartVerticalPageCalculationAsync();
        private List<AozoraBindingModel> PaginateAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null, CancellationToken token = default) =>
            _documentReaderController.PaginateAozoraPage(ref index, blocks, availableWidth, availableHeight, device, token);
        private Color GetVerticalTextColor() => _documentReaderController.GetVerticalTextColor();
        private Color GetVerticalBackgroundColor() => _documentReaderController.GetVerticalBackgroundColor();
        private void NavigateVerticalPage(int direction) => _documentReaderController.NavigateVerticalPage(direction);
        private void UpdateVerticalStatusBar() => _documentReaderController.UpdateVerticalStatusBar();
        private void ClearBackwardCache() => _documentReaderController.ClearBackwardCache();
        private int FindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, ICanvasResourceCreator device, bool isVertical, CancellationToken token = default) =>
            _documentReaderController.FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical, token);
        private FontWeight GetFontWeightForFamily(string fontFamily) =>
            _documentReaderController.GetFontWeightForFamily(fontFamily);

    }
}
