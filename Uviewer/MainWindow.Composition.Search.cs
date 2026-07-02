using System;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class SearchFeatureComposition
            {
                public static void Initialize(MainWindow window)
                {
                    window._searchController = new MainWindowSearchController(
                        window._documentSearchCoordinatorService,
                        () => CreateContext(window));
                }

                private static DocumentSearchCoordinatorContext CreateContext(MainWindow window)
                {
                    return new DocumentSearchCoordinatorContext
                    {
                        State = window._documentSearchState,
                        OverlayService = window._searchOverlayService,
                        HighlightService = window._searchHighlightService,
                        DocumentSearchService = window._documentSearchService,
                        TextDocumentSearchService = window._textDocumentSearchService,
                        TextHighlightPresenterService = window._textSearchHighlightPresenterService,
                        TextReaderState = window._textReaderState,
                        EpubSession = window._epubSession,
                        EpubDocumentService = window._epubDocumentService,
                        MainToolbar = window.MainToolbar,
                        RootGrid = window.RootGrid,
                        TextItemsRepeater = window.TextItemsRepeater,
                        MainCanvas = window.MainCanvas,
                        AozoraTextCanvas = window.AozoraTextCanvas,
                        VerticalTextCanvas = window.VerticalTextCanvas,
                        EpubTextCanvas = window.EpubTextCanvas,
                        TextLines = window._textLines,
                        AozoraBlocks = window._aozoraBlocks,
                        IsTextMode = window._isTextMode,
                        IsEpubMode = window._isEpubMode,
                        IsVerticalMode = window._isVerticalMode,
                        IsAozoraMode = window._isAozoraMode,
                        IsMarkdownRenderMode = window._isMarkdownRenderMode,
                        IsPdfMode = window._currentPdfDocument != null,
                        CurrentPdfPath = window._currentPdfPath,
                        EpubCacheKey = $"epub:{window._currentEpubFilePath ?? window._currentEpubDisplayName ?? string.Empty}:{window._epubSpine.Count}",
                        EpubSpineCount = window._epubSpine.Count,
                        CurrentIndex = window._currentIndex,
                        ImageEntryCount = window._imageEntries.Count,
                        CurrentEpubChapterIndex = window._currentEpubChapterIndex,
                        CurrentEpubPageStartLine = window.CurrentEpubWin2DPage?.StartLine ?? 1,
                        CurrentVerticalStartLine = window._currentVerticalPageInfo.StartLine,
                        CurrentAozoraStartLine = window._currentAozoraPageInfo.StartLine,
                        EncodingName = window._settingsManager.EncodingName,
                        SetCurrentIndex = value => window._currentIndex = value,
                        SetCurrentEpubChapterIndex = value => window._currentEpubChapterIndex = value,
                        DisableVerticalModeForImageDocument = window.DisableVerticalModeForImageDocument,
                        ShowNotification = (message, icon, color) => window.ShowNotification(message, icon, color),
                        GetCurrentIndex = () => window._currentIndex,
                        GetCurrentPdfPath = () => window._currentPdfPath,
                        GetTopVisibleLineIndex = window.GetTopVisibleLineIndex,
                        FindAozoraStartBlockIndex = (line, blockIndex) =>
                            window._textBlockDocumentService.FindStartBlockIndex(window._aozoraBlocks, line, blockIndex),
                        FindEpubPageIndex = match => FindEpubPageIndex(window, match),
                        DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                        PrepareVerticalTextAsync = (line, blockIndex) => window.PrepareVerticalTextAsync(line, blockIndex),
                        RenderAozoraDynamicPageAsync = window.RenderAozoraDynamicPage,
                        LoadEpubChapterAsync = (chapterIndex, line, blockIndex) =>
                            window._epubReaderController.LoadEpubChapterAsync(chapterIndex, targetLine: line, targetBlockIndex: blockIndex),
                        UpdateAozoraStatusBar = window.UpdateAozoraStatusBar,
                        UpdateTextStatusBar = () => window.UpdateTextStatusBar(),
                        ScrollToLine = window.ScrollToLine,
                        SetEpubPageIndex = window._epubReaderController.SetEpubPageIndex
                    };
                }

                private static int FindEpubPageIndex(MainWindow window, DocumentSearchMatch match)
                {
                    if (window._epubWin2DPages.Count == 0) return -1;

                    if (match.BlockIndex >= 0)
                    {
                        for (int i = window._epubWin2DPages.Count - 1; i >= 0; i--)
                        {
                            if (window._epubWin2DPages[i].StartBlockIndex <= match.BlockIndex)
                            {
                                return i;
                            }
                        }
                    }

                    return window._epubPageFlowService.FindPageByLine(window._epubWin2DPages, match.LineNumber);
                }
            }
        }
    }
}
