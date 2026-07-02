using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class PdfDocumentHandlers
    {
        public Func<bool> IsWindowClosing { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentArchiveAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentEpubAsync { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> LoadBitmapForPreloadAsync { get; init; } = null!;
        public Func<int> GetPendingPdfPageIndex { get; init; } = null!;
        public Action<int> SetPendingPdfPageIndex { get; init; } = null!;
        public Action CancelImageLoading { get; init; } = null!;
        public Action SwitchToImageMode { get; init; } = null!;
        public Action<string, int, CancellationToken> StartPdfTocLoad { get; init; } = null!;
        public Action<bool> SetPdfGoToPageVisible { get; init; } = null!;
        public Action<bool> SetSideBySideToolbarVisible { get; init; } = null!;
        public Action<bool> SetSharpenControlsVisible { get; init; } = null!;
        public Action<double> SetZoomLevel { get; init; } = null!;
        public Action<int> ResetImageViewportNavigation { get; init; } = null!;
        public Action InvalidateMainCanvas { get; init; } = null!;
        public Action ApplyPdfClosedUi { get; init; } = null!;
        public Action<string> SetTitle { get; init; } = null!;
        public Action<string> SetStatusText { get; init; } = null!;
    }

    internal sealed class PdfDocumentController
    {
        private readonly DocumentSessionTracker _documentSessionTracker;
        private readonly DocumentSearchService _documentSearchService;
        private readonly PreloadManager _preloadManager;
        private readonly ImageCacheManager _imageCache;
        private readonly ImageViewerState _imageViewerState;
        private readonly ImageViewportNavigationService _imageViewportNavigationService;
        private readonly FastNavigationService _fastNavigationService;
        private readonly TocService _tocService;
        private readonly PdfDocumentHandlers _handlers;

        public PdfDocumentController(
            DocumentSessionTracker documentSessionTracker,
            DocumentSearchService documentSearchService,
            PreloadManager preloadManager,
            ImageCacheManager imageCache,
            ImageViewerState imageViewerState,
            ImageViewportNavigationService imageViewportNavigationService,
            FastNavigationService fastNavigationService,
            TocService tocService,
            PdfDocumentHandlers handlers)
        {
            _documentSessionTracker = documentSessionTracker ?? throw new ArgumentNullException(nameof(documentSessionTracker));
            _documentSearchService = documentSearchService ?? throw new ArgumentNullException(nameof(documentSearchService));
            _preloadManager = preloadManager ?? throw new ArgumentNullException(nameof(preloadManager));
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _imageViewerState = imageViewerState ?? throw new ArgumentNullException(nameof(imageViewerState));
            _imageViewportNavigationService = imageViewportNavigationService ?? throw new ArgumentNullException(nameof(imageViewportNavigationService));
            _fastNavigationService = fastNavigationService ?? throw new ArgumentNullException(nameof(fastNavigationService));
            _tocService = tocService ?? throw new ArgumentNullException(nameof(tocService));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public async Task LoadImagesFromPdfAsync(string pdfPath)
        {
            if (_handlers.IsWindowClosing()) return;

            _documentSearchService.Clear();
            _preloadManager.CancelAll();
            _handlers.CancelImageLoading();
            _handlers.SwitchToImageMode();

            if (!await _handlers.CloseCurrentArchiveAsync()) return;
            if (!await _handlers.CloseCurrentEpubAsync()) return;
            if (!await CloseCurrentPdfAsync()) return;

            try
            {
                var pdfSession = new PdfDocumentSession(pdfPath);
                _documentSessionTracker.Replace(pdfSession);
                await pdfSession.LoadFileAsync();

                _imageViewerState.Entries = CreatePdfEntries(pdfPath, pdfSession.PageCount);

                _handlers.SetPdfGoToPageVisible(true);
                _handlers.StartPdfTocLoad(pdfPath, pdfSession.Generation, pdfSession.DocumentToken);
                _handlers.SetSideBySideToolbarVisible(false);
                _handlers.SetSharpenControlsVisible(false);

                _handlers.SetZoomLevel(1.0);
                _handlers.ResetImageViewportNavigation(1);

                if (_imageViewerState.Entries.Count > 0)
                {
                    ApplyInitialPageIndex();
                    await _handlers.DisplayCurrentImageAsync();
                    StartPreload();
                    _handlers.SetTitle("Uviewer - Image & Text Viewer");
                }
                else
                {
                    _handlers.SetStatusText("이 PDF 파일에 페이지가 없습니다");
                }
            }
            catch (Exception ex)
            {
                _documentSessionTracker.Clear(DocumentKind.Pdf);
                _handlers.SetStatusText($"PDF 열기 실패: {ex.Message}");
            }
        }

        public async Task<bool> CloseCurrentPdfAsync()
        {
            CancelPdfOperations();
            var pdfSession = CurrentPdfSession;
            if (pdfSession?.HasDocument != true) return true;

            if (!await pdfSession.CloseAsync(TimeSpan.FromSeconds(10)))
            {
                return false;
            }

            CloseCurrentPdfInternal();
            return true;
        }

        public void ShutdownPdfResources()
        {
            CancelPdfOperations();
            CurrentPdfSession?.Shutdown();
            _documentSessionTracker.Clear(DocumentKind.Pdf);
            _tocService.Clear();
            _fastNavigationService.StopTimers();
            _imageViewerState.ClearBitmaps();
        }

        private PdfDocumentSession? CurrentPdfSession => _documentSessionTracker.Current as PdfDocumentSession;

        private void CloseCurrentPdfInternal()
        {
            CancelPdfOperations();
            _documentSessionTracker.Clear(DocumentKind.Pdf);
            _tocService.Clear();

            if (!_handlers.IsWindowClosing())
            {
                _handlers.ApplyPdfClosedUi();
                _imageCache.ClearAll();
            }

            _fastNavigationService.StopTimers();
            _imageViewerState.ClearBitmaps();
        }

        private void CancelPdfOperations()
        {
            CurrentPdfSession?.CancelOperations();
            try { _preloadManager.CancelAll(); } catch { }
            try { _imageViewportNavigationService.StopSmoothZoom(); } catch { }
        }

        private void ApplyInitialPageIndex()
        {
            int pendingPageIndex = _handlers.GetPendingPdfPageIndex();
            if (pendingPageIndex >= 0 && pendingPageIndex < _imageViewerState.Entries.Count)
            {
                _imageViewerState.CurrentIndex = pendingPageIndex;
                _handlers.SetPendingPdfPageIndex(-1);
                return;
            }

            _imageViewerState.CurrentIndex = 0;
        }

        private void StartPreload()
        {
            _ = _preloadManager.StartPreloadAsync(
                _imageViewerState.CurrentIndex,
                _imageViewerState.Entries,
                isPdfMode: true,
                zoomLevel: 1.0,
                _imageViewerState.CurrentBitmap,
                _imageViewerState.LeftBitmap,
                _imageViewerState.RightBitmap,
                _handlers.LoadBitmapForPreloadAsync,
                _handlers.InvalidateMainCanvas,
                prioritizeNext: true,
                requireSharpening: _imageViewerState.IsSharpenEnabled);
        }

        private static List<ImageEntry> CreatePdfEntries(string pdfPath, uint pageCount)
        {
            var entries = new List<ImageEntry>();
            for (uint i = 0; i < pageCount; i++)
            {
                entries.Add(new ImageEntry
                {
                    DisplayName = $"{Path.GetFileName(pdfPath)} - Page {i + 1}",
                    FilePath = pdfPath,
                    IsPdfEntry = true,
                    PdfPageIndex = i
                });
            }

            return entries;
        }
    }
}
