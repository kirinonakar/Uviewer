using Microsoft.Graphics.Canvas;
using System.Collections.Generic;

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

        public void ClearBitmaps()
        {
            CurrentBitmap = null;
            LeftBitmap = null;
            RightBitmap = null;
        }
    }
}
