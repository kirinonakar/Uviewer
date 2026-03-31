using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public class KeyboardShortcutService : IKeyboardShortcutService
    {
        public Task HandlePreviewKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions)
        {
            if (actions.IsColorPickerOpen && e.Key == Windows.System.VirtualKey.Escape) return Task.CompletedTask;

            // Allow text input controls to function normally (e.g. WebDAV dialog)
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox || e.OriginalSource is NumberBox) return Task.CompletedTask;

            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // --- Immediate Handled Actions (Sync) ---

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                if (actions.IsAboutDialogActive)
                {
                    actions.HideAboutDialog();
                }
                else if (actions.IsFullscreen) actions.ToggleFullscreen();
                else actions.CloseApp();
                return Task.CompletedTask;
            }

            if (e.Key == Windows.System.VirtualKey.F11)
            {
                e.Handled = true;
                actions.ToggleFullscreen();
                return Task.CompletedTask;
            }

            // --- EPUB/Vertical Mode Handling ---
            if (actions.IsEpubMode)
            {
                if (e.Key == Windows.System.VirtualKey.Left)
                {
                    e.Handled = true;
                    if (actions.IsVerticalMode) actions.NavigateVerticalPage(1);
                    else _ = actions.NavigateEpubAsync(-1);
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.Right)
                {
                    e.Handled = true;
                    if (actions.IsVerticalMode) actions.NavigateVerticalPage(-1);
                    else _ = actions.NavigateEpubAsync(1);
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.G)
                {
                    e.Handled = true;
                    _ = actions.ShowEpubGoToLineDialog();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.F)
                {
                    e.Handled = true;
                    actions.ToggleFont();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.V)
                {
                    e.Handled = true;
                    actions.ToggleVerticalMode();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.Subtract || e.Key == (Windows.System.VirtualKey)189) // - key
                {
                    e.Handled = true;
                    actions.DecreaseTextSize();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.Add || e.Key == (Windows.System.VirtualKey)187) // + key
                {
                    e.Handled = true;
                    actions.IncreaseTextSize();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.B)
                {
                    e.Handled = true;
                    if (ctrlPressed) actions.ToggleSidebar();
                    else actions.ToggleTheme();
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.Home)
                {
                    e.Handled = true;
                    if (actions.CurrentEpubChapterIndex > 0)
                    {
                        actions.CurrentEpubChapterIndex--;
                        _ = actions.LoadEpubChapterAsync(actions.CurrentEpubChapterIndex);
                    }
                    return Task.CompletedTask;
                }
                else if (e.Key == Windows.System.VirtualKey.End)
                {
                    e.Handled = true;
                    if (actions.CurrentEpubChapterIndex < actions.EpubSpineCount - 1)
                    {
                        actions.CurrentEpubChapterIndex++;
                        _ = actions.LoadEpubChapterAsync(actions.CurrentEpubChapterIndex);
                    }
                    return Task.CompletedTask;
                }
            }

            // Handle Space to prevent toolbar buttons from capturing it
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                if (actions.IsTextMode) return Task.CompletedTask; // Block and ignore in text mode
                actions.ToggleSideBySide();
                return Task.CompletedTask;
            }

            // --- Async Actions (Fire and Forget with Handled = true) ---

            if (!actions.IsTextMode && !actions.IsEpubMode)
            {
                // Intercept Left/Right for Archive/Image internal navigation
                if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right)
                {
                    e.Handled = true;
                    if (e.Key == Windows.System.VirtualKey.Left)
                    {
                        if (actions.ShouldInvertControls) _ = actions.NavigateToNextAsync(false);
                        else _ = actions.NavigateToPreviousAsync(false);
                    }
                    else
                    {
                        if (actions.ShouldInvertControls) _ = actions.NavigateToPreviousAsync(false);
                        else _ = actions.NavigateToNextAsync(false);
                    }
                    return Task.CompletedTask;
                }

                // Intercept Home/End for first/last image
                if (actions.ImageEntriesCount > 0)
                {
                    if (e.Key == Windows.System.VirtualKey.Home)
                    {
                        e.Handled = true;
                        if (actions.CurrentImageIndex != 0)
                        {
                            actions.CurrentImageIndex = 0;
                            _ = actions.DisplayCurrentImageAsync();
                        }
                        return Task.CompletedTask;
                    }
                    else if (e.Key == Windows.System.VirtualKey.End)
                    {
                        e.Handled = true;
                        if (actions.CurrentImageIndex != actions.ImageEntriesCount - 1)
                        {
                            actions.CurrentImageIndex = actions.ImageEntriesCount - 1;
                            _ = actions.DisplayCurrentImageAsync();
                        }
                        return Task.CompletedTask;
                    }
                }
            }

            // Handle Up/Down keys in PreviewKeyDown for file navigation (Works in EPUB mode too as requested)
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.PageUp)
            {
                e.Handled = true;
                _ = actions.NavigateToFileAsync(false);
                return Task.CompletedTask;
            }
            if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.PageDown)
            {
                e.Handled = true;
                _ = actions.NavigateToFileAsync(true);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        public async Task HandleKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions)
        {
            if (e.Handled) return;
            // Allow text input controls to function normally
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox || e.OriginalSource is NumberBox) return;

            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            switch (e.Key)
            {
                case Windows.System.VirtualKey.S:
                    if (ctrlPressed) await actions.AddToFavoritesAsync();
                    else if (!actions.IsTextMode || actions.IsAozoraMode || actions.IsVerticalMode) // Enable in EPUB, Aozora, and Vertical modes, keep disabled in raw text mode
                    {
                        actions.ToggleSharpening();
                    }
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.G:
                    if (!actions.IsTextMode && !actions.IsEpubMode && actions.HasPdfDocument)
                    {
                        await actions.ShowGoToLineDialog();
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.Back:
                    await actions.NavigateToParentFolderAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.O when ctrlPressed:
                    await actions.OpenFileAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.B when ctrlPressed:
                    actions.ToggleSidebar();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Add:
                case (Windows.System.VirtualKey)187: // Main keyboard Plus/Equal
                    actions.ZoomIn();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Subtract:
                case (Windows.System.VirtualKey)189: // Main keyboard Minus
                    actions.ZoomOut();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Number0:
                case Windows.System.VirtualKey.NumberPad0:
                    if (!ctrlPressed)
                    {
                        actions.FitToWindow();
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.Number1:
                case Windows.System.VirtualKey.NumberPad1:
                    if (!ctrlPressed)
                    {
                        actions.ZoomActual();
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.T when !ctrlPressed:
                    actions.ToggleAlwaysOnTop();
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.D when !ctrlPressed:
                    actions.ToggleGlobalTheme();
                    e.Handled = true;
                    break;
                case (Windows.System.VirtualKey)192: // ` (backtick / OEM_3)
                    actions.TogglePin();
                    e.Handled = true;
                    break;
            }
        }
    }
}
