using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {

        #region Keyboard Shortcuts

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
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
                    // 체크 상태만 바꾸고 클릭 핸들러를 실행하면, 핸들러 내부에서 UI를 업데이트합니다.
                    SharpenButton.IsChecked = !(SharpenButton.IsChecked ?? false);
                    SharpenButton_Click(SharpenButton, new RoutedEventArgs());
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

    }
}