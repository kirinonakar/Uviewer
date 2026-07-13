using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public Action CancelImageLoading { get; init; } = null!;
        public Action CancelTextLoading { get; init; } = null!;
        public Action CancelExplorerThumbnailLoading { get; init; } = null!;
        public Action PrepareForImageLoad { get; init; } = null!;
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
                _imageViewerState.Entries = await CreateEntriesFromFolderAsync(parentFolder);
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

            _imageViewerState.Entries = await CreateEntriesFromFolderAsync(folder);

            if (_imageViewerState.Entries.Count > 0)
            {
                _imageViewerState.CurrentIndex = 0;
                await _handlers.DisplayCurrentImageAsync();
            }
            else
            {
                _handlers.SetStatusText("이 폴더에 이미지가 없습니다");
            }
        }

        private void CancelActiveImageWork(bool cancelExplorerThumbnails)
        {
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
            _ = Task.Run(async () =>
            {
                try
                {
                    var folder = await file.GetParentAsync();
                    if (folder == null) return;

                    var allEntries = await CreateEntriesFromFolderAsync(folder);

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_imageViewerState.Entries.Count != 1 ||
                            _imageViewerState.Entries[0].FilePath != file.Path)
                        {
                            return;
                        }

                        _imageViewerState.Entries = allEntries;
                        _imageViewerState.CurrentIndex = _imageViewerState.Entries.FindIndex(e => e.FilePath == file.Path);
                        _handlers.RefreshCurrentStatusBar();
                    });
                }
                catch
                {
                }
            });
        }

        private static async Task<List<ImageEntry>> CreateEntriesFromFolderAsync(StorageFolder folder)
        {
            var files = await folder.GetFilesAsync();
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
    }
}
