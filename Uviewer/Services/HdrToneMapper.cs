using System;

namespace Uviewer.Services;

internal static class HdrToneMapper
{
    public static void MapToDisplay(
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
}
