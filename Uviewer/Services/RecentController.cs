using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class RecentController
    {
        private readonly RecentService _recentService;
        private readonly BookmarkPanelController _panelController;

        public RecentController(
            RecentService recentService,
            BookmarkPanelController panelController)
        {
            _recentService = recentService;
            _panelController = panelController;
        }

        public void RefreshRecent()
        {
            _panelController.RefreshRecent();
        }

        public async Task AddCurrentAsync(
            RecentCaptureContext context,
            bool saveCurrentPosition = false)
        {
            try
            {
                if (context.IsNavigatingRecent)
                {
                    Debug.WriteLine("Skipping AddToRecentAsync during navigation.");
                    return;
                }

                var newItem = CreateCurrentRecentItem(context, saveCurrentPosition);
                if (newItem == null) return;

                await _recentService.AddToRecentAsync(newItem);
                RefreshRecent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding to recent: {ex.Message}");
            }
        }

        public async Task RemoveAsync(RecentItem recent)
        {
            try
            {
                Debug.WriteLine($"Removing recent item: {recent.Name}");
                await _recentService.RemoveRecentAsync(recent);
                RefreshRecent();
                Debug.WriteLine("Recent item removed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing recent item: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task NavigateAsync(
            RecentItem recent,
            IBookmarkNavigationHost host,
            Func<RecentCaptureContext> captureContext)
        {
            var target = RecentNavigationSnapshot.From(recent);

            try
            {
                await AddCurrentAsync(captureContext(), saveCurrentPosition: true);
                host.IsNavigatingRecent = true;

                if (target.IsWebDav && !string.IsNullOrEmpty(target.WebDavServerName))
                {
                    try
                    {
                        await NavigateToWebDavRecentAsync(target, host);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error opening WebDAV recent: {ex.Message}");
                        host.NotifyWebDavRecentOpenFailed(ex.Message);
                        return;
                    }
                }

                await NavigateToLocalRecentAsync(target, host);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to recent: {ex.Message}");
            }
            finally
            {
                host.IsNavigatingRecent = false;
            }

            await AddCurrentAsync(captureContext(), saveCurrentPosition: false);
        }

        private RecentItem? CreateCurrentRecentItem(
            RecentCaptureContext context,
            bool saveCurrentPosition)
        {
            string name = "";
            string path = "";
            string type = "";

            if (context.IsTextMode && !string.IsNullOrEmpty(context.CurrentTextFilePath))
            {
                name = Path.GetFileName(context.CurrentTextFilePath);
                path = context.CurrentTextFilePath;
                type = "File";
            }
            else if (context.IsEpubMode && !string.IsNullOrEmpty(context.CurrentEpubFilePath))
            {
                name = Path.GetFileName(context.CurrentEpubFilePath);
                path = context.CurrentEpubFilePath;
                type = "File";
            }
            else if (context.HasArchive && !string.IsNullOrEmpty(context.CurrentArchivePath))
            {
                path = context.CurrentArchivePath!;
                type = "Archive";
                if (TryGetCurrentEntry(context, out var entry))
                {
                    name = $"{Path.GetFileName(path)} - {entry.DisplayName}";
                }
                else
                {
                    name = Path.GetFileName(path);
                }
            }
            else if (TryGetCurrentEntry(context, out var currentEntry) &&
                     !string.IsNullOrEmpty(currentEntry.FilePath))
            {
                name = currentEntry.DisplayName;
                path = currentEntry.FilePath;
                type = "File";
            }
            else if (!string.IsNullOrEmpty(context.CurrentExplorerPath))
            {
                name = Path.GetFileName(context.CurrentExplorerPath);
                if (string.IsNullOrEmpty(name)) name = context.CurrentExplorerPath;
                path = context.CurrentExplorerPath;
                type = "Folder";
            }
            else if (context.IsWebDavMode && !string.IsNullOrEmpty(context.CurrentWebDavPath))
            {
                path = context.CurrentWebDavPath;
                name = Path.GetFileName(path.TrimEnd('/'));
                if (string.IsNullOrEmpty(name)) name = context.WebDavServerName ?? "WebDAV";
                type = "Folder";
            }

            if (string.IsNullOrEmpty(path)) return null;

            string? webDavServerName = null;
            bool isWebDav = false;

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

                        string originalArchiveName = Path.GetFileName(context.CurrentWebDavItemPath);
                        if (TryGetCurrentEntry(context, out var entry))
                        {
                            name = $"{originalArchiveName} - {entry.DisplayName}";
                        }
                    }
                }
                else if (type == "File")
                {
                    if (!string.IsNullOrEmpty(context.CurrentWebDavItemPath))
                    {
                        path = context.CurrentWebDavItemPath;
                        name = Path.GetFileName(context.CurrentWebDavItemPath);
                    }
                }
                else if (type == "Folder")
                {
                    if (!string.IsNullOrEmpty(context.CurrentWebDavPath))
                    {
                        path = context.CurrentWebDavPath;
                        name = Path.GetFileName(path.TrimEnd('/'));
                        if (string.IsNullOrEmpty(name)) name = context.WebDavServerName;
                    }
                }
            }

            RecentItem? existing = _recentService.FindExisting(path, type);

            double? targetOffset = existing?.ScrollOffset;
            int targetPage = existing?.SavedPage ?? 0;
            int targetChapter = existing?.ChapterIndex ?? 0;
            int targetLine = existing?.SavedLine ?? 1;
            int targetBlockIndex = existing?.SavedBlockIndex ?? -1;
            double targetProgress = existing?.Progress ?? 0;
            string? targetArchiveKey = existing?.ArchiveEntryKey;

            if (saveCurrentPosition)
            {
                targetProgress = CalculateCurrentProgress(context);

                if (context.IsEpubMode)
                {
                    ApplyEpubPosition(
                        context,
                        existing,
                        ref targetPage,
                        ref targetChapter,
                        ref targetLine,
                        ref targetBlockIndex,
                        ref targetProgress);
                }
                else if (context.IsTextMode)
                {
                    ApplyTextPosition(
                        context,
                        existing,
                        ref targetOffset,
                        ref targetLine,
                        ref targetProgress);
                }
                else if (context.HasPdfDocument)
                {
                    targetPage = context.CurrentIndex;
                }
                else if (type == "Archive" && TryGetCurrentEntry(context, out var entry))
                {
                    targetArchiveKey = entry.ArchiveEntryKey;
                    name = $"{Path.GetFileName(path)} - {entry.DisplayName}";
                    targetPage = context.CurrentIndex;
                }
            }

            return new RecentItem
            {
                Name = name,
                Path = path,
                Type = type,
                ArchiveEntryKey = targetArchiveKey,
                AccessedAt = DateTime.Now,
                ScrollOffset = targetOffset,
                SavedPage = targetPage,
                ChapterIndex = targetChapter,
                SavedLine = targetLine,
                SavedBlockIndex = targetBlockIndex,
                IsWebDav = isWebDav,
                WebDavServerName = webDavServerName,
                IsVertical = context.IsVerticalMode,
                Progress = saveCurrentPosition ? targetProgress : existing?.Progress ?? 0
            };
        }

        private static void ApplyEpubPosition(
            RecentCaptureContext context,
            RecentItem? existing,
            ref int targetPage,
            ref int targetChapter,
            ref int targetLine,
            ref int targetBlockIndex,
            ref double targetProgress)
        {
            bool isResetState = context.CurrentEpubPageIndex == 0 &&
                context.CurrentEpubChapterIndex == 0 &&
                (context.CurrentEpubPage?.StartBlockIndex ?? 0) == 0;
            bool hasExistingProgress = existing != null &&
                (existing.SavedPage > 0 ||
                 existing.ChapterIndex > 0 ||
                 existing.SavedBlockIndex > 0 ||
                 existing.SavedLine > 1);

            if (isResetState && existing != null && hasExistingProgress)
            {
                targetPage = existing.SavedPage;
                targetChapter = existing.ChapterIndex;
                targetLine = existing.SavedLine;
                targetBlockIndex = existing.SavedBlockIndex;
                targetProgress = existing.Progress;
                Debug.WriteLine($"[SafeGuard] Epub reset state detected. Keeping previous position: Ch.{targetChapter} P.{targetPage} Block.{targetBlockIndex}");
                return;
            }

            targetPage = context.CurrentEpubPageIndex;
            targetChapter = context.CurrentEpubChapterIndex;
            targetBlockIndex = context.CurrentEpubPage?.StartBlockIndex ?? -1;

            if (context.IsVerticalMode)
            {
                targetLine = context.CurrentEpubPage?.StartLine ?? 1;
                targetPage = 0;
            }
            else if (targetPage >= 0 && targetPage < context.EpubPages.Count)
            {
                var page = context.EpubPages[targetPage];
                if (!page.IsImagePage)
                {
                    targetLine = page.StartLine;
                }
                else
                {
                    var targetBlock = context.AozoraBlocks.FirstOrDefault(
                        b => b.Inlines.OfType<AozoraImage>().Any(img => img.Source == page.ImagePath));
                    targetLine = targetBlock?.SourceLineNumber ?? 1;
                }
            }
        }

        private static void ApplyTextPosition(
            RecentCaptureContext context,
            RecentItem? existing,
            ref double? targetOffset,
            ref int targetLine,
            ref double targetProgress)
        {
            int currentLine = 1;
            double? currentOffset = 0;
            bool hasViewerContent = false;

            if (context.IsVerticalMode)
            {
                currentLine = context.CurrentVerticalStartLine;
                currentOffset = 0;
                hasViewerContent = context.CurrentVerticalHasContent;
            }
            else if (context.IsAozoraMode)
            {
                if (context.AozoraBlocks.Count > 0 &&
                    context.CurrentAozoraStartBlockIndex >= 0 &&
                    context.CurrentAozoraStartBlockIndex < context.AozoraBlocks.Count)
                {
                    currentLine = context.AozoraBlocks[context.CurrentAozoraStartBlockIndex].SourceLineNumber;
                    hasViewerContent = true;
                }
                currentOffset = 0;
            }
            else
            {
                currentOffset = context.TextScrollOffset;
                currentLine = context.TopVisibleLineIndex;
                hasViewerContent = context.TextLineCount > 0;
            }

            bool isResetState = currentLine <= 1 && (currentOffset == 0 || currentOffset == null);
            if (isResetState && existing != null && (existing.SavedLine > 1 || (existing.ScrollOffset ?? 0) > 0))
            {
                if (!hasViewerContent || context.LastRecentSaveLine == -1)
                {
                    targetLine = existing.SavedLine;
                    targetOffset = existing.ScrollOffset;
                    targetProgress = existing.Progress;
                    Debug.WriteLine($"[SafeGuard] Text loading/restoring state. Preserving previous: Line {targetLine}");
                    return;
                }
            }

            targetLine = currentLine;
            targetOffset = currentOffset;
        }

        private async Task NavigateToWebDavRecentAsync(
            RecentNavigationSnapshot target,
            IBookmarkNavigationHost host)
        {
            if (!host.IsWebDavMode ||
                !string.Equals(host.CurrentWebDavServerName, target.WebDavServerName, StringComparison.OrdinalIgnoreCase))
            {
                await host.ConnectToWebDavServerAsync(target.WebDavServerName!, false);
                if (!host.IsWebDavMode) return;
            }

            var fileItem = CreateWebDavFileItem(target);
            string extension = Path.GetExtension(target.Path).ToLowerInvariant();

            if (target.Type != "Folder")
            {
                await host.LoadWebDavFolderAsync(GetWebDavParentPath(target.Path));
            }

            if (target.Type == "Folder")
            {
                await host.LoadWebDavFolderAsync(target.Path);
            }
            else if (target.Type == "Archive")
            {
                if (extension == ".7z")
                {
                    await host.OpenWebDavFileAsync(fileItem);
                }
                else
                {
                    await host.OpenWebDavArchiveAsync(fileItem);
                }

                await RestoreArchivePositionAsync(target, host);
            }
            else
            {
                ApplyPendingPosition(target, fileItem, host);
                await host.OpenWebDavFileAsync(fileItem);
            }
        }

        private static async Task NavigateToLocalRecentAsync(
            RecentNavigationSnapshot target,
            IBookmarkNavigationHost host)
        {
            switch (target.Type)
            {
                case "Folder":
                    if (Directory.Exists(target.Path))
                    {
                        host.LoadExplorerFolder(target.Path);
                    }
                    else
                    {
                        host.NotifyFileNotFound();
                    }
                    break;

                case "File":
                    if (File.Exists(target.Path))
                    {
                        ApplyPendingPosition(target, null, host);

                        var parentDir = Path.GetDirectoryName(target.Path);
                        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                        {
                            host.LoadExplorerFolder(parentDir);
                            host.SelectExplorerItemByName(Path.GetFileName(target.Path));
                        }

                        var file = await StorageFile.GetFileFromPathAsync(target.Path);
                        if (target.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            await host.LoadImagesFromPdfAsync(target.Path);
                        }
                        else
                        {
                            await host.LoadImageFromFileAsync(file);
                        }
                    }
                    else
                    {
                        host.NotifyFileNotFound();
                    }
                    break;

                case "Archive":
                    if (File.Exists(target.Path))
                    {
                        var archiveFolder = Path.GetDirectoryName(target.Path);
                        if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                        {
                            host.LoadExplorerFolder(archiveFolder);
                            if (!string.IsNullOrEmpty(target.ArchiveEntryKey))
                            {
                                await host.LoadImagesFromArchiveAsync(target.Path);
                                await RestoreArchivePositionAsync(target, host);
                            }
                        }
                    }
                    break;
            }
        }

        private static async Task RestoreArchivePositionAsync(
            RecentNavigationSnapshot target,
            IBookmarkNavigationHost host)
        {
            if (string.IsNullOrEmpty(target.ArchiveEntryKey))
            {
                return;
            }

            int entryIndex = FindEntryIndex(host.ImageEntries, e => e.ArchiveEntryKey == target.ArchiveEntryKey);
            if (entryIndex < 0)
            {
                return;
            }

            host.CurrentImageIndex = entryIndex;
            await host.DisplayCurrentImageAsync();
            if (target.ScrollOffset.HasValue)
            {
                await host.RestoreTextScrollOffsetAsync(target.ScrollOffset.Value);
            }
        }

        private static void ApplyPendingPosition(
            RecentNavigationSnapshot target,
            FileItem? fileItem,
            IBookmarkNavigationHost host)
        {
            var kind = FileExplorerService.GetSupportedFileKind(target.Path);
            bool isEpub = fileItem?.IsEpub == true || kind == SupportedFileKind.Epub;
            bool isText = fileItem?.IsText == true || kind == SupportedFileKind.Text;
            bool isPdf = fileItem?.IsPdf == true || kind == SupportedFileKind.Pdf;

            if (isEpub)
            {
                host.SetPendingEpubPosition(
                    target.ChapterIndex,
                    target.SavedPage,
                    target.SavedBlockIndex,
                    target.SavedLine > 1 ? target.SavedLine : 1);
            }
            else if (isText)
            {
                host.SetPendingTextPosition(target.SavedLine, target.SavedPage);
            }

            if (isPdf)
            {
                host.SetPendingPdfPage(target.SavedPage);
            }
        }

        private static FileItem CreateWebDavFileItem(RecentNavigationSnapshot target)
        {
            var fileItem = new FileItem
            {
                Name = target.Name,
                WebDavPath = target.Path,
                IsWebDav = true,
                IsDirectory = target.Type == "Folder"
            };
            FileExplorerService.ApplyFileKind(fileItem, FileExplorerService.GetSupportedFileKind(target.Path));
            fileItem.IsArchive = fileItem.IsArchive || target.Type == "Archive";
            return fileItem;
        }

        private static double CalculateCurrentProgress(RecentCaptureContext context)
        {
            if (context.IsEpubMode && context.EpubSpineCount > 0)
            {
                int currentPage = context.CurrentEpubPageIndex + 1;
                int totalPages = context.EpubPageCount > 0 ? context.EpubPageCount : 1;
                double chapterProgress = (double)context.CurrentEpubChapterIndex / context.EpubSpineCount;
                double pageProgressInChapter = (double)(currentPage - 1) / totalPages / context.EpubSpineCount;
                return ClampProgress((chapterProgress + pageProgressInChapter) * 100.0);
            }

            if (context.IsTextMode)
            {
                int totalLines = context.TextTotalLineCountInSource > 0
                    ? context.TextTotalLineCountInSource
                    : context.AozoraTotalLineCountInSource;
                if (totalLines > 0)
                {
                    int currentLine = 1;
                    if (context.IsVerticalMode)
                    {
                        currentLine = context.CurrentVerticalStartLine;
                    }
                    else if (context.IsAozoraMode &&
                             context.AozoraBlocks.Count > 0 &&
                             context.CurrentAozoraStartBlockIndex >= 0 &&
                             context.CurrentAozoraStartBlockIndex < context.AozoraBlocks.Count)
                    {
                        currentLine = context.AozoraBlocks[context.CurrentAozoraStartBlockIndex].SourceLineNumber;
                    }
                    else
                    {
                        currentLine = context.TopVisibleLineIndex;
                    }

                    return ClampProgress((double)currentLine / totalLines * 100.0);
                }
            }
            else if ((context.HasArchive || context.ImageEntries.Count > 0) &&
                     context.CurrentIndex >= 0 &&
                     context.ImageEntries.Count > 0)
            {
                return ClampProgress((double)(context.CurrentIndex + 1) / context.ImageEntries.Count * 100.0);
            }

            return 0;
        }

        private static bool TryGetCurrentEntry(
            RecentCaptureContext context,
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
            System.Collections.Generic.IReadOnlyList<ImageEntry> entries,
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

            if (!parentPath.EndsWith("/"))
            {
                parentPath += "/";
            }

            return parentPath;
        }

        private static double ClampProgress(double value) => Math.Min(Math.Max(value, 0), 100);

        private sealed record RecentNavigationSnapshot(
            string Name,
            string Path,
            string Type,
            string? ArchiveEntryKey,
            double? ScrollOffset,
            int SavedPage,
            int ChapterIndex,
            int SavedLine,
            int SavedBlockIndex,
            bool IsWebDav,
            string? WebDavServerName)
        {
            public static RecentNavigationSnapshot From(RecentItem recent) => new(
                recent.Name,
                recent.Path,
                recent.Type,
                recent.ArchiveEntryKey,
                recent.ScrollOffset,
                recent.SavedPage,
                recent.ChapterIndex,
                recent.SavedLine,
                recent.SavedBlockIndex,
                recent.IsWebDav,
                recent.WebDavServerName);
        }
    }
}
