using Microsoft.Graphics.Canvas;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Uviewer.Services
{
    /// <summary>
    /// Decodes nclx-tagged PQ still images into linear FP16 scRGB. Windows' regular
    /// CanvasBitmap loader produces an 8-bit SDR bitmap, which discards HDR headroom.
    /// </summary>
    internal static class HdrImageDecoder
    {
        private const int ProbeByteLimit = 4 * 1024 * 1024;
        private const ushort PqTransfer = 16;
        private static readonly Lazy<float[]> PqToScRgbLut = new(CreatePqToScRgbLut);

        private readonly record struct NclxColorInfo(ushort Primaries, ushort Transfer);

        public static bool IsHdrBitmap(CanvasBitmap? bitmap)
        {
            if (bitmap == null) return false;

            try
            {
                return bitmap.Format == DirectXPixelFormat.R16G16B16A16Float;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<CanvasBitmap?> TryLoadAsync(
            CanvasDevice device,
            IRandomAccessStream stream,
            CancellationToken token)
        {
            if (!device.IsPixelFormatSupported(DirectXPixelFormat.R16G16B16A16Float))
                return null;

            var colorInfo = await TryReadNclxColorInfoAsync(stream, token);
            if (colorInfo == null || colorInfo.Value.Transfer != PqTransfer)
                return null;

            token.ThrowIfCancellationRequested();
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Rgba16,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Rgba16)
                return null;

            int width = softwareBitmap.PixelWidth;
            int height = softwareBitmap.PixelHeight;
            int byteCount = checked(width * height * 8);
            var pixelBuffer = new Windows.Storage.Streams.Buffer((uint)byteCount)
            {
                Length = (uint)byteCount
            };
            softwareBitmap.CopyToBuffer(pixelBuffer);

            var encodedPixels = new byte[byteCount];
            using (var reader = DataReader.FromBuffer(pixelBuffer))
            {
                reader.ReadBytes(encodedPixels);
            }

            var scRgbPixels = await Task.Run(
                () => ConvertPqToScRgb(encodedPixels, colorInfo.Value.Primaries, token),
                token);

            token.ThrowIfCancellationRequested();
            return CanvasBitmap.CreateFromBytes(
                device,
                scRgbPixels,
                width,
                height,
                DirectXPixelFormat.R16G16B16A16Float,
                96.0f,
                CanvasAlphaMode.Premultiplied);
        }

        private static async Task<NclxColorInfo?> TryReadNclxColorInfoAsync(
            IRandomAccessStream stream,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            uint length = (uint)Math.Min(stream.Size, ProbeByteLimit);
            if (length < 19) return null;

            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            uint loaded = await reader.LoadAsync(length);
            token.ThrowIfCancellationRequested();
            if (loaded < 19) return null;

            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);

            // ISO BMFF colour_information_box: size + "colr" + "nclx" +
            // primaries(u16), transfer(u16), matrix(u16), full-range flag(u8).
            for (int i = 8; i <= bytes.Length - 11; i++)
            {
                if (bytes[i] != (byte)'n' || bytes[i + 1] != (byte)'c' ||
                    bytes[i + 2] != (byte)'l' || bytes[i + 3] != (byte)'x')
                    continue;

                if (bytes[i - 4] != (byte)'c' || bytes[i - 3] != (byte)'o' ||
                    bytes[i - 2] != (byte)'l' || bytes[i - 1] != (byte)'r')
                    continue;

                ushort primaries = ReadUInt16BigEndian(bytes, i + 4);
                ushort transfer = ReadUInt16BigEndian(bytes, i + 6);
                return new NclxColorInfo(primaries, transfer);
            }

            return null;
        }

        private static byte[] ConvertPqToScRgb(
            byte[] source,
            ushort primaries,
            CancellationToken token)
        {
            var destination = new byte[source.Length];
            var pqLut = PqToScRgbLut.Value;
            var matrix = GetRgbToScRgbMatrix(primaries);
            int pixelCount = source.Length / 8;

            Parallel.For(0, pixelCount, new ParallelOptions { CancellationToken = token }, pixelIndex =>
            {
                int offset = pixelIndex * 8;
                ushort rCode = ReadUInt16LittleEndian(source, offset);
                ushort gCode = ReadUInt16LittleEndian(source, offset + 2);
                ushort bCode = ReadUInt16LittleEndian(source, offset + 4);
                ushort aCode = ReadUInt16LittleEndian(source, offset + 6);

                float r = pqLut[rCode];
                float g = pqLut[gCode];
                float b = pqLut[bCode];
                float alpha = aCode / 65535.0f;

                float outR = (matrix.M11 * r + matrix.M12 * g + matrix.M13 * b) * alpha;
                float outG = (matrix.M21 * r + matrix.M22 * g + matrix.M23 * b) * alpha;
                float outB = (matrix.M31 * r + matrix.M32 * g + matrix.M33 * b) * alpha;

                WriteHalf(destination, offset, outR);
                WriteHalf(destination, offset + 2, outG);
                WriteHalf(destination, offset + 4, outB);
                WriteHalf(destination, offset + 6, alpha);
            });

            return destination;
        }

        private static float[] CreatePqToScRgbLut()
        {
            // SMPTE ST 2084 EOTF. scRGB 1.0 represents 80 cd/m², so retaining
            // values above 1.0 preserves the absolute HDR luminance for DWM.
            const double m1 = 2610.0 / 16384.0;
            const double m2 = 2523.0 / 32.0;
            const double c1 = 3424.0 / 4096.0;
            const double c2 = 2413.0 / 128.0;
            const double c3 = 2392.0 / 128.0;

            var lut = new float[65536];
            for (int i = 0; i < lut.Length; i++)
            {
                double encoded = i / 65535.0;
                double p = Math.Pow(encoded, 1.0 / m2);
                double normalizedLuminance = Math.Pow(
                    Math.Max(p - c1, 0.0) / Math.Max(c2 - c3 * p, double.Epsilon),
                    1.0 / m1);
                lut[i] = (float)(normalizedLuminance * 10000.0 / 80.0);
            }

            return lut;
        }

        private static RgbMatrix GetRgbToScRgbMatrix(ushort primaries) => primaries switch
        {
            // ITU-R BT.2020 to linear sRGB/scRGB.
            9 => new RgbMatrix(
                1.660491f, -0.587641f, -0.072850f,
                -0.124550f, 1.132900f, -0.008349f,
                -0.018151f, -0.100579f, 1.118730f),

            // SMPTE ST 432 / Display-P3 (D65) to linear sRGB/scRGB.
            12 => new RgbMatrix(
                1.224745f, -0.224904f, -0.000081f,
                -0.042058f, 1.042081f, -0.000079f,
                -0.019642f, -0.078655f, 1.098537f),

            // BT.709/sRGB primaries, and a conservative fallback for unknown
            // primaries. PQ has already been linearized by this stage.
            _ => RgbMatrix.Identity
        };

        private static ushort ReadUInt16BigEndian(byte[] bytes, int offset) =>
            (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

        private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset) =>
            (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

        private static void WriteHalf(byte[] bytes, int offset, float value)
        {
            ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
            bytes[offset] = (byte)bits;
            bytes[offset + 1] = (byte)(bits >> 8);
        }

        private readonly record struct RgbMatrix(
            float M11, float M12, float M13,
            float M21, float M22, float M23,
            float M31, float M32, float M33)
        {
            public static RgbMatrix Identity { get; } = new(
                1, 0, 0,
                0, 1, 0,
                0, 0, 1);
        }
    }
}
