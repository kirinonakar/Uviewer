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
        private readonly Services.ArchiveSession _archiveSession = new();

        #region Drag and Drop

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

            if (e.DragUIOverride != null)
            {
                e.DragUIOverride.Caption = "이미지 열기";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();

                    if (items.Count > 0)
                    {
                        var item = items[0];

                        if (item is StorageFile file)
                        {
                            var ext = Path.GetExtension(file.Name).ToLowerInvariant();

                            if (FileExplorerService.SupportedArchiveExtensions.Contains(ext))
                            {
                                await LoadImagesFromArchiveAsync(file.Path);
                            }
                            else if (FileExplorerService.SupportedEpubExtensions.Contains(ext))
                            {
                                await LoadImageFromFileAsync(file);
                            }
                            else if (FileExplorerService.SupportedPdfExtensions.Contains(ext))
                            {
                                await LoadImagesFromPdfAsync(file.Path);
                            }
                            else if (FileExplorerService.SupportedImageExtensions.Contains(ext) || FileExplorerService.SupportedTextExtensions.Contains(ext))
                            {
                                await LoadImageFromFileAsync(file);
                            }

                            // Update explorer
                            var folder = Path.GetDirectoryName(file.Path);
                            if (folder != null)
                            {
                                LoadExplorerFolder(folder);
                            }
                        }
                        else if (item is StorageFolder folder)
                        {
                            LoadExplorerFolder(folder.Path);
                            await LoadImagesFromFolderAsync(folder);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Grid_Drop: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        #endregion

        #region File Operations

        private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            try { await OpenFileAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try { await OpenFolderAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

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
                if (_isVerticalMode)
                {
                    // In vertical mode, the left button (Prev) should go to the Next page for EPUB
                    if (_isEpubMode) NavigateVerticalPage(1);
                    else NavigateVerticalPage(-1); // Default behavior for other modes
                }
                else if (_isEpubMode)
                {
                    await NavigateEpubAsync(-1);
                }
                else if (_isTextMode)
                {
                    if (_isAozoraMode)
                    {
                        NavigateAozoraPage(-1);
                    }
                    else
                    {
                        NavigateTextPage(-1);
                    }
                }
                else
                {
                    await NavigateToPreviousAsync();
                }
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
                if (_isVerticalMode)
                {
                    // In vertical mode, the right button (Next) should go to the Previous page for EPUB
                    if (_isEpubMode) NavigateVerticalPage(-1);
                    else NavigateVerticalPage(1); // Default behavior for other modes
                }
                else if (_isEpubMode)
                {
                    await NavigateEpubAsync(1);
                }
                else if (_isTextMode)
                {
                    if (_isAozoraMode)
                    {
                        NavigateAozoraPage(1);
                    }
                    else
                    {
                        NavigateTextPage(1);
                    }
                }
                else
                {
                    await NavigateToNextAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NextPageButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task OpenFileAsync()
        {
            var picker = new FileOpenPicker();

            // Initialize picker with window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            // Add image extensions
            foreach (var ext in FileExplorerService.SupportedImageExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            // Add archive extensions
            foreach (var ext in FileExplorerService.SupportedArchiveExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            foreach (var ext in FileExplorerService.SupportedEpubExtensions)
            {
                 picker.FileTypeFilter.Add(ext);
            }

            // Add text extensions
            foreach (var ext in FileExplorerService.SupportedFileExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                var extension = Path.GetExtension(file.Path).ToLowerInvariant();
                if (FileExplorerService.SupportedArchiveExtensions.Contains(extension))
                {
                    await LoadImagesFromArchiveAsync(file.Path);
                }
                else if (FileExplorerService.SupportedPdfExtensions.Contains(extension))
                {
                    await LoadImagesFromPdfAsync(file.Path);
                }
                else if (FileExplorerService.SupportedEpubExtensions.Contains(extension))
                {
                    await LoadImageFromFileAsync(file);
                }
                else
                {
                    await AddToRecentAsync(true);
                    await LoadImageFromFileAsync(file);
                }

                // Update explorer to show the file's folder
                var folder = Path.GetDirectoryName(file.Path);
                if (folder != null && folder != _currentExplorerPath)
                {
                    LoadExplorerFolder(folder);
                }
            }
        }

        private async Task OpenFolderAsync()
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
                await LoadImagesFromFolderAsync(folder);
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

        private async Task LoadImagesFromArchiveAsync(string archivePath)
        {
            // 이전 작업 즉시 중단
            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();
            _imageLoadingCts?.Cancel();
            _globalTextCts?.Cancel();

            // Close other formats first
            if (!await CloseCurrentPdfAsync()) return;
            if (!await CloseCurrentEpubAsync()) return;
            if (!await CloseCurrentArchiveAsync()) return;

            try
            {
                _imageEntries = (await _archiveSession.OpenLocalAsync(archivePath)).ToList();

                if (_imageEntries.Count > 0)
                {
                    _currentIndex = 0;

                    await DisplayCurrentImageAsync();
 
                    // 7z 파일인 경우 백그라운드 압축 해제 시작
                    if (_archiveSession.IsSevenZipArchive)
                    {
                        var extractToken = _sevenZipExtraction.StartNewExtraction();
                        _ = _archiveSession.ExtractSevenZipEntriesInBackgroundAsync(
                            archivePath,
                            _imageEntries,
                            () => _currentIndex,
                            _sevenZipExtraction,
                            extractToken);
                    }

                    // Start preloading images after displaying the first one
                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: true,
                        requireSharpening: _sharpenEnabled);

                    // Update title to show archive name
                    Title = "Uviewer - Image & Text Viewer";
                }
                else
                {
                    FileNameText.Text = "이 압축 파일에 이미지가 없습니다";
                }
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"압축 파일 열기 실패: {ex.Message}";
            }
        }

        private void CloseCurrentArchive()
        {
            if (!_archiveSession.HasArchive) return;

            // [Immediate Stop] 락을 기다리기 전에 추출 작업 즉시 취소
            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();

            if (_archiveSession.Close(TimeSpan.FromSeconds(2)))
            {
                AfterArchiveClosed();
            }
        }

        private async Task<bool> CloseCurrentArchiveAsync()
        {
            if (!_archiveSession.HasArchive) return true;

            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();

            if (!await _archiveSession.CloseAsync(TimeSpan.FromSeconds(10)))
            {
                return false;
            }

            AfterArchiveClosed();
            return true;
        }

        private void AfterArchiveClosed()
        {
            // 메인 UI 스레드에서 타이틀 변경 (안전하게 처리)
            DispatcherQueue.TryEnqueue(() =>
            {
                Title = "Uviewer - Image & Text Viewer";
            });

            _imageCache?.ClearAll();

            // Clean up fast navigation
            _fastNavigationService?.StopTimers();

            _imageViewerState.ClearBitmaps();

            _sevenZipExtraction.CleanupTempData();
        }

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

            openExternalItem.Click += async (_, _) => await OpenExplorerItemWithExternalProgramAsync(GetExplorerContextItem());
            openDefaultItem.Click += (_, _) => OpenExplorerItemWithDefaultProgram(GetExplorerContextItem());
            openExplorerItem.Click += (_, _) => OpenExplorerItemInWindowsExplorer(GetExplorerContextItem());
            refreshItem.Click += (_, _) => RefreshExplorer();
            renameItem.Click += async (_, _) => await RenameExplorerItemAsync(GetExplorerContextItem());
            deleteItem.Click += async (_, _) => await DeleteExplorerItemAsync(GetExplorerContextItem());

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

        private async Task SelectExternalProgramAsync()
        {
            const double dialogContentWidth = 420;

            var input = new TextBox
            {
                Text = _externalProgramPath,
                PlaceholderText = Strings.ExternalProgramPathPlaceholder,
                Width = dialogContentWidth,
                MaxWidth = dialogContentWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var browseButton = new Button
            {
                Content = Strings.ExternalProgramBrowseButton,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            browseButton.Click += async (_, _) =>
            {
                var file = await PickExternalProgramFileAsync();
                if (file == null) return;

                input.Text = file.Path;
                input.Focus(FocusState.Programmatic);
                input.Select(input.Text.Length, 0);
            };

            var validationText = new TextBlock
            {
                Text = Strings.ExternalProgramPathRequired,
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };
            input.TextChanged += (_, _) => validationText.Visibility = Visibility.Collapsed;

            var panel = new StackPanel
            {
                Width = dialogContentWidth,
                MaxWidth = dialogContentWidth,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.ExternalProgramPathDescription,
                        TextWrapping = TextWrapping.Wrap
                    },
                    input,
                    browseButton,
                    validationText
                }
            };

            var dialog = new ContentDialog
            {
                Title = Strings.ExternalProgramSettings,
                Content = panel,
                PrimaryButtonText = Strings.ExternalProgramSaveButton,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(NormalizeExternalProgramPath(input.Text))) return;

                validationText.Visibility = Visibility.Visible;
                input.Focus(FocusState.Programmatic);
                args.Cancel = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _externalProgramPath = NormalizeExternalProgramPath(input.Text);
            MainToolbar.SetExternalProgramPath(_externalProgramPath);
            _windowSettingsCoordinator?.SaveWindowSettings();
            ShowNotification(Strings.ExternalProgramConfiguredNotification(GetExternalProgramDisplayName(_externalProgramPath)));
        }

        private async Task<StorageFile?> PickExternalProgramFileAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".cmd");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".com");

            return await picker.PickSingleFileAsync();
        }

        private async Task OpenExplorerItemWithExternalProgramAsync(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var externalProgramPath = NormalizeExternalProgramPath(_externalProgramPath);
            if (string.IsNullOrWhiteSpace(externalProgramPath))
            {
                ShowNotification(Strings.ExternalProgramPathRequired, "\uE783", "Red");
                await SelectExternalProgramAsync();
                return;
            }

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                RefreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = externalProgramPath,
                    Arguments = QuoteArgument(item.FullPath),
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowNotification(Strings.ExternalProgramLaunchFailed(ex.Message), "\uE783", "Red");
            }
        }

        private void OpenExplorerItemWithDefaultProgram(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                RefreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowNotification(Strings.DefaultProgramLaunchFailed(ex.Message), "\uE783", "Red");
            }
        }

        private static string NormalizeExternalProgramPath(string? path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                return value[1..^1].Trim();
            }

            return value;
        }

        private static string GetExternalProgramDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }

        private void OpenExplorerItemInWindowsExplorer(FileItem? item)
        {
            if (item == null || item.IsWebDav) return;

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                RefreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = item.IsDirectory ? QuoteArgument(item.FullPath) : $"/select,{QuoteArgument(item.FullPath)}",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowNotification(Strings.ExplorerOpenFailed(ex.Message), "\uE783", "Red");
            }
        }

        private async Task RenameExplorerItemAsync(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var originalPath = item.FullPath;
            if (!File.Exists(originalPath) && !Directory.Exists(originalPath))
            {
                ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                RefreshExplorer();
                return;
            }

            var input = new TextBox
            {
                Text = item.Name,
                SelectionStart = 0,
                SelectionLength = Path.GetFileNameWithoutExtension(item.Name).Length,
                MinWidth = 320
            };

            var dialog = new ContentDialog
            {
                Title = Strings.ExplorerRename,
                Content = input,
                PrimaryButtonText = Strings.RenamePrimary,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var newName = input.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == item.Name) return;

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                ShowNotification(Strings.InvalidFileName, "\uE783", "Red");
                return;
            }

            var parent = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(parent)) return;

            var newPath = Path.Combine(parent, newName);
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                ShowNotification(Strings.FileNameAlreadyExists, "\uE783", "Red");
                return;
            }

            var shouldReopen = IsExplorerOperationTargetOpen(originalPath, item.IsDirectory);
            await ReleaseCurrentDocumentForExplorerOperationAsync(originalPath, item.IsDirectory);

            try
            {
                if (item.IsDirectory)
                {
                    Directory.Move(originalPath, newPath);
                }
                else
                {
                    File.Move(originalPath, newPath);
                }

                RefreshExplorer();

                if (shouldReopen && !item.IsDirectory)
                {
                    await OpenLocalFilePathAsync(newPath);
                }

                ShowNotification(Strings.RenameSucceeded);
            }
            catch (Exception ex)
            {
                ShowNotification(Strings.RenameFailed(ex.Message), "\uE783", "Red");
            }
        }

        private async Task DeleteExplorerItemAsync(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var path = item.FullPath;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                RefreshExplorer();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Strings.ExplorerDelete,
                Content = Strings.DeleteConfirmation(item.Name),
                PrimaryButtonText = Strings.DeletePrimary,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var shouldClearViewer = IsExplorerOperationTargetOpen(path, item.IsDirectory);
            await ReleaseCurrentDocumentForExplorerOperationAsync(path, item.IsDirectory);

            try
            {
                if (item.IsDirectory)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                RefreshExplorer();
                if (shouldClearViewer)
                {
                    ClearViewerAfterExplorerDeletion();
                }
                ShowNotification(Strings.MovedToRecycleBin);
            }
            catch (Exception ex)
            {
                ShowNotification(Strings.DeleteFailed(ex.Message), "\uE783", "Red");
            }
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
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return false;

            var currentPath = GetCurrentNavigatingPath();
            if (PathsEqual(currentPath, targetPath)) return true;
            if (IsCurrentFile(targetPath)) return true;

            if (!string.IsNullOrEmpty(_currentPdfPath) && PathsEqual(_currentPdfPath, targetPath)) return true;
            if (!string.IsNullOrEmpty(_archiveSession.CurrentPath) && PathsEqual(_archiveSession.CurrentPath, targetPath)) return true;
            if (!string.IsNullOrEmpty(_currentEpubFilePath) && PathsEqual(_currentEpubFilePath, targetPath)) return true;
            if (!string.IsNullOrEmpty(_currentTextFilePath) && PathsEqual(_currentTextFilePath, targetPath)) return true;

            if (_currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (PathsEqual(entry.FilePath, targetPath) || PathsEqual(entry.WebDavPath, targetPath)) return true;
            }

            if (targetIsDirectory && !string.IsNullOrEmpty(currentPath) && IsSameOrChildPath(currentPath, targetPath))
            {
                return true;
            }

            return false;
        }

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
            var extension = Path.GetExtension(path).ToLowerInvariant();

            if (FileExplorerService.SupportedArchiveExtensions.Contains(extension))
            {
                await LoadImagesFromArchiveAsync(path);
            }
            else if (FileExplorerService.SupportedPdfExtensions.Contains(extension))
            {
                await LoadImagesFromPdfAsync(path);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await LoadImageFromFileAsync(file);
            }
        }

        private static bool IsSameOrChildPath(string? candidatePath, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            try
            {
                return Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return first.Equals(second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

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
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Direct match for major formats
            if (!string.IsNullOrEmpty(_currentPdfPath) && _currentPdfPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(_archiveSession.CurrentPath) && _archiveSession.CurrentPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(_currentEpubFilePath) && _currentEpubFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;

            // WebDAV 모드인 경우 원격 경로 비교 추가
            if (_isWebDavMode && _currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (entry.IsWebDavEntry && entry.WebDavPath == path) return true;
                if (entry.IsArchiveEntry && _archiveSession.CurrentPath != null &&
                    (_archiveSession.CurrentPath == path || _archiveSession.CurrentPath == $"WebDAV:{path}")) return true;
            }

            if (_currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (entry.IsArchiveEntry)
                {
                    return _archiveSession.CurrentPath != null && _archiveSession.CurrentPath.Equals(path, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    return entry.FilePath != null && entry.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

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
                            SideBySideButton_Click(sender, new RoutedEventArgs());
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
            else if (item.IsArchive)
            {
                if (!File.Exists(item.FullPath))
                {
                    ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    RefreshExplorer();
                    return;
                }
                await AddToRecentAsync(true);
                await LoadImagesFromArchiveAsync(item.FullPath);
            }
            else if (item.IsPdf)
            {
                if (!File.Exists(item.FullPath))
                {
                    ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    RefreshExplorer();
                    return;
                }
                await AddToRecentAsync(true);
                await LoadImagesFromPdfAsync(item.FullPath);
            }
            else if (item.IsImage || item.IsText || item.IsEpub)
            {
                if (!File.Exists(item.FullPath))
                {
                    ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    RefreshExplorer();
                    return;
                }
                await AddToRecentAsync(true);
                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                await LoadImageFromFileAsync(file);
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
