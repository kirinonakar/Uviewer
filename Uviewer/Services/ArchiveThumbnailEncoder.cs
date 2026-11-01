using Microsoft.Graphics.Canvas;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Uviewer.Services
{
    internal static class ArchiveThumbnailEncoder
    {
        public static async Task<byte[]> EncodeAsync(Stream image, int size, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            token.ThrowIfCancellationRequested();
            image.Position = 0;
            // Use a native stream: Win2D's asynchronous decoder must not call back
            // through a managed stream adapter while XAML marshals UI properties.
            using var input = new InMemoryRandomAccessStream();
            using var inputWriter = input.AsStreamForWrite();
            await image.CopyToAsync(inputWriter, token).ConfigureAwait(false);
            await inputWriter.FlushAsync(token).ConfigureAwait(false);
            input.Seek(0);

            var device = CanvasDevice.GetSharedDevice();
            // Virtual bitmaps retain EXIF orientation and accept covers larger than
            // the GPU bitmap limit. CacheOnDemand also avoids full JPEG decoding.
            using var source = await CanvasVirtualBitmap.LoadAsync(device, input,
                CanvasVirtualBitmapOptions.CacheOnDemand).AsTask(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var bounds = source.Bounds;
            double scale = Math.Min(1, (double)size / Math.Max(bounds.Width, bounds.Height));
            int width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
            using var thumbnail = new CanvasRenderTarget(device, width, height, 96);
            using (var session = thumbnail.CreateDrawingSession())
            {
                session.Clear(Microsoft.UI.Colors.Transparent);
                session.DrawImage(source, new Rect(0, 0, width, height), bounds, 1,
                    CanvasImageInterpolation.HighQualityCubic);
            }
            token.ThrowIfCancellationRequested();
            using var output = new InMemoryRandomAccessStream();
            await thumbnail.SaveAsync(output, CanvasBitmapFileFormat.Png).AsTask(token).ConfigureAwait(false);
            output.Seek(0);
            using var reader = new DataReader(output);
            var bytes = new byte[checked((int)output.Size)];
            await reader.LoadAsync((uint)bytes.Length).AsTask(token).ConfigureAwait(false);
            reader.ReadBytes(bytes);
            token.ThrowIfCancellationRequested();
            return bytes;
        }
    }
}
