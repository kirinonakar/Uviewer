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
        private UglyToad.PdfPig.PdfDocument? _pdfPigDocument; // For parsing TOC
        private List<TocItem> _pdfToc = new();
        private string? _currentPdfPath;
        private readonly SemaphoreSlim _pdfLock = new(1, 1);

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

                    // Load TOC with PdfPig
                    try
                    {
                        _pdfToc.Clear();
                        _pdfPigDocument = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
                        if (_pdfPigDocument.TryGetBookmarks(out var bookmarks))
                        {
                            ParsePdfBookmarks(bookmarks.GetNodes().ToList(), 1);
                        }
                        DispatcherQueue.TryEnqueue(() =>
                        {
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

        private void CloseCurrentPdf()
        {
            if (_currentPdfDocument == null && _pdfPigDocument == null) return;

            if (_pdfLock.Wait(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    CloseCurrentPdfInternal();
                }
                finally
                {
                    _pdfLock.Release();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("PDF lock timeout - forcing cleanup");
                if (_currentPdfDocument != null)
                {
                    _currentPdfDocument = null;
                    _currentPdfPath = null;
                }
            }
        }

        private void CloseCurrentPdfInternal()
        {
            if (_currentPdfDocument != null)
            {
                _currentPdfDocument = null;
            }

            if (_pdfPigDocument != null)
            {
                _pdfPigDocument.Dispose();
                _pdfPigDocument = null;
            }

            _currentPdfPath = null;
            _pdfToc.Clear();

            // UI 스레드에서 버튼 가리기
            DispatcherQueue.TryEnqueue(() =>
            {
                if (PdfTocButton != null)
                {
                    PdfTocButton.Visibility = Visibility.Collapsed;
                }
                if (PdfGoToPageButton != null)
                {
                    PdfGoToPageButton.Visibility = Visibility.Collapsed;
                }
                if (PdfSeparator != null)
                {
                    PdfSeparator.Visibility = Visibility.Collapsed;
                }
            });

            // Title updating handles securely inside CloseCurrentArchiveInternal already, so here we might just duplicate or skip if handled generally.
            DispatcherQueue.TryEnqueue(() =>
            {
                Title = "Uviewer - Image & Text Viewer";
            });

            lock (_preloadedImages)
            {
                foreach (var bitmap in _preloadedImages.Values)
                {
                    bitmap?.Dispose();
                }
                _preloadedImages.Clear();
            }

            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    bitmap?.Dispose();
                }
                _sharpenedImageCache.Clear();
            }

            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts?.Dispose();
            _fastNavigationResetCts = null;
        }

        private async Task<CanvasBitmap?> LoadPdfPageBitmapAsync(uint pageIndex, CanvasControl canvas, CancellationToken token = default)
        {
            if (_currentPdfDocument == null || pageIndex >= _currentPdfDocument.PageCount) return null;

            try
            {
                // PDF pages can be large, get it with default options or specify size.
                using var pdfPage = _currentPdfDocument.GetPage(pageIndex);
                
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                
                // Render with preferred options (e.g., higher DPI if needed, but default is fine for display)
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)pdfPage.Size.Width * 2, // 2x scale for better quality on zoom
                    DestinationHeight = (uint)pdfPage.Size.Height * 2
                };
                
                await pdfPage.RenderToStreamAsync(stream, options);
                
                if (token.IsCancellationRequested) return null;

                stream.Seek(0);
                var bitmap = await CanvasBitmap.LoadAsync(canvas, stream);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF page: {ex.Message}");
                return null;
            }
        }

        private void ParsePdfBookmarks(IReadOnlyList<BookmarkNode> nodes, int level)
        {
            foreach (var node in nodes)
            {
                int pageIndex = -1;
                
                if (node is DocumentBookmarkNode docNode)
                {
                    // PdfPig uses 1-based page numbers
                    pageIndex = docNode.PageNumber - 1;
                }

                _pdfToc.Add(new TocItem 
                { 
                    HeadingText = node.Title, 
                    HeadingLevel = level, 
                    SourceLineNumber = pageIndex, // We'll repurpose SourceLineNumber for page index in PDF context
                    Tag = "PDF"
                });

                if (node.Children != null && node.Children.Count > 0)
                {
                    ParsePdfBookmarks(node.Children, level + 1);
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
