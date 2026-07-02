using Microsoft.UI.Xaml;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static partial class ShellComposition
            {
                private static BookmarkCaptureContextFactory CreateBookmarkCaptureContextFactory(MainWindow window)
                {
                    return new BookmarkCaptureContextFactory(() => CreateBookmarkCaptureSnapshot(window));
                }

                private static BookmarkCaptureSnapshot CreateBookmarkCaptureSnapshot(MainWindow window)
                {
                    return new BookmarkCaptureSnapshot(
                        IsNavigatingRecent: window._isNavigatingRecent,
                        IsTextMode: window._isTextMode,
                        IsEpubMode: window._isEpubMode,
                        IsWebDavMode: window._isWebDavMode,
                        IsVerticalMode: window._isVerticalMode,
                        IsAozoraMode: window._isAozoraMode,
                        HasArchive: window._archiveSession.HasArchive,
                        HasPdfDocument: window._currentPdfDocument != null,
                        HasVisibleContent: window.EmptyStatePanel == null || window.EmptyStatePanel.Visibility == Visibility.Collapsed,
                        CurrentTextFilePath: window._currentTextFilePath,
                        CurrentEpubFilePath: window._currentEpubFilePath,
                        CurrentArchivePath: window._archiveSession.CurrentPath,
                        CurrentExplorerPath: window._currentExplorerPath,
                        CurrentWebDavPath: window._currentWebDavPath,
                        CurrentWebDavItemPath: window._currentWebDavItemPath,
                        WebDavServerName: window._webDavService.CurrentServer?.ServerName,
                        CurrentIndex: window._currentIndex,
                        ImageEntries: window._imageEntries,
                        CurrentEpubPageIndex: window.CurrentEpubPageIndex,
                        CurrentEpubChapterIndex: window.CurrentEpubChapterIndex,
                        CurrentEpubPage: window.CurrentEpubWin2DPage,
                        EpubPages: window._epubWin2DPages,
                        EpubSpineCount: window._epubSpine.Count,
                        EpubPageCount: window._epubPages.Count,
                        TextTotalLineCountInSource: window._textTotalLineCountInSource,
                        AozoraTotalLineCountInSource: window._aozoraTotalLineCountInSource,
                        AozoraBlocks: window._aozoraBlocks,
                        CurrentAozoraStartBlockIndex: window._currentAozoraStartBlockIndex,
                        CurrentVerticalStartLine: window._currentVerticalPageInfo.StartLine,
                        CurrentVerticalHasContent: window._currentVerticalPageInfo.Blocks != null && window._currentVerticalPageInfo.Blocks.Count > 0,
                        TopVisibleLineIndex: window.TextScrollViewer != null ? window.GetTopVisibleLineIndex() : 1,
                        TextLineCount: window._textLines.Count,
                        LastRecentSaveLine: window._lastRecentSaveLine,
                        TextScrollOffset: (window._isTextMode && window.TextScrollViewer != null)
                            ? window.TextScrollViewer.VerticalOffset
                            : null);
                }
            }
        }
    }
}
