using System;

namespace Uviewer.Services;

internal readonly record struct PeleRasterLayout(int Width, int Height, float Scale, float Dpi)
{
    public static PeleRasterLayout Create(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Pele returned invalid diagram dimensions.");

        // Preserve the reader's existing 2x logical diagram size, but provide
        // two physical pixels per DIP for 200% displays. Long/narrow diagrams
        // can use 8192px while the total bitmap stays within 64 MiB of RGBA.
        const double maxPixels = 16 * 1024 * 1024;
        double scale = Math.Min(4, Math.Min(8192.0 / width, 8192.0 / height));
        int pixelsWide, pixelsHigh;
        while (true)
        {
            pixelsWide = Math.Clamp((int)Math.Ceiling(width * scale), 1, 8192);
            pixelsHigh = Math.Clamp((int)Math.Ceiling(height * scale), 1, 8192);
            if ((long)pixelsWide * pixelsHigh <= maxPixels) break;
            // Account for rounding as well as aspect ratio in the pixel budget.
            scale *= Math.Sqrt(maxPixels / ((double)pixelsWide * pixelsHigh)) * 0.9999;
        }
        return new(pixelsWide, pixelsHigh, (float)scale, (float)(96 * scale / 2));
    }
}
