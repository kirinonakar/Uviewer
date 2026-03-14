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
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private Windows.Data.Pdf.PdfDocument? _currentPdfDocument;
        private List<TocItem> _pdfToc = new();
        private string? _currentPdfPath;
        private readonly SemaphoreSlim _pdfLock = new(1, 1);
        private readonly SemaphoreSlim _pdfRenderSemaphore = new(1); // Limit concurrent rendering to avoid UI freeze

        private async Task LoadImagesFromPdfAsync(string pdfPath)
        {
            _preloadCts?.Cancel();
            _imageLoadingCts?.Cancel(); // Cancel any ongoing image load
            _thumbnailLoadingCts?.Cancel(); // Cancel thumbnail loading
            
            SwitchToImageMode(); // Force UI to image mode immediately

            // Close other formats first - outside the lock to avoid double-locking/deadlocks
            if (_currentArchive != null) CloseCurrentArchive();
            if (_currentEpubFilePath != null) CloseCurrentEpub();

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

                    // Load TOC with PdfPig in background
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var tempToc = new List<TocItem>();
                            using var pdfPigDocument = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
                            if (pdfPigDocument.TryGetBookmarks(out var bookmarks))
                            {
                                ParsePdfBookmarks(bookmarks.GetNodes().ToList(), 1, tempToc);
                            }
                            
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                _pdfToc = tempToc;
                                if (PdfTocButton != null)
                                {
                                    PdfTocButton.Visibility = Visibility.Visible;
                                }
                                if (PdfGoToPageButton != null)
                                {
                                    PdfGoToPageButton.Visibility = Visibility.Visible;
                                }
                                if (PdfSeparator != null)
                                {
                                    PdfSeparator.Visibility = Visibility.Visible;
                                }
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

                SideBySideToolbarPanel.Visibility = Visibility.Collapsed;
                SharpenButton.Visibility = Visibility.Collapsed;

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

                    _preloadCts?.Cancel();
                    _preloadCts?.Dispose();
                    _preloadCts = new CancellationTokenSource();
                    var token = _preloadCts.Token;
                    _ = Task.Run(() => PreloadNextImagesAsync(token));

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

        private async Task CloseCurrentPdfAsync() // 동기 메서드 대신 비동기로 변경하여 UI 프리징 방지
        {
            if (_currentPdfDocument == null) return;

            // UI 스레드를 막지 않고 비동기로 락 획득 대기
            bool lockAcquired = await _pdfLock.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                CloseCurrentPdfInternal();
            }
            finally
            {
                if (lockAcquired) _pdfLock.Release();
            }
        }

        private void CloseCurrentPdfInternal()
        {
            // PDF Document 참조 해제
            var oldDoc = _currentPdfDocument;
            _currentPdfDocument = null;
            _currentPdfPath = null;
            _pdfToc.Clear();

            // UI 스레드에서 버튼 가리기
            DispatcherQueue.TryEnqueue(() =>
            {
                if (PdfTocButton != null) PdfTocButton.Visibility = Visibility.Collapsed;
                if (PdfGoToPageButton != null) PdfGoToPageButton.Visibility = Visibility.Collapsed;
                if (PdfSeparator != null) PdfSeparator.Visibility = Visibility.Collapsed;
                Title = "Uviewer - Image & Text Viewer";
            });

            // 딕셔너리 안전한 초기화 (크래시 방지)
            lock (_preloadedImages)
            {
                foreach (var bitmap in _preloadedImages.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _preloadedImages.Clear();
            }

            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _sharpenedImageCache.Clear();
            }

            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts?.Dispose();
            _fastNavigationResetCts = null;

            _currentBitmap = null;
            _leftBitmap = null;
            _rightBitmap = null;
        }

        private async Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default)
        {
            // 로컬 변수에 캡처하여 도중 _currentPdfDocument가 null이 되어도 크래시 방지
            var pdfDoc = _currentPdfDocument;
            if (pdfDoc == null || pageIndex >= pdfDoc.PageCount) return null;

            try
            {
                // 취소 요청이 들어왔다면 세마포어 대기 전에 빠른 반환
                if (token.IsCancellationRequested) return null;

                using var pdfPage = pdfDoc.GetPage(pageIndex);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                
                double scale = 1.0;
                double canvasWidth = DispatcherQueue.HasThreadAccess ? canvas.ActualWidth : _lastCanvasWidth;

                if (canvasWidth > 0 && pdfPage.Size.Width > 0)
                {
                    scale = canvasWidth / pdfPage.Size.Width;
                }
                
                scale = Math.Max(1.0, Math.Min(scale * 1.5, 2.0));

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)(pdfPage.Size.Width * scale),
                    DestinationHeight = (uint)(pdfPage.Size.Height * scale)
                };
                
                // 세마포어 대기 중 취소될 수 있으므로 token 전달
                await _pdfRenderSemaphore.WaitAsync(token);
                try
                {
                    if (token.IsCancellationRequested) return null;

                    // 핵심 수정: AsTask(token)을 사용하여 네이티브 렌더링 중에도 취소 신호에 반응하도록 함
                    await pdfPage.RenderToStreamAsync(stream, options).AsTask(token);
                }
                finally
                {
                    _pdfRenderSemaphore.Release();
                }
                
                if (token.IsCancellationRequested) return null;

                stream.Seek(0);
                
                // 스레드 민첩성을 위해 CanvasDevice 직접 가져오기 (Background Thread Crash 방지)
                var device = canvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
                var bitmap = await CanvasBitmap.LoadAsync(device, stream);
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

        private void ParsePdfBookmarks(IReadOnlyList<BookmarkNode> nodes, int level, List<TocItem> targetList)
        {
            foreach (var node in nodes)
            {
                int pageIndex = -1;
                
                if (node is DocumentBookmarkNode docNode)
                {
                    // PdfPig uses 1-based page numbers
                    pageIndex = docNode.PageNumber - 1;
                }

                targetList.Add(new TocItem 
                { 
                    HeadingText = node.Title, 
                    HeadingLevel = level, 
                    SourceLineNumber = pageIndex, // We'll repurpose SourceLineNumber for page index in PDF context
                    Tag = "PDF"
                });

                if (node.Children != null && node.Children.Count > 0)
                {
                    ParsePdfBookmarks(node.Children, level + 1, targetList);
                }
            }
        }

        private void PdfTocButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfDocument == null) return;

            // Ensure TOC Title
            if (PdfTocFlyout.Content is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock tb)
            {
                tb.Text = Strings.TocTitle;
            }

            List<TocItem> items = _pdfToc.ToList();

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

            if (currentIndex >= 0 && currentIndex < items.Count)
            {
                items[currentIndex].HeadingText = "⮕ " + items[currentIndex].HeadingText;
            }

            if (items.Count == 0)
            {
                items.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
            }

            PdfTocListView.ItemsSource = items;
            
            if (currentIndex >= 0)
            {
                // Ensure layout updated before scrolling
                this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        PdfTocListView.ScrollIntoView(items[currentIndex], ScrollIntoViewAlignment.Leading);
                    }
                    catch { }
                });
            }
        }

        private void PdfTocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                PdfTocFlyout.Hide();
                
                if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageEntries.Count)
                {
                    _currentIndex = item.SourceLineNumber;
                    _ = DisplayCurrentImageAsync();
                }
            }
        }
    }
}
