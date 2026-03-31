using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public enum ExplorerSortMode
    {
        Name,
        DateDesc,
        DateAsc
    }

    public static class FileExplorerService
    {
        #region Existing Extension Helpers
        public static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".jxl", ".ico", ".tiff", ".tif" };
        public static readonly string[] SupportedTextExtensions = { ".txt", ".html", ".htm", ".md", ".xml" };
        public static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar", ".7z", ".tar", ".gz", ".cbz", ".cbr" };
        public static readonly string[] SupportedEpubExtensions = { ".epub" };
        public static readonly string[] SupportedPdfExtensions = { ".pdf" };

        public static IEnumerable<string> SupportedFileExtensions =>
            SupportedImageExtensions.Concat(SupportedTextExtensions)
                                    .Concat(SupportedArchiveExtensions)
                                    .Concat(SupportedEpubExtensions)
                                    .Concat(SupportedPdfExtensions);

        public static string? GetEntryExtension(ImageEntry entry)
        {
            if (entry == null) return null;
            if (entry.FilePath != null) return Path.GetExtension(entry.FilePath);
            if (entry.ArchiveEntryKey != null) return Path.GetExtension(entry.ArchiveEntryKey);
            if (entry.WebDavPath != null) return Path.GetExtension(entry.WebDavPath);
            return null;
        }

        public static bool IsTextEntry(ImageEntry entry) => !string.IsNullOrEmpty(GetEntryExtension(entry)) && SupportedTextExtensions.Contains(GetEntryExtension(entry)!.ToLowerInvariant());
        public static bool IsEpubEntry(ImageEntry entry) => !string.IsNullOrEmpty(GetEntryExtension(entry)) && SupportedEpubExtensions.Contains(GetEntryExtension(entry)!.ToLowerInvariant());
        public static bool IsPdfEntry(ImageEntry entry) => (!string.IsNullOrEmpty(GetEntryExtension(entry)) && SupportedPdfExtensions.Contains(GetEntryExtension(entry)!.ToLowerInvariant())) || (entry?.IsPdfEntry ?? false);
        public static bool IsImageEntry(ImageEntry entry) => !string.IsNullOrEmpty(GetEntryExtension(entry)) && SupportedImageExtensions.Contains(GetEntryExtension(entry)!.ToLowerInvariant());
        public static bool IsNavigableImage(ImageEntry entry) => entry != null && (IsPdfEntry(entry) || IsImageEntry(entry));
        
        public static string GetFormattedDisplayName(string displayName, bool isArchiveEntry, string? archivePath = null, string? webDavItemPath = null)
        {
            if (isArchiveEntry && !string.IsNullOrEmpty(archivePath))
            {
                if (archivePath.StartsWith("WebDAV:")) archivePath = archivePath.Substring("WebDAV:".Length);
                return $"{Path.GetFileName(archivePath)} - {displayName}";
            }

            if (!string.IsNullOrEmpty(webDavItemPath))
            {
                string realName = Path.GetFileName(webDavItemPath);
                if (!string.IsNullOrEmpty(displayName))
                {
                    int dashIndex = displayName.IndexOf(" - ");
                    return dashIndex > 0 ? realName + displayName.Substring(dashIndex) : realName;
                }
            }
            return displayName;
        }
        #endregion

        #region Folder Exploration Logic

        /// <summary>
        /// 특정 경로의 폴더를 읽어 지정된 정렬 방식으로 FileItem 리스트를 반환합니다.
        /// </summary>
        public static Task<List<FileItem>> GetFolderContentsAsync(string path, ExplorerSortMode sortMode)
        {
            return Task.Run(() =>
            {
                var items = new List<FileItem>();
                var parentDir = Directory.GetParent(path);
                
                if (parentDir != null)
                {
                    items.Add(new FileItem { Name = "..", FullPath = parentDir.FullName, IsDirectory = true, IsParentDirectory = true });
                }

                var di = new DirectoryInfo(path);
                var allDirs = di.GetDirectories();
                var allFiles = di.GetFiles();

                IEnumerable<DirectoryInfo> sortedDirs;
                IEnumerable<FileInfo> sortedFiles;

                switch (sortMode)
                {
                    case ExplorerSortMode.DateDesc:
                        sortedDirs = allDirs.OrderByDescending(d => d.LastWriteTime);
                        sortedFiles = allFiles.OrderByDescending(f => f.LastWriteTime);
                        break;
                    case ExplorerSortMode.DateAsc:
                        sortedDirs = allDirs.OrderBy(d => d.LastWriteTime);
                        sortedFiles = allFiles.OrderBy(f => f.LastWriteTime);
                        break;
                    default: // Name (Natural Sort)
                        sortedDirs = allDirs.OrderBy(d => d.Name, NaturalSortComparer.Default);
                        sortedFiles = allFiles.OrderBy(f => f.Name, NaturalSortComparer.Default);
                        break;
                }

                // Add directories (숨김 폴더 제외)
                items.AddRange(sortedDirs.Where(dir => !dir.Name.StartsWith("."))
                                         .Select(dir => new FileItem { Name = dir.Name, FullPath = dir.FullName, IsDirectory = true }));

                // Add supported files
                foreach (var file in sortedFiles)
                {
                    var ext = file.Extension.ToLowerInvariant();
                    var isImage = SupportedImageExtensions.Contains(ext);
                    var isArchive = SupportedArchiveExtensions.Contains(ext);
                    var isText = SupportedTextExtensions.Contains(ext);
                    var isEpub = SupportedEpubExtensions.Contains(ext);
                    var isPdf = SupportedPdfExtensions.Contains(ext);

                    if (isImage || isArchive || isText || isEpub || isPdf)
                    {
                        items.Add(new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            IsImage = isImage,
                            IsArchive = isArchive,
                            IsText = isText,
                            IsEpub = isEpub,
                            IsPdf = isPdf
                        });
                    }
                }

                return items;
            });
        }
        #endregion

        #region Navigation Logic

        /// <summary>
        /// 파일 리스트 안에서 현재 파일 경로를 기준으로 이전/다음 탐색 가능한 파일을 찾습니다.
        /// </summary>
        public static FileItem? GetNextNavigableFile(IList<FileItem> fileItems, string currentPath, bool isNext, bool isWebDavMode = false)
        {
            int currentItemIndex = -1;
            for (int i = 0; i < fileItems.Count; i++)
            {
                if (isWebDavMode && fileItems[i].WebDavPath == currentPath) { currentItemIndex = i; break; }
                else if (!isWebDavMode && fileItems[i].FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase)) { currentItemIndex = i; break; }
            }

            if (currentItemIndex == -1) return null;

            int newIndex = currentItemIndex;
            while (true)
            {
                newIndex = isNext ? newIndex + 1 : newIndex - 1;
                
                if (newIndex < 0 || newIndex >= fileItems.Count) return null; // 리스트 끝
                
                var item = fileItems[newIndex];
                if (item.IsDirectory || item.IsParentDirectory) continue; // 폴더 스킵
                
                return item; // 찾음
            }
        }

        /// <summary>
        /// 뷰어 안에서 다음/이전 이미지 인덱스를 계산합니다. (두장 보기 스텝 지원)
        /// </summary>
        public static int GetNextImageIndex(IList<ImageEntry> entries, int currentIndex, int step, bool isNext)
        {
            if (entries == null || entries.Count == 0) return currentIndex;

            int newIndex = currentIndex;
            for (int i = 0; i < step; i++)
            {
                int searchIdx = newIndex;
                if (isNext)
                {
                    while (searchIdx < entries.Count - 1)
                    {
                        searchIdx++;
                        if (IsNavigableImage(entries[searchIdx])) { newIndex = searchIdx; break; }
                    }
                }
                else
                {
                    while (searchIdx > 0)
                    {
                        searchIdx--;
                        if (IsNavigableImage(entries[searchIdx])) { newIndex = searchIdx; break; }
                    }
                }
            }
            return newIndex;
        }

        #endregion
    }
}
