using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;

namespace Uviewer.Services
{
    public class SharpeningService : ISharpeningService
    {
        public async Task<CanvasBitmap?> ApplySharpenToBitmapAsync(
            CanvasBitmap originalBitmap,
            float upscaleFactor,
            float sharpenAmount,
            float sharpenThreshold,
            float unsharpAmount,
            float unsharpRadius,
            bool skipUpscale = false)
        {
            try
            {
                var device = originalBitmap.Device;
                if (device == null)
                    return originalBitmap;

                // 1. skipUpscale이 아닐 때만 슬라이더에 지정된 비율 적용
                float currentUpscale = (!skipUpscale && upscaleFactor > 1.0f) ? upscaleFactor : 1.0f;

                float finalWidth = (float)originalBitmap.Size.Width * currentUpscale;
                float finalHeight = (float)originalBitmap.Size.Height * currentUpscale;

                bool isHdr = originalBitmap.Format == DirectXPixelFormat.R16G16B16A16Float;
                float hdrNormalizationScale = isHdr
                    ? HdrImageDecoder.GetNormalizationScale(originalBitmap)
                    : 1.0f;
                ICanvasImage currentEffect = originalBitmap;

                // Direct2D sharpening effects are defined for normalized color
                // values. Normalize extended-range scRGB first so HDR highlights
                // do not turn into clipped/negative halos.
                if (isHdr)
                {
                    currentEffect = new ColorMatrixEffect
                    {
                        Source = currentEffect,
                        ColorMatrix = CreateHdrNormalizeMatrix(hdrNormalizationScale),
                        AlphaMode = CanvasAlphaMode.Straight,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float
                    };
                }

                // 1. 업스케일 (ScaleEffect 사용 - 기본적으로 HighQualityCubic 적용됨)
                if (currentUpscale > 1.0f)
                {
                    var scaleEffect = new ScaleEffect
                    {
                        Source = currentEffect,
                        Scale = new Vector2(currentUpscale, currentUpscale),
                        InterpolationMode = CanvasImageInterpolation.HighQualityCubic
                    };
                    if (isHdr) scaleEffect.BufferPrecision = CanvasBufferPrecision.Precision16Float;
                    currentEffect = scaleEffect;
                }

                // 2. 샤프닝 (SharpenEffect)
                if (sharpenAmount > 0.0f)
                {
                    var sharpenEffect = new SharpenEffect
                    {
                        Source = currentEffect,
                        Amount = sharpenAmount,
                        Threshold = isHdr
                            ? sharpenThreshold / (hdrNormalizationScale + 0.5f)
                            : sharpenThreshold
                    };
                    if (isHdr) sharpenEffect.BufferPrecision = CanvasBufferPrecision.Precision16Float;
                    currentEffect = sharpenEffect;
                }

                // 3. 언샵 마스크 (Manual Implementation using GaussianBlur + ArithmeticComposite)
                if (unsharpAmount > 0.0f)
                {
                    var blurred = new GaussianBlurEffect
                    {
                        Source = currentEffect,
                        BlurAmount = unsharpRadius,
                        Optimization = EffectOptimization.Speed
                    };
                    if (isHdr) blurred.BufferPrecision = CanvasBufferPrecision.Precision16Float;

                    var unsharpEffect = new ArithmeticCompositeEffect
                    {
                        Source1 = currentEffect,
                        Source2 = blurred,
                        MultiplyAmount = 0.0f,
                        Source1Amount = 1.0f + unsharpAmount,
                        Source2Amount = -unsharpAmount,
                        Offset = 0.0f
                    };
                    if (isHdr) unsharpEffect.BufferPrecision = CanvasBufferPrecision.Precision16Float;
                    currentEffect = unsharpEffect;
                }

                if (isHdr)
                {
                    currentEffect = new ColorMatrixEffect
                    {
                        Source = new ColorMatrixEffect
                        {
                            Source = currentEffect,
                            ColorMatrix = CreateRgbScaleMatrix(1.0f),
                            AlphaMode = CanvasAlphaMode.Straight,
                            ClampOutput = true,
                            BufferPrecision = CanvasBufferPrecision.Precision16Float
                        },
                        ColorMatrix = CreateHdrDenormalizeMatrix(hdrNormalizationScale),
                        AlphaMode = CanvasAlphaMode.Straight,
                        BufferPrecision = CanvasBufferPrecision.Precision16Float
                    };
                }

                // 4. 최종 결과물 렌더링
                var finalTarget = originalBitmap.Format == DirectXPixelFormat.R16G16B16A16Float
                    ? new CanvasRenderTarget(
                        device,
                        finalWidth,
                        finalHeight,
                        originalBitmap.Dpi,
                        DirectXPixelFormat.R16G16B16A16Float,
                        CanvasAlphaMode.Premultiplied)
                    : new CanvasRenderTarget(device, finalWidth, finalHeight, originalBitmap.Dpi);
                using (var ds = finalTarget.CreateDrawingSession())
                {
                    // Render targets are not cleared automatically. Transparent GIF/WebP pixels
                    // must not blend with stale texture contents from an earlier frame.
                    ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    ds.Antialiasing = CanvasAntialiasing.Antialiased;
                    ds.DrawImage(currentEffect);
                }

                if (isHdr) HdrImageDecoder.CopyMetadata(originalBitmap, finalTarget);

                // 메모리 관리 (업스케일이 진행되었다면 중간 파이프라인에서 생성된 리소스들은 GC가 수거)
                return finalTarget;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Sharpening Processing: {ex.Message}");
                return originalBitmap;
            }
        }


        private static Matrix5x4 CreateRgbScaleMatrix(float scale) => new()
        {
            M11 = scale,
            M22 = scale,
            M33 = scale,
            M44 = 1.0f
        };

        private static Matrix5x4 CreateHdrNormalizeMatrix(float scale) => new()
        {
            M11 = 1.0f / (scale + 0.5f),
            M22 = 1.0f / (scale + 0.5f),
            M33 = 1.0f / (scale + 0.5f),
            M44 = 1.0f,
            M51 = 0.5f / (scale + 0.5f),
            M52 = 0.5f / (scale + 0.5f),
            M53 = 0.5f / (scale + 0.5f)
        };

        private static Matrix5x4 CreateHdrDenormalizeMatrix(float scale) => new()
        {
            M11 = scale + 0.5f,
            M22 = scale + 0.5f,
            M33 = scale + 0.5f,
            M44 = 1.0f,
            M51 = -0.5f,
            M52 = -0.5f,
            M53 = -0.5f
        };
    }
}
