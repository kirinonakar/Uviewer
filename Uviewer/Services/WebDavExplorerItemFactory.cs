using System.Collections.Generic;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public static class WebDavExplorerItemFactory
    {
        public static List<FileItem> CreateFolderItems(
            string remotePath,
            IEnumerable<WebDavItem> items,
            ExplorerSortMode sortMode)
        {
            var fileItems = new List<FileItem>();
            var parentItem = CreateParentItem(remotePath);
            if (parentItem != null)
            {
                fileItems.Add(parentItem);
            }

            foreach (var item in SortItems(items, sortMode))
            {
                var fileItem = CreateFileItem(item);
                if (fileItem != null)
                {
                    fileItems.Add(fileItem);
                }
            }

            return fileItems;
        }

        public static FileItem? CreateParentItem(string remotePath)
        {
            if (remotePath == "/") return null;

            var parentPath = remotePath.TrimEnd('/');
            var lastSlash = parentPath.LastIndexOf('/');
            var parent = lastSlash > 0 ? parentPath.Substring(0, lastSlash + 1) : "/";

            return new FileItem
            {
                Name = "..",
                FullPath = parent,
                IsDirectory = true,
                IsParentDirectory = true,
                IsWebDav = true,
                WebDavPath = parent
            };
        }

        private static IEnumerable<WebDavItem> SortItems(IEnumerable<WebDavItem> items, ExplorerSortMode sortMode)
        {
            return sortMode switch
            {
                ExplorerSortMode.DateDesc => items
                    .OrderByDescending(i => i.IsDirectory)
                    .ThenByDescending(i => i.LastModified),
                ExplorerSortMode.DateAsc => items
                    .OrderByDescending(i => i.IsDirectory)
                    .ThenBy(i => i.LastModified),
                _ => items
            };
        }

        private static FileItem? CreateFileItem(WebDavItem item)
        {
            var name = item.Name;
            var kind = FileExplorerService.GetSupportedFileKind(name);

            if (!item.IsDirectory && kind == SupportedFileKind.Unsupported)
            {
                return null;
            }

            var fileItem = new FileItem
            {
                Name = name,
                FullPath = item.FullPath,
                IsDirectory = item.IsDirectory,
                IsWebDav = true,
                WebDavPath = item.FullPath
            };

            FileExplorerService.ApplyFileKind(fileItem, kind);
            return fileItem;
        }
    }
}
