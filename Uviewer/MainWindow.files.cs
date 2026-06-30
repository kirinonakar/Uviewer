using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Media.Imaging;
using Uviewer.Models;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private Services.ArchiveSession _archiveSession = null!;

        #region File Operations

        private async void PrevFileButton_Click(object sender, RoutedEventArgs e)
        {
            try { await NavigateToFileAsync(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

        private async void NextFileButton_Click(object sender, RoutedEventArgs e)
        {
            try { await NavigateToFileAsync(true); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _documentNavigationCoordinator.NavigatePreviousAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrevPageButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _documentNavigationCoordinator.NavigateNextAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NextPageButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task LoadImageFromFileAsync(StorageFile file, bool isInitial = false)
        {
            // 이전 작업 즉시 중단
            _sevenZipExtraction.CancelExtraction();
            _imageLoadingCts?.Cancel();
            _preloadManager.CancelAll();
            _globalTextCts?.Cancel();

            if (!await CloseCurrentArchiveAsync()) return;
            if (!await CloseCurrentPdfAsync()) return;
            if (!await CloseCurrentEpubAsync()) return;
            CloseCurrentText();

            // Cancel any ongoing preloading and clear cache
            _preloadManager.CancelAll();
            
            _imageCache?.ClearAll();

            if (isInitial)
            {
                // [Step 1] FAST LOAD: Display only the selected file first
                _imageEntries = new List<ImageEntry>
                {
                    new ImageEntry { DisplayName = file.Name, FilePath = file.Path }
                };
                _currentIndex = 0;
                await DisplayCurrentImageAsync();

                // [Step 2] BACKGROUND: Gather other files in folder
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var folder = await file.GetParentAsync();
                        if (folder == null) return;
                        
                        var files = await folder.GetFilesAsync();
                        var allEntries = files
                            .Where(f => FileExplorerService.SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                            .OrderBy(f => f.Name, NaturalSortComparer.Default)
                            .Select(f => new ImageEntry
                            {
                                DisplayName = f.Name,
                                FilePath = f.Path
                            })
                            .ToList();

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            // Only update if we are still on the first manual load file
                            if (_imageEntries != null && _imageEntries.Count == 1 && _imageEntries[0].FilePath == file.Path)
                            {
                                var oldIndex = _currentIndex;
                                _imageEntries = allEntries;
                                _currentIndex = _imageEntries.FindIndex(e => e.FilePath == file.Path);
                                // Refresh status bar to show correct "1 / N"
                                if (_currentBitmap != null && _currentIndex >= 0)
                                {
                                    UpdateStatusBar(_imageEntries[_currentIndex], _currentBitmap);
                                }
                            }
                        });
                    }
                    catch { }
                });
            }
            else
            {
                // Normal sequential load
                var folder = await file.GetParentAsync();
                if (folder != null)
                {
                    var files = await folder.GetFilesAsync();
                    _imageEntries = files
                        .Where(f => FileExplorerService.SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                        .OrderBy(f => f.Name, NaturalSortComparer.Default)
                        .Select(f => new ImageEntry
                        {
                            DisplayName = f.Name,
                            FilePath = f.Path
                        })
                        .ToList();

                    _currentIndex = _imageEntries.FindIndex(e => e.FilePath == file.Path);
                }
                else
                {
                    _imageEntries = new List<ImageEntry>
                    {
                        new ImageEntry { DisplayName = file.Name, FilePath = file.Path }
                    };
                    _currentIndex = 0;
                }

                await DisplayCurrentImageAsync();
            }
        }

        private async Task LoadImagesFromFolderAsync(StorageFolder folder)
        {
            // 이전 작업 즉시 중단
            _sevenZipExtraction.CancelExtraction();
            _imageLoadingCts?.Cancel();
            _explorerState.CancelThumbnailLoading();
            _preloadManager.CancelAll();
            _globalTextCts?.Cancel();

            if (!await CloseCurrentArchiveAsync()) return;
            if (!await CloseCurrentPdfAsync()) return;
            if (!await CloseCurrentEpubAsync()) return;

            // Cancel any ongoing preloading and clear cache
            _preloadManager.CancelAll();
            
            _imageCache?.ClearAll();

            var files = await folder.GetFilesAsync();
            _imageEntries = files
                .Where(f => FileExplorerService.SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                .OrderBy(f => f.Name, NaturalSortComparer.Default)
                .Select(f => new ImageEntry
                {
                    DisplayName = f.Name,
                    FilePath = f.Path
                })
                .ToList();

            if (_imageEntries.Count > 0)
            {
                _currentIndex = 0;
                await DisplayCurrentImageAsync();
            }
            else
            {
                FileNameText.Text = "이 폴더에 이미지가 없습니다";
            }
        }

        private Task LoadImagesFromArchiveAsync(string archivePath)
            => _archiveDocumentController.LoadImagesFromArchiveAsync(archivePath);

        private async Task<bool> CloseCurrentArchiveAsync()
            => await _archiveDocumentController.CloseCurrentArchiveAsync();

        #endregion

        #region Folder Explorer
        
        private void LoadExplorerFolder(string path)
        {
            // WebDAV 모드에서 로컬 폴더로 이동 시 모드 해제
            if (_isWebDavMode)
            {
                DisconnectWebDav();
                _currentWebDavItemPath = null; 
            }

            _explorerController.LoadFolder(
                path,
                currentPath => CurrentPathText.Text = currentPath,
                ex => CurrentPathText.Text = $"오류: {ex.Message}",
                () =>
                {
                    ApplyThumbnailSizeToFileItems();
                    SyncCurrentExplorerSelection();
                });
        }

        private void SyncCurrentExplorerSelection()
        {
            if (_currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                SyncSidebarSelection(_imageEntries[_currentIndex]);
            }
        }

        private void ToggleExplorerViewButton_Click(object sender, RoutedEventArgs e)
        {
            _explorerController.ToggleViewMode();
            UpdateExplorerView();
        }

        private void UpdateExplorerView()
        {
            if (FileListView == null || FileGridView == null) return;

            if (_isExplorerGrid)
            {
                FileListView.Visibility = Visibility.Collapsed;
                FileGridView.Visibility = Visibility.Visible;
                
                if (ToggleViewButton?.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE8B9"; // List view icon (to switch back)
                }
                if (ToggleViewButton != null)
                {
                    UpdateToggleViewButtonTooltip();
                }
            }
            else
            {
                FileListView.Visibility = Visibility.Visible;
                FileGridView.Visibility = Visibility.Collapsed;

                if (ToggleViewButton?.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE80A"; // Grid view icon
                }
                if (ToggleViewButton != null)
                {
                    UpdateToggleViewButtonTooltip();
                }
            }
        }

        private void ApplyThumbnailSettingsToControls()
        {
            _explorerThumbnailSize = Math.Clamp(_explorerThumbnailSize, 64, 180);
            ApplyExplorerThumbnailOptions();

            if (ThumbnailSizeSlider != null && Math.Abs(ThumbnailSizeSlider.Value - _explorerThumbnailSize) > 0.1)
            {
                ThumbnailSizeSlider.Value = _explorerThumbnailSize;
            }
            if (ThumbnailSizeValueText != null)
            {
                ThumbnailSizeValueText.Text = $"{_explorerThumbnailSize:F0}px";
            }
            if (FolderThumbnailsCheckBox != null)
            {
                FolderThumbnailsCheckBox.IsChecked = _showFolderThumbnails;
            }

            ApplyThumbnailSizeToFileItems();
        }

        private void ApplyExplorerThumbnailOptions()
        {
            if (_explorerController == null) return;

            _explorerController.ThumbnailDecodePixelWidth = Math.Max(200, (int)Math.Ceiling(_explorerThumbnailSize * 2));
            _explorerController.ShowFolderThumbnails = _showFolderThumbnails;
        }

        private void ApplyThumbnailSizeToFileItems()
        {
            foreach (var item in _fileItems)
            {
                item.ApplyThumbnailSize(_explorerThumbnailSize);
            }
        }

        private void InitializeExplorerContextMenus()
        {
            FileListView.ContextFlyout = CreateExplorerContextFlyout();
            FileGridView.ContextFlyout = CreateExplorerContextFlyout();
        }

        private MenuFlyout CreateExplorerContextFlyout()
        {
            var flyout = new MenuFlyout();

            var openExternalItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenExternal, Icon = new FontIcon { Glyph = "\uE8E5" } };
            var openDefaultItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenDefault, Icon = new FontIcon { Glyph = "\uE8E5" } };
            var openExplorerItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenInWindowsExplorer, Icon = new FontIcon { Glyph = "\uED25" } };
            var refreshItem = new MenuFlyoutItem { Text = Strings.ExplorerRefresh, Icon = new FontIcon { Glyph = "\uE72C" } };
            var renameItem = new MenuFlyoutItem { Text = Strings.ExplorerRename, Icon = new FontIcon { Glyph = "\uE8AC" } };
            var deleteItem = new MenuFlyoutItem { Text = Strings.ExplorerDelete, Icon = new FontIcon { Glyph = "\uE74D" } };

            openExternalItem.Click += async (_, _) => await _explorerItemOperationController.OpenWithExternalProgramAsync(GetExplorerContextItem());
            openDefaultItem.Click += (_, _) => _explorerItemOperationController.OpenWithDefaultProgram(GetExplorerContextItem());
            openExplorerItem.Click += (_, _) => _explorerItemOperationController.OpenInWindowsExplorer(GetExplorerContextItem());
            refreshItem.Click += (_, _) => RefreshExplorer();
            renameItem.Click += async (_, _) => await _explorerItemOperationController.RenameAsync(GetExplorerContextItem());
            deleteItem.Click += async (_, _) => await _explorerItemOperationController.DeleteAsync(GetExplorerContextItem());

            flyout.Opening += (_, _) =>
            {
                var item = GetExplorerContextItem();
                var hasLocalItem = item != null && !item.IsWebDav;
                var canModify = hasLocalItem && !item!.IsParentDirectory;
                var canOpen = hasLocalItem && !item!.IsParentDirectory;

                openExternalItem.IsEnabled = canOpen;
                openDefaultItem.IsEnabled = canOpen;
                openExplorerItem.IsEnabled = hasLocalItem;
                renameItem.IsEnabled = canModify;
                deleteItem.IsEnabled = canModify;
            };

            flyout.Items.Add(openExternalItem);
            flyout.Items.Add(openDefaultItem);
            flyout.Items.Add(openExplorerItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(refreshItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(renameItem);
            flyout.Items.Add(deleteItem);

            return flyout;
        }

        private void ExplorerView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            _explorerContextItem = FindExplorerItemFromSource(e.OriginalSource as DependencyObject);
            _windowChromeController?.RefreshPointerCursor();
        }

        private FileItem? GetExplorerContextItem()
        {
            if (_explorerContextItem != null)
            {
                return _explorerContextItem;
            }

            return FileGridView.Visibility == Visibility.Visible
                ? FileGridView.SelectedItem as FileItem
                : FileListView.SelectedItem as FileItem;
        }

        private static FileItem? FindExplorerItemFromSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is FileItem item)
                {
                    return item;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private async Task ReleaseCurrentDocumentForExplorerOperationAsync(string targetPath, bool targetIsDirectory)
        {
            var shouldClose = IsExplorerOperationTargetOpen(targetPath, targetIsDirectory);

            if (!shouldClose) return;

            _sevenZipExtraction.CancelExtraction();
            _imageLoadingCts?.Cancel();
            _preloadManager.CancelAll();
            _globalTextCts?.Cancel();

            await CloseCurrentPdfAsync();
            await CloseCurrentEpubAsync();
            await CloseCurrentArchiveAsync();
            CloseCurrentText();

            ClearViewerAfterExplorerDeletion();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private bool IsExplorerOperationTargetOpen(string targetPath, bool targetIsDirectory)
            => _documentOpenStateQuery.IsExplorerOperationTargetOpen(targetPath, targetIsDirectory);

        private void ClearViewerAfterExplorerDeletion()
        {
            _animatedWebpService.Stop();
            _fastNavigationService?.StopTimers();
            _imageCache?.ClearAll();
            _imageViewerState.ClearBitmaps();
            _imageEntries = new List<ImageEntry>();
            _currentIndex = -1;
            _isCurrentViewSideBySide = false;

            SwitchToImageMode();
            EmptyStatePanel.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Visible;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            FastNavOverlay.Visibility = Visibility.Collapsed;
            FileNameText.Text = Strings.FileSelectPlaceholder;
            ImageInfoText.Text = string.Empty;
            ImageIndexText.Text = string.Empty;
            TextProgressText.Text = string.Empty;

            MainCanvas?.Invalidate();
            LeftCanvas?.Invalidate();
            RightCanvas?.Invalidate();
        }

        private async Task OpenLocalFilePathAsync(string path)
        {
            await _localDocumentOpenCoordinator.OpenExistingFilePathAsync(path, saveCurrentPositionBeforeOpen: false);
        }

        private void UpdateToggleViewButtonTooltip()
        {
            if (ToggleViewButton == null) return;

            string baseTooltip = _isExplorerGrid ? Strings.ListViewTooltip : Strings.ToggleViewTooltip;
            ToolTipService.SetToolTip(ToggleViewButton, $"{baseTooltip}\n{Strings.RightClickSettingsHint}");
        }

        private void ThumbnailSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _explorerThumbnailSize = Math.Clamp(e.NewValue, 64, 180);
            ApplyExplorerThumbnailOptions();
            ApplyThumbnailSizeToFileItems();

            if (ThumbnailSizeValueText != null)
            {
                ThumbnailSizeValueText.Text = $"{_explorerThumbnailSize:F0}px";
            }

            _windowSettingsCoordinator?.SaveWindowSettings();
        }

        private void FolderThumbnailsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _showFolderThumbnails = FolderThumbnailsCheckBox?.IsChecked == true;
            ApplyExplorerThumbnailOptions();

            if (_explorerController != null)
            {
                _explorerController.RefreshThumbnails(clearExisting: false);
            }

            _windowSettingsCoordinator?.SaveWindowSettings();
        }

        private async void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (FileListView.SelectedItem is FileItem item)
                {
                    if (IsCurrentFile(item.FullPath)) return;
                    
                    await HandleFileSelectionAsync(item);
                    // Do not clear selection so user can see what's selected
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileListView_SelectionChanged: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void FileGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (FileGridView.SelectedItem is FileItem item)
                {
                    if (IsCurrentFile(item.FullPath)) return;

                    await HandleFileSelectionAsync(item);
                    // Do not clear selection so user can see what's selected
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileGridView_SelectionChanged: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private bool IsCurrentFile(string path)
            => _documentOpenStateQuery.IsCurrentFile(path);

        private async void FileGridView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            try
            {
                // If we are viewing an image (archive or file) and the sidebar is focused
                if (_imageEntries.Count > 0)
                {
                    // When viewing an archive (or images in a folder), 
                    // Left/Right should navigate IMAGES (override GridView default)
                    // Up/Down should navigate FILES (override GridView default)
                    
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Enter:
                            if (FileGridView.SelectedItem is FileItem item)
                            {
                                if (item.IsDirectory)
                                {
                                    LoadExplorerFolder(item.FullPath);
                                }
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
                            MoveExplorerSelection(-1);
                            e.Handled = true;
                            break;
                        case Windows.System.VirtualKey.Down:
                            MoveExplorerSelection(1);
                            e.Handled = true;
                            break;
                        case Windows.System.VirtualKey.Space:
                            // Toggle Side by Side
                            _imageViewerController.ToggleSideBySide();
                            e.Handled = true;
                            break;
                        case Windows.System.VirtualKey.Home:
                            _currentIndex = 0;
                            await DisplayCurrentImageAsync();
                            e.Handled = true;
                            break;
                        case Windows.System.VirtualKey.End:
                            _currentIndex = _imageEntries.Count - 1;
                            await DisplayCurrentImageAsync();
                            e.Handled = true;
                            break;
                    }
                }
                else
                {
                     // Handle Enter key for directories even if no image is loaded
                     if (e.Key == Windows.System.VirtualKey.Enter)
                     {
                         if (FileGridView.SelectedItem is FileItem item && item.IsDirectory)
                         {
                             LoadExplorerFolder(item.FullPath);
                             e.Handled = true;
                         }
                     }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileGridView_PreviewKeyDown: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void FileListView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            try
            {
                if (_imageEntries.Count > 0)
                {
                    if (e.Key == Windows.System.VirtualKey.Home)
                    {
                        _currentIndex = 0;
                        await DisplayCurrentImageAsync();
                        e.Handled = true;
                        return;
                    }
                    else if (e.Key == Windows.System.VirtualKey.End)
                    {
                        _currentIndex = _imageEntries.Count - 1;
                        await DisplayCurrentImageAsync();
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    if (FileListView.SelectedItem is FileItem item && item.IsDirectory)
                    {
                        LoadExplorerFolder(item.FullPath);
                        e.Handled = true;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileListView_PreviewKeyDown: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void MoveExplorerSelection(int direction)
        {
            if (FileGridView.Visibility == Visibility.Visible)
            {
                int newIndex = FileGridView.SelectedIndex + direction;
                if (newIndex >= 0 && newIndex < _fileItems.Count)
                {
                    FileGridView.SelectedIndex = newIndex;
                    FileGridView.ScrollIntoView(FileGridView.SelectedItem);
                }
            }
            else
            {
                int newIndex = FileListView.SelectedIndex + direction;
                if (newIndex >= 0 && newIndex < _fileItems.Count)
                {
                    FileListView.SelectedIndex = newIndex;
                    FileListView.ScrollIntoView(FileListView.SelectedItem);
                }
            }
        }

        private async Task HandleFileSelectionAsync(FileItem item)
        {
            if (_isNavigatingRecent) return;

            // WebDAV 항목 처리
            if (item.IsWebDav)
            {
                await HandleWebDavFileSelectionAsync(item);
                return;
            }

            if (item.IsDirectory)
            {
                if (!Directory.Exists(item.FullPath))
                {
                    ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    RefreshExplorer();
                    return;
                }
                // Do not auto-navigate to folder on selection (Arrow keys/Single click)
                // LoadExplorerFolder(item.FullPath);
            }
            else if (item.IsArchive || item.IsPdf || item.IsImage || item.IsText || item.IsEpub)
            {
                if (!File.Exists(item.FullPath))
                {
                    ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    RefreshExplorer();
                    return;
                }
                await _localDocumentOpenCoordinator.OpenExistingFilePathAsync(
                    item.FullPath,
                    saveCurrentPositionBeforeOpen: true);
            }
        }

        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await BrowseAndLoadFolderAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in BrowseFolderButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var nextMode = _explorerSortMode switch
            {
                ExplorerSortMode.Name => ExplorerSortMode.DateDesc,
                ExplorerSortMode.DateDesc => ExplorerSortMode.DateAsc,
                _ => ExplorerSortMode.Name
            };

            ApplyExplorerSortMode(nextMode);
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.Name);
        }

        private void SortByDateDesc_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.DateDesc);
        }

        private void SortByDateAsc_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.DateAsc);
        }

        private void ApplyExplorerSortMode(ExplorerSortMode sortMode)
        {
            _explorerController.SetSortMode(sortMode);
            UpdateSortIcon();
            RefreshExplorer();
        }

        private void RefreshExplorer()
        {
            if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavPath))
            {
                _ = LoadWebDavFolderAsync(_currentWebDavPath);
            }
            else if (!string.IsNullOrEmpty(_currentExplorerPath))
            {
                LoadExplorerFolder(_currentExplorerPath);
            }
        }

        private void UpdateSortIcon()
        {
            if (SortIcon == null) return;

            switch (_explorerSortMode)
            {
                case ExplorerSortMode.DateDesc:
                    SortIcon.Glyph = "\uE1FD"; // Down arrow
                    ToolTipService.SetToolTip(SortByDateButton, Strings.SortByDateDescTooltip);
                    break;
                case ExplorerSortMode.DateAsc:
                    SortIcon.Glyph = "\uE110"; // Up arrow
                    ToolTipService.SetToolTip(SortByDateButton, Strings.SortByDateAscTooltip);
                    break;
                default:
                    SortIcon.Glyph = "\uE174"; // Default Sort (Name)
                    ToolTipService.SetToolTip(SortByDateButton, Strings.SortByNameTooltip);
                    break;
            }
        }

        private async void ParentFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await NavigateToParentFolderAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ParentFolderButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void AddToFavoritesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await AddToFavoritesAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddToFavoritesMenuItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void SidebarFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private void SidebarRecentButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private async void FileItem_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                if (e.ClickedItem is FileItem item && item.IsDirectory)
                {
                    if (item.IsWebDav && !string.IsNullOrEmpty(item.WebDavPath))
                    {
                        await LoadWebDavFolderAsync(item.WebDavPath);
                    }
                    else
                    {
                        LoadExplorerFolder(item.FullPath);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileItem_ItemClick: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task NavigateToParentFolderAsync()
        {
            // WebDAV 모드에서 상위 폴더 이동
            if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavPath) && _currentWebDavPath != "/")
            {
                var parentPath = _currentWebDavPath.TrimEnd('/');
                var lastSlash = parentPath.LastIndexOf('/');
                var parent = lastSlash > 0 ? parentPath.Substring(0, lastSlash + 1) : "/";
                await LoadWebDavFolderAsync(parent);
                return;
            }

            if (!string.IsNullOrEmpty(_currentExplorerPath))
            {
                var parentDir = Directory.GetParent(_currentExplorerPath);
                if (parentDir != null)
                {
                    LoadExplorerFolder(parentDir.FullName);
                }
            }
        }

        private async Task BrowseAndLoadFolderAsync()
        {
            var picker = new FolderPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();

            if (folder != null)
            {
                LoadExplorerFolder(folder.Path);
            }
        }

        private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSidebar();
        }

        private void ToggleSidebar()
        {
            if (_windowState.IsSidebarVisible && !_windowState.IsFullscreen)
            {
                _windowState.SidebarWidth = (int)SidebarColumn.Width.Value > 200 ? (int)SidebarColumn.Width.Value : 320;
            }
            if ((int)SidebarColumn.Width.Value > 200)
            {
                _windowState.IsSidebarVisible = true;
            }
            _windowState.IsSidebarVisible = !_windowState.IsSidebarVisible;
            SidebarGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            if (SplitterGrid != null)
            {
                SplitterGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            SidebarColumn.Width = _windowState.IsSidebarVisible ? new GridLength(_windowState.SidebarWidth) : new GridLength(0);
            _windowSettingsCoordinator.SaveWindowSettings();
        }

        #endregion


        #region Sidebar Resizing

        private void Splitter_ResizeCompleted(object? sender, EventArgs e)
        {
            if (SidebarColumn.Width.IsAbsolute && SidebarColumn.Width.Value > 200)
            {
                _windowState.SidebarWidth = (int)SidebarColumn.Width.Value;
            }
        }

        #endregion

    }
}
