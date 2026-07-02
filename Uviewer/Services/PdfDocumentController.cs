using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public Func<double> GetZoomLevel { get; init; } = null!;
        public Func<CanvasControl> GetMainCanvas { get; init; } = null!;
        public Action CancelImageLoading { get; init; } = null!;
        public Action SwitchToImageMode { get; init; } = null!;
        public Action<ImageEntry, CanvasBitmap> UpdateStatusBar { get; init; } = null!;
        public Action<bool> SetPdfTocVisible { get; init; } = null!;
        public Action<bool> SetPdfGoToPageVisible { get; init; } = null!;
        public Action<bool> SetSideBySideToolbarVisible { get; init; } = null!;
        public Action<bool> SetSharpenControlsVisible { get; init; } = null!;
        public Action<string> SetPdfTocTitle { get; init; } = null!;
        public Action<object> SetPdfTocItems { get; init; } = null!;
        public Action<object> ScrollPdfTocIntoView { get; init; } = null!;
        public Action HidePdfTocFlyout { get; init; } = null!;
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
                StartPdfTocLoad(pdfPath, pdfSession.Generation, pdfSession.DocumentToken);
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

        public Windows.Data.Pdf.PdfDocument? CurrentDocument => CurrentPdfSession?.Document;

        public string? CurrentPath => CurrentPdfSession?.SourcePath;

        public bool HasOpenDocument => CurrentPdfSession?.HasDocument == true;

        public bool IsCurrentPath(string pdfPath) =>
            CurrentPdfSession?.IsCurrentPath(pdfPath) == true;

        public bool IsCurrentScope(int generation, string? pdfPath) =>
            CurrentPdfSession?.IsCurrentScope(generation, pdfPath) == true;

        public Task<CanvasBitmap?> LoadPageBitmapAsync(
            uint pageIndex,
            CanvasControl canvas,
            CancellationToken token = default,
            bool isPreload = false)
        {
            var pdfSession = CurrentPdfSession;
            return pdfSession == null
                ? Task.FromResult<CanvasBitmap?>(null)
                : pdfSession.LoadPageBitmapAsync(
                    pageIndex,
                    canvas,
                    _handlers.GetZoomLevel(),
                    _handlers.IsWindowClosing,
                    token,
                    isPreload);
        }

        public void ShowToc()
        {
            if (CurrentDocument == null) return;

            _handlers.SetPdfTocTitle(Strings.TocTitle);

            var items = _tocService.CurrentToc;
            int currentIndex = -1;

            if (items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= _imageViewerState.CurrentIndex)
                        currentIndex = i;
                    else
                        break;
                }
            }

            var displayItems = items.Select(item => new TocItem
            {
                HeadingText = item.HeadingText,
                HeadingLevel = item.HeadingLevel,
                SourceLineNumber = item.SourceLineNumber,
                Tag = item.Tag
            }).ToList();

            if (currentIndex >= 0 && currentIndex < displayItems.Count)
            {
                displayItems[currentIndex].HeadingText = "⮕ " + displayItems[currentIndex].HeadingText;
            }

            if (displayItems.Count == 0)
            {
                displayItems.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
            }

            _handlers.SetPdfTocItems(displayItems);

            if (currentIndex >= 0)
            {
                _handlers.ScrollPdfTocIntoView(displayItems[currentIndex]);
            }
        }

        public void OpenTocItem(object? clickedItem)
        {
            if (clickedItem is not TocItem item)
            {
                return;
            }

            _handlers.HidePdfTocFlyout();

            if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageViewerState.Entries.Count)
            {
                _imageViewerState.CurrentIndex = item.SourceLineNumber;
                _ = _handlers.DisplayCurrentImageAsync();
            }
        }

        public async Task RerenderCurrentPageAsync()
        {
            try
            {
                if (_handlers.IsWindowClosing()) return;
                var pdfSession = CurrentPdfSession;
                if (pdfSession?.HasDocument != true || _imageViewerState.CurrentBitmap == null) return;
                if (_imageViewerState.CurrentIndex < 0 || _imageViewerState.CurrentIndex >= _imageViewerState.Entries.Count) return;

                var entry = _imageViewerState.Entries[_imageViewerState.CurrentIndex];
                if (!entry.IsPdfEntry) return;

                var token = pdfSession.RestartZoomRerender();
                int capturedIndex = _imageViewerState.CurrentIndex;
                int capturedPdfGeneration = pdfSession.Generation;
                string? capturedPdfPath = pdfSession.SourcePath;

                await Task.Delay(350, token);
                if (!IsCurrentScope(capturedPdfGeneration, capturedPdfPath) || token.IsCancellationRequested) return;

                var newBitmap = await LoadPageBitmapAsync(
                    entry.PdfPageIndex,
                    _handlers.GetMainCanvas(),
                    token,
                    isPreload: false);

                if (_handlers.IsWindowClosing() ||
                    token.IsCancellationRequested ||
                    newBitmap == null ||
                    !IsCurrentScope(capturedPdfGeneration, capturedPdfPath))
                {
                    if (newBitmap != null) _imageCache.SafeDisposeBitmap(newBitmap);
                    return;
                }

                if (capturedIndex != _imageViewerState.CurrentIndex)
                {
                    _imageCache.SafeDisposeBitmap(newBitmap);
                    return;
                }

                var oldBitmap = _imageViewerState.CurrentBitmap;
                _imageViewerState.CurrentBitmap = newBitmap;
                _imageCache.UpdateCache(capturedIndex, newBitmap, true, _handlers.GetZoomLevel(), oldBitmap);

                _handlers.InvalidateMainCanvas();
                _handlers.UpdateStatusBar(entry, newBitmap);

                _ = _preloadManager.StartPreloadAsync(
                    _imageViewerState.CurrentIndex,
                    _imageViewerState.Entries,
                    isPdfMode: HasOpenDocument,
                    zoomLevel: _handlers.GetZoomLevel(),
                    _imageViewerState.CurrentBitmap,
                    _imageViewerState.LeftBitmap,
                    _imageViewerState.RightBitmap,
                    _handlers.LoadBitmapForPreloadAsync,
                    _handlers.InvalidateMainCanvas,
                    prioritizeNext: true,
                    requireSharpening: _imageViewerState.IsSharpenEnabled);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF rerender skipped: {ex.Message}");
            }
        }

        private PdfDocumentSession? CurrentPdfSession => _documentSessionTracker.Current as PdfDocumentSession;

        private void StartPdfTocLoad(string pdfPath, int pdfGeneration, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(750, token);
                    if (!IsCurrentScope(pdfGeneration, pdfPath) || token.IsCancellationRequested) return;

                    _tocService.SetProvider(new PdfTocProvider(pdfPath));
                    await _tocService.LoadTocAsync(token);

                    if (!IsCurrentScope(pdfGeneration, pdfPath)) return;

                    _handlers.SetPdfTocVisible(true);
                }
                catch (OperationCanceledException) { }
                catch (Exception tocEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading PDF TOC: {tocEx.Message}");
                }
            });
        }

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
