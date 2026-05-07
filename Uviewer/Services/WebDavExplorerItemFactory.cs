using System.Collections.Generic;
using System.IO;
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
            var ext = Path.GetExtension(name).ToLowerInvariant();

            var isImage = FileExplorerService.SupportedImageExtensions.Contains(ext);
            var isArchive = FileExplorerService.SupportedArchiveExtensions.Contains(ext);
            var isText = FileExplorerService.SupportedTextExtensions.Contains(ext);
            var isEpub = FileExplorerService.SupportedEpubExtensions.Contains(ext);
            var isPdf = FileExplorerService.SupportedPdfExtensions.Contains(ext);

            if (!item.IsDirectory && !isImage && !isArchive && !isText && !isEpub && !isPdf)
            {
                return null;
            }

            return new FileItem
            {
                Name = name,
                FullPath = item.FullPath,
                IsDirectory = item.IsDirectory,
                IsImage = isImage,
                IsArchive = isArchive,
                IsText = isText,
                IsEpub = isEpub,
                IsPdf = isPdf,
                IsWebDav = true,
                WebDavPath = item.FullPath
            };
        }
    }
}
