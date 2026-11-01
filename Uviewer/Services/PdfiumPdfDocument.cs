using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Uviewer.Services
{
    /// <summary>A decrypted PDFium document retained for the reading session.</summary>
    internal sealed class PdfiumPdfDocument : IDisposable
    {
        private readonly DocumentHandle _document;
        private readonly PdfiumPageSize[] _pageSizes;

        private PdfiumPdfDocument(DocumentHandle document, PdfiumPageSize[] pageSizes)
        {
            _document = document;
            _pageSizes = pageSizes;
        }

        public int PageCount => _pageSizes.Length;

        public static PdfiumPdfDocument Open(string path, string? password)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("PDF path is required.", nameof(path));
            var buffer = GCHandle.Alloc(File.ReadAllBytes(path), GCHandleType.Pinned);
            DocumentHandle? document = null;
            try
            {
                lock (Native.Sync)
                {
                    var bytes = (byte[])buffer.Target!;
                    var handle = Native.FPDF_LoadMemDocument64(buffer.AddrOfPinnedObject(), (nuint)bytes.Length, password);
                    if (handle == IntPtr.Zero) throw Native.CreateException();
                    document = new DocumentHandle(handle, buffer);
                    buffer = default; // The native document now owns the pinned input.

                    int count = Native.FPDF_GetPageCount(handle);
                    if (count <= 0) throw new PdfiumException(PdfiumError.InvalidFormat);
                    var sizes = new PdfiumPageSize[count];
                    for (int index = 0; index < count; index++)
                    {
                        if (Native.FPDF_GetPageSizeByIndex(handle, index, out double width, out double height) == 0 ||
                            !double.IsFinite(width) || !double.IsFinite(height) ||
                            width <= 0 || height <= 0) throw new PdfiumException(PdfiumError.PageNotFound);
                        sizes[index] = new PdfiumPageSize(width, height);
                    }
                    return new PdfiumPdfDocument(document, sizes);
                }
            }
            catch
            {
                document?.Dispose();
                if (buffer.IsAllocated) buffer.Free();
                throw;
            }
        }

        public PdfiumPageSize GetPageSize(uint pageIndex)
        {
            ObjectDisposedException.ThrowIf(_document.IsClosed, this);
            if (pageIndex >= _pageSizes.Length) throw new ArgumentOutOfRangeException(nameof(pageIndex));
            return _pageSizes[(int)pageIndex];
        }

        public byte[] Render(uint pageIndex, uint width, uint height, CancellationToken token)
        {
            GetPageSize(pageIndex);
            token.ThrowIfCancellationRequested();
            int pixelWidth = checked((int)width);
            int pixelHeight = checked((int)height);
            if (pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(width));

            bool acquired = false;
            try
            {
                // Dispose/Shutdown can return while an in-flight native call finishes;
                // the document and pinned bytes remain alive until the last render exits.
                _document.DangerousAddRef(ref acquired);
                lock (Native.Sync)
                {
                    token.ThrowIfCancellationRequested();
                    var page = Native.FPDF_LoadPage(_document.DangerousGetHandle(), checked((int)pageIndex));
                    if (page == IntPtr.Zero) throw new PdfiumException(PdfiumError.PageNotFound);
                    try
                    {
                        int stride = checked(pixelWidth * 4);
                        var bitmap = GC.AllocateUninitializedArray<byte>(checked(stride * pixelHeight));
                        var buffer = GCHandle.Alloc(bitmap, GCHandleType.Pinned);
                        try
                        {
                            // Render into the final BGRA pixels. Tiles bound native work
                            // and allow cancellation between strips at large zoom levels.
                            for (int y = 0; y < pixelHeight; y += 1024)
                            {
                                token.ThrowIfCancellationRequested();
                                int tileHeight = Math.Min(1024, pixelHeight - y);
                                var pixels = IntPtr.Add(buffer.AddrOfPinnedObject(), checked(y * stride));
                                var target = Native.FPDFBitmap_CreateEx(pixelWidth, tileHeight, 4, pixels, stride);
                                if (target == IntPtr.Zero) throw new PdfiumException(PdfiumError.Unknown);
                                try
                                {
                                    Native.FPDFBitmap_FillRect(target, 0, 0, pixelWidth, tileHeight, 0xFFFFFFFF);
                                    Native.FPDF_RenderPageBitmap(target, page, 0, -y, pixelWidth, pixelHeight, 0, 0x01);
                                }
                                finally { Native.FPDFBitmap_Destroy(target); }
                            }
                            token.ThrowIfCancellationRequested();
                            return bitmap;
                        }
                        finally { buffer.Free(); }
                    }
                    finally { Native.FPDF_ClosePage(page); }
                }
            }
            finally { if (acquired) _document.DangerousRelease(); }
        }

        public void Dispose() => _document.Dispose();

        private sealed class DocumentHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private GCHandle _buffer;
            public DocumentHandle(IntPtr document, GCHandle buffer) : base(true)
            {
                SetHandle(document);
                _buffer = buffer;
            }

            protected override bool ReleaseHandle()
            {
                lock (Native.Sync)
                {
                    Native.FPDF_CloseDocument(handle);
                    if (_buffer.IsAllocated) _buffer.Free();
                }
                return true;
            }
        }

        // PDFium is not thread-safe, including calls against different documents.
        // Own its process-wide initialization here for the persistent document path.
        private static class Native
        {
            internal static readonly object Sync = new();
            static Native() => FPDF_InitLibrary();

            internal static Exception CreateException() => new PdfiumException((PdfiumError)FPDF_GetLastError());

            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            private static extern void FPDF_InitLibrary();
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint length,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            private static extern uint FPDF_GetLastError();
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern int FPDF_GetPageCount(IntPtr document);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern int FPDF_GetPageSizeByIndex(IntPtr document, int index, out double width, out double height);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern void FPDF_ClosePage(IntPtr page);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern void FPDF_CloseDocument(IntPtr document);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr buffer, int stride);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
            [DllImport("pdfium", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
                int left, int top, int width, int height, int rotation, int flags);
        }
    }

    internal readonly record struct PdfiumPageSize(double Width, double Height);
}
