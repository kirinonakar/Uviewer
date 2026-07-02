using Microsoft.Graphics.Canvas;
using System.Collections.Generic;
using System.Threading;

namespace Uviewer.Models
{
    public sealed class ImageViewerState
    {
        public List<ImageEntry> Entries { get; set; } = new();
        public int CurrentIndex { get; set; } = -1;
        public CanvasBitmap? CurrentBitmap { get; set; }
        public CanvasBitmap? LeftBitmap { get; set; }
        public CanvasBitmap? RightBitmap { get; set; }
        public bool IsSideBySideMode { get; set; }
        public bool NextImageOnRight { get; set; } = true;
        public bool AutoDoublePageForArchive { get; set; }
        public bool IsCurrentViewSideBySide { get; set; }
        public bool IsSharpenEnabled { get; set; }
        public bool IsAnimatedFrameActive { get; set; }
        public double LastCanvasWidth { get; set; }
        public bool IsSeamlessScroll { get; set; }
        public CancellationTokenSource? ImageLoadingCts { get; set; }

        public void ClearBitmaps()
        {
            CurrentBitmap = null;
            LeftBitmap = null;
            RightBitmap = null;
        }
    }
}
