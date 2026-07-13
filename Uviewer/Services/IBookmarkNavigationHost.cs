using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal interface IBookmarkNavigationHost
    {
        bool IsWebDavMode { get; }
        bool IsNavigatingRecent { get; set; }
        string? CurrentWebDavServerName { get; }
        IReadOnlyList<ImageEntry> ImageEntries { get; }
        int CurrentImageIndex { get; set; }

        Task ConnectToWebDavServerAsync(string serverName, bool loadRoot);
        Task LoadWebDavFolderAsync(string remotePath);
        Task OpenWebDavFileAsync(FileItem item);
        Task OpenWebDavArchiveAsync(FileItem item);

        void LoadExplorerFolder(string path);
        void SelectExplorerItemByName(string fileName);
        Task LoadImagesFromPdfAsync(string path);
        Task LoadImageFromFileAsync(StorageFile file);
        Task LoadImagesFromArchiveAsync(string path);
        Task DisplayCurrentImageAsync();

        void SetPendingEpubPosition(int chapterIndex, int pageIndex, int blockIndex, int savedLine);
        void SetPendingTextPosition(int savedLine, int savedPage, int blockIndex);
        void SetPendingPdfPage(int pageIndex);
        Task RestoreTextScrollOffsetAsync(double scrollOffset);

        void NotifyFileNotFound();
        void NotifyWebDavFavoriteOpenFailed(string message);
        void NotifyWebDavRecentOpenFailed(string message);
    }
}
