using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public interface IAnimatedWebpService : IDisposable
    {
        bool IsDecoding { get; }
        bool IsAnimationSupported(ImageEntry entry);
        void Stop();
        Task StartAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token, 
            float upscaleFactor, float sharpenAmount, float sharpenThreshold, float unsharpAmount, float unsharpRadius,
            bool sharpenEnabled, float hdrPeak = 0, ArchiveSession? archiveSession = null);
        
        bool IsBitmapInCache(CanvasBitmap bitmap);

        event EventHandler<CanvasBitmap>? FrameUpdated;
        event EventHandler? AnimationStopped;
    }
}
