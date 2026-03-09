using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {

        #region Keyboard Shortcuts

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_isColorPickerOpen && e.Key == Windows.System.VirtualKey.Escape) return;

            // Allow text input controls to function normally (e.g. WebDAV dialog)
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox || e.OriginalSource is NumberBox) return;
            
            // --- Immediate Handled Actions (Sync) ---

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                if (_isFullscreen) ToggleFullscreen();
                else CloseWindowButton_Click(sender, new RoutedEventArgs());
                return;
            }

            if (e.Key == Windows.System.VirtualKey.F11)
            {
                e.Handled = true;
                ToggleFullscreen();
                return;
            }

            // Handle Space to prevent toolbar buttons from capturing it
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                SideBySideButton_Click(sender, new RoutedEventArgs());
                return;
            }

            // --- Async Actions (Fire and Forget with Handled = true) ---

            // Text/Epub Mode usually handled elsewhere, but we block certain keys here if needed
            if (!_isTextMode && !_isEpubMode)
            {
                // Intercept Left/Right for Archive/Image internal navigation
                if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right)
                {
                    e.Handled = true;
                    if (e.Key == Windows.System.VirtualKey.Left)
                    {
                        if (ShouldInvertControls) _ = NavigateToNextAsync(false);
                        else _ = NavigateToPreviousAsync(false);
                    }
                    else
                    {
                        if (ShouldInvertControls) _ = NavigateToPreviousAsync(false);
                        else _ = NavigateToNextAsync(false);
                    }
                    return;
                }

                // Intercept Home/End for first/last image
                if (_imageEntries != null && _imageEntries.Count > 0)
                {
                    if (e.Key == Windows.System.VirtualKey.Home)
                    {
                        e.Handled = true;
                        if (_currentIndex != 0)
                        {
                            _currentIndex = 0;
                            _ = DisplayCurrentImageAsync();
                        }
                        return;
                    }
                    else if (e.Key == Windows.System.VirtualKey.End)
                    {
                        e.Handled = true;
                        if (_currentIndex != _imageEntries.Count - 1)
                        {
                            _currentIndex = _imageEntries.Count - 1;
                            _ = DisplayCurrentImageAsync();
                        }
                        return;
                    }
                }
            }

            // Handle Up/Down keys in PreviewKeyDown for file navigation
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.PageUp)
            {
                e.Handled = true;
                _ = NavigateToFileAsync(false);
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.PageDown)
            {
                e.Handled = true;
                _ = NavigateToFileAsync(true);
                return;
            }
        }

        private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled) return;
            // Allow text input controls to function normally
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox || e.OriginalSource is NumberBox) return;

            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            switch (e.Key)
            {
                case Windows.System.VirtualKey.S:
                    if (ctrlPressed) _ = AddToFavoritesAsync();
                    else
                    {
                        SharpenButton.IsChecked = !(SharpenButton.IsChecked ?? false);
                        SharpenButton_Click(SharpenButton, new RoutedEventArgs());
                    }
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.G:
                    if (!_isTextMode && !_isEpubMode && _currentPdfDocument != null)
                    {
                        _ = ShowGoToLineDialog();
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.Back:
                    await NavigateToParentFolderAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.O when ctrlPressed:
                    await OpenFileAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.B when ctrlPressed:
                    ToggleSidebar();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Add:
                case (Windows.System.VirtualKey)187: // Main keyboard Plus/Equal
                    ZoomIn();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Subtract:
                case (Windows.System.VirtualKey)189: // Main keyboard Minus
                    ZoomOut();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Number0 when ctrlPressed:
                    FitToWindow();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Number1 when ctrlPressed:
                    ZoomActualButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.T when !ctrlPressed:
                    ToggleAlwaysOnTop();
                    e.Handled = true;
                    break;
                case (Windows.System.VirtualKey)192: // ` (backtick / OEM_3)
                    TogglePin();
                    e.Handled = true;
                    break;
            }
        }

        #endregion
    }
}