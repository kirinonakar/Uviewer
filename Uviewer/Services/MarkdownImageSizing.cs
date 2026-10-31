using System;
using Uviewer.Models;

namespace Uviewer.Services;

internal static class MarkdownImageSizing
{
    public static (double Width, double Height) Resolve(AozoraImage image, double naturalWidth, double naturalHeight, double availableWidth)
    {
        naturalWidth = Math.Max(1, naturalWidth);
        naturalHeight = Math.Max(1, naturalHeight);
        double width = image.Width.HasValue
            ? image.Width.Value * (image.WidthIsPercent ? availableWidth / 100 : 1) : naturalWidth;
        // Percentage heights need a definite containing height; prose has none.
        double height = image.Height.HasValue && !image.HeightIsPercent ? image.Height.Value : naturalHeight;
        if (image.Width.HasValue && (!image.Height.HasValue || image.HeightIsPercent)) height = width * naturalHeight / naturalWidth;
        else if (!image.Width.HasValue && image.Height.HasValue && !image.HeightIsPercent) width = height * naturalWidth / naturalHeight;
        double maxWidth = availableWidth;
        if (image.MaxWidth.HasValue)
            maxWidth = Math.Min(maxWidth, image.MaxWidth.Value * (image.MaxWidthIsPercent ? availableWidth / 100 : 1));
        double scale = Math.Min(1, Math.Max(1, maxWidth) / Math.Max(1, width));
        return (Math.Max(1, width * scale), Math.Max(1, height * scale));
    }
}
