using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class FavoritesController
    {
        private readonly FavoritesService _favoritesService;
        private readonly BookmarkPanelController _panelController;

        public FavoritesController(
            FavoritesService favoritesService,
            BookmarkPanelController panelController)
        {
            _favoritesService = favoritesService;
            _panelController = panelController;
        }

        public void RefreshFavorites()
        {
            _panelController.RefreshFavorites();
        }

        public async Task<FavoriteSaveResult?> AddCurrentAsync(
            FavoriteCaptureContext context,
            bool isManualSave = true)
        {
            Debug.WriteLine("=== FavoritesController.AddCurrentAsync called ===");
            Debug.WriteLine($"CurrentArchivePath: {context.CurrentArchivePath}");
            Debug.WriteLine($"CurrentExplorerPath: {context.CurrentExplorerPath}");
            Debug.WriteLine($"CurrentIndex: {context.CurrentIndex}");
            Debug.WriteLine($"ImageEntries.Count: {context.ImageEntries.Count}");

            FavoriteItem? favorite = await CreateFavoriteAsync(context);
            if (favorite == null)
            {
                Debug.WriteLine("Cannot add favorite - missing name or path");
                return null;
            }

            FavoriteSaveResult saveResult = await _favoritesService.AddOrUpdateFavoriteAsync(favorite, isManualSave);
            Debug.WriteLine(saveResult == FavoriteSaveResult.Added
                ? $"Added favorite: {favorite.Name}"
                : $"Updated favorite: {favorite.Name}");

            RefreshFavorites();
            return saveResult;
        }

        public async Task RemoveAsync(FavoriteItem favorite)
        {
            Debug.WriteLine($"Removing favorite: {favorite.Name}");
            await _favoritesService.RemoveFavoriteAsync(favorite);
            RefreshFavorites();
            Debug.WriteLine("Favorite removed successfully");
        }

        public async Task TogglePinAsync(FavoriteItem favorite)
        {
            await _favoritesService.TogglePinAsync(favorite);
            RefreshFavorites();
        }

        public async Task NavigateAsync(FavoriteItem favorite, IBookmarkNavigationHost host)
        {
            if (favorite.IsWebDav && !string.IsNullOrEmpty(favorite.WebDavServerName))
            {
                await NavigateToWebDavFavoriteAsync(favorite, host);
                return;
            }

            await NavigateToLocalFavoriteAsync(favorite, host);
        }

        private async Task<FavoriteItem?> CreateFavoriteAsync(FavoriteCaptureContext context)
        {
            string name = "";
            string path = "";
            string type = "";
            string? archiveEntryKey = null;

            if (context.IsTextMode && !string.IsNullOrEmpty(context.CurrentTextFilePath))
            {
                name = Path.GetFileName(context.CurrentTextFilePath);
                path = context.CurrentTextFilePath;
                type = "File";
                if (!context.IsWebDavMode)
                {
                    await AddFolderFavoriteAsync(Path.GetDirectoryName(path), false, null);
                }
            }
            else if (context.IsEpubMode && !string.IsNullOrEmpty(context.CurrentEpubFilePath))
            {
                name = Path.GetFileName(context.CurrentEpubFilePath);
                path = context.CurrentEpubFilePath;
                type = "File";
                if (!context.IsWebDavMode)
                {
                    await AddFolderFavoriteAsync(Path.GetDirectoryName(path), false, null);
                }
            }
            else if (context.HasArchive && !string.IsNullOrEmpty(context.CurrentArchivePath))
            {
                if (TryGetCurrentEntry(context, out var currentEntry))
                {
                    name = $"{Path.GetFileName(context.CurrentArchivePath)} - {currentEntry.DisplayName}";
                    path = context.CurrentArchivePath;
                    type = "Archive";
                    archiveEntryKey = currentEntry.ArchiveEntryKey;
                    if (!context.IsWebDavMode)
                    {
                        await AddFolderFavoriteAsync(Path.GetDirectoryName(context.CurrentArchivePath), false, null);
                    }
                }
            }
            else if (TryGetCurrentEntry(context, out var currentEntry) && context.HasVisibleContent)
            {
                if (!string.IsNullOrEmpty(currentEntry.FilePath))
                {
                    name = currentEntry.DisplayName;
                    path = currentEntry.FilePath;
                    type = "File";
                    if (!context.IsWebDavMode)
                    {
                        await AddFolderFavoriteAsync(Path.GetDirectoryName(path), false, null);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(context.CurrentExplorerPath) ||
                     (context.IsWebDavMode && !string.IsNullOrEmpty(context.CurrentWebDavPath)))
            {
                if (!string.IsNullOrEmpty(context.CurrentExplorerPath))
                {
                    name = Path.GetFileName(context.CurrentExplorerPath);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = context.CurrentExplorerPath;
                    }

                    path = context.CurrentExplorerPath;
                    type = "Folder";
                }
                else
                {
                    type = "Folder";
                    path = context.CurrentWebDavPath!;
                    name = Path.GetFileName(path.TrimEnd('/')) ?? "";
                }
            }

            bool isWebDav = false;
            string? webDavServerName = null;
            if (context.IsWebDavMode && !string.IsNullOrEmpty(context.WebDavServerName))
            {
                isWebDav = true;
                webDavServerName = context.WebDavServerName;

                if (type == "Archive")
                {
                    if (path.StartsWith("WebDAV:", StringComparison.OrdinalIgnoreCase))
                    {
                        path = path.Substring("WebDAV:".Length);
                    }

                    if (!string.IsNullOrEmpty(context.CurrentWebDavItemPath))
                    {
                        path = context.CurrentWebDavItemPath;
                        string archiveName = Path.GetFileName(context.CurrentWebDavItemPath);
                        if (TryGetCurrentEntry(context, out var entry))
                        {
                            name = $"{archiveName} - {entry.DisplayName}";
                        }
                    }

                    await AddFolderFavoriteAsync(context.CurrentWebDavPath, true, webDavServerName);
                }
                else if (type == "File")
                {
                    if (!string.IsNullOrEmpty(context.CurrentWebDavItemPath))
                    {
                        path = context.CurrentWebDavItemPath;
                        name = Path.GetFileName(context.CurrentWebDavItemPath);
                    }

                    await AddFolderFavoriteAsync(context.CurrentWebDavPath, true, webDavServerName);
                }
                else if (type == "Folder" && !string.IsNullOrEmpty(context.CurrentWebDavPath))
                {
                    path = context.CurrentWebDavPath;
                    name = Path.GetFileName(path.TrimEnd('/'));
                    if (string.IsNullOrEmpty(name))
                    {
                        name = webDavServerName;
                    }
                }
            }

            Debug.WriteLine($"Final favorite values - Name: '{name}', Path: '{path}', Type: '{type}'");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var position = CapturePosition(context);
            return new FavoriteItem
            {
                Name = name,
                Path = path,
                Type = type,
                ArchiveEntryKey = archiveEntryKey,
                ScrollOffset = context.TextScrollOffset,
                SavedPage = position.SavedPage,
                SavedLine = position.SavedLine,
                SavedBlockIndex = position.SavedBlockIndex,
                ChapterIndex = position.ChapterIndex,
                IsWebDav = isWebDav,
                WebDavServerName = webDavServerName,
                IsVertical = context.IsVerticalMode,
                Progress = Math.Max(position.Progress, 0),
                IsPinned = false
            };
        }

        private async Task AddFolderFavoriteAsync(
            string? folderPath,
            bool isWebDav,
            string? webDavServerName)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            string folderName;
            if (isWebDav)
            {
                folderName = Path.GetFileName(folderPath.TrimEnd('/'));
                if (string.IsNullOrEmpty(folderName))
                {
                    folderName = webDavServerName ?? "WebDAV";
                }
            }
            else
            {
                if (!Directory.Exists(folderPath))
                {
                    return;
                }

                folderName = Path.GetFileName(folderPath);
                if (string.IsNullOrEmpty(folderName))
                {
                    folderName = folderPath;
                }
            }

            if (_favoritesService.AnyFolderFavoriteExists(folderPath, isWebDav, webDavServerName))
            {
                return;
            }

            var folderFavorite = new FavoriteItem
            {
                Name = folderName,
                Path = folderPath,
                Type = "Folder",
                IsWebDav = isWebDav,
                WebDavServerName = webDavServerName
            };

            await _favoritesService.AddOrUpdateFavoriteAsync(folderFavorite, true);
        }

        private static FavoritePosition CapturePosition(FavoriteCaptureContext context)
        {
            int savedPage = 0;
            int savedLine = 1;
            int savedBlockIndex = -1;
            int chapterIndex = context.IsEpubMode ? context.CurrentEpubChapterIndex : 0;

            if (context.IsEpubMode)
            {
                savedPage = context.CurrentEpubPageIndex;
                savedBlockIndex = context.CurrentEpubPage?.StartBlockIndex ?? -1;

                if (context.IsVerticalMode)
                {
                    savedLine = context.CurrentEpubPage?.StartLine ?? 1;
                    savedPage = 0;
                }
                else if (context.CurrentEpubPageIndex >= 0 &&
                         context.CurrentEpubPageIndex < context.EpubPages.Count)
                {
                    var page = context.EpubPages[context.CurrentEpubPageIndex];
                    if (!page.IsImagePage)
                    {
                        savedLine = page.StartLine;
                    }
                    else
                    {
                        var targetBlock = context.AozoraBlocks.FirstOrDefault(
                            b => b.Inlines.OfType<AozoraImage>().Any(img => img.Source == page.ImagePath));
                        savedLine = targetBlock?.SourceLineNumber ?? 1;
                    }
                }
            }
            else if (context.IsTextMode)
            {
                if (context.IsVerticalMode)
                {
                    savedLine = context.CurrentVerticalStartLine;
                }
                else if (context.IsAozoraMode &&
                         context.AozoraBlocks.Count > 0 &&
                         context.CurrentAozoraStartBlockIndex >= 0 &&
                         context.CurrentAozoraStartBlockIndex < context.AozoraBlocks.Count)
                {
                    savedLine = context.AozoraBlocks[context.CurrentAozoraStartBlockIndex].SourceLineNumber;
                }
                else
                {
                    savedLine = context.TopVisibleLineIndex;
                }
            }
            else if (context.HasPdfDocument || context.HasArchive)
            {
                savedPage = context.CurrentIndex;
            }

            double progress = CalculateProgress(context, savedPage, savedLine, chapterIndex);
            return new FavoritePosition(savedPage, savedLine, savedBlockIndex, chapterIndex, progress);
        }

        private static double CalculateProgress(
            FavoriteCaptureContext context,
            int savedPage,
            int savedLine,
            int chapterIndex)
        {
            if (context.IsEpubMode && context.EpubSpineCount > 0)
            {
                int totalPages = context.EpubPageCount > 0 ? context.EpubPageCount : 1;
                double chapterProgress = (double)chapterIndex / context.EpubSpineCount;
                double pageProgress = (double)savedPage / totalPages / context.EpubSpineCount;
                return Math.Min((chapterProgress + pageProgress) * 100.0, 100);
            }

            if (context.IsTextMode)
            {
                int totalLines = context.TextTotalLineCountInSource > 0
                    ? context.TextTotalLineCountInSource
                    : context.AozoraTotalLineCountInSource;
                if (totalLines > 0)
                {
                    return Math.Min((double)savedLine / totalLines * 100.0, 100);
                }
            }
            else if (context.ImageEntries.Count > 0 && context.CurrentIndex >= 0)
            {
                return Math.Min((double)(context.CurrentIndex + 1) / context.ImageEntries.Count * 100.0, 100);
            }

            return 0;
        }

        private async Task NavigateToWebDavFavoriteAsync(FavoriteItem favorite, IBookmarkNavigationHost host)
        {
            try
            {
                if (!host.IsWebDavMode ||
                    !string.Equals(host.CurrentWebDavServerName, favorite.WebDavServerName, StringComparison.OrdinalIgnoreCase))
                {
                    await host.ConnectToWebDavServerAsync(favorite.WebDavServerName!, true);
                    if (!host.IsWebDavMode)
                    {
                        return;
                    }
                }

                var fileItem = CreateWebDavFileItem(favorite);
                if (favorite.Type != "Folder")
                {
                    await host.LoadWebDavFolderAsync(GetWebDavParentPath(favorite.Path));
                }

                if (favorite.Type == "Folder")
                {
                    await host.LoadWebDavFolderAsync(favorite.Path);
                }
                else if (favorite.Type == "Archive")
                {
                    if (Path.GetExtension(favorite.Path).Equals(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        await host.OpenWebDavFileAsync(fileItem);
                    }
                    else
                    {
                        await host.OpenWebDavArchiveAsync(fileItem);
                    }

                    await RestoreArchivePositionAsync(favorite, host);
                }
                else
                {
                    ApplyPendingPosition(favorite, fileItem, host);
                    await host.OpenWebDavFileAsync(fileItem);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening WebDAV favorite: {ex.Message}");
                host.NotifyWebDavFavoriteOpenFailed(ex.Message);
            }
        }

        private static async Task NavigateToLocalFavoriteAsync(FavoriteItem favorite, IBookmarkNavigationHost host)
        {
            switch (favorite.Type)
            {
                case "Folder":
                    if (Directory.Exists(favorite.Path))
                    {
                        host.LoadExplorerFolder(favorite.Path);
                    }
                    else
                    {
                        host.NotifyFileNotFound();
                    }
                    break;

                case "File":
                    if (File.Exists(favorite.Path))
                    {
                        ApplyPendingPosition(favorite, null, host);
                        string? parentDir = Path.GetDirectoryName(favorite.Path);
                        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                        {
                            host.LoadExplorerFolder(parentDir);
                        }

                        var file = await StorageFile.GetFileFromPathAsync(favorite.Path);
                        if (favorite.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            await host.LoadImagesFromPdfAsync(favorite.Path);
                        }
                        else
                        {
                            await host.LoadImageFromFileAsync(file);
                        }

                        if (!favorite.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) &&
                            favorite.ScrollOffset.HasValue &&
                            favorite.SavedLine <= 1)
                        {
                            await host.RestoreTextScrollOffsetAsync(favorite.ScrollOffset.Value);
                        }
                    }
                    else
                    {
                        host.NotifyFileNotFound();
                    }
                    break;

                case "Archive":
                    if (File.Exists(favorite.Path))
                    {
                        string? archiveFolder = Path.GetDirectoryName(favorite.Path);
                        if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                        {
                            host.LoadExplorerFolder(archiveFolder);
                            await RestoreLocalArchiveFavoriteAsync(favorite, host);
                        }
                    }
                    break;
            }
        }

        private static async Task RestoreLocalArchiveFavoriteAsync(
            FavoriteItem favorite,
            IBookmarkNavigationHost host)
        {
            if (string.IsNullOrEmpty(favorite.ArchiveEntryKey))
            {
                return;
            }

            await host.LoadImagesFromArchiveAsync(favorite.Path);
            await RestoreArchivePositionAsync(favorite, host);
        }

        private static async Task RestoreArchivePositionAsync(FavoriteItem favorite, IBookmarkNavigationHost host)
        {
            if (string.IsNullOrEmpty(favorite.ArchiveEntryKey))
            {
                return;
            }

            int entryIndex = FindEntryIndex(host.ImageEntries, e => e.ArchiveEntryKey == favorite.ArchiveEntryKey);
            if (entryIndex < 0)
            {
                return;
            }

            host.CurrentImageIndex = entryIndex;
            await host.DisplayCurrentImageAsync();
            if (favorite.ScrollOffset.HasValue)
            {
                await host.RestoreTextScrollOffsetAsync(favorite.ScrollOffset.Value);
            }
        }

        private static void ApplyPendingPosition(
            FavoriteItem favorite,
            FileItem? fileItem,
            IBookmarkNavigationHost host)
        {
            var favoriteKind = FileExplorerService.GetSupportedFileKind(favorite.Path);
            bool isEpub = fileItem?.IsEpub == true || favoriteKind == SupportedFileKind.Epub;
            bool isText = fileItem?.IsText == true || favoriteKind == SupportedFileKind.Text;
            bool isPdf = fileItem?.IsPdf == true || favoriteKind == SupportedFileKind.Pdf;

            if (isEpub)
            {
                host.SetPendingEpubPosition(
                    favorite.ChapterIndex,
                    favorite.SavedPage,
                    favorite.SavedBlockIndex,
                    favorite.SavedLine > 1 ? favorite.SavedLine : 1);
            }
            else if (isText)
            {
                host.SetPendingTextPosition(favorite.SavedLine, favorite.SavedPage);
            }

            if (isPdf)
            {
                host.SetPendingPdfPage(favorite.SavedPage);
            }
        }

        private static FileItem CreateWebDavFileItem(FavoriteItem favorite)
        {
            var fileItem = new FileItem
            {
                Name = favorite.Name,
                WebDavPath = favorite.Path,
                IsWebDav = true,
                IsDirectory = favorite.Type == "Folder"
            };
            FileExplorerService.ApplyFileKind(fileItem, FileExplorerService.GetSupportedFileKind(favorite.Path));
            fileItem.IsArchive = fileItem.IsArchive || favorite.Type == "Archive";
            return fileItem;
        }

        private static string GetWebDavParentPath(string path)
        {
            string? parentPath = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(parentPath))
            {
                return "/";
            }

            if (!parentPath.StartsWith("/"))
            {
                parentPath = "/" + parentPath;
            }

            return parentPath;
        }

        private static bool TryGetCurrentEntry(
            FavoriteCaptureContext context,
            out ImageEntry currentEntry)
        {
            if (context.CurrentIndex >= 0 && context.CurrentIndex < context.ImageEntries.Count)
            {
                currentEntry = context.ImageEntries[context.CurrentIndex];
                return true;
            }

            currentEntry = default!;
            return false;
        }

        private static int FindEntryIndex(
            IReadOnlyList<ImageEntry> entries,
            Func<ImageEntry, bool> predicate)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (predicate(entries[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed record FavoritePosition(
            int SavedPage,
            int SavedLine,
            int SavedBlockIndex,
            int ChapterIndex,
            double Progress);
    }
}
