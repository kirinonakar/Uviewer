using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class KeyboardShortcutActionsAdapter : IKeyboardShortcutActions
        {
            private readonly MainWindow _window;

            public KeyboardShortcutActionsAdapter(MainWindow window)
            {
                _window = window;
            }

            public bool IsColorPickerOpen => _window._isColorPickerOpen;
            public bool IsFullscreen => _window._windowState.IsFullscreen;
            public bool IsEpubMode => _window._isEpubMode;
            public bool IsVerticalMode
            {
                get => _window._isVerticalMode;
                set
                {
                    _window._isVerticalMode = value;
                    _window.MainToolbar.SetVerticalToggleState(isChecked: value);
                }
            }

            public bool IsTextMode => _window._isTextMode;
            public bool IsAozoraMode => _window._isAozoraMode;
            public bool ShouldInvertControls => _window.ShouldInvertControls;

            public int CurrentEpubChapterIndex
            {
                get => _window._currentEpubChapterIndex;
                set => _window._currentEpubChapterIndex = value;
            }

            public int EpubSpineCount => _window._epubSpine.Count;

            public int CurrentImageIndex
            {
                get => _window._imageViewerState.CurrentIndex;
                set => _window._imageViewerState.CurrentIndex = value;
            }

            public int ImageEntriesCount => _window._imageViewerState.Entries.Count;
            public bool HasPdfDocument => _window._currentPdfDocument != null;
            public bool IsAboutDialogActive => _window._aboutDialog != null;
            public bool IsSearchOverlayOpen => _window._searchOverlayService?.IsOpen == true;
            public bool CanSearchCurrentDocument => _window._searchController.CanSearchCurrentDocument;

            public void ToggleFullscreen() => _window.ToggleFullscreen();
            public void ToggleMaximizeRestore() => _window.ToggleMaximizeRestore();
            public void CloseApp() => _window.RequestWindowClose();
            public Task ShowEpubGoToLineDialog() => _window._epubReaderController.ShowEpubGoToLineDialog();
            public void ToggleFont() => _window._documentReaderController.ToggleFont();

            public void ToggleVerticalMode()
            {
                if (_window._documentReaderController.IsPlainTextModeLockedDocumentActive())
                {
                    _window._documentReaderController.ApplyPlainTextModeLock();
                    return;
                }

                _window._isVerticalMode = !_window._isVerticalMode;
                _window.MainToolbar.SetVerticalToggleState(isChecked: _window._isVerticalMode);
                _window.SaveTextSettings();
                _window._documentReaderController.ToggleVerticalMode();
            }

            public void DecreaseTextSize() => _window._documentReaderController.DecreaseTextSize();
            public void IncreaseTextSize() => _window._documentReaderController.IncreaseTextSize();
            public void ToggleSidebar() => _window._windowChromeController.ToggleSidebar();
            public void ToggleTheme() => _window._documentReaderController.ToggleTheme();
            public Task LoadEpubChapterAsync(int index) => _window._epubReaderController.LoadEpubChapterAsync(index);
            public void ToggleSideBySide() => _window._imageViewerController.ToggleSideBySide();
            public Task NavigateDocumentPageAsync(int direction) =>
                _window._documentNavigationCoordinator.NavigatePageAsync(direction);
            public Task DisplayCurrentImageAsync() => _window._imageViewerController.DisplayCurrentImageAsync();
            public Task NavigateToFileAsync(bool forward) => _window._imageViewerController.NavigateToFileAsync(forward);
            public Task AddToFavoritesAsync() => _window._bookmarkInteractionController.AddCurrentFavoriteAsync();

            public void ToggleSharpening()
            {
                _ = _window._imageViewerController.ToggleSharpeningAsync();
            }

            public Task ShowGoToLineDialog() => _window._documentReaderController.ShowGoToLineDialog();
            public Task NavigateToParentFolderAsync() => _window._explorerSidebarController.NavigateToParentFolderAsync();
            public Task OpenFileAsync() => _window._fileOpenController.OpenFileAsync();
            public void ZoomIn() => _window._imageViewerController.ZoomIn();
            public void ZoomOut() => _window._imageViewerController.ZoomOut();
            public void FitToWindow() => _window._imageViewerController.FitToWindow();
            public void ZoomActual() => _window._imageViewerController.ZoomActual();
            public void ToggleAlwaysOnTop() => _window.ToggleAlwaysOnTop();
            public void ToggleGlobalTheme() => _window.GlobalThemeToggleButton_Click(_window.MainToolbar, new RoutedEventArgs());
            public void TogglePin() => _window.TogglePin();
            public void ShowSearchOverlay() => _window._searchController.ShowOverlay();
            public void HideSearchOverlay() => _window._searchOverlayService?.Hide();

            public void HideAboutDialog()
            {
                if (_window._aboutDialog != null)
                {
                    _window._aboutDialog.Hide();
                    _window._aboutDialog = null;
                }
            }
        }
    }
}
