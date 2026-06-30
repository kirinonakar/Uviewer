using System;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageExplorerNavigationCoordinator
    {
        private readonly IImageExplorerNavigationHost _host;
        private readonly Func<Task> _displayCurrentImageAsync;

        public ImageExplorerNavigationCoordinator(
            IImageExplorerNavigationHost host,
            Func<Task> displayCurrentImageAsync)
        {
            _host = host;
            _displayCurrentImageAsync = displayCurrentImageAsync;
        }

        public void SyncSidebarSelection(ImageEntry entry)
        {
            try
            {
                if (_host.FileItems.Count == 0) return;

                FileItem? item = null;

                if (_host.IsWebDavMode && entry.IsWebDavEntry && !entry.IsArchiveEntry)
                {
                    item = _host.FileItems.FirstOrDefault(f => f.IsWebDav && f.WebDavPath == entry.WebDavPath);
                }
                else
                {
                    string targetPath = entry.IsArchiveEntry ? (_host.ArchiveSession.CurrentPath ?? "") : (entry.FilePath ?? "");
                    if (string.IsNullOrEmpty(targetPath)) return;

                    item = _host.FileItems.FirstOrDefault(f =>
                        f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase) ||
                        (f.IsWebDav && targetPath.Equals($"WebDAV:{f.WebDavPath}", StringComparison.OrdinalIgnoreCase)));
                }

                if (item == null) return;

                SyncExplorerSelection(item);
            }
            catch { }
        }

        public string? GetCurrentNavigatingPath()
        {
            if (_host.IsWebDavMode) return _host.CurrentWebDavItemPath;
            if (_host.ArchiveSession.HasArchive && !string.IsNullOrEmpty(_host.ArchiveSession.CurrentPath)) return _host.ArchiveSession.CurrentPath;
            if (_host.IsEpubMode && !string.IsNullOrEmpty(_host.CurrentEpubFilePath)) return _host.CurrentEpubFilePath;
            if (_host.IsTextMode && !string.IsNullOrEmpty(_host.CurrentTextFilePath)) return _host.CurrentTextFilePath;
            if (_host.ImageEntries.Count > 0 && _host.CurrentIndex >= 0 && _host.CurrentIndex < _host.ImageEntries.Count)
            {
                return _host.ImageEntries[_host.CurrentIndex].FilePath;
            }

            return null;
        }

        public async Task NavigateToFileAsync(bool isNext)
        {
            await _host.AddToRecentAsync(true);
            string? currentPath = GetCurrentNavigatingPath();
            if (string.IsNullOrEmpty(currentPath)) return;

            var nextItem = FileExplorerService.GetNextNavigableFile(
                _host.FileItems,
                currentPath,
                isNext,
                _host.IsWebDavMode);

            if (nextItem != null)
            {
                if (_host.ImageEntries.Count > 0 && !nextItem.IsDirectory && !nextItem.IsArchive && !nextItem.IsPdf)
                {
                    int index = _host.ImageEntries.FindIndex(e => e.FilePath == nextItem.FullPath);
                    if (index != -1)
                    {
                        _host.CurrentIndex = index;
                        await _displayCurrentImageAsync();
                        SyncExplorerSelection(nextItem);
                        _host.FocusRoot();
                        return;
                    }
                }

                await _host.HandleFileSelectionAsync(nextItem);
                SyncExplorerSelection(nextItem);
            }

            _host.FocusRoot();
        }

        private void SyncExplorerSelection(FileItem item)
        {
            var list = _host.IsExplorerGrid ? _host.FileGridView : _host.FileListView;
            list.SelectedItem = item;
            list.ScrollIntoView(item);
        }
    }
}
