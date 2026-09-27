using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ArchiveDocumentHandlers
    {
        public Func<Task<bool>> CloseCurrentPdfAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentEpubAsync { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> LoadBitmapForPreloadAsync { get; init; } = null!;
        public Func<int> GetCurrentIndex { get; init; } = null!;
        public Action<int> SetCurrentIndex { get; init; } = null!;
        public Func<List<ImageEntry>> GetImageEntries { get; init; } = null!;
        public Action<List<ImageEntry>> SetImageEntries { get; init; } = null!;
        public Func<bool> IsPdfOpen { get; init; } = null!;
        public Func<double> GetZoomLevel { get; init; } = null!;
        public Func<CanvasBitmap?> GetCurrentBitmap { get; init; } = null!;
        public Func<CanvasBitmap?> GetLeftBitmap { get; init; } = null!;
        public Func<CanvasBitmap?> GetRightBitmap { get; init; } = null!;
        public Func<bool> IsSharpenEnabled { get; init; } = null!;
        public Action CancelImageLoading { get; init; } = null!;
        public Action CancelTextLoading { get; init; } = null!;
        public Action ClearImageResources { get; init; } = null!;
        public Action InvalidateMainCanvas { get; init; } = null!;
        public Action<string> SetWindowTitle { get; init; } = null!;
        public Action<string> SetStatusText { get; init; } = null!;
    }

    internal sealed class ArchiveDocumentController
    {
        private readonly ArchiveSession _archiveSession;
        private readonly SevenZipExtractionCoordinator _sevenZipExtraction;
        private readonly PreloadManager _preloadManager;
        private readonly ImageCacheManager _imageCache;
        private readonly ImageViewerState _imageViewerState;
        private readonly FastNavigationService _fastNavigationService;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly ArchiveDocumentHandlers _handlers;
        private readonly SemaphoreSlim _transitionLock = new(1, 1);
        private int _transitionVersion;

        public ArchiveDocumentController(
            ArchiveSession archiveSession,
            SevenZipExtractionCoordinator sevenZipExtraction,
            PreloadManager preloadManager,
            ImageCacheManager imageCache,
            ImageViewerState imageViewerState,
            FastNavigationService fastNavigationService,
            DispatcherQueue dispatcherQueue,
            ArchiveDocumentHandlers handlers)
        {
            _archiveSession = archiveSession ?? throw new ArgumentNullException(nameof(archiveSession));
            _sevenZipExtraction = sevenZipExtraction ?? throw new ArgumentNullException(nameof(sevenZipExtraction));
            _preloadManager = preloadManager ?? throw new ArgumentNullException(nameof(preloadManager));
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _imageViewerState = imageViewerState ?? throw new ArgumentNullException(nameof(imageViewerState));
            _fastNavigationService = fastNavigationService ?? throw new ArgumentNullException(nameof(fastNavigationService));
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public async Task LoadImagesFromArchiveAsync(string archivePath)
        {
            int version = ++_transitionVersion;
            using var suspension = _fastNavigationService.SuspendNavigation();
            CancelActiveWork();
            _handlers.CancelTextLoading();
            await _transitionLock.WaitAsync();
            try
            {
                if (version != _transitionVersion) return;
                await LoadImagesFromArchiveCoreAsync(archivePath, version);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Record("Switching archive", ex);
                if (version == _transitionVersion)
                    _handlers.SetStatusText(Strings.ArchiveOpenFailed(ex.Message));
            }
            finally
            {
                _transitionLock.Release();
            }
        }

        private void CancelActiveWork()
        {
            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();
            _handlers.CancelImageLoading();
        }

        private async Task LoadImagesFromArchiveCoreAsync(string archivePath, int version)
        {
            if (!await _handlers.CloseCurrentPdfAsync() || version != _transitionVersion) return;
            if (!await _handlers.CloseCurrentEpubAsync() || version != _transitionVersion) return;
            if (!await CloseCurrentArchiveCoreAsync() || version != _transitionVersion) return;

            _handlers.ClearImageResources();

            try
            {
                var entries = (await _archiveSession.OpenLocalAsync(archivePath)).ToList();
                if (version != _transitionVersion) return;
                _handlers.SetImageEntries(entries);
                _handlers.SetCurrentIndex(entries.Count > 0 ? 0 : -1);

                if (entries.Count > 0)
                {
                    await _handlers.DisplayCurrentImageAsync();
                    if (version != _transitionVersion ||
                        !ReferenceEquals(entries, _handlers.GetImageEntries())) return;

                    if (_archiveSession.IsSevenZipArchive)
                    {
                        var extractToken = _sevenZipExtraction.StartNewExtraction();
                        _ = _archiveSession.ExtractSevenZipEntriesInBackgroundAsync(
                            archivePath,
                            entries,
                            _handlers.GetCurrentIndex,
                            _sevenZipExtraction,
                            extractToken);
                    }

                    _ = _preloadManager.StartPreloadAsync(
                        _handlers.GetCurrentIndex(),
                        _handlers.GetImageEntries(),
                        _handlers.IsPdfOpen(),
                        _handlers.GetZoomLevel(),
                        _handlers.GetCurrentBitmap(),
                        _handlers.GetLeftBitmap(),
                        _handlers.GetRightBitmap(),
                        _handlers.LoadBitmapForPreloadAsync,
                        _handlers.InvalidateMainCanvas,
                        prioritizeNext: true,
                        requireSharpening: _handlers.IsSharpenEnabled());

                    _handlers.SetWindowTitle("Uviewer - Image & Text Viewer");
                }
                else
                {
                    _handlers.SetStatusText(Strings.ArchiveNoImages);
                }
            }
            catch (Exception ex)
            {
                if (version == _transitionVersion)
                    _handlers.SetStatusText(Strings.ArchiveOpenFailed(ex.Message));
            }
        }

        public async Task<bool> CloseCurrentArchiveAsync()
        {
            // Other document openers call this even without an archive. Do not
            // cancel the new document's load unless an archive is open/opening.
            if (!_archiveSession.HasArchive && _transitionLock.CurrentCount != 0) return true;
            ++_transitionVersion;
            using var suspension = _fastNavigationService.SuspendNavigation();
            CancelActiveWork();
            await _transitionLock.WaitAsync();
            try
            {
                return await CloseCurrentArchiveCoreAsync();
            }
            finally
            {
                _transitionLock.Release();
            }
        }

        private async Task<bool> CloseCurrentArchiveCoreAsync()
        {
            if (!_archiveSession.HasArchive) return true;

            if (!await _archiveSession.CloseAsync(TimeSpan.FromSeconds(10)))
            {
                return false;
            }

            AfterArchiveClosed();
            return true;
        }

        private void AfterArchiveClosed()
        {
            _handlers.SetWindowTitle("Uviewer - Image & Text Viewer");

            _handlers.ClearImageResources();
            _handlers.SetImageEntries(new List<ImageEntry>());
            _handlers.SetCurrentIndex(-1);
            _fastNavigationService.StopTimers();
            _sevenZipExtraction.CleanupTempData();
        }
    }
}
