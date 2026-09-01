using Microsoft.Graphics.Canvas;
using System;
using System.Runtime.CompilerServices;
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
        private static readonly ConditionalWeakTable<CanvasBitmap, HdrBitmapMetadata> BitmapMetadata = new();

        private readonly record struct NclxColorInfo(
            ushort Primaries,
            ushort Transfer,
            float MaxContentLightLevel);

        private sealed record HdrBitmapMetadata(float NormalizationScale);

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

        public static float GetNormalizationScale(CanvasBitmap bitmap)
        {
            return BitmapMetadata.TryGetValue(bitmap, out var metadata)
                ? metadata.NormalizationScale
                : 125.0f;
        }

        public static void CopyMetadata(CanvasBitmap source, CanvasBitmap destination)
        {
            if (!BitmapMetadata.TryGetValue(source, out var metadata)) return;
            BitmapMetadata.Remove(destination);
            BitmapMetadata.Add(destination, metadata);
        }

        public static async Task<CanvasBitmap?> TryLoadAsync(
            CanvasDevice device,
            IRandomAccessStream stream,
            float displayMaxLuminance,
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
            // The Windows AVIF codec can return already expanded/clipped channel
            // values for an RGBA16 request. Its BGRA8 path retains the stable PQ
            // signal shape; expand that signal into FP16 linear scRGB ourselves.
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                return null;

            int width = softwareBitmap.PixelWidth;
            int height = softwareBitmap.PixelHeight;
            int byteCount = checked(width * height * 4);
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
                () => ConvertPqToScRgb(
                    encodedPixels,
                    colorInfo.Value.Primaries,
                    colorInfo.Value.MaxContentLightLevel,
                    displayMaxLuminance,
                    token),
                token);

            token.ThrowIfCancellationRequested();
            var bitmap = CanvasBitmap.CreateFromBytes(
                device,
                scRgbPixels,
                width,
                height,
                DirectXPixelFormat.R16G16B16A16Float,
                96.0f,
                CanvasAlphaMode.Premultiplied);
            BitmapMetadata.Add(
                bitmap,
                new HdrBitmapMetadata(Math.Clamp(displayMaxLuminance / 80.0f, 1.0f, 125.0f)));
            return bitmap;
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

            ushort primaries = 0;
            ushort transfer = 0;
            float maxContentLightLevel = 4000.0f;
            bool foundNclx = false;

            // ISO BMFF colour_information_box: size + "colr" + "nclx" +
            // primaries(u16), transfer(u16), matrix(u16), full-range flag(u8).
            for (int i = 8; i <= bytes.Length - 8; i++)
            {
                if (i <= bytes.Length - 11 &&
                    bytes[i] == (byte)'n' && bytes[i + 1] == (byte)'c' &&
                    bytes[i + 2] == (byte)'l' && bytes[i + 3] == (byte)'x' &&
                    bytes[i - 4] == (byte)'c' && bytes[i - 3] == (byte)'o' &&
                    bytes[i - 2] == (byte)'l' && bytes[i - 1] == (byte)'r')
                {
                    primaries = ReadUInt16BigEndian(bytes, i + 4);
                    transfer = ReadUInt16BigEndian(bytes, i + 6);
                    foundNclx = true;
                }

                // AVIF ContentLightLevelInformationProperty: MaxCLL and MaxFALL.
                if (bytes[i] == (byte)'c' && bytes[i + 1] == (byte)'l' &&
                    bytes[i + 2] == (byte)'l' && bytes[i + 3] == (byte)'i')
                {
                    ushort parsedMaxCll = ReadUInt16BigEndian(bytes, i + 4);
                    if (parsedMaxCll > 0) maxContentLightLevel = parsedMaxCll;
                }
            }

            return foundNclx
                ? new NclxColorInfo(primaries, transfer, maxContentLightLevel)
                : null;
        }

        private static byte[] ConvertPqToScRgb(
            byte[] source,
            ushort primaries,
            float maxContentLightLevel,
            float displayMaxLuminance,
            CancellationToken token)
        {
            var destination = new byte[checked(source.Length * 2)];
            var pqLut = PqToScRgbLut.Value;
            var matrix = GetRgbToScRgbMatrix(primaries);
            int pixelCount = source.Length / 4;

            Parallel.For(0, pixelCount, new ParallelOptions { CancellationToken = token }, pixelIndex =>
            {
                int sourceOffset = pixelIndex * 4;
                int destinationOffset = pixelIndex * 8;
                ushort bCode = (ushort)(source[sourceOffset] * 257);
                ushort gCode = (ushort)(source[sourceOffset + 1] * 257);
                ushort rCode = (ushort)(source[sourceOffset + 2] * 257);
                float alpha = source[sourceOffset + 3] / 255.0f;

                float r = pqLut[rCode];
                float g = pqLut[gCode];
                float b = pqLut[bCode];

                float outR = matrix.M11 * r + matrix.M12 * g + matrix.M13 * b;
                float outG = matrix.M21 * r + matrix.M22 * g + matrix.M23 * b;
                float outB = matrix.M31 * r + matrix.M32 * g + matrix.M33 * b;

                ToneMapToDisplay(
                    ref outR,
                    ref outG,
                    ref outB,
                    maxContentLightLevel,
                    displayMaxLuminance);

                outR *= alpha;
                outG *= alpha;
                outB *= alpha;

                WriteHalf(destination, destinationOffset, outR);
                WriteHalf(destination, destinationOffset + 2, outG);
                WriteHalf(destination, destinationOffset + 4, outB);
                WriteHalf(destination, destinationOffset + 6, alpha);
            });

            return destination;
        }

        private static void ToneMapToDisplay(
            ref float r,
            ref float g,
            ref float b,
            float maxContentLightLevel,
            float displayMaxLuminance)
        {
            // scRGB 1.0 is 80 nits. A saturated PQ primary can legitimately
            // exceed MaxCLL even when its pixel luminance does not, so use the
            // largest positive channel to avoid hard channel clipping. Scaling
            // all three channels by the same amount preserves hue.
            float outputPeak = Math.Clamp(displayMaxLuminance, 80.0f, 10000.0f);
            float signal = Math.Max(0.0f, Math.Max(r, Math.Max(g, b))) * 80.0f;
            float sourcePeak = Math.Clamp(maxContentLightLevel, 80.0f, 10000.0f);

            if (sourcePeak <= outputPeak)
            {
                if (signal <= outputPeak) return;

                float highlightScale = outputPeak / signal;
                r *= highlightScale;
                g *= highlightScale;
                b *= highlightScale;
                return;
            }

            float knee = Math.Min(203.0f, outputPeak * 0.75f);
            if (signal <= knee) return;

            float mapped;

            if (signal >= sourcePeak)
            {
                mapped = outputPeak;
            }
            else
            {
                float outputRange = Math.Max(outputPeak - knee, 1.0f);
                float sourceRange = Math.Max(sourcePeak - knee, 1.0f);
                float curve = 1.0f / outputRange;
                float denominator = 1.0f - MathF.Exp(-curve * sourceRange);
                float numerator = 1.0f - MathF.Exp(-curve * (signal - knee));
                mapped = knee + outputRange * numerator / Math.Max(denominator, 0.0001f);
            }

            float scale = mapped / signal;
            r *= scale;
            g *= scale;
            b *= scale;
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
