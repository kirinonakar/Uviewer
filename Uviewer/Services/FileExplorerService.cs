using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    public enum SupportedFileKind
    {
        Unsupported,
        Image,
        Text,
        Archive,
        Epub,
        Pdf
    }

    public static class FileExplorerService
    {
        public const string LocalRootPath = "/";

        public static string? GetParentFolderPath(string path)
        {
            if (path == LocalRootPath) return null;

            var normalizedPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(normalizedPath);
            if (string.Equals(
                normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                return LocalRootPath;
            }

            return Directory.GetParent(normalizedPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
        }

        #region Existing Extension Helpers
        public static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".jxl", ".ico", ".tiff", ".tif" };
        public static readonly string[] SupportedTextExtensions = { ".txt", ".log", ".json", ".toml", ".csv", ".html", ".htm", ".md", ".xml" };
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

        public static SupportedFileKind GetSupportedFileKind(string? pathOrExtension)
        {
            var extension = NormalizeExtension(pathOrExtension);

            if (SupportedImageExtensions.Contains(extension)) return SupportedFileKind.Image;
            if (SupportedTextExtensions.Contains(extension)) return SupportedFileKind.Text;
            if (SupportedArchiveExtensions.Contains(extension)) return SupportedFileKind.Archive;
            if (SupportedEpubExtensions.Contains(extension)) return SupportedFileKind.Epub;
            if (SupportedPdfExtensions.Contains(extension)) return SupportedFileKind.Pdf;

            return SupportedFileKind.Unsupported;
        }

        public static bool IsSupportedFile(string? pathOrExtension) =>
            GetSupportedFileKind(pathOrExtension) != SupportedFileKind.Unsupported;

        public static void ApplyFileKind(FileItem item, SupportedFileKind kind)
        {
            item.IsImage = kind == SupportedFileKind.Image;
            item.IsText = kind == SupportedFileKind.Text;
            item.IsArchive = kind == SupportedFileKind.Archive;
            item.IsEpub = kind == SupportedFileKind.Epub;
            item.IsPdf = kind == SupportedFileKind.Pdf;
        }

        public static bool IsTextEntry(ImageEntry entry) => GetSupportedFileKind(GetEntryExtension(entry)) == SupportedFileKind.Text;
        public static bool IsEpubEntry(ImageEntry entry) => GetSupportedFileKind(GetEntryExtension(entry)) == SupportedFileKind.Epub;
        public static bool IsPdfEntry(ImageEntry entry) => GetSupportedFileKind(GetEntryExtension(entry)) == SupportedFileKind.Pdf || (entry?.IsPdfEntry ?? false);
        public static bool IsImageEntry(ImageEntry entry) => GetSupportedFileKind(GetEntryExtension(entry)) == SupportedFileKind.Image;
        // Only pages from an opened PDF can use the image navigation pipeline.
        // A PDF file listed beside images must be opened through the document loader.
        public static bool IsNavigableImage(ImageEntry entry) => entry != null && (entry.IsPdfEntry || IsImageEntry(entry));
        
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
                if (path == LocalRootPath)
                {
                    return DriveInfo.GetDrives()
                        .OrderBy(drive => drive.Name, NaturalSortComparer.Default)
                        .Select(drive => new FileItem
                        {
                            Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            FullPath = drive.Name,
                            IsDirectory = true,
                            IsDrive = true
                        }).ToList();
                }

                var items = new List<FileItem>();
                var parentPath = GetParentFolderPath(path);
                
                if (parentPath != null)
                {
                    items.Add(new FileItem { Name = "..", FullPath = parentPath, IsDirectory = true, IsParentDirectory = true });
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
                    var kind = GetSupportedFileKind(file.Extension);
                    if (kind != SupportedFileKind.Unsupported)
                    {
                        var fileItem = new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false
                        };
                        ApplyFileKind(fileItem, kind);
                        items.Add(fileItem);
                    }
                }

                return items;
            });
        }
        #endregion
        #region Recursive Filter Search

        /// <summary>하위 폴더 검색 결과의 최대 개수 (과도한 탐색과 표시를 방지)</summary>
        private const int MaxDescendantResultCount = 1000;

        /// <summary>
        /// 현재 폴더의 하위 폴더를 재귀적으로 탐색해 필터와 이름이 일치하는 항목을 반환합니다.
        /// 숨김 폴더(".", 시작)와 재분석 지점(심볼릭 링크)은 건너뛰고, 접근할 수 없는 폴더는 무시합니다.
        /// </summary>
        public static Task<List<FileItem>> GetDescendantContentsAsync(
            string rootPath,
            string filterText,
            ExplorerFilterKind kind,
            ExplorerSortMode sortMode,
            CancellationToken token)
        {
            return Task.Run(() =>
            {
                var items = new List<FileItem>();
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return items;

                var pending = new Stack<string>();
                pending.Push(rootPath);

                while (pending.Count > 0 && items.Count < MaxDescendantResultCount)
                {
                    token.ThrowIfCancellationRequested();

                    var current = pending.Pop();
                    string[] dirs;
                    string[] files;
                    try
                    {
                        dirs = Directory.GetDirectories(current);
                        files = Directory.GetFiles(current);
                    }
                    catch (UnauthorizedAccessException) { continue; }
                    catch (IOException) { continue; }

                    foreach (var filePath in files)
                    {
                        token.ThrowIfCancellationRequested();
                        if (items.Count >= MaxDescendantResultCount) break;

                        var fileName = Path.GetFileName(filePath);
                        if (fileName.StartsWith(".")) continue;

                        var fileKind = GetSupportedFileKind(Path.GetExtension(filePath));
                        if (fileKind == SupportedFileKind.Unsupported) continue;

                        var fileItem = new FileItem { Name = fileName, FullPath = filePath, IsDirectory = false };
                        ApplyFileKind(fileItem, fileKind);
                        if (!MatchesFilter(fileItem, filterText, kind)) continue;

                        fileItem.DisplayPath = GetRelativeFolderPath(rootPath, filePath);
                        items.Add(fileItem);
                    }

                    foreach (var dirPath in dirs)
                    {
                        token.ThrowIfCancellationRequested();
                        if (items.Count >= MaxDescendantResultCount) break;

                        var dirName = Path.GetFileName(dirPath);
                        if (dirName.StartsWith(".") || IsReparsePoint(dirPath)) continue;

                        var dirItem = new FileItem { Name = dirName, FullPath = dirPath, IsDirectory = true };
                        if (MatchesFilter(dirItem, filterText, kind))
                        {
                            dirItem.DisplayPath = GetRelativeFolderPath(rootPath, dirPath);
                            items.Add(dirItem);
                        }

                        pending.Push(dirPath);
                    }
                }

                IEnumerable<FileItem> sorted = sortMode switch
                {
                    ExplorerSortMode.DateDesc => items.OrderByDescending(GetLastWriteTimeSafe),
                    ExplorerSortMode.DateAsc => items.OrderBy(GetLastWriteTimeSafe),
                    _ => items.OrderBy(i => i.Name, NaturalSortComparer.Default)
                };
                return sorted.ToList();
            }, token);
        }

        /// <summary>
        /// 항목이 필터(이름 일치 + 종류 일치)를 통과하는지 판단합니다.
        /// 폴더는 파일 종류 필터와 무관하게 계속 탐색할 수 있도록 남겨 둡니다.
        /// </summary>
        public static bool MatchesFilter(FileItem item, string filterText, ExplorerFilterKind kind)
        {
            if (item == null) return false;
            if (item.IsParentDirectory) return true;
            if (!item.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)) return false;

            // Keep matching folders available for navigation with any file-type filter.
            if (item.IsDirectory) return true;
            return kind switch
            {
                ExplorerFilterKind.All => true,
                ExplorerFilterKind.Image => item.IsImage,
                ExplorerFilterKind.Text => item.IsText,
                ExplorerFilterKind.Pdf => item.IsPdf,
                ExplorerFilterKind.Epub => item.IsEpub,
                ExplorerFilterKind.Archive => item.IsArchive,
                _ => false
            };
        }

        /// <summary>현재 폴더를 기준으로 항목이 들어 있는 폴더의 상대 경로를 반환합니다(예: 하위\깊은폴더).</summary>
        private static string GetRelativeFolderPath(string rootPath, string path)
        {
            try
            {
                var relative = Path.GetRelativePath(rootPath, path);
                if (relative.StartsWith("..", StringComparison.Ordinal)) return "";

                var folder = Path.GetDirectoryName(relative);
                return string.IsNullOrEmpty(folder) || folder == "." ? "" : folder;
            }
            catch
            {
                return "";
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return true;
            }
        }

        private static DateTime GetLastWriteTimeSafe(FileItem item)
        {
            try
            {
                return item.IsDirectory
                    ? Directory.GetLastWriteTime(item.FullPath)
                    : File.GetLastWriteTime(item.FullPath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
        #endregion

        #region Navigation Logic

        /// <summary>
        /// 파일 리스트 안에서 현재 파일 경로를 기준으로 이전/다음 탐색 가능한 파일을 찾습니다.
        /// </summary>
        public static FileItem? GetNextNavigableFile(
            IList<FileItem> fileItems,
            string currentPath,
            bool isNext,
            bool isWebDavMode = false,
            bool archivesOnly = false)
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
                if (archivesOnly && !item.IsArchive) continue;
                
                return item; // 찾음
            }
        }

        /// <summary>
        /// 뷰어 안에서 다음/이전 이미지 인덱스를 계산합니다. (두장 보기 스텝 지원)
        /// </summary>
        public static int GetNextImageIndex(IReadOnlyList<ImageEntry> entries, int currentIndex, int step, bool isNext)
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

        private static string NormalizeExtension(string? pathOrExtension)
        {
            if (string.IsNullOrWhiteSpace(pathOrExtension))
            {
                return string.Empty;
            }

            var value = pathOrExtension.Trim();
            bool looksLikeExtension = value.StartsWith('.') &&
                !value.Contains(Path.DirectorySeparatorChar) &&
                !value.Contains(Path.AltDirectorySeparatorChar);

            return (looksLikeExtension ? value : Path.GetExtension(value)).ToLowerInvariant();
        }
    }
}
