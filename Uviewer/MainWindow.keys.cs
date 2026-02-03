using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        #region Keyboard Shortcuts

        private void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ProcessKeyDown(sender.Key, ctrlPressed))
            {
                args.Handled = true;
            }
        }

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ProcessKeyDown(e.Key, ctrlPressed))
            {
                e.Handled = true;
            }
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled) return;

            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ProcessKeyDown(e.Key, ctrlPressed))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Central key processing logic shared between XAML events and WebView2
        /// </summary>
        private bool ProcessKeyDown(Windows.System.VirtualKey key, bool ctrlPressed)
        {
            switch (key)
            {
                case Windows.System.VirtualKey.Escape:
                    if (SearchOverlay.Visibility == Visibility.Visible)
                    {
                        SearchOverlay.Visibility = Visibility.Collapsed;
                        return true;
                    }
                    if (PageJumpOverlay.Visibility == Visibility.Visible)
                    {
                        PageJumpOverlay.Visibility = Visibility.Collapsed;
                        return true;
                    }
                    if (_isFullscreen)
                    {
                        ToggleFullscreen();
                        return true;
                    }
                    
                    // Not in fullscreen, close app
                    Application.Current.Exit();
                    return true;

                case Windows.System.VirtualKey.F11:
                    ToggleFullscreen();
                    return true;

                case Windows.System.VirtualKey.Space:
                    if (_isTextMode)
                    {
                        // Text mode: 2-column view disabled, ignore space key
                        return true;
                    }
                    else
                    {
                        SideBySideButton_Click(this, new RoutedEventArgs());
                    }
                    return true;

                case Windows.System.VirtualKey.S:
                    if (!_isTextMode)
                    {
                        SharpenButton.IsChecked = !(SharpenButton.IsChecked ?? false);
                        SharpenButton_Click(SharpenButton, new RoutedEventArgs());
                        return true;
                    }
                    break;

                case Windows.System.VirtualKey.F:
                    if (_isTextMode)
                    {
                        SearchOverlay.Visibility = Visibility.Visible;
                        SearchBox.Focus(FocusState.Programmatic);
                        return true;
                    }
                    break;

                case Windows.System.VirtualKey.G:
                    if (_isTextMode)
                    {
                        PageJumpOverlay.Visibility = Visibility.Visible;
                        
                        // Extract current page number from ImageIndexText and set it in PageJumpBox
                        if (ImageIndexText.Text.Contains("Page:"))
                        {
                            var parts = ImageIndexText.Text.Split('/');
                            if (parts.Length >= 1)
                            {
                                string currentPageStr = parts[0].Replace("Page: ", "").Trim();
                                if (int.TryParse(currentPageStr, out int currentPage))
                                {
                                    PageJumpBox.Text = currentPage.ToString();
                                    // Select all text so user can easily type over it or use backspace
                                    PageJumpBox.SelectAll();
                                }
                                else
                                {
                                    PageJumpBox.Text = "";
                                }
                            }
                            else
                            {
                                PageJumpBox.Text = "";
                            }
                        }
                        else
                        {
                            PageJumpBox.Text = "";
                        }
                        
                        PageJumpBox.Focus(FocusState.Programmatic);
                        return true;
                    }
                    break;

                case Windows.System.VirtualKey.Left:
                    if (_isTextMode)
                    {
                        if (_isEpubMode)
                        {
                            _ = NavigateToPreviousEpubPageAsync();
                        }
                        else
                            _ = ScrollTextPage(false);
                    }
                    else
                    {
                        _ = NavigateToPreviousAsync();
                    }
                    return true;

                case Windows.System.VirtualKey.Right:
                    if (_isTextMode)
                    {
                        if (_isEpubMode)
                        {
                            _ = NavigateToNextEpubPageAsync();
                        }
                        else
                            _ = ScrollTextPage(true);
                    }
                    else
                    {
                        _ = NavigateToNextAsync();
                    }
                    return true;

                case Windows.System.VirtualKey.Up:
                    if (_isTextMode)
                    {
                        // In text mode, navigate to previous text file only
                        _ = NavigateToFileByTypeAsync(false, "text");
                    }
                    else if (_currentArchive != null)
                    {
                        // In archive mode, navigate to previous archive only
                        _ = NavigateToFileByTypeAsync(false, "archive");
                    }
                    else if (_imageEntries.Count > 0)
                    {
                        // In image mode, navigate to previous image only
                        _ = NavigateToFileByTypeAsync(false, "image");
                    }
                    else
                    {
                        _ = NavigateToFileAsync(false);
                    }
                    return true;

                case Windows.System.VirtualKey.Down:
                    if (_isTextMode)
                    {
                        // In text mode, navigate to next text file only
                        _ = NavigateToFileByTypeAsync(true, "text");
                    }
                    else if (_currentArchive != null)
                    {
                        // In archive mode, navigate to next archive only
                        _ = NavigateToFileByTypeAsync(true, "archive");
                    }
                    else if (_imageEntries.Count > 0)
                    {
                        // In image mode, navigate to next image only
                        _ = NavigateToFileByTypeAsync(true, "image");
                    }
                    else
                    {
                        _ = NavigateToFileAsync(true);
                    }
                    return true;

                case Windows.System.VirtualKey.Back:
                    // In text mode, if page jump dialog is open, don't navigate to parent folder
                    // Also don't process if PageJumpBox is focused
                    if (!(_isTextMode && PageJumpOverlay.Visibility == Visibility.Visible) && 
                        PageJumpBox != null && PageJumpBox.FocusState != FocusState.Unfocused)
                    {
                        _ = NavigateToParentFolderAsync();
                    }
                    return true;

                case Windows.System.VirtualKey.O when ctrlPressed:
                    _ = OpenFileAsync();
                    return true;

                case Windows.System.VirtualKey.B when ctrlPressed:
                    ToggleSidebar();
                    return true;

                case Windows.System.VirtualKey.Add:
                case (Windows.System.VirtualKey)187: // OemPlus
                    if (_isTextMode)
                    {
                        ZoomTextStyle(true);
                    }
                    else
                    {
                        ZoomIn();
                    }
                    return true;

                case Windows.System.VirtualKey.Subtract:
                case (Windows.System.VirtualKey)189: // OemMinus
                    if (_isTextMode)
                    {
                        ZoomTextStyle(false);
                    }
                    else
                    {
                        ZoomOut();
                    }
                    return true;

                case Windows.System.VirtualKey.Number0 when ctrlPressed:
                    if (_isTextMode)
                    {
                        // Save current scroll position before resetting font size
                        _ = Task.Run(async () =>
                        {
                            double? scrollPosition = null;
                            if (TextViewer.CoreWebView2 != null)
                            {
                                scrollPosition = await GetTextScrollPositionAsync();
                            }
                            
                            _textFontSize = 18;
                            _ = UpdateTextViewer();
                            UpdateStatusBarForText();
                            ZoomLevelText.Text = $"{_textFontSize}pt";
                            
                            // Restore scroll position after font size reset
                            if (scrollPosition.HasValue)
                            {
                                // Give it a moment to render the new content
                                await Task.Delay(100);
                                await SetTextScrollPosition(scrollPosition.Value);
                            }
                        });
                    }
                    else
                    {
                        FitToWindow();
                    }
                    return true;

                case Windows.System.VirtualKey.Home:
                    if (_isTextMode)
                    {
                        if (_isEpubMode)
                        {
                            if (_epubChapters.Count > 0)
                            {
                                _ = LoadEpubChapterAsync(0);
                            }
                        }
                        else
                        {
                            _ = TextViewer.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0)").AsTask();
                        }
                    }
                    else if (_imageEntries.Count > 0)
                    {
                        _currentIndex = 0;
                        _ = DisplayCurrentImageAsync();
                    }
                    return true;

                case Windows.System.VirtualKey.End:
                    if (_isTextMode)
                    {
                        if (_isEpubMode)
                        {
                            if (_epubChapters.Count > 0)
                            {
                                _ = LoadEpubChapterAsync(_epubChapters.Count - 1);
                            }
                        }
                        else
                        {
                            _ = TextViewer.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, document.body.scrollHeight)").AsTask();
                        }
                    }
                    else if (_imageEntries.Count > 0)
                    {
                        _currentIndex = _imageEntries.Count - 1;
                        _ = DisplayCurrentImageAsync();
                    }
                    return true;
            }

            return false;
        }

        #endregion

        /// <summary>
        /// Navigate to next/previous file of specific type (image, archive, or text)
        /// </summary>
        private async Task NavigateToFileByTypeAsync(bool isNext, string fileType)
        {
            // For text mode, try to get files from current text file's directory if no explorer path
            if (string.IsNullOrEmpty(_currentExplorerPath))
            {
                if (!string.IsNullOrEmpty(_currentTextFilePath))
                {
                    // Get directory from current text file and load files from there
                    string? textFileDir = Path.GetDirectoryName(_currentTextFilePath);
                    if (!string.IsNullOrEmpty(textFileDir))
                    {
                        LoadExplorerFolder(textFileDir);
                    }
                    else
                    {
                        return; // Can't determine directory
                    }
                }
                else
                {
                    return; // No current text file to use as reference
                }
            }

            // Ensure file list is loaded
            if (_fileItems.Count == 0)
            {
                if (!string.IsNullOrEmpty(_currentExplorerPath))
                {
                    LoadExplorerFolder(_currentExplorerPath);
                }
                else
                {
                    return; // No path to load files from
                }
            }

            // Find current file/archive in the list
            string? currentPath = null;
            if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
            {
                currentPath = _currentArchivePath;
            }
            else if (_imageEntries.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageEntries.Count)
            {
                currentPath = _imageEntries[_currentIndex].FilePath;
            }
            
            // Fallback for text mode - use current text file path
            if (string.IsNullOrEmpty(currentPath) && !string.IsNullOrEmpty(_currentTextFilePath))
            {
                // For archive text files, extract the archive path
                if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    currentPath = _currentArchivePath;
                }
                else
                {
                    currentPath = _currentTextFilePath;
                }
            }

            if (string.IsNullOrEmpty(currentPath))
                return;

            var currentItemIndex = -1;
            for (int i = 0; i < _fileItems.Count; i++)
            {
                if (_fileItems[i].FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentItemIndex = i;
                    break;
                }
            }

            if (currentItemIndex == -1) return;

            // Find next/prev file of specific type
            int newIndex = currentItemIndex;
            while (true)
            {
                newIndex = isNext ? newIndex + 1 : newIndex - 1;

                if (newIndex < 0 || newIndex >= _fileItems.Count)
                    break; // End of list

                var item = _fileItems[newIndex];
                if (item.IsDirectory || item.IsParentDirectory)
                    continue; // Skip directories

                // Check if this file matches the requested type
                bool matchesType = fileType switch
                {
                    "image" => item.IsImage,
                    "archive" => item.IsArchive,
                    "text" => item.IsText,
                    _ => false
                };

                if (!matchesType)
                    continue; // Skip files that don't match the requested type

                try
                {
                    if (item.IsArchive)
                    {
                        await LoadImagesFromArchiveAsync(item.FullPath);

                        // Select in sidebar
                        FileListView.SelectedItem = item;
                        FileListView.ScrollIntoView(item);
                        return;
                    }
                    else if (item.IsImage)
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                        await LoadImageFromFileAsync(file);

                        // Select in sidebar
                        FileListView.SelectedItem = item;
                        FileListView.ScrollIntoView(item);
                        return;
                    }
                    else if (item.IsText)
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                        LoadTextFromFileWithDebounce(file);

                        // Select in sidebar
                        FileListView.SelectedItem = item;
                        FileListView.ScrollIntoView(item);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error navigating to file: {ex.Message}");
                    // If error, continue to next file
                }
            }
        }
    }
}