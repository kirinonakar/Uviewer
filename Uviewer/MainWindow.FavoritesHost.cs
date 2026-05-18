using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : IBookmarkNavigationHost
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

        bool IBookmarkNavigationHost.IsWebDavMode => _isWebDavMode;
        string? IBookmarkNavigationHost.CurrentWebDavServerName => _webDavService.CurrentServer?.ServerName;
        System.Collections.Generic.IReadOnlyList<ImageEntry> IBookmarkNavigationHost.ImageEntries => _imageEntries;

        int IBookmarkNavigationHost.CurrentImageIndex
        {
            get => _currentIndex;
            set => _currentIndex = value;
        }

        Task IBookmarkNavigationHost.ConnectToWebDavServerAsync(string serverName, bool loadRoot) =>
            ConnectToWebDavServerAsync(serverName, loadRoot);

        Task IBookmarkNavigationHost.LoadWebDavFolderAsync(string remotePath) =>
            LoadWebDavFolderAsync(remotePath);

        Task IBookmarkNavigationHost.OpenWebDavFileAsync(FileItem item) =>
            OpenWebDavFileAsync(item);

        Task IBookmarkNavigationHost.OpenWebDavArchiveAsync(FileItem item) =>
            OpenWebDavArchiveAsync(item);

        void IBookmarkNavigationHost.LoadExplorerFolder(string path) =>
            LoadExplorerFolder(path);

        Task IBookmarkNavigationHost.LoadImagesFromPdfAsync(string path) =>
            LoadImagesFromPdfAsync(path);

        Task IBookmarkNavigationHost.LoadImageFromFileAsync(StorageFile file) =>
            LoadImageFromFileAsync(file);

        Task IBookmarkNavigationHost.LoadImagesFromArchiveAsync(string path) =>
            LoadImagesFromArchiveAsync(path);

        Task IBookmarkNavigationHost.DisplayCurrentImageAsync() =>
            DisplayCurrentImageAsync();

        void IBookmarkNavigationHost.SetPendingEpubPosition(
            int chapterIndex,
            int pageIndex,
            int blockIndex,
            int savedLine)
        {
            PendingEpubChapterIndex = chapterIndex;
            PendingEpubPageIndex = pageIndex;
            _pendingEpubStartBlockIndex = blockIndex;
            _aozoraPendingTargetLine = savedLine > 1 ? savedLine : 1;
        }

        void IBookmarkNavigationHost.SetPendingTextPosition(int savedLine, int savedPage)
        {
            _aozoraPendingTargetLine = savedLine > 1 ? savedLine : (savedPage > 0 ? -savedPage : 1);
        }

        void IBookmarkNavigationHost.SetPendingPdfPage(int pageIndex)
        {
            _pendingPdfPageIndex = pageIndex;
        }

        async Task IBookmarkNavigationHost.RestoreTextScrollOffsetAsync(double scrollOffset)
        {
            if (TextScrollViewer == null)
            {
                return;
            }

            await Task.Delay(100);
            TextScrollViewer.ChangeView(null, scrollOffset, null);
            UpdateTextStatusBar();
        }

        void IBookmarkNavigationHost.NotifyFileNotFound()
        {
            ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
        }

        void IBookmarkNavigationHost.NotifyWebDavFavoriteOpenFailed(string message)
        {
            ShowNotification($"WebDAV 즐겨찾기 열기 실패: {message}");
        }
    }
}
