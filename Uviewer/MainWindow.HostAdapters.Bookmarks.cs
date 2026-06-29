using System.Collections.Generic;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class BookmarkNavigationHostAdapter : IBookmarkNavigationHost
        {
            private readonly MainWindow _window;

            public BookmarkNavigationHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsWebDavMode => _window._isWebDavMode;
            public string? CurrentWebDavServerName => _window._webDavService.CurrentServer?.ServerName;
            public IReadOnlyList<ImageEntry> ImageEntries => _window._imageEntries;

            public int CurrentImageIndex
            {
                get => _window._currentIndex;
                set => _window._currentIndex = value;
            }

            public Task ConnectToWebDavServerAsync(string serverName, bool loadRoot) =>
                _window.ConnectToWebDavServerAsync(serverName, loadRoot);

            public Task LoadWebDavFolderAsync(string remotePath) =>
                _window.LoadWebDavFolderAsync(remotePath);

            public Task OpenWebDavFileAsync(FileItem item) =>
                _window.OpenWebDavFileAsync(item);

            public Task OpenWebDavArchiveAsync(FileItem item) =>
                _window.OpenWebDavArchiveAsync(item);

            public void LoadExplorerFolder(string path) =>
                _window.LoadExplorerFolder(path);

            public Task LoadImagesFromPdfAsync(string path) =>
                _window.LoadImagesFromPdfAsync(path);

            public Task LoadImageFromFileAsync(StorageFile file) =>
                _window.LoadImageFromFileAsync(file);

            public Task LoadImagesFromArchiveAsync(string path) =>
                _window.LoadImagesFromArchiveAsync(path);

            public Task DisplayCurrentImageAsync() =>
                _window.DisplayCurrentImageAsync();

            public void SetPendingEpubPosition(
                int chapterIndex,
                int pageIndex,
                int blockIndex,
                int savedLine)
            {
                _window.PendingEpubChapterIndex = chapterIndex;
                _window.PendingEpubPageIndex = pageIndex;
                _window._pendingEpubStartBlockIndex = blockIndex;
                _window._aozoraPendingTargetLine = savedLine > 1 ? savedLine : 1;
            }

            public void SetPendingTextPosition(int savedLine, int savedPage)
            {
                _window._aozoraPendingTargetLine = savedLine > 1 ? savedLine : (savedPage > 0 ? -savedPage : 1);
            }

            public void SetPendingPdfPage(int pageIndex)
            {
                _window._pendingPdfPageIndex = pageIndex;
            }

            public async Task RestoreTextScrollOffsetAsync(double scrollOffset)
            {
                if (_window.TextScrollViewer == null)
                {
                    return;
                }

                await Task.Delay(100);
                _window.TextScrollViewer.ChangeView(null, scrollOffset, null);
                _window.UpdateTextStatusBar();
            }

            public void NotifyFileNotFound()
            {
                _window.ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
            }

            public void NotifyWebDavFavoriteOpenFailed(string message)
            {
                _window.ShowNotification($"WebDAV 즐겨찾기 열기 실패: {message}");
            }
        }
    }
}
