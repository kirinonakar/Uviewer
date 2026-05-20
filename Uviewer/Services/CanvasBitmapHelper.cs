using Microsoft.Graphics.Canvas;
using System.Diagnostics.CodeAnalysis;
using Windows.Foundation;

namespace Uviewer.Services
{
    internal static class CanvasBitmapHelper
    {
        internal static bool TryGetBitmapSize([NotNullWhen(true)] CanvasBitmap? bitmap, out Size size)
        {
            size = default;

            if (bitmap == null) return false;

            try
            {
                if (bitmap.Device == null) return false;
                size = bitmap.Size;
                return size.Width > 0 && size.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsUsable(CanvasBitmap? bitmap)
        {
            return TryGetBitmapSize(bitmap, out _);
        }
    }
}
