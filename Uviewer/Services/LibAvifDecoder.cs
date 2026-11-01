using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    /// <summary>
    /// Direct bindings for the bundled libavif 1.4.2 C ABI. A decoder is owned by
    /// one decoding task at a time; returned frames contain independent pixel copies.
    /// </summary>
    internal sealed class LibAvifDecoder : IDisposable
    {
        private readonly DecoderHandle _decoder;

        private LibAvifDecoder(DecoderHandle decoder) => _decoder = decoder;

        public int ImageCount => ReadDecoder().ImageCount;
        public ushort ColorPrimaries => ReadImage().ColorPrimaries;
        public ushort TransferCharacteristics => ReadImage().TransferCharacteristics;
        public double FrameDuration => ReadDecoder().ImageTiming.Duration;

        internal readonly record struct Frame(byte[] Pixels, int Width, int Height);

        public static LibAvifDecoder Create(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length == 0) throw new InvalidDataException("The AVIF input is empty.");

            // RGBImage is caller-allocated, so its layout must match the bundled ABI.
            string? version = Marshal.PtrToStringUTF8(NativeMethods.avifVersion());
            if (version != "1.4.2")
                throw new NotSupportedException($"Expected libavif 1.4.2, found {version}.");

            var handle = new DecoderHandle();
            try
            {
                if (handle.IsInvalid) throw new OutOfMemoryException("Could not allocate an AVIF decoder.");
                handle.PinInput(bytes);
                Marshal.WriteInt32(handle.DangerousGetHandle(),
                    (int)Marshal.OffsetOf<DecoderPrefix>(nameof(DecoderPrefix.MaxThreads)),
                    Environment.ProcessorCount);
                CheckResult(NativeMethods.avifDecoderSetIOMemory(
                    handle, handle.InputPointer, (nuint)bytes.Length), "Set input");
                CheckResult(NativeMethods.avifDecoderParse(handle), "Parse");
                return new LibAvifDecoder(handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static async Task<LibAvifDecoder> CreateAsync(Stream stream, CancellationToken token)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return Create(buffer.ToArray());
        }

        public Frame DecodeNextFrame(int depth, bool bgra = false)
        {
            ObjectDisposedException.ThrowIf(_decoder.IsClosed, this);
            if (depth != 8 && depth != 16) throw new ArgumentOutOfRangeException(nameof(depth));

            CheckResult(NativeMethods.avifDecoderNextImage(_decoder), "Decode frame");
            IntPtr image = ReadDecoder().Image;
            NativeMethods.avifRGBImageSetDefaults(out var rgb, image);
            rgb.Depth = (uint)depth;
            rgb.Format = bgra ? 4 : 1; // AVIF_RGB_FORMAT_BGRA / AVIF_RGB_FORMAT_RGBA
            rgb.AlphaPremultiplied = 0;
            rgb.IsFloat = 0; // HDR conversion needs full-range uint16 PQ codes, not FP16.

            int width = checked((int)rgb.Width);
            int height = checked((int)rgb.Height);
            int rowBytes = checked(width * 4 * (depth / 8));
            if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid AVIF dimensions.");
            var pixels = new byte[checked(rowBytes * height)];
            try
            {
                CheckResult(NativeMethods.avifRGBImageAllocatePixels(ref rgb), "Allocate RGB pixels");
                CheckResult(NativeMethods.avifImageYUVToRGB(image, ref rgb), "Convert YUV to RGB");
                if (rgb.Pixels == IntPtr.Zero || rgb.RowBytes < rowBytes)
                    throw new InvalidDataException("Invalid AVIF RGB buffer.");

                // Do not expose libavif-owned memory or assume rows have no padding.
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(IntPtr.Add(rgb.Pixels, checked((int)(row * (long)rgb.RowBytes))),
                        pixels, checked(row * rowBytes), rowBytes);
                }
                return new Frame(pixels, width, height);
            }
            finally
            {
                NativeMethods.avifRGBImageFreePixels(ref rgb);
                GC.KeepAlive(_decoder);
            }
        }

        private DecoderPrefix ReadDecoder()
        {
            ObjectDisposedException.ThrowIf(_decoder.IsClosed, this);
            var decoder = Marshal.PtrToStructure<DecoderPrefix>(_decoder.DangerousGetHandle());
            GC.KeepAlive(_decoder);
            return decoder;
        }

        private ImagePrefix ReadImage()
        {
            var image = Marshal.PtrToStructure<ImagePrefix>(ReadDecoder().Image);
            GC.KeepAlive(_decoder);
            return image;
        }

        private static void CheckResult(int result, string operation)
        {
            if (result == 0) return;
            string? message = Marshal.PtrToStringUTF8(NativeMethods.avifResultToString(result));
            throw new InvalidDataException($"AVIF {operation}: {message} ({result}).");
        }

        public void Dispose() => _decoder.Dispose();

        private sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private GCHandle _input;

            public DecoderHandle() : base(ownsHandle: true) => SetHandle(NativeMethods.avifDecoderCreate());
            public IntPtr InputPointer => _input.AddrOfPinnedObject();
            public void PinInput(byte[] bytes) => _input = GCHandle.Alloc(bytes, GCHandleType.Pinned);

            protected override bool ReleaseHandle()
            {
                // The memory reader borrows the input for the entire decoder lifetime.
                NativeMethods.avifDecoderDestroy(handle);
                if (_input.IsAllocated) _input.Free();
                return true;
            }
        }

        // Only the stable prefixes we read are declared. libavif allocates the
        // complete decoder/image structs, including fields added after these.
        // Source: libavif v1.4.2 include/avif/avif.h (default C packing).
        [StructLayout(LayoutKind.Sequential)]
        private struct DecoderPrefix
        {
            public int CodecChoice, MaxThreads, RequestedSource, AllowProgressive, AllowIncremental;
            public int IgnoreExif, IgnoreXmp;
            public uint ImageSizeLimit, ImageDimensionLimit, ImageCountLimit, StrictFlags;
            public IntPtr Image;
            public int ImageIndex, ImageCount, ProgressiveState;
            public ImageTiming ImageTiming;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ImageTiming
        {
            public ulong Timescale;
            public double Pts;
            public ulong PtsInTimescales;
            public double Duration;
            public ulong DurationInTimescales;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ImagePrefix
        {
            public uint Width, Height, Depth;
            public int YuvFormat, YuvRange, YuvChromaSamplePosition;
            public IntPtr YPlane, UPlane, VPlane;
            public uint YRowBytes, URowBytes, VRowBytes;
            public int ImageOwnsYuvPlanes;
            public IntPtr AlphaPlane;
            public uint AlphaRowBytes;
            public int ImageOwnsAlphaPlane, AlphaPremultiplied;
            public IntPtr IccData;
            public nuint IccSize;
            public ushort ColorPrimaries, TransferCharacteristics, MatrixCoefficients;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RgbImage
        {
            public uint Width, Height, Depth;
            public int Format, ChromaUpsampling, ChromaDownsampling;
            public int AvoidLibYuv, IgnoreAlpha, AlphaPremultiplied, IsFloat, MaxThreads;
            public IntPtr Pixels;
            public uint RowBytes;
        }

        private static class NativeMethods
        {
            private const string LibraryName = "libavif.dll";
            private static readonly IntPtr Library = NativeLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Libs", LibraryName));

            static NativeMethods()
            {
                NativeLibrary.SetDllImportResolver(typeof(LibAvifDecoder).Assembly,
                    (name, _, _) => name == LibraryName ? Library : IntPtr.Zero);
            }

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern IntPtr avifVersion();
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern IntPtr avifResultToString(int result);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern IntPtr avifDecoderCreate();
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern void avifDecoderDestroy(IntPtr decoder);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int avifDecoderSetIOMemory(DecoderHandle decoder, IntPtr data, nuint size);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int avifDecoderParse(DecoderHandle decoder);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int avifDecoderNextImage(DecoderHandle decoder);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern void avifRGBImageSetDefaults(out RgbImage rgb, IntPtr image);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int avifRGBImageAllocatePixels(ref RgbImage rgb);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern void avifRGBImageFreePixels(ref RgbImage rgb);
            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int avifImageYUVToRGB(IntPtr image, ref RgbImage rgb);
        }
    }
}
