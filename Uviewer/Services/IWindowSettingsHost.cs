using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Uviewer.Models;

namespace Uviewer.Services
{
    public interface IWindowSettingsHost
    {
        AppWindow AppWindow { get; }
        WindowStateManager WindowState { get; }
        ImageProcessingViewModel ImageOptions { get; }

        bool SharpenEnabled { get; set; }
        bool SideBySideMode { get; set; }
        bool NextImageOnRight { get; set; }
        ElementTheme CurrentTheme { get; }
        bool MatchControlDirection { get; set; }
        bool AllowMultipleInstances { get; set; }
        bool AutoDoublePageForArchive { get; set; }
        bool IsRegistered { get; set; }
        double ExplorerThumbnailSize { get; set; }
        bool ShowFolderThumbnails { get; set; }
        string ExternalProgramPath { get; set; }

        void SetTheme(ElementTheme theme);
        void RestoreMaximizedWhenActivated();
        void ApplyInitialWindowSettingsUiState();
    }
}
