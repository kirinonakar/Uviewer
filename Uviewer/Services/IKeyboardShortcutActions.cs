using System.Threading.Tasks;

namespace Uviewer.Services
{
    public interface IKeyboardShortcutActions
    {
        bool IsColorPickerOpen { get; }
        bool IsFullscreen { get; }
        bool IsEpubMode { get; }
        bool IsTextMode { get; }
        bool IsAozoraMode { get; }
        bool IsVerticalMode { get; set; }
        bool ShouldInvertControls { get; }
        int CurrentEpubChapterIndex { get; set; }
        int EpubSpineCount { get; }
        int CurrentImageIndex { get; set; }
        int ImageEntriesCount { get; }
        bool HasPdfDocument { get; }
        bool IsSharpenEnabled { get; set; }
        bool IsAboutDialogActive { get; }

        void ToggleFullscreen();
        void CloseApp();
        void NavigateVerticalPage(int offset);
        Task NavigateEpubAsync(int offset);
        Task ShowEpubGoToLineDialog();
        void ToggleFont();
        void ToggleVerticalMode();
        void SaveTextSettings();
        void DecreaseTextSize();
        void IncreaseTextSize();
        void ToggleSidebar();
        void ToggleTheme();
        Task LoadEpubChapterAsync(int index);
        void ToggleSideBySide();
        Task NavigateToNextAsync(bool handled);
        Task NavigateToPreviousAsync(bool handled);
        Task DisplayCurrentImageAsync();
        Task NavigateToFileAsync(bool forward);
        Task AddToFavoritesAsync();
        void ToggleSharpening();
        Task ShowGoToLineDialog();
        Task NavigateToParentFolderAsync();
        Task OpenFileAsync();
        void ZoomIn();
        void ZoomOut();
        void FitToWindow();
        void ZoomActual();
        void ToggleAlwaysOnTop();
        void ToggleGlobalTheme();
        void TogglePin();
        void HideAboutDialog();
    }
}
