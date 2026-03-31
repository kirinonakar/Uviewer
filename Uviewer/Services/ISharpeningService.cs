using Microsoft.Graphics.Canvas;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public interface ISharpeningService
    {
        /// <summary>
        /// Applies sharpening and upscaling effects to a bitmap.
        /// </summary>
        /// <param name="originalBitmap">The source bitmap to process.</param>
        /// <param name="upscaleFactor">The factor by which to upscale the image.</param>
        /// <param name="sharpenAmount">The amount of sharpening to apply.</param>
        /// <param name="sharpenThreshold">The threshold for sharpening.</param>
        /// <param name="unsharpAmount">The amount of unsharp mask to apply.</param>
        /// <param name="unsharpRadius">The radius of the unsharp mask blur.</param>
        /// <param name="skipUpscale">If true, skips the upscaling step even if upscaleFactor > 1.0.</param>
        /// <returns>A new CanvasBitmap with effects applied, or the original if an error occurs.</returns>
        Task<CanvasBitmap?> ApplySharpenToBitmapAsync(
            CanvasBitmap originalBitmap,
            float upscaleFactor,
            float sharpenAmount,
            float sharpenThreshold,
            float unsharpAmount,
            float unsharpRadius,
            bool skipUpscale = false);
    }
}
