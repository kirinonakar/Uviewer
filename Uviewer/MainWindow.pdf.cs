using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private PdfDocumentSession? CurrentPdfSession => _documentSessionTracker.Current as PdfDocumentSession;
        private Windows.Data.Pdf.PdfDocument? _currentPdfDocument => CurrentPdfSession?.Document;
        private string? _currentPdfPath
        {
            get => CurrentPdfSession?.SourcePath;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _documentSessionTracker.Clear(DocumentKind.Pdf);
                    return;
                }

                _documentSessionTracker.Replace(new PdfDocumentSession(value));
            }
        }

        private async Task LoadImagesFromPdfAsync(string pdfPath)
        {
            if (_isWindowClosing) return;

            _documentSearchService.Clear();
            _preloadManager.CancelAll();
            _imageLoadingCts?.Cancel(); // Cancel any ongoing image load
            
            SwitchToImageMode(); // Force UI to image mode immediately

            // Close other formats first - outside the lock to avoid double-locking/deadlocks
            if (!await CloseCurrentArchiveAsync()) return;
            if (!await CloseCurrentEpubAsync()) return;
            if (!await CloseCurrentPdfAsync()) return;

            try
            {
                var pdfSession = new PdfDocumentSession(pdfPath);
                _documentSessionTracker.Replace(pdfSession);
                await pdfSession.LoadFileAsync();

                var newEntries = new List<ImageEntry>();
                for (uint i = 0; i < pdfSession.PageCount; i++)
                {
                    newEntries.Add(new ImageEntry
                    {
                        DisplayName = $"{Path.GetFileName(pdfPath)} - Page {i + 1}",
                        FilePath = pdfPath,
                        IsPdfEntry = true,
                        PdfPageIndex = i
                    });
                }
                _imageEntries = newEntries;

                MainToolbar.SetPdfGoToPageVisible(true);
                StartPdfTocLoad(pdfPath, pdfSession.Generation, pdfSession.DocumentToken);

                MainToolbar.SetSideBySideToolbarVisible(false);
                MainToolbar.SetSharpenControlsVisible(false);

                // Reset PDF view state for the new document
                _zoomLevel = 1.0;
                _imageViewportNavigationService.Reset(scrollDirection: 1); // Start from top

                if (_imageEntries.Count > 0)
                {
                    if (_pendingPdfPageIndex >= 0 && _pendingPdfPageIndex < _imageEntries.Count)
                    {
                        _currentIndex = _pendingPdfPageIndex;
                        _pendingPdfPageIndex = -1;
                    }
                    else
                    {
                        _currentIndex = 0;
                    }
                    await DisplayCurrentImageAsync();

                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: true,
                        requireSharpening: _sharpenEnabled);

                    Title = "Uviewer - Image & Text Viewer";
                }
                else
                {
                    FileNameText.Text = "이 PDF 파일에 페이지가 없습니다";
                }
            }
            catch (Exception ex)
            {
                _documentSessionTracker.Clear(DocumentKind.Pdf);
                FileNameText.Text = $"PDF 열기 실패: {ex.Message}";
            }
        }

        private void StartPdfTocLoad(string pdfPath, int pdfGeneration, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(750, token);
                    if (!IsCurrentPdfScope(pdfGeneration, pdfPath) || token.IsCancellationRequested) return;

                    _tocService.SetProvider(new PdfTocProvider(pdfPath));
                    await _tocService.LoadTocAsync(token);

                    if (!IsCurrentPdfScope(pdfGeneration, pdfPath)) return;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!IsCurrentPdfScope(pdfGeneration, pdfPath)) return;

                        MainToolbar.SetPdfTocVisible(true);
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception tocEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading PDF TOC: {tocEx.Message}");
                }
            });
        }

        private async Task<bool> CloseCurrentPdfAsync() // 동기 메서드 대신 비동기로 변경하여 UI 프리징 방지
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

        private void CloseCurrentPdfInternal()
        {
            CancelPdfOperations();

            // PDF Document 참조 해제
            _currentPdfPath = null;
            _tocService.Clear();

            if (!_isWindowClosing)
            {
                // UI 스레드에서 버튼 가리기
                DispatcherQueue.TryEnqueue(() =>
                {
                    MainToolbar.SetPdfTocVisible(false);
                    MainToolbar.SetPdfGoToPageVisible(false);
                    Title = "Uviewer - Image & Text Viewer";
                });

                _imageCache?.ClearAll();
            }

            _fastNavigationService?.StopTimers();

            _imageViewerState.ClearBitmaps();
        }

        private bool IsCurrentPdfPath(string pdfPath)
        {
            return CurrentPdfSession?.IsCurrentPath(pdfPath) == true;
        }

        private bool IsCurrentPdfScope(int generation, string? pdfPath)
        {
            return CurrentPdfSession?.IsCurrentScope(generation, pdfPath) == true;
        }

        private void CancelPdfOperations()
        {
            CurrentPdfSession?.CancelOperations();
            try { _preloadManager?.CancelAll(); } catch { }
            try { _imageViewportNavigationService?.StopSmoothZoom(); } catch { }
        }

        private void ShutdownPdfResources()
        {
            CancelPdfOperations();
            CurrentPdfSession?.Shutdown();
            _currentPdfPath = null;
            _tocService.Clear();
            _fastNavigationService?.StopTimers();
            _imageViewerState.ClearBitmaps();
        }

        private async Task<CanvasBitmap?> LoadPdfPageBitmapAsync(
            uint pageIndex,
            CanvasControl canvas,
            CancellationToken token = default,
            bool isPreload = false)
        {
            var pdfSession = CurrentPdfSession;
            return pdfSession == null
                ? null
                : await pdfSession.LoadPageBitmapAsync(
                    pageIndex,
                    canvas,
                    _zoomLevel,
                    () => _isWindowClosing,
                    token,
                    isPreload);
        }

        private void PdfTocButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfDocument == null) return;

            MainToolbar.SetPdfTocTitle(Strings.TocTitle);

            var items = _tocService.CurrentToc;

            // Highlight current item and scroll
            int currentIndex = -1;

            if (items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= _currentIndex)
                        currentIndex = i;
                    else
                        break;
                }
            }

            // Create a display-only list
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

            MainToolbar.SetPdfTocItems(displayItems);
            
            if (currentIndex >= 0)
            {
                MainToolbar.ScrollPdfTocIntoView(displayItems[currentIndex]);
            }
        }

        private void PdfTocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                MainToolbar.HidePdfTocFlyout();
                
                if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageEntries.Count)
                {
                    _currentIndex = item.SourceLineNumber;
                    _ = DisplayCurrentImageAsync();
                }
            }
        }

        private async Task RerenderPdfCurrentPageAsync()
        {
            try
            {
                if (_isWindowClosing) return;
                var pdfSession = CurrentPdfSession;
                if (pdfSession?.HasDocument != true || _currentBitmap == null) return;
                if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count) return;

                var entry = _imageEntries[_currentIndex];
                if (!entry.IsPdfEntry) return;

                var token = pdfSession.RestartZoomRerender();

                // [Important] Capture index before await to avoid race condition if _currentIndex changes during render
                int capturedIndex = _currentIndex;
                int capturedPdfGeneration = pdfSession.Generation;
                string? capturedPdfPath = pdfSession.SourcePath;

                await Task.Delay(350, token);
                if (!IsCurrentPdfScope(capturedPdfGeneration, capturedPdfPath) || token.IsCancellationRequested) return;

                var canvas = MainCanvas;
                var newBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, canvas, token, isPreload: false);

                if (_isWindowClosing || token.IsCancellationRequested || newBitmap == null ||
                    !IsCurrentPdfScope(capturedPdfGeneration, capturedPdfPath))
                {
                    if (newBitmap != null) _imageCache.SafeDisposeBitmap(newBitmap);
                    return;
                }

                // [Important] If index changed while rendering, discard this result as it's no longer the "current" page
                if (capturedIndex != _currentIndex)
                {
                    _imageCache.SafeDisposeBitmap(newBitmap);
                    return;
                }

                var oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;
                _imageCache.UpdateCache(capturedIndex, newBitmap, true, _zoomLevel, oldBitmap);

                MainCanvas.Invalidate();
                UpdateStatusBar(entry, _currentBitmap);

                // [추가] 현재 페이지 렌더링이 끝난 직후, 변경된 줌 레벨로 다음/이전 페이지들을 백그라운드에서 다시 그리도록 지시합니다.
                _ = _preloadManager.StartPreloadAsync(
                    _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                    _currentBitmap, _leftBitmap, _rightBitmap,
                    LoadBitmapForPreloadAsync,
                    () => MainCanvas?.Invalidate(),
                    prioritizeNext: true,
                    requireSharpening: _sharpenEnabled);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF rerender skipped: {ex.Message}");
            }
        }
    }
}
