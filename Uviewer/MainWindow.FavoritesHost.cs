using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private FavoriteCaptureContext CreateFavoriteCaptureContext()
        {
            return new FavoriteCaptureContext(
                IsTextMode: _isTextMode,
                IsEpubMode: _isEpubMode,
                IsWebDavMode: _isWebDavMode,
                IsVerticalMode: _isVerticalMode,
                IsAozoraMode: _isAozoraMode,
                HasArchive: _archiveSession.HasArchive,
                HasPdfDocument: _currentPdfDocument != null,
                HasVisibleContent: EmptyStatePanel == null || EmptyStatePanel.Visibility == Visibility.Collapsed,
                CurrentTextFilePath: _currentTextFilePath,
                CurrentEpubFilePath: _currentEpubFilePath,
                CurrentArchivePath: _archiveSession.CurrentPath,
                CurrentExplorerPath: _currentExplorerPath,
                CurrentWebDavPath: _currentWebDavPath,
                CurrentWebDavItemPath: _currentWebDavItemPath,
                WebDavServerName: _webDavService.CurrentServer?.ServerName,
                CurrentIndex: _currentIndex,
                ImageEntries: _imageEntries,
                CurrentEpubPageIndex: CurrentEpubPageIndex,
                CurrentEpubChapterIndex: CurrentEpubChapterIndex,
                CurrentEpubPage: CurrentEpubWin2DPage,
                EpubPages: _epubWin2DPages,
                EpubSpineCount: _epubSpine.Count,
                EpubPageCount: _epubPages.Count,
                TextTotalLineCountInSource: _textTotalLineCountInSource,
                AozoraTotalLineCountInSource: _aozoraTotalLineCountInSource,
                AozoraBlocks: _aozoraBlocks,
                CurrentAozoraStartBlockIndex: _currentAozoraStartBlockIndex,
                CurrentVerticalStartLine: _currentVerticalPageInfo.StartLine,
                TopVisibleLineIndex: TextScrollViewer != null ? GetTopVisibleLineIndex() : 1,
                TextScrollOffset: (_isTextMode && TextScrollViewer != null) ? TextScrollViewer.VerticalOffset : null);
        }

    }
}
