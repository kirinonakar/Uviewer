using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class KeyboardShortcutFeatureController
    {
        public async Task ExecuteAsync(ShortcutRoute route, IKeyboardShortcutActions actions)
        {
            switch (route.Command)
            {
                case AppCommand.HandledOnly:
                    return;

                case AppCommand.ShowSearchOverlay:
                    actions.ShowSearchOverlay();
                    return;

                case AppCommand.HideSearchOverlay:
                    actions.HideSearchOverlay();
                    return;

                case AppCommand.HideAboutDialog:
                    actions.HideAboutDialog();
                    return;

                case AppCommand.ToggleFullscreen:
                    actions.ToggleFullscreen();
                    return;

                case AppCommand.ToggleMaximizeRestore:
                    actions.ToggleMaximizeRestore();
                    return;

                case AppCommand.CloseApp:
                    actions.CloseApp();
                    return;

                case AppCommand.ShowEpubGoToLineDialog:
                    await actions.ShowEpubGoToLineDialog();
                    return;

                case AppCommand.ToggleFont:
                    actions.ToggleFont();
                    return;

                case AppCommand.ToggleVerticalMode:
                    actions.ToggleVerticalMode();
                    return;

                case AppCommand.DecreaseTextSize:
                    actions.DecreaseTextSize();
                    return;

                case AppCommand.IncreaseTextSize:
                    actions.IncreaseTextSize();
                    return;

                case AppCommand.ToggleSidebar:
                    actions.ToggleSidebar();
                    return;

                case AppCommand.ToggleTheme:
                    actions.ToggleTheme();
                    return;

                case AppCommand.LoadEpubChapter:
                    await LoadEpubChapterAsync(route.Direction, actions);
                    return;

                case AppCommand.ToggleSideBySide:
                    actions.ToggleSideBySide();
                    return;

                case AppCommand.NavigateDocumentPage:
                    await actions.NavigateDocumentPageAsync(route.Direction);
                    return;

                case AppCommand.NavigateToFirstImage:
                    await NavigateToImageAsync(0, actions);
                    return;

                case AppCommand.NavigateToLastImage:
                    await NavigateToImageAsync(actions.ImageEntriesCount - 1, actions);
                    return;

                case AppCommand.NavigateToFile:
                    await actions.NavigateToFileAsync(route.Direction > 0);
                    return;

                case AppCommand.AddToFavorites:
                    await actions.AddToFavoritesAsync();
                    return;

                case AppCommand.ToggleSharpening:
                    actions.ToggleSharpening();
                    return;

                case AppCommand.ShowGoToLineDialog:
                    await actions.ShowGoToLineDialog();
                    return;

                case AppCommand.NavigateToParentFolder:
                    await actions.NavigateToParentFolderAsync();
                    return;

                case AppCommand.OpenFile:
                    await actions.OpenFileAsync();
                    return;

                case AppCommand.ZoomIn:
                    actions.ZoomIn();
                    return;

                case AppCommand.ZoomOut:
                    actions.ZoomOut();
                    return;

                case AppCommand.FitToWindow:
                    actions.FitToWindow();
                    return;

                case AppCommand.ZoomActual:
                    actions.ZoomActual();
                    return;

                case AppCommand.ToggleAlwaysOnTop:
                    actions.ToggleAlwaysOnTop();
                    return;

                case AppCommand.ToggleGlobalTheme:
                    actions.ToggleGlobalTheme();
                    return;

                case AppCommand.TogglePin:
                    actions.TogglePin();
                    return;
            }
        }

        private static async Task LoadEpubChapterAsync(int direction, IKeyboardShortcutActions actions)
        {
            int targetIndex = actions.CurrentEpubChapterIndex + direction;
            if (targetIndex < 0 || targetIndex >= actions.EpubSpineCount)
            {
                return;
            }

            actions.CurrentEpubChapterIndex = targetIndex;
            await actions.LoadEpubChapterAsync(targetIndex);
        }

        private static async Task NavigateToImageAsync(int targetIndex, IKeyboardShortcutActions actions)
        {
            if (targetIndex < 0 || targetIndex >= actions.ImageEntriesCount || actions.CurrentImageIndex == targetIndex)
            {
                return;
            }

            actions.CurrentImageIndex = targetIndex;
            await actions.DisplayCurrentImageAsync();
        }
    }
}
