using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Numerics;
using System.Threading.Tasks;

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

                ICanvasImage currentEffect = originalBitmap;

                // 1. 업스케일 (ScaleEffect 사용 - 기본적으로 HighQualityCubic 적용됨)
                if (currentUpscale > 1.0f)
                {
                    currentEffect = new ScaleEffect
                    {
                        Source = currentEffect,
                        Scale = new Vector2(currentUpscale, currentUpscale),
                        InterpolationMode = CanvasImageInterpolation.HighQualityCubic
                    };
                }

                // 2. 샤프닝 (SharpenEffect)
                if (sharpenAmount > 0.0f)
                {
                    currentEffect = new SharpenEffect
                    {
                        Source = currentEffect,
                        Amount = sharpenAmount,
                        Threshold = sharpenThreshold
                    };
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

                    currentEffect = new ArithmeticCompositeEffect
                    {
                        Source1 = currentEffect,
                        Source2 = blurred,
                        MultiplyAmount = 0.0f,
                        Source1Amount = 1.0f + unsharpAmount,
                        Source2Amount = -unsharpAmount,
                        Offset = 0.0f
                    };
                }

                // 4. 최종 결과물 렌더링
                var finalTarget = new CanvasRenderTarget(device, finalWidth, finalHeight, originalBitmap.Dpi);
                using (var ds = finalTarget.CreateDrawingSession())
                {
                    ds.Antialiasing = CanvasAntialiasing.Antialiased;
                    ds.DrawImage(currentEffect);
                }

                // 메모리 관리 (업스케일이 진행되었다면 중간 파이프라인에서 생성된 리소스들은 GC가 수거)
                return finalTarget;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Sharpening Processing: {ex.Message}");
                return originalBitmap;
            }
        }
    }
}
