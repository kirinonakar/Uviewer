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
            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();
            _handlers.CancelImageLoading();
            _handlers.CancelTextLoading();

            if (!await _handlers.CloseCurrentPdfAsync()) return;
            if (!await _handlers.CloseCurrentEpubAsync()) return;
            if (!await CloseCurrentArchiveAsync()) return;

            try
            {
                var entries = (await _archiveSession.OpenLocalAsync(archivePath)).ToList();
                _handlers.SetImageEntries(entries);

                if (entries.Count > 0)
                {
                    _handlers.SetCurrentIndex(0);

                    await _handlers.DisplayCurrentImageAsync();

                    if (_archiveSession.IsSevenZipArchive)
                    {
                        var extractToken = _sevenZipExtraction.StartNewExtraction();
                        _ = _archiveSession.ExtractSevenZipEntriesInBackgroundAsync(
                            archivePath,
                            _handlers.GetImageEntries(),
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
                    _handlers.SetStatusText("이 압축 파일에 이미지가 없습니다");
                }
            }
            catch (Exception ex)
            {
                _handlers.SetStatusText($"압축 파일 열기 실패: {ex.Message}");
            }
        }

        public async Task<bool> CloseCurrentArchiveAsync()
        {
            if (!_archiveSession.HasArchive) return true;

            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();

            if (!await _archiveSession.CloseAsync(TimeSpan.FromSeconds(10)))
            {
                return false;
            }

            AfterArchiveClosed();
            return true;
        }

        private void AfterArchiveClosed()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _handlers.SetWindowTitle("Uviewer - Image & Text Viewer");
            });

            _imageCache.ClearAll();
            _fastNavigationService.StopTimers();
            _imageViewerState.ClearBitmaps();
            _sevenZipExtraction.CleanupTempData();
        }
    }
}
