using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool CanSearchCurrentDocument =>
            _documentSearchCoordinatorService.CanSearch(CreateDocumentSearchCoordinatorContext());

        private void SearchButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowSearchOverlay(sender as FrameworkElement);
        }

        private void ShowSearchOverlay(FrameworkElement? anchor = null)
        {
            _documentSearchCoordinatorService.ShowOverlay(
                CreateDocumentSearchCoordinatorContext(),
                anchor);
        }

        private void SetActiveSearchQuery(string? query)
        {
            _documentSearchCoordinatorService.SetActiveQuery(
                CreateDocumentSearchCoordinatorContext(),
                query);
        }

        private Task RefreshPdfSearchHighlightsAsync(int pageIndex, int currentMatchIndex = -1)
        {
            return _documentSearchCoordinatorService.RefreshPdfHighlightsAsync(
                CreateDocumentSearchCoordinatorContext(),
                pageIndex,
                currentMatchIndex);
        }

        private void InvalidateSearchHighlights()
        {
            _documentSearchCoordinatorService.InvalidateHighlights(CreateDocumentSearchCoordinatorContext());
        }

        private void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber)
        {
            _documentSearchCoordinatorService.ApplyHighlightsToTextBlock(
                CreateDocumentSearchCoordinatorContext(),
                textBlock,
                content,
                lineNumber);
        }

        private DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind)
        {
            return _documentSearchCoordinatorService.GetActiveMatchFor(
                CreateDocumentSearchCoordinatorContext(),
                kind);
        }

        private Task<IReadOnlyList<DocumentSearchMatch>> SearchCurrentDocumentAsync(
            string query,
            CancellationToken token)
        {
            return _documentSearchCoordinatorService.SearchCurrentDocumentAsync(
                CreateDocumentSearchCoordinatorContext(),
                query,
                token);
        }

        private long GetCurrentSearchPosition()
        {
            return _documentSearchCoordinatorService.GetCurrentPosition(CreateDocumentSearchCoordinatorContext());
        }

        private Task NavigateToSearchMatchAsync(DocumentSearchMatch match)
        {
            return _documentSearchCoordinatorService.NavigateToMatchAsync(
                CreateDocumentSearchCoordinatorContext(),
                match);
        }

        private DocumentSearchCoordinatorContext CreateDocumentSearchCoordinatorContext()
        {
            return new DocumentSearchCoordinatorContext
            {
                State = _documentSearchState,
                OverlayService = _searchOverlayService,
                HighlightService = _searchHighlightService,
                DocumentSearchService = _documentSearchService,
                TextDocumentSearchService = _textDocumentSearchService,
                TextHighlightPresenterService = _textSearchHighlightPresenterService,
                TextReaderState = _textReaderState,
                EpubSession = _epubSession,
                EpubDocumentService = _epubDocumentService,
                MainToolbar = MainToolbar,
                RootGrid = RootGrid,
                TextItemsRepeater = TextItemsRepeater,
                MainCanvas = MainCanvas,
                AozoraTextCanvas = AozoraTextCanvas,
                VerticalTextCanvas = VerticalTextCanvas,
                EpubTextCanvas = EpubTextCanvas,
                TextLines = _textLines,
                AozoraBlocks = _aozoraBlocks,
                IsTextMode = _isTextMode,
                IsEpubMode = _isEpubMode,
                IsVerticalMode = _isVerticalMode,
                IsAozoraMode = _isAozoraMode,
                IsMarkdownRenderMode = _isMarkdownRenderMode,
                IsPdfMode = _currentPdfDocument != null,
                CurrentPdfPath = _currentPdfPath,
                EpubCacheKey = $"epub:{_currentEpubFilePath ?? _currentEpubDisplayName ?? string.Empty}:{_epubSpine.Count}",
                EpubSpineCount = _epubSpine.Count,
                CurrentIndex = _currentIndex,
                ImageEntryCount = _imageEntries.Count,
                CurrentEpubChapterIndex = _currentEpubChapterIndex,
                CurrentEpubPageStartLine = CurrentEpubWin2DPage?.StartLine ?? 1,
                CurrentVerticalStartLine = _currentVerticalPageInfo.StartLine,
                CurrentAozoraStartLine = _currentAozoraPageInfo.StartLine,
                EncodingName = _settingsManager.EncodingName,
                SetCurrentIndex = value => _currentIndex = value,
                SetCurrentEpubChapterIndex = value => _currentEpubChapterIndex = value,
                DisableVerticalModeForImageDocument = DisableVerticalModeForImageDocument,
                ShowNotification = (message, icon, color) => ShowNotification(message, icon, color),
                GetCurrentIndex = () => _currentIndex,
                GetCurrentPdfPath = () => _currentPdfPath,
                GetTopVisibleLineIndex = GetTopVisibleLineIndex,
                FindAozoraStartBlockIndex = (line, blockIndex) =>
                    _textBlockDocumentService.FindStartBlockIndex(_aozoraBlocks, line, blockIndex),
                FindEpubPageIndex = FindEpubSearchPageIndex,
                DisplayCurrentImageAsync = DisplayCurrentImageAsync,
                PrepareVerticalTextAsync = (line, blockIndex) => PrepareVerticalTextAsync(line, blockIndex),
                RenderAozoraDynamicPageAsync = RenderAozoraDynamicPage,
                LoadEpubChapterAsync = (chapterIndex, line, blockIndex) =>
                    LoadEpubChapterAsync(chapterIndex, targetLine: line, targetBlockIndex: blockIndex),
                UpdateAozoraStatusBar = UpdateAozoraStatusBar,
                UpdateTextStatusBar = () => UpdateTextStatusBar(),
                ScrollToLine = ScrollToLine,
                SetEpubPageIndex = SetEpubPageIndex
            };
        }

        private int FindEpubSearchPageIndex(DocumentSearchMatch match)
        {
            if (_epubWin2DPages.Count == 0) return -1;

            if (match.BlockIndex >= 0)
            {
                for (int i = _epubWin2DPages.Count - 1; i >= 0; i--)
                {
                    if (_epubWin2DPages[i].StartBlockIndex <= match.BlockIndex)
                    {
                        return i;
                    }
                }
            }

            return _epubPageFlowService.FindPageByLine(_epubWin2DPages, match.LineNumber);
        }
    }
}
