using Microsoft.Graphics.Canvas;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;

namespace Uviewer.Services;

internal static class JxlImageDecoder
{
    public const DirectXPixelFormat HdrFormat = DirectXPixelFormat.R16G16B16A16Float;
    public const DirectXPixelFormat SdrFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    public static bool IsJxl(string? name) =>
        string.Equals(Path.GetExtension(name), ".jxl", StringComparison.OrdinalIgnoreCase);

    public static float GetDisplayPeak(CanvasDevice device, float requestedPeak) =>
        requestedPeak > 0 && device.IsPixelFormatSupported(HdrFormat) ? requestedPeak : 0;

    public static async Task<(CanvasBitmap Bitmap, bool IsAnimated)> LoadAsync(
        CanvasDevice device, IRandomAccessStream stream, byte[]? bytes,
        float requestedPeak, CancellationToken token)
    {
        if (bytes == null)
        {
            bytes = new byte[checked((int)stream.Size)];
            using var input = stream.GetInputStreamAt(0).AsStreamForRead();
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        }

        return await Task.Run(() =>
        {
            using var decoder = new LibJxlDecoder(bytes, device.MaximumBitmapSizeInPixels);
            var frame = decoder.ReadFrame(GetDisplayPeak(device, requestedPeak), token) ??
                        throw new InvalidDataException("The JPEG XL image has no visible frame.");
            var bitmap = CanvasBitmap.CreateFromBytes(device, frame.Pixels, decoder.Width, decoder.Height,
                frame.HdrNormalizationScale > 0 ? HdrFormat : SdrFormat, 96f, CanvasAlphaMode.Premultiplied);
            if (frame.HdrNormalizationScale > 0)
                HdrImageDecoder.SetNormalizationScale(bitmap, frame.HdrNormalizationScale);
            return (bitmap, decoder.IsAnimated && !frame.IsLast);
        }, token).ConfigureAwait(false);
    }
}
