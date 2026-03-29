using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Uviewer.Models
{
    public class AppSettings
    {
        public RectInt32 LastNonMaximizedRect { get; set; } = new RectInt32(100, 100, 1200, 800);
        public bool IsMaximized { get; set; } = false;
        public bool SharpenEnabled { get; set; } = false;
        public bool IsSideBySideMode { get; set; } = false;
        public bool NextImageOnRight { get; set; } = true;
        public ElementTheme Theme { get; set; } = ElementTheme.Default;
        public bool MatchControlDirection { get; set; } = false;
        public bool AllowMultipleInstances { get; set; } = true;
        public bool IsSidebarVisible { get; set; } = true;
        public bool IsPinned { get; set; } = true;
        public bool IsAlwaysOnTop { get; set; } = false;
        public bool AutoDoublePageForArchive { get; set; } = false;
        public bool IsRegistered { get; set; } = false;
    }
}
