using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Uviewer.Services;

internal static class JxlFramePixels
{
    public static byte[] Convert(byte[] source, float sourcePeak, float hdrDisplayPeak, CancellationToken token)
    {
        if (sourcePeak <= 0)
        {
            var bgra = new byte[source.Length];
            for (int i = 0; i < source.Length; i += 4)
            {
                if ((i & 0xffff) == 0) token.ThrowIfCancellationRequested();
                int alpha = source[i + 3];
                bgra[i] = (byte)((source[i + 2] * alpha + 127) / 255);
                bgra[i + 1] = (byte)((source[i + 1] * alpha + 127) / 255);
                bgra[i + 2] = (byte)((source[i] * alpha + 127) / 255);
                bgra[i + 3] = (byte)alpha;
            }
            return bgra;
        }

        bool hdrOutput = hdrDisplayPeak > 0;
        float outputPeak = hdrOutput ? hdrDisplayPeak : 255f;
        float sourceScale = sourcePeak / 80f;
        float sdrScale = 80f / outputPeak;
        var rgba = MemoryMarshal.Cast<byte, float>(source);
        var pixels = new byte[source.Length / (hdrOutput ? 2 : 4)];
        var halfPixels = MemoryMarshal.Cast<byte, Half>(pixels);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if ((i & 0xffff) == 0) token.ThrowIfCancellationRequested();
            float alpha = Math.Clamp(FiniteOrZero(rgba[i + 3]), 0f, 1f);
            if (alpha == 0) continue;

            float r = FiniteOrZero(rgba[i] * sourceScale);
            float g = FiniteOrZero(rgba[i + 1] * sourceScale);
            float b = FiniteOrZero(rgba[i + 2] * sourceScale);
            HdrToneMapper.MapToDisplay(ref r, ref g, ref b, sourcePeak, outputPeak);

            if (hdrOutput)
            {
                // Negative scRGB components encode colors outside the sRGB gamut.
                halfPixels[i] = ToHalf(r * alpha);
                halfPixels[i + 1] = ToHalf(g * alpha);
                halfPixels[i + 2] = ToHalf(b * alpha);
                halfPixels[i + 3] = (Half)alpha;
            }
            else
            {
                pixels[i] = ToSrgbByte(b * sdrScale, alpha);
                pixels[i + 1] = ToSrgbByte(g * sdrScale, alpha);
                pixels[i + 2] = ToSrgbByte(r * sdrScale, alpha);
                pixels[i + 3] = (byte)(alpha * 255f + 0.5f);
            }
        }
        return pixels;
    }

    private static float FiniteOrZero(float value) => float.IsFinite(value) ? value : 0f;

    private static Half ToHalf(float value) => (Half)Math.Clamp(value, -65504f, 65504f);

    private static byte ToSrgbByte(float linear, float alpha)
    {
        linear = Math.Clamp(linear, 0f, 1f);
        float encoded = linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)(Math.Clamp(encoded, 0f, 1f) * alpha * 255f + 0.5f);
    }
}
