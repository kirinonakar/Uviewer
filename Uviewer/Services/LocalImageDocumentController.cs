using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.Storage;

namespace Uviewer.Services
{
    internal sealed class LocalImageDocumentHandlers
    {
        public Func<Task<bool>> CloseCurrentArchiveAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentPdfAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentEpubAsync { get; init; } = null!;
        public Action CloseCurrentText { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Func<bool> IsRecursiveImageBrowsingEnabled { get; init; } = () => false;
        public Func<string?> GetRecursiveRootPath { get; init; } = () => null;
        public Action CancelImageLoading { get; init; } = null!;
        public Action CancelTextLoading { get; init; } = null!;
        public Action CancelExplorerThumbnailLoading { get; init; } = null!;
        public Action PrepareForImageLoad { get; init; } = null!;
        public Action ClearImageCacheForEntryListChange { get; init; } = null!;
        public Action RefreshCurrentStatusBar { get; init; } = null!;
        public Action<string> SetStatusText { get; init; } = null!;
    }

    internal sealed class LocalImageDocumentController
    {
        private readonly SevenZipExtractionCoordinator _sevenZipExtraction;
        private readonly PreloadManager _preloadManager;
        private readonly ImageViewerState _imageViewerState;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly LocalImageDocumentHandlers _handlers;
        private CancellationTokenSource? _folderEntryScanCts;

        public LocalImageDocumentController(
            SevenZipExtractionCoordinator sevenZipExtraction,
            PreloadManager preloadManager,
            ImageViewerState imageViewerState,
            DispatcherQueue dispatcherQueue,
            LocalImageDocumentHandlers handlers)
        {
            _sevenZipExtraction = sevenZipExtraction ?? throw new ArgumentNullException(nameof(sevenZipExtraction));
            _preloadManager = preloadManager ?? throw new ArgumentNullException(nameof(preloadManager));
            _imageViewerState = imageViewerState ?? throw new ArgumentNullException(nameof(imageViewerState));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public async Task LoadImageFromFileAsync(StorageFile file, bool isInitial = false)
        {
            CancelActiveImageWork(cancelExplorerThumbnails: false);

            if (!await CloseOpenDocumentsAsync(closeText: true))
            {
                return;
            }

            ResetImagePipeline();

            if (isInitial)
            {
                _imageViewerState.Entries = new List<ImageEntry>
                {
                    new() { DisplayName = file.Name, FilePath = file.Path }
                };
                _imageViewerState.CurrentIndex = 0;
                await _handlers.DisplayCurrentImageAsync();

                StartBackgroundFolderEntryRefresh(file);
                return;
            }

            var parentFolder = await file.GetParentAsync();
            if (parentFolder != null)
            {
                var scan = RestartFolderEntryScan();
                var rootPath = GetRecursiveRootPath(file.Path, parentFolder.Path);
                _imageViewerState.Entries = await CreateEntriesFromFolderAsync(rootPath, scan.Token);
                _imageViewerState.CurrentIndex = _imageViewerState.Entries.FindIndex(e => e.FilePath == file.Path);
            }
            else
            {
                _imageViewerState.Entries = new List<ImageEntry>
                {
                    new() { DisplayName = file.Name, FilePath = file.Path }
                };
                _imageViewerState.CurrentIndex = 0;
            }

            await _handlers.DisplayCurrentImageAsync();
        }

        public async Task LoadImagesFromFolderAsync(StorageFolder folder)
        {
            CancelActiveImageWork(cancelExplorerThumbnails: true);

            if (!await CloseOpenDocumentsAsync(closeText: false))
            {
                return;
            }

            ResetImagePipeline();

            var scan = RestartFolderEntryScan();
            _imageViewerState.Entries = await CreateEntriesFromFolderAsync(folder.Path, scan.Token);

            if (_imageViewerState.Entries.Count > 0)
            {
                _imageViewerState.CurrentIndex = 0;
                await _handlers.DisplayCurrentImageAsync();
            }
            else
            {
                _handlers.SetStatusText(Strings.FolderNoImages);
            }
        }

        private void CancelActiveImageWork(bool cancelExplorerThumbnails)
        {
            CancelFolderEntryScan();
            _sevenZipExtraction.CancelExtraction();
            _handlers.CancelImageLoading();
            if (cancelExplorerThumbnails)
            {
                _handlers.CancelExplorerThumbnailLoading();
            }

            _preloadManager.CancelAll();
            _handlers.CancelTextLoading();
        }

        private async Task<bool> CloseOpenDocumentsAsync(bool closeText)
        {
            if (!await _handlers.CloseCurrentArchiveAsync()) return false;
            if (!await _handlers.CloseCurrentPdfAsync()) return false;
            if (!await _handlers.CloseCurrentEpubAsync()) return false;

            if (closeText)
            {
                _handlers.CloseCurrentText();
            }

            return true;
        }

        private void ResetImagePipeline()
        {
            _preloadManager.CancelAll();
            _handlers.PrepareForImageLoad();
        }

        private void StartBackgroundFolderEntryRefresh(StorageFile file)
        {
            var scan = RestartFolderEntryScan();
            var token = scan.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    var folder = await file.GetParentAsync();
                    if (folder == null) return;

                    var rootPath = GetRecursiveRootPath(file.Path, folder.Path);
                    var allEntries = await CreateEntriesFromFolderAsync(rootPath, token);

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        if (_imageViewerState.Entries.Count != 1 ||
                            _imageViewerState.Entries[0].FilePath != file.Path)
                        {
                            return;
                        }

                        // The initial launch uses a one-item list, so index-based
                        // image caches must not be reused after the list becomes the
                        // full folder. Keep active bitmaps alive while dropping
                        // stale index mappings.
                        _handlers.ClearImageCacheForEntryListChange();
                        _imageViewerState.Entries = allEntries;
                        _imageViewerState.CurrentIndex = _imageViewerState.Entries.FindIndex(e => e.FilePath == file.Path);
                        _handlers.RefreshCurrentStatusBar();
                    });
                }
                catch (OperationCanceledException) { }
                catch
                {
                }
            });
        }

        private string GetRecursiveRootPath(string filePath, string fallbackPath)
        {
            if (!_handlers.IsRecursiveImageBrowsingEnabled()) return fallbackPath;

            var configuredRoot = _handlers.GetRecursiveRootPath();
            if (string.IsNullOrWhiteSpace(configuredRoot)) return fallbackPath;

            try
            {
                var relative = Path.GetRelativePath(configuredRoot, filePath);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                {
                    return fallbackPath;
                }

                return configuredRoot;
            }
            catch
            {
                return fallbackPath;
            }
        }

        private async Task<List<ImageEntry>> CreateEntriesFromFolderAsync(string folderPath, CancellationToken token)
        {
            if (_handlers.IsRecursiveImageBrowsingEnabled())
            {
                return await FileExplorerService.GetRecursiveImageEntriesAsync(folderPath, token).ConfigureAwait(false);
            }

            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            var files = await folder.GetFilesAsync();
            token.ThrowIfCancellationRequested();
            return files
                .Where(file => FileExplorerService.SupportedFileExtensions.Contains(
                    Path.GetExtension(file.Name).ToLowerInvariant()))
                .OrderBy(file => file.Name, Uviewer.NaturalSortComparer.Default)
                .Select(file => new ImageEntry
                {
                    DisplayName = file.Name,
                    FilePath = file.Path
                })
                .ToList();
        }

        private CancellationTokenSource RestartFolderEntryScan()
        {
            CancelFolderEntryScan();
            _folderEntryScanCts = new CancellationTokenSource();
            return _folderEntryScanCts;
        }

        private void CancelFolderEntryScan()
        {
            var scan = _folderEntryScanCts;
            _folderEntryScanCts = null;
            if (scan == null) return;

            scan.Cancel();
            scan.Dispose();
        }
    }
}
