using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;

namespace Uviewer.Services
{
    public sealed partial class PdfDocumentSession
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        // High-resolution PDF rendering is CPU and memory-bandwidth intensive. Keep
        // speculative work serialized so it cannot contend with the visible page.
        private readonly SemaphoreSlim _preloadRenderSemaphore = new(1, 1);
        private readonly SemaphoreSlim _currentPageRenderSemaphore = new(2, 2);

        private CancellationTokenSource? _zoomRerenderCts;
        private CancellationTokenSource? _documentCts;
        private int _generation;

        public PdfDocument? Document { get; private set; }
        public bool HasDocument => Document != null;
        public uint PageCount => Document?.PageCount ?? 0;
        public int Generation => Volatile.Read(ref _generation);
        public CancellationToken DocumentToken => _documentCts?.Token ?? CancellationToken.None;

        public override Task OpenAsync(CancellationToken token) => LoadFileAsync(token);

        public async Task LoadFileAsync(CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(SourcePath))
            {
                return;
            }

            await _lock.WaitAsync(token);
            try
            {
                CloseInternal();

                var file = await StorageFile.GetFileFromPathAsync(SourcePath);
                StartNewDocumentScope();
                Document = await PdfDocument.LoadFromFileAsync(file);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> CloseAsync(TimeSpan timeout)
        {
            CancelOperations();
            if (Document == null) return true;

            if (!await _lock.WaitAsync(timeout))
            {
                System.Diagnostics.Debug.WriteLine("PDF lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseInternal();
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Shutdown()
        {
            CancelOperations();
            Document = null;
        }

        public void CancelOperations()
        {
            try { _zoomRerenderCts?.Cancel(); } catch { }
            try { _documentCts?.Cancel(); } catch { }
        }

        public CancellationToken RestartZoomRerender()
        {
            _zoomRerenderCts?.Cancel();
            _zoomRerenderCts = CancellationTokenSource.CreateLinkedTokenSource(DocumentToken);
            return _zoomRerenderCts.Token;
        }

        public bool IsCurrentPath(string pdfPath)
        {
            return Document != null &&
                string.Equals(SourcePath, pdfPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCurrentScope(int generation, string? pdfPath)
        {
            return Document != null &&
                Volatile.Read(ref _generation) == generation &&
                (pdfPath == null || string.Equals(SourcePath, pdfPath, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsPageBitmapResolutionSufficient(
            uint pageIndex,
            CanvasControl canvas,
            double zoomLevel,
            CanvasBitmap bitmap)
        {
            var pdfDoc = Document;
            if (pdfDoc == null || pageIndex >= pdfDoc.PageCount) return false;

            try
            {
                using var pdfPage = pdfDoc.GetPage(pageIndex);
                var (width, height) = CalculateRenderDimensions(pdfPage, canvas, zoomLevel);
                var bitmapSize = bitmap.SizeInPixels;

                // A larger existing render is at least as sharp as a newly requested
                // smaller one, so it can be reused without reducing visual quality.
                return bitmapSize.Width + 0.5 >= width && bitmapSize.Height + 0.5 >= height;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CanvasBitmap?> LoadPageBitmapAsync(
            uint pageIndex,
            CanvasControl canvas,
            double zoomLevel,
            Func<bool> isWindowClosing,
            CancellationToken token = default,
            bool isPreload = false)
        {
            if (isWindowClosing() || token.IsCancellationRequested) return null;

            var pdfDoc = Document;
            int pdfGenerationAtStart = Volatile.Read(ref _generation);
            string? pdfPathAtStart = SourcePath;
            if (pdfDoc == null || pageIndex >= pdfDoc.PageCount) return null;

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    token,
                    DocumentToken);
                var linkedToken = linkedCts.Token;

                if (linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart)) return null;

                using var pdfPage = pdfDoc.GetPage(pageIndex);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

                var (destinationWidth, destinationHeight) =
                    CalculateRenderDimensions(pdfPage, canvas, zoomLevel);

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = destinationWidth,
                    DestinationHeight = destinationHeight
                };

                var semaphore = isPreload ? _preloadRenderSemaphore : _currentPageRenderSemaphore;
                await semaphore.WaitAsync(linkedToken);
                try
                {
                    if (isWindowClosing() || linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart)) return null;

                    var renderOperation = pdfPage.RenderToStreamAsync(stream, options);
                    using var renderCancel = linkedToken.Register(() =>
                    {
                        try { renderOperation.Cancel(); }
                        catch { }
                    });
                    await renderOperation.AsTask(linkedToken);

                    if (isWindowClosing() || linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart)) return null;

                    stream.Seek(0);

                    var device = canvas.Device ?? CanvasDevice.GetSharedDevice();
                    var loadOperation = CanvasBitmap.LoadAsync(device, stream, 96.0f);
                    using var loadCancel = linkedToken.Register(() =>
                    {
                        try { loadOperation.Cancel(); }
                        catch { }
                    });
                    var bitmap = await loadOperation.AsTask(linkedToken);
                    if (isWindowClosing() || linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart))
                    {
                        bitmap.Dispose();
                        return null;
                    }

                    return bitmap;
                }
                finally
                {
                    // Include stream decoding in the concurrency budget. Otherwise
                    // several 6K PNG decodes can run after the render slots are freed.
                    semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF page: {ex.Message}");
                return null;
            }
        }

        private static (uint Width, uint Height) CalculateRenderDimensions(
            PdfPage pdfPage,
            CanvasControl canvas,
            double zoomLevel)
        {
            float currentDpiScale = canvas.Dpi / 96.0f;
            if (currentDpiScale <= 0) currentDpiScale = 1.0f;

            double canvasWidth = canvas.Size.Width;
            double canvasHeight = canvas.Size.Height;

            if (canvasWidth <= 0) canvasWidth = 1000;
            if (canvasHeight <= 0) canvasHeight = 1000;

            double pageAR = pdfPage.Size.Width / pdfPage.Size.Height;
            double canvasAR = canvasWidth / canvasHeight;
            double visibleWidthInDips = pageAR > canvasAR
                ? canvasWidth
                : canvasHeight * pageAR;

            double targetWidth = visibleWidthInDips * zoomLevel;
            double minDip = 1920.0 / currentDpiScale;
            double maxDip = 6016.0 / currentDpiScale;
            targetWidth = Math.Clamp(targetWidth, minDip, maxDip);

            double scale = pdfPage.Size.Width > 0
                ? targetWidth / pdfPage.Size.Width
                : 1.0;

            return (
                Math.Max(1, (uint)Math.Round(pdfPage.Size.Width * scale)),
                Math.Max(1, (uint)Math.Round(pdfPage.Size.Height * scale)));
        }

        private void StartNewDocumentScope()
        {
            try { _documentCts?.Cancel(); } catch { }
            _documentCts = new CancellationTokenSource();
            Interlocked.Increment(ref _generation);
        }

        private void CloseInternal()
        {
            CancelOperations();
            Document = null;
        }
    }
}
