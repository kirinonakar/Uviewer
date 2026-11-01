using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Uviewer.Services
{
    public sealed partial class PdfDocumentSession
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private const int PreloadRenderSlots = 2;
        private readonly SemaphoreSlim _preloadRenderSemaphore = new(PreloadRenderSlots, PreloadRenderSlots);
        private readonly SemaphoreSlim _currentPageRenderSemaphore = new(2, 2);

        private CancellationTokenSource? _zoomRerenderCts;
        private CancellationTokenSource? _documentCts;
        private PdfiumPdfDocument? _pdfiumDocument;
        private int _generation;

        // HRESULT_FROM_WIN32(ERROR_WRONG_PASSWORD): 비밀번호 누락/불일치 시 Windows.Data.Pdf가 반환한다.
        public const int WrongPasswordHResult = unchecked((int)0x8007052B);

        // E_FAIL: 비밀번호가 필요한 PDF를 비밀번호 없이 열 때 반환될 수 있는 일반 실패 코드.
        public const int GenericFailHResult = unchecked((int)0x80004005);

        // Windows.Data.Pdf returns this for unsupported PDF variants, including
        // AES-256 encrypted PDFs on Windows versions where that format is unsupported.
        public const int UnsupportedPdfHResult = unchecked((int)0x80048040);

        public PdfDocument? Document { get; private set; }
        public bool HasDocument => Document != null || _pdfiumDocument != null;
        public uint PageCount => Document?.PageCount ?? (uint)(_pdfiumDocument?.PageCount ?? 0);
        public int Generation => Volatile.Read(ref _generation);
        public CancellationToken DocumentToken => _documentCts?.Token ?? CancellationToken.None;
        public string? Password { get; private set; }

        public override Task OpenAsync(CancellationToken token) => LoadFileAsync(token);

        public Task LoadFileAsync(CancellationToken token = default) => LoadFileAsync(null, false, token);

        public async Task LoadFileAsync(
            string? password,
            bool usePdfium = false,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(SourcePath))
            {
                return;
            }

            await _lock.WaitAsync(token);
            try
            {
                CloseInternal();

                StartNewDocumentScope();
                if (usePdfium)
                {
                    _pdfiumDocument = await Task.Run(
                        () => PdfiumPdfDocument.Open(SourcePath, password),
                        token);
                }
                else
                {
                    var file = await StorageFile.GetFileFromPathAsync(SourcePath);
                    Document = string.IsNullOrEmpty(password)
                        ? await PdfDocument.LoadFromFileAsync(file)
                        : await PdfDocument.LoadFromFileAsync(file, password);
                }

                Password = password;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> CloseAsync(TimeSpan timeout)
        {
            CancelOperations();
            using var timeoutCts = new CancellationTokenSource(timeout);
            bool lockHeld = false;
            int preloadSlotsHeld = 0;
            int currentSlotsHeld = 0;
            try
            {
                await _lock.WaitAsync(timeoutCts.Token);
                lockHeld = true;
                CancelOperations();

                // Cancellation only requests shutdown. Wait until native rendering,
                // decoding, and their page/stream disposal have actually completed
                // before dropping the document and collecting its memory.
                for (; preloadSlotsHeld < PreloadRenderSlots; preloadSlotsHeld++)
                {
                    await _preloadRenderSemaphore.WaitAsync(timeoutCts.Token);
                }
                for (; currentSlotsHeld < 2; currentSlotsHeld++)
                {
                    await _currentPageRenderSemaphore.WaitAsync(timeoutCts.Token);
                }

                CloseInternal();
                return true;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("PDF close timed out waiting for active rendering to stop");
                return false;
            }
            finally
            {
                if (currentSlotsHeld > 0) _currentPageRenderSemaphore.Release(currentSlotsHeld);
                if (preloadSlotsHeld > 0) _preloadRenderSemaphore.Release(preloadSlotsHeld);
                if (lockHeld) _lock.Release();
            }
        }

        public void Shutdown()
        {
            CancelOperations();
            Document = null;
            _pdfiumDocument?.Dispose();
            _pdfiumDocument = null;
            Password = null;
        }

        public void CancelOperations()
        {
            try { _zoomRerenderCts?.Cancel(); } catch { }
            try { _documentCts?.Cancel(); } catch { }
        }

        public CancellationToken RestartZoomRerender()
        {
            _zoomRerenderCts?.Cancel();
            _zoomRerenderCts?.Dispose();
            _zoomRerenderCts = CancellationTokenSource.CreateLinkedTokenSource(DocumentToken);
            return _zoomRerenderCts.Token;
        }

        public bool IsCurrentPath(string pdfPath)
        {
            return HasDocument &&
                string.Equals(SourcePath, pdfPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCurrentScope(int generation, string? pdfPath)
        {
            return HasDocument &&
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
            var pdfiumDoc = _pdfiumDocument;
            if (pdfDoc == null && pdfiumDoc == null) return false;
            if (pageIndex >= PageCount) return false;

            try
            {
                double pageWidth;
                double pageHeight;
                if (pdfDoc != null)
                {
                    using var pdfPage = pdfDoc.GetPage(pageIndex);
                    pageWidth = pdfPage.Size.Width;
                    pageHeight = pdfPage.Size.Height;
                }
                else
                {
                    var pageSize = pdfiumDoc!.GetPageSize(pageIndex);
                    pageWidth = pageSize.Width;
                    pageHeight = pageSize.Height;
                }

                var (width, height) = CalculateRenderDimensions(
                    pageWidth,
                    pageHeight,
                    canvas,
                    zoomLevel);
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
            var pdfiumDoc = _pdfiumDocument;
            int pdfGenerationAtStart = Volatile.Read(ref _generation);
            string? pdfPathAtStart = SourcePath;
            if ((pdfDoc == null && pdfiumDoc == null) || pageIndex >= PageCount) return null;

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    token,
                    DocumentToken);
                var linkedToken = linkedCts.Token;

                if (linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart)) return null;

                var semaphore = isPreload ? _preloadRenderSemaphore : _currentPageRenderSemaphore;
                await semaphore.WaitAsync(linkedToken);
                try
                {
                    if (isWindowClosing() || linkedToken.IsCancellationRequested || !IsCurrentScope(pdfGenerationAtStart, pdfPathAtStart)) return null;

                    if (pdfiumDoc != null)
                    {
                        return await LoadPdfiumPageBitmapAsync(
                            pdfiumDoc,
                            pageIndex,
                            canvas,
                            zoomLevel,
                            isWindowClosing,
                            pdfGenerationAtStart,
                            pdfPathAtStart,
                            linkedToken,
                            isPreload);
                    }

                    using var pdfPage = pdfDoc!.GetPage(pageIndex);
                    using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    var (destinationWidth, destinationHeight) =
                        CalculateRenderDimensions(pdfPage, canvas, zoomLevel, isPreload);
                    var options = new PdfPageRenderOptions
                    {
                        DestinationWidth = destinationWidth,
                        DestinationHeight = destinationHeight,
                        // Avoid PNG compression/decompression for an in-memory image.
                        BitmapEncoderId = BitmapEncoder.BmpEncoderId
                    };

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
                    // several large bitmap decodes can run after render slots are freed.
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

        private async Task<CanvasBitmap?> LoadPdfiumPageBitmapAsync(
            PdfiumPdfDocument pdfiumDoc,
            uint pageIndex,
            CanvasControl canvas,
            double zoomLevel,
            Func<bool> isWindowClosing,
            int pdfGenerationAtStart,
            string? pdfPathAtStart,
            CancellationToken token,
            bool isPreload)
        {
            var pageSize = pdfiumDoc.GetPageSize(pageIndex);
            var (destinationWidth, destinationHeight) = CalculateRenderDimensions(
                pageSize.Width,
                pageSize.Height,
                canvas,
                zoomLevel,
                isPreload);

            // Render directly into managed BGRA pixels off the UI thread, then upload
            // once to Win2D without an intermediate bitmap or PNG encode/decode.
            var pixels = await Task.Run(
                () => pdfiumDoc.Render(pageIndex, destinationWidth, destinationHeight, token), token);

            if (isWindowClosing() || token.IsCancellationRequested ||
                !IsCurrentScopeForRender(pdfiumDoc, pdfGenerationAtStart, pdfPathAtStart))
            {
                return null;
            }

            var device = canvas.Device ?? CanvasDevice.GetSharedDevice();
            var bitmap = CanvasBitmap.CreateFromBytes(device, pixels,
                checked((int)destinationWidth), checked((int)destinationHeight),
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 96.0f, CanvasAlphaMode.Premultiplied);

            if (isWindowClosing() || token.IsCancellationRequested ||
                !IsCurrentScopeForRender(pdfiumDoc, pdfGenerationAtStart, pdfPathAtStart))
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }

        private bool IsCurrentScopeForRender(
            PdfiumPdfDocument pdfiumDoc,
            int generation,
            string? pdfPath)
        {
            return ReferenceEquals(_pdfiumDocument, pdfiumDoc) &&
                IsCurrentScope(generation, pdfPath);
        }

        private static (uint Width, uint Height) CalculateRenderDimensions(
            PdfPage pdfPage,
            CanvasControl canvas,
            double zoomLevel,
            bool isPreload = false)
        {
            return CalculateRenderDimensions(
                pdfPage.Size.Width,
                pdfPage.Size.Height,
                canvas,
                zoomLevel,
                isPreload);
        }

        private static (uint Width, uint Height) CalculateRenderDimensions(
            double pageWidth,
            double pageHeight,
            CanvasControl canvas,
            double zoomLevel,
            bool isPreload = false)
        {
            float currentDpiScale = canvas.Dpi / 96.0f;
            if (currentDpiScale <= 0) currentDpiScale = 1.0f;

            double canvasWidth = canvas.Size.Width;
            double canvasHeight = canvas.Size.Height;

            if (canvasWidth <= 0) canvasWidth = 1000;
            if (canvasHeight <= 0) canvasHeight = 1000;

            double pageAR = pageWidth / pageHeight;
            double canvasAR = canvasWidth / canvasHeight;
            double visibleWidthInDips = pageAR > canvasAR
                ? canvasWidth
                : canvasHeight * pageAR;

            // Preloads keep their bounded memory budget. The visible page instead
            // needs one rendered pixel per physical screen pixel at the current zoom.
            double targetWidth = isPreload
                ? Math.Clamp(visibleWidthInDips * zoomLevel * currentDpiScale, 1280.0, 2560.0)
                : Math.Max(1920.0, visibleWidthInDips * zoomLevel * currentDpiScale);

            // CanvasBitmap must fit the device texture limit in both dimensions.
            var device = canvas.Device ?? CanvasDevice.GetSharedDevice();
            double maxDimension = device.MaximumBitmapSizeInPixels;
            targetWidth = Math.Min(targetWidth, maxDimension * Math.Min(1.0, pageAR));
            // Bound each speculative BGRA bitmap to about 24 MiB, including
            // unusually tall pages. Foreground rendering retains full resolution.
            if (isPreload) targetWidth = Math.Min(targetWidth, Math.Sqrt(6_000_000 * pageAR));

            double scale = pageWidth > 0
                ? targetWidth / pageWidth
                : 1.0;

            return (
                Math.Max(1, (uint)Math.Round(pageWidth * scale)),
                Math.Max(1, (uint)Math.Round(pageHeight * scale)));
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
            _pdfiumDocument?.Dispose();
            _pdfiumDocument = null;
            Password = null;
            _zoomRerenderCts?.Dispose();
            _zoomRerenderCts = null;
            _documentCts?.Dispose();
            _documentCts = null;
        }
    }
}
