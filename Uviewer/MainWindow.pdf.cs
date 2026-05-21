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
using Windows.Data.Pdf;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private Windows.Data.Pdf.PdfDocument? _currentPdfDocument;
        private string? _currentPdfPath;
        private readonly SemaphoreSlim _pdfLock = new(1, 1);

        // [최적화] 세마포어를 두 개로 분리
        // - _pdfRenderSemaphore: 백그라운드 프리로드 전용 (동시 3개)
        // - _pdfCurrentPageSemaphore: 현재 페이지 렌더링 전용 (동시 2개, 프리로드와 독립)
        // 이전: 모든 렌더링이 하나의 세마포어(2)를 공유 → 프리로드가 현재 페이지 렌더를 블록
        private readonly SemaphoreSlim _pdfRenderSemaphore = new(3);
        private readonly SemaphoreSlim _pdfCurrentPageSemaphore = new(2, 2);


        private CancellationTokenSource? _pdfZoomRerenderCts;

        private async Task LoadImagesFromPdfAsync(string pdfPath)
        {
            if (_isWindowClosing) return;

            _currentPdfPath = pdfPath;
            _documentSearchService.Clear();
            _preloadManager.CancelAll();
            _imageLoadingCts?.Cancel(); // Cancel any ongoing image load
            
            SwitchToImageMode(); // Force UI to image mode immediately

            // Close other formats first - outside the lock to avoid double-locking/deadlocks
            if (!await CloseCurrentArchiveAsync()) return;
            if (!await CloseCurrentEpubAsync()) return;

            try
            {
                await _pdfLock.WaitAsync();
                try
                {
                    CloseCurrentPdfInternal();
                    _currentPdfPath = pdfPath;


                    var file = await StorageFile.GetFileFromPathAsync(pdfPath);
                    _currentPdfDocument = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

                    var newEntries = new List<ImageEntry>();
                    for (uint i = 0; i < _currentPdfDocument.PageCount; i++)
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

                    // Load TOC with PdfPig in background
                    string tocPdfPath = pdfPath;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _tocService.SetProvider(new PdfTocProvider(tocPdfPath));
                            await _tocService.LoadTocAsync();

                            if (!IsCurrentPdfPath(tocPdfPath)) return;
                            
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (!IsCurrentPdfPath(tocPdfPath)) return;

                                MainToolbar.SetPdfTocVisible(true);
                            });
                        }
                        catch (Exception tocEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error reading PDF TOC: {tocEx.Message}");
                        }
                    });
                }
                finally
                {
                    _pdfLock.Release();
                }

                MainToolbar.SetSideBySideToolbarVisible(false);
                MainToolbar.SetSharpenControlsVisible(false);

                // Reset PDF view state for the new document
                _zoomLevel = 1.0;
                _pdfPanX = 0;
                _pdfPanY = 0;
                _pdfScrollDirection = 1; // Start from top

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
                FileNameText.Text = $"PDF 열기 실패: {ex.Message}";
            }
        }

        private async Task<bool> CloseCurrentPdfAsync() // 동기 메서드 대신 비동기로 변경하여 UI 프리징 방지
        {
            CancelPdfOperations();
            if (_currentPdfDocument == null) return true;

            // UI 스레드를 막지 않고 비동기로 락 획득 대기
            bool lockAcquired = await _pdfLock.WaitAsync(TimeSpan.FromSeconds(10));
            if (!lockAcquired)
            {
                System.Diagnostics.Debug.WriteLine("PDF lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseCurrentPdfInternal();
                return true;
            }
            finally
            {
                _pdfLock.Release();
            }
        }

        private void CloseCurrentPdfInternal()
        {
            CancelPdfOperations();

            // PDF Document 참조 해제
            _currentPdfPath = null;
            var oldDoc = _currentPdfDocument;
            _currentPdfDocument = null;
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
            return _currentPdfDocument != null &&
                string.Equals(_currentPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase);
        }

        private void CancelPdfOperations()
        {
            try { _pdfZoomRerenderCts?.Cancel(); } catch { }
            try { _preloadManager?.CancelAll(); } catch { }
            try { _smoothZoomTimer?.Stop(); } catch { }
        }

        private void ShutdownPdfResources()
        {
            CancelPdfOperations();
            _currentPdfPath = null;
            _currentPdfDocument = null;
            _tocService.Clear();
            _fastNavigationService?.StopTimers();
            _imageViewerState.ClearBitmaps();
        }

        /// <summary>
        /// PDF 페이지를 비트맵으로 렌더링합니다.
        /// </summary>
        /// <param name="pageIndex">렌더링할 페이지 인덱스</param>
        /// <param name="canvas">Win2D 캔버스</param>
        /// <param name="token">취소 토큰</param>
        /// <param name="isPreload">true이면 백그라운드 프리로드용 세마포어(_pdfRenderSemaphore)를 사용.
        ///   false(기본값)이면 현재 페이지 우선 세마포어(_pdfCurrentPageSemaphore)를 사용하여
        ///   프리로드에 의해 블록되지 않음.</param>
        /// <param name="isPreview">true이면 저해상도(최대 1200px)로 렌더링. 빠른 프리로드용.
        ///   현재 페이지로 이동 시 자동으로 풀 해상도로 업그레이드됨.</param>
        private async Task<CanvasBitmap?> LoadPdfPageBitmapAsync(
            uint pageIndex,
            CanvasControl canvas,
            CancellationToken token = default,
            bool isPreload = false)
        {
            if (_isWindowClosing || token.IsCancellationRequested) return null;

            // 로컬 변수에 캡처하여 도중 _currentPdfDocument가 null이 되어도 크래시 방지
            var pdfDoc = _currentPdfDocument;
            if (pdfDoc == null || pageIndex >= pdfDoc.PageCount) return null;

            try
            {
                if (token.IsCancellationRequested) return null;

                using var pdfPage = pdfDoc.GetPage(pageIndex);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

                // [최적화] 실제 모니터 표시 영역(DIP)에 맞춘 동적 해상도 결정
                double targetWidth;
                float currentDpiScale = canvas.Dpi / 96.0f;
                if (currentDpiScale <= 0) currentDpiScale = 1.0f;

                // 실제 캔버스 크기와 줌 배율을 고려하여 렌더링 해상도(DIP) 결정
                // [수정] WinUI 3 환경에서는 PdfPageRenderOptions 설정 시 시스템 DPI 배율이 자동으로 적용될 수 있으므로,
                // 타겟 해상도를 물리 픽셀이 아닌 DIP 단위로 계산하여 중복 확대를 방지합니다.
                double canvasWidth = canvas.Size.Width;
                double canvasHeight = canvas.Size.Height;

                // 캔버스 크기가 유효하지 않을 경우 기본값 사용
                if (canvasWidth <= 0) canvasWidth = 1000;
                if (canvasHeight <= 0) canvasHeight = 1000;

                double pageAR = pdfPage.Size.Width / pdfPage.Size.Height;
                double canvasAR = canvasWidth / canvasHeight;

                double visibleWidthInDips;
                if (pageAR > canvasAR)
                    visibleWidthInDips = canvasWidth;
                else
                    visibleWidthInDips = canvasHeight * pageAR;

                // 화면에 표시되는 영역(DIP)만큼 렌더링 (줌 배율 반영)
                targetWidth = visibleWidthInDips * _zoomLevel;

                // 품질과 메모리 사용량의 균형을 위해 임계값 적용 (물리 픽셀 기준 1920px~6016px을 DIP로 변환)
                double minDip = 1920.0 / currentDpiScale;
                double maxDip = 6016.0 / currentDpiScale;
                targetWidth = Math.Clamp(targetWidth, minDip, maxDip);

                double scale = 1.0;
                if (pdfPage.Size.Width > 0)
                {
                    // DestinationWidth에 DIP 단위를 전달하면 시스템이 현재 DPI에 맞춰 물리 픽셀로 변환하여 렌더링합니다.
                    scale = targetWidth / pdfPage.Size.Width;
                }

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Round(pdfPage.Size.Width * scale),
                    DestinationHeight = (uint)Math.Round(pdfPage.Size.Height * scale)
                };

                // [최적화] 세마포어 분리:
                // - isPreload=true → _pdfRenderSemaphore (백그라운드, 동시 3개)
                // - isPreload=false → _pdfCurrentPageSemaphore (우선 처리, 동시 2개, 프리로드 블록 없음)
                var semaphore = isPreload ? _pdfRenderSemaphore : _pdfCurrentPageSemaphore;
                await semaphore.WaitAsync(token);
                try
                {
                    if (_isWindowClosing || token.IsCancellationRequested) return null;
                    await pdfPage.RenderToStreamAsync(stream, options).AsTask(token);
                }
                finally
                {
                    semaphore.Release();
                }

                if (_isWindowClosing || token.IsCancellationRequested) return null;

                stream.Seek(0);

                // 스레드 민첩성을 위해 CanvasDevice 직접 가져오기 (Background Thread Crash 방지)
                var device = canvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
                // 스트림에 담긴 픽셀을 HiDPI 보정 없이 1:1로 읽기 위해 96 DPI 옵션 명시
                var bitmap = await CanvasBitmap.LoadAsync(device, stream, 96.0f);
                if (_isWindowClosing || token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return null;
                }
                return bitmap;
            }
            catch (OperationCanceledException)
            {
                return null; // 정상적인 취소
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF page: {ex.Message}");
                return null;
            }
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
                if (_currentPdfDocument == null || _currentBitmap == null) return;
                if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count) return;

                var entry = _imageEntries[_currentIndex];
                if (!entry.IsPdfEntry) return;

                _pdfZoomRerenderCts?.Cancel();
                _pdfZoomRerenderCts = new CancellationTokenSource();
                var token = _pdfZoomRerenderCts.Token;

                // [Important] Capture index before await to avoid race condition if _currentIndex changes during render
                int capturedIndex = _currentIndex;

                var canvas = MainCanvas;
                // [최적화] isPreload=false → _pdfCurrentPageSemaphore 사용으로 프리로드에 의해 블록되지 않음
                var newBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, canvas, token, isPreload: false);

                if (_isWindowClosing || token.IsCancellationRequested || newBitmap == null)
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
