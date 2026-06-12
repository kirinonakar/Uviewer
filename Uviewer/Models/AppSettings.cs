using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Uviewer.Models
{
    public class AppSettings
    {
        public const string DefaultExternalProgramPath = "txtaieditor";

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
        public double UpscaleFactor { get; set; } = 2.0;
        public double SharpenAmount { get; set; } = 1.0;
        public double SharpenThreshold { get; set; } = 0.01;
        public double UnsharpAmount { get; set; } = 2.0;
        public double UnsharpRadius { get; set; } = 1.0;
        public double ExplorerThumbnailSize { get; set; } = 80;
        public bool ShowFolderThumbnails { get; set; } = false;
        public string ExternalProgramPath { get; set; } = DefaultExternalProgramPath;
    }
}
