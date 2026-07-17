using Microsoft.UI.Xaml;
using System.Collections.Generic;
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
        public AppToolbarSettings Toolbar { get; set; } = AppToolbarSettings.CreateDefault();
    }

    public class AppSettingsDocument
    {
        public const int CurrentVersion = 4;

        public int Version { get; set; } = CurrentVersion;
        public AppWindowSettings Window { get; set; } = new();
        public AppViewerSettings Viewer { get; set; } = new();
        public AppExplorerSettings Explorer { get; set; } = new();
        public AppBehaviorSettings App { get; set; } = new();
        public AppImageProcessingSettings ImageProcessing { get; set; } = new();
        public string ExternalProgramPath { get; set; } = AppSettings.DefaultExternalProgramPath;
        public AppToolbarSettings Toolbar { get; set; } = AppToolbarSettings.CreateDefault();
    }

    public sealed class AppToolbarSettings
    {
        public List<string> LeftItems { get; set; } = new();
        public List<string> RightItems { get; set; } = new();
        public List<string> HiddenItems { get; set; } = new();

        public static AppToolbarSettings CreateDefault() => new()
        {
            LeftItems = new List<string>
            {
                ToolbarItemIds.Settings,
                ToolbarItemIds.GlobalTheme,
                ToolbarItemIds.Pin,
                ToolbarItemIds.AlwaysOnTop
            },
            RightItems = new List<string>(ToolbarItemIds.DefaultRightItems),
            HiddenItems = new List<string>()
        };

        public AppToolbarSettings Clone() => new()
        {
            LeftItems = new List<string>(LeftItems ?? new List<string>()),
            RightItems = new List<string>(RightItems ?? new List<string>()),
            HiddenItems = new List<string>(HiddenItems ?? new List<string>())
        };
    }

    public static class ToolbarItemIds
    {
        public const string Settings = "settings";
        public const string GlobalTheme = "globalTheme";
        public const string Pin = "pin";
        public const string AlwaysOnTop = "alwaysOnTop";
        public const string ToggleSidebar = "toggleSidebar";
        public const string Favorites = "favorites";
        public const string Recent = "recent";
        public const string OpenFile = "openFile";
        public const string OpenFolder = "openFolder";
        public const string PdfToc = "pdfToc";
        public const string PdfGoToPage = "pdfGoToPage";
        public const string ZoomOut = "zoomOut";
        public const string ZoomIn = "zoomIn";
        public const string ZoomFit = "zoomFit";
        public const string ZoomActual = "zoomActual";
        public const string Aozora = "aozora";
        public const string Vertical = "vertical";
        public const string Font = "font";
        public const string TextToc = "textToc";
        public const string GoToPage = "goToPage";
        public const string TextSizeDown = "textSizeDown";
        public const string TextSizeUp = "textSizeUp";
        public const string TextTheme = "textTheme";
        public const string SideBySide = "sideBySide";
        public const string NextImageSide = "nextImageSide";
        public const string Sharpen = "sharpen";
        public const string PreviousFile = "previousFile";
        public const string PreviousPage = "previousPage";
        public const string NextPage = "nextPage";
        public const string NextFile = "nextFile";
        public const string Fullscreen = "fullscreen";
        public const string CloseWindow = "closeWindow";

        public static readonly string[] DefaultRightItems =
        {
            ToggleSidebar, Favorites, Recent, OpenFile, OpenFolder,
            PdfToc, PdfGoToPage,
            ZoomOut, ZoomIn, ZoomFit, ZoomActual,
            Aozora, Vertical, Font, TextToc, GoToPage, TextSizeDown, TextSizeUp, TextTheme,
            SideBySide, NextImageSide, Sharpen,
            PreviousFile, PreviousPage, NextPage, NextFile, Fullscreen, CloseWindow
        };

        public static readonly string[] All =
        {
            Settings, GlobalTheme, Pin, AlwaysOnTop,
            ToggleSidebar, Favorites, Recent, OpenFile, OpenFolder,
            PdfToc, PdfGoToPage,
            ZoomOut, ZoomIn, ZoomFit, ZoomActual,
            Aozora, Vertical, Font, TextToc, GoToPage, TextSizeDown, TextSizeUp, TextTheme,
            SideBySide, NextImageSide, Sharpen,
            PreviousFile, PreviousPage, NextPage, NextFile, Fullscreen, CloseWindow
        };
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
