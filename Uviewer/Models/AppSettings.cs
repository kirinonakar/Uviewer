using Microsoft.UI.Xaml;
using System.Text.Json.Serialization;
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

    public class AppSettingsDocument
    {
        public const int CurrentVersion = 3;

        public int Version { get; set; } = CurrentVersion;
        public AppWindowSettings Window { get; set; } = new();
        public AppViewerSettings Viewer { get; set; } = new();
        public AppExplorerSettings Explorer { get; set; } = new();
        public AppBehaviorSettings App { get; set; } = new();
        public AppImageProcessingSettings ImageProcessing { get; set; } = new();
        public string ExternalProgramPath { get; set; } = AppSettings.DefaultExternalProgramPath;
    }

    public class AppWindowSettings
    {
        public int X { get; set; } = 100;
        public int Y { get; set; } = 100;
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 800;
        public bool Maximized { get; set; }
    }

    public class AppViewerSettings
    {
        public bool SideBySide { get; set; }
        public bool NextImageOnRight { get; set; } = true;
        public bool Sharpen { get; set; }
        public bool MatchControlDirection { get; set; }
        public bool AutoDoublePageForArchive { get; set; }
    }

    public class AppExplorerSettings
    {
        public double ThumbnailSize { get; set; } = 80;
        public bool ShowFolderThumbnails { get; set; }
        public bool SidebarVisible { get; set; } = true;
        public bool Pinned { get; set; } = true;
    }

    public class AppBehaviorSettings
    {
        public int Theme { get; set; } = (int)ElementTheme.Default;
        public bool AllowMultipleInstances { get; set; } = true;
        public bool AlwaysOnTop { get; set; }
        public bool Registered { get; set; }
    }

    public class AppImageProcessingSettings
    {
        public double UpscaleFactor { get; set; } = 2.0;
        public double SharpenAmount { get; set; } = 1.0;
        public double SharpenThreshold { get; set; } = 0.01;
        public double UnsharpAmount { get; set; } = 2.0;
        public double UnsharpRadius { get; set; } = 1.0;
    }

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AppSettingsDocument))]
    public partial class AppSettingsJsonContext : JsonSerializerContext
    {
    }
}
