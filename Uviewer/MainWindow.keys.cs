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
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Disable specific keys in Text/Epub mode if needed, or prevent interference
            if (_isEpubMode || _isTextMode)
            {
                // S is blocked unless Ctrl is pressed
                if (e.Key == Windows.System.VirtualKey.Space || (e.Key == Windows.System.VirtualKey.S && !ctrlPressed))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Handle Up/Down keys in PreviewKeyDown to ensure they always navigate files
            // even if controls like ScrollViewer or FlipView would otherwise capture them.
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.PageUp)
            {
                _ = NavigateToFileAsync(false);
                e.Handled = true;
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.PageDown)
            {
                _ = NavigateToFileAsync(true);
                e.Handled = true;
                return;
            }

            // Handle Space key in PreviewKeyDown to prevent toolbar buttons from capturing it
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                // Toggle between single and side-by-side mode
                SideBySideButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Escape when !_isFullscreen:
                    // Close the app when ESC is pressed (but not in fullscreen)
                    CloseWindowButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Escape when _isFullscreen:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.S:
                    if (ctrlPressed)
                    {
                        _ = AddToFavoritesAsync();
                    }
                    else
                    {
                        // 체크 상태만 바꾸고 클릭 핸들러를 실행하면, 핸들러 내부에서 UI를 업데이트합니다.
                        SharpenButton.IsChecked = !(SharpenButton.IsChecked ?? false);
                        SharpenButton_Click(SharpenButton, new RoutedEventArgs());
                    }
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Left:
                    await NavigateToPreviousAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Right:
                    await NavigateToNextAsync();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Up:
                    await NavigateToFileAsync(false);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Down:
                    await NavigateToFileAsync(true);
                    e.Handled = true;
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
                    ZoomIn();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Subtract:
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

                case Windows.System.VirtualKey.Home:
                    if (_imageEntries.Count > 0)
                    {
                        _currentIndex = 0;
                        await DisplayCurrentImageAsync();
                    }
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.End:
                    if (_imageEntries.Count > 0)
                    {
                        _currentIndex = _imageEntries.Count - 1;
                        await DisplayCurrentImageAsync();
                    }
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        private void RootGrid_Global_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled) return;
            // Global handler to intercept navigation keys when in specific modes
            // This prevents controls like ListView from consuming Home/End keys when we want to navigate images.

            // 1. Text/Epub Mode should be handled by their specific logic (or we can block here)
            // But they usually have their own PreviewKeyDown handlers attached to RootGrid possibly.
            // If they handled it, e.Handled might be true? 
            // NOTE: PreviewKeyDown tunnels. We are at RootGrid.
            // If we set Handled=true, children won't get it (e.g. Textbox).
            
            // Allow text boxes to funciton
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox) return;

            if (_isTextMode || _isEpubMode) return; 

            // Intercept Left/Right for Archive internal navigation even when explorer grid is focused
            if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right)
            {
                if (_currentArchive != null)
                {
                    if (e.Key == Windows.System.VirtualKey.Left) _ = NavigateToPreviousAsync();
                    else _ = NavigateToNextAsync();
                    e.Handled = true;
                    return;
                }
            }

            // Image Mode (Archive or Folder with images)
            if (_imageEntries != null && _imageEntries.Count > 0)
            {
                if (e.Key == Windows.System.VirtualKey.Home)
                {
                    if (_currentIndex != 0)
                    {
                        _currentIndex = 0;
                        _ = DisplayCurrentImageAsync();
                    }
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.End)
                {
                    if (_currentIndex != _imageEntries.Count - 1)
                    {
                        _currentIndex = _imageEntries.Count - 1;
                        _ = DisplayCurrentImageAsync();
                    }
                    e.Handled = true;
                }
            }
        }
    }
}