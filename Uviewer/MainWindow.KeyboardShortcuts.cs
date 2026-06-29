using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        bool IKeyboardShortcutActions.IsColorPickerOpen => _isColorPickerOpen;
        bool IKeyboardShortcutActions.IsFullscreen => _windowState.IsFullscreen;
        bool IKeyboardShortcutActions.IsEpubMode => _isEpubMode;
        bool IKeyboardShortcutActions.IsVerticalMode
        {
            get => _isVerticalMode;
            set
            {
                _isVerticalMode = value;
                MainToolbar.SetVerticalToggleState(isChecked: value);
            }
        }

        bool IKeyboardShortcutActions.IsTextMode => _isTextMode;
        bool IKeyboardShortcutActions.IsAozoraMode => _isAozoraMode;
        bool IKeyboardShortcutActions.ShouldInvertControls => ShouldInvertControls;

        int IKeyboardShortcutActions.CurrentEpubChapterIndex
        {
            get => _currentEpubChapterIndex;
            set => _currentEpubChapterIndex = value;
        }

        int IKeyboardShortcutActions.EpubSpineCount => _epubSpine.Count;

        int IKeyboardShortcutActions.CurrentImageIndex
        {
            get => _currentIndex;
            set => _currentIndex = value;
        }

        int IKeyboardShortcutActions.ImageEntriesCount => _imageEntries.Count;
        bool IKeyboardShortcutActions.HasPdfDocument => _currentPdfDocument != null;

        bool IKeyboardShortcutActions.IsAboutDialogActive => _aboutDialog != null;
        bool IKeyboardShortcutActions.IsSearchOverlayOpen => _searchOverlayService?.IsOpen == true;
        bool IKeyboardShortcutActions.CanSearchCurrentDocument => CanSearchCurrentDocument;

        void IKeyboardShortcutActions.ToggleFullscreen() => ToggleFullscreen();
        void IKeyboardShortcutActions.ToggleMaximizeRestore() => ToggleMaximizeRestore();
        void IKeyboardShortcutActions.CloseApp() => RequestWindowClose();
        Task IKeyboardShortcutActions.ShowEpubGoToLineDialog() => ShowEpubGoToLineDialog();
        void IKeyboardShortcutActions.ToggleFont() => ToggleFont();

        void IKeyboardShortcutActions.ToggleVerticalMode()
        {
            _isVerticalMode = !_isVerticalMode;
            MainToolbar.SetVerticalToggleState(isChecked: _isVerticalMode);
            SaveTextSettings();
            ToggleVerticalMode();
        }

        void IKeyboardShortcutActions.DecreaseTextSize() => DecreaseTextSize();
        void IKeyboardShortcutActions.IncreaseTextSize() => IncreaseTextSize();
        void IKeyboardShortcutActions.ToggleSidebar() => ToggleSidebar();
        void IKeyboardShortcutActions.ToggleTheme() => ToggleTheme();
        Task IKeyboardShortcutActions.LoadEpubChapterAsync(int index) => LoadEpubChapterAsync(index);
        void IKeyboardShortcutActions.ToggleSideBySide() => SideBySideButton_Click(MainToolbar, new RoutedEventArgs());
        Task IKeyboardShortcutActions.NavigateDocumentPageAsync(int direction) =>
            _documentNavigationCoordinator.NavigatePageAsync(direction);
        Task IKeyboardShortcutActions.DisplayCurrentImageAsync() => DisplayCurrentImageAsync();
        Task IKeyboardShortcutActions.NavigateToFileAsync(bool forward) => NavigateToFileAsync(forward);
        Task IKeyboardShortcutActions.AddToFavoritesAsync() => AddToFavoritesAsync();

        void IKeyboardShortcutActions.ToggleSharpening()
        {
            SharpenButton_Click(MainToolbar, new RoutedEventArgs());
        }

        Task IKeyboardShortcutActions.ShowGoToLineDialog() => ShowGoToLineDialog();
        Task IKeyboardShortcutActions.NavigateToParentFolderAsync() => NavigateToParentFolderAsync();
        Task IKeyboardShortcutActions.OpenFileAsync() => OpenFileAsync();
        void IKeyboardShortcutActions.ZoomIn() => ZoomIn();
        void IKeyboardShortcutActions.ZoomOut() => ZoomOut();
        void IKeyboardShortcutActions.FitToWindow() => FitToWindow();
        void IKeyboardShortcutActions.ZoomActual() => ZoomActualButton_Click(MainToolbar, new RoutedEventArgs());
        void IKeyboardShortcutActions.ToggleAlwaysOnTop() => ToggleAlwaysOnTop();
        void IKeyboardShortcutActions.ToggleGlobalTheme() => GlobalThemeToggleButton_Click(MainToolbar, new RoutedEventArgs());
        void IKeyboardShortcutActions.TogglePin() => TogglePin();
        void IKeyboardShortcutActions.ShowSearchOverlay() => ShowSearchOverlay();
        void IKeyboardShortcutActions.HideSearchOverlay() => _searchOverlayService?.Hide();

        void IKeyboardShortcutActions.HideAboutDialog()
        {
            if (_aboutDialog != null)
            {
                _aboutDialog.Hide();
                _aboutDialog = null;
            }
        }
    }
}
