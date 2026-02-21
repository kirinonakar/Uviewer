using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Media.Imaging;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // Current archive (if viewing from archive)
        private IArchive? _currentArchive;
        private string? _currentArchivePath;
        // 아카이브 동시 접근 방지를 위한 Semaphore 추가
        private readonly SemaphoreSlim _archiveLock = new(1, 1);

        #region File Operations

        private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenFileAsync();
        }

        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenFolderAsync();
        }

        private async void PrevFileButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateToFileAsync(false);
        }

        private async void NextFileButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateToFileAsync(true);
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVerticalMode)
            {
                NavigateVerticalPage(-1);
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

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVerticalMode)
            {
                NavigateVerticalPage(1);
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

        private async Task OpenFileAsync()
        {
            var picker = new FileOpenPicker();

            // Initialize picker with window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            // Add image extensions
            foreach (var ext in SupportedImageExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            // Add archive extensions
            foreach (var ext in SupportedArchiveExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            foreach (var ext in SupportedEpubExtensions)
            {
                 picker.FileTypeFilter.Add(ext);
            }

            // Add text extensions
            foreach (var ext in SupportedTextExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();

                if (SupportedArchiveExtensions.Contains(ext))
                {
                    await AddToRecentAsync(true);
                    await LoadImagesFromArchiveAsync(file.Path);
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

        private async Task LoadImageFromFileAsync(StorageFile file)
        {
            CloseCurrentArchive();

            // Get all images in the same folder
            var folder = await file.GetParentAsync();
            if (folder != null)
            {
                var files = await folder.GetFilesAsync();
                _imageEntries = files
                    .Where(f => SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
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

        private async Task LoadImagesFromFolderAsync(StorageFolder folder)
        {
            CloseCurrentArchive();

            var files = await folder.GetFilesAsync();
            _imageEntries = files
                .Where(f => SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
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
            try
            {
                // Lock을 사용하여 아카이브를 닫고 새로 여는 과정 보호
                await _archiveLock.WaitAsync();
                try
                {
                    CloseCurrentArchiveInternal(); // Lock이 걸린 상태에서 내부 메서드 호출

                    _currentArchivePath = archivePath;
                    _currentArchive = ArchiveFactory.Open(archivePath);

                    _imageEntries = _currentArchive.Entries
                        .Where(e => !e.IsDirectory &&
                            SupportedImageExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant()))
                        .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(e => new ImageEntry
                        {
                            DisplayName = Path.GetFileName(e.Key ?? "Unknown"),
                            ArchiveEntryKey = e.Key
                        })
                        .ToList();
                }
                finally
                {
                    _archiveLock.Release();
                }

                if (_imageEntries.Count > 0)
                {
                    _currentIndex = 0;

                    await DisplayCurrentImageAsync();

                    // Start preloading images after displaying the first one
                    _preloadCts?.Cancel();
                    _preloadCts?.Dispose();
                    _preloadCts = new CancellationTokenSource();
                    var token = _preloadCts.Token;
                    _ = Task.Run(() => PreloadNextImagesAsync(token));

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
            // 외부에서 호출될 때 Lock 대기 (타임아웃 설정으로 데드락 방지)
            if (_archiveLock.Wait(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    CloseCurrentArchiveInternal();
                }
                finally
                {
                    _archiveLock.Release();
                }
            }
            else
            {
                // 타임아웃 발생 시 강제로 정리
                System.Diagnostics.Debug.WriteLine("Archive lock timeout - forcing cleanup");
                if (_currentArchive != null)
                {
                    try
                    {
                        _currentArchive.Dispose();
                    }
                    catch { }
                    _currentArchive = null;
                    _currentArchivePath = null;
                }
            }
        }

        // Lock 내부에서 호출되거나, 단독으로 사용되는 해제 로직
        private void CloseCurrentArchiveInternal()
        {
            if (_currentArchive != null)
            {
                _currentArchive.Dispose();
                _currentArchive = null;
                _currentArchivePath = null;
            }

            // 메인 UI 스레드에서 타이틀 변경 (안전하게 처리)
            DispatcherQueue.TryEnqueue(() =>
            {
                Title = "Uviewer - Image & Text Viewer";
            });

            // Clear preloaded images
            lock (_preloadedImages)
            {
                foreach (var bitmap in _preloadedImages.Values)
                {
                    bitmap?.Dispose();
                }
                _preloadedImages.Clear();
            }

            // Clear sharpened image cache
            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    bitmap?.Dispose();
                }
                _sharpenedImageCache.Clear();
            }

            // Clean up fast navigation
            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts?.Dispose();
            _fastNavigationResetCts = null;
        }

        #endregion

        #region Folder Explorer
        
        private CancellationTokenSource? _thumbnailLoadingCts;

        private void LoadExplorerFolder(string path)
        {
            // WebDAV 모드에서 로컬 폴더로 이동 시 모드 해제
            if (_isWebDavMode)
            {
                // DisconnectWebDav는 private이지만 같은 partial class이므로 호출 가능
                DisconnectWebDav();
                _currentWebDavItemPath = null; 
            }

            try
            {
                _currentExplorerPath = path;
                _fileItems.Clear();

                CurrentPathText.Text = path;

                // Add parent directory entry
                var parentDir = Directory.GetParent(path);
                if (parentDir != null)
                {
                    _fileItems.Add(new FileItem
                    {
                        Name = "..",
                        FullPath = parentDir.FullName,
                        IsDirectory = true,
                        IsParentDirectory = true
                    });
                }

                // Add directories
                var directories = Directory.GetDirectories(path)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var dir in directories)
                {
                    var name = Path.GetFileName(dir);
                    if (!name.StartsWith(".")) // Hide hidden folders
                    {
                        _fileItems.Add(new FileItem
                        {
                            Name = name,
                            FullPath = dir,
                            IsDirectory = true
                        });
                    }
                }

                // Add files (images and archives)
                var files = Directory.GetFiles(path)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    var isImage = SupportedImageExtensions.Contains(ext);
                    var isArchive = SupportedArchiveExtensions.Contains(ext);
                    var isText = SupportedTextExtensions.Contains(ext);
                    var isEpub = SupportedEpubExtensions.Contains(ext);

                    if (isImage || isArchive || isText || isEpub)
                    {
                        _fileItems.Add(new FileItem
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsDirectory = false,
                            IsImage = isImage,
                            IsArchive = isArchive,
                            IsText = isText,
                            IsEpub = isEpub
                        });
                    }
                }

                // Start loading thumbnails
                _thumbnailLoadingCts?.Cancel();
                _thumbnailLoadingCts = new CancellationTokenSource();
                _ = LoadThumbnailsAsync(_thumbnailLoadingCts.Token);
            }
            catch (Exception ex)
            {
                CurrentPathText.Text = $"오류: {ex.Message}";
            }
        }

        private async Task LoadThumbnailsAsync(CancellationToken token)
        {
            try
            {
                var items = _fileItems.ToList();

                // [변경 1] 동시 작업 수를 2개로 제한하여 스레드 풀 과부하 방지
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 2, 
                    CancellationToken = token
                };

                await Parallel.ForEachAsync(items, parallelOptions, async (item, ct) =>
                {
                    // 이미지가 아닌 항목은 빠르게 스킵
                    if (!item.IsImage && !item.IsArchive && !item.IsEpub) return;

                    // [변경 2] UI 스레드에 숨통을 틔워주기 위해 아주 잠깐 대기
                    // 파일이 수백 개일 때 UI 메시지 큐가 꽉 차는 것을 방지합니다.
                    await Task.Delay(10, ct);

                    try
                    {
                        if (item.IsArchive)
                        {
                            using var archive = ArchiveFactory.Open(item.FullPath);
                            var entry = archive.Entries
                                .Where(e => !e.IsDirectory &&
                                       SupportedImageExtensions.Contains(Path.GetExtension(e.Key)?.ToLowerInvariant() ?? ""))
                                .OrderBy(e => e.Key)
                                .FirstOrDefault();

                            if (entry != null)
                            {
                                using var entryStream = entry.OpenEntryStream();
                                var memStream = new MemoryStream(); // using 없음 (UI로 넘겨야 하므로)
                                await entryStream.CopyToAsync(memStream, ct);
                                memStream.Position = 0;

                                // [핵심 변경 3] Low 우선순위 사용 & SetSourceAsync 사용
                                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                                {
                                    if (ct.IsCancellationRequested) 
                                    {
                                        memStream.Dispose();
                                        return;
                                    }

                                    try 
                                    {
                                        var bitmap = new BitmapImage();
                                        bitmap.DecodePixelWidth = 200;
                                        
                                        // [가장 중요한 수정] SetSource(동기) -> SetSourceAsync(비동기)
                                        // UI 스레드가 디코딩을 기다리지 않고 즉시 반환되게 하여 멈춤 현상 해결
                                        await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                                        item.Thumbnail = bitmap;
                                    }
                                    catch 
                                    { 
                                        memStream.Dispose();
                                    }
                                });
                            }
                        }
                        else if (item.IsImage)
                        {
                            // 일반 이미지도 동일하게 비동기 처리
                            try
                            {
                                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                                // GetThumbnailAsync는 내부적으로 이미 비동기이므로 그대로 사용
                                var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 200);

                                if (thumbnail != null)
                                {
                                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                                    {
                                        if (ct.IsCancellationRequested) return;
                                        var bitmap = new BitmapImage();
                                        bitmap.DecodePixelWidth = 200;
                                        await bitmap.SetSourceAsync(thumbnail);
                                        item.Thumbnail = bitmap;
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                    catch 
                    {
                        // 개별 실패 무시
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 탐색 중단 시 자연스럽게 종료
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail load error: {ex.Message}");
            }
        }

        private void ToggleExplorerViewButton_Click(object sender, RoutedEventArgs e)
        {
            _isExplorerGrid = !_isExplorerGrid;
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
                    ToolTipService.SetToolTip(ToggleViewButton, Strings.ListViewTooltip);
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
                    ToolTipService.SetToolTip(ToggleViewButton, Strings.ToggleViewTooltip);
                }
            }
        }

        private async void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileListView.SelectedItem is FileItem item)
            {
                if (IsCurrentFile(item.FullPath)) return;
                
                await HandleFileSelectionAsync(item);
                // Do not clear selection so user can see what's selected
            }
        }

        private async void FileGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileGridView.SelectedItem is FileItem item)
            {
                if (IsCurrentFile(item.FullPath)) return;

                await HandleFileSelectionAsync(item);
                // Do not clear selection so user can see what's selected
            }
        }

        private bool IsCurrentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath))
            {
                return _currentEpubFilePath.Equals(path, StringComparison.OrdinalIgnoreCase);
            }

            if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath))
            {
                return _currentTextFilePath.Equals(path, StringComparison.OrdinalIgnoreCase);
            }

            if (_currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (entry.IsArchiveEntry)
                {
                    return _currentArchivePath != null && _currentArchivePath.Equals(path, StringComparison.OrdinalIgnoreCase);
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

        private async void FileListView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
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
            // WebDAV 항목 처리
            if (item.IsWebDav)
            {
                await HandleWebDavFileSelectionAsync(item);
                return;
            }

            if (item.IsDirectory)
            {
                // Do not auto-navigate to folder on selection (Arrow keys/Single click)
                // LoadExplorerFolder(item.FullPath);
            }
            else if (item.IsArchive)
            {
                await AddToRecentAsync(true);
                await LoadImagesFromArchiveAsync(item.FullPath);
            }
            else if (item.IsImage || item.IsText || item.IsEpub)
            {
                await AddToRecentAsync(true);
                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                await LoadImageFromFileAsync(file);
            }
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _ = BrowseAndLoadFolderAsync();
        }

        private void ParentFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _ = NavigateToParentFolderAsync();
        }

        private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the flyout
            var flyout = FavoritesButton.Flyout as MenuFlyout;
            flyout?.ShowAt(FavoritesButton);
        }

        private void AddToFavoritesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = AddToFavoritesAsync();
        }

        private void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the flyout
            var flyout = RecentButton.Flyout as MenuFlyout;
            flyout?.ShowAt(RecentButton);
        }

        private void SidebarFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private void SidebarRecentButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private void FileItem_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FileItem item && item.IsDirectory)
            {
                if (item.IsWebDav && !string.IsNullOrEmpty(item.WebDavPath))
                {
                    _ = LoadWebDavFolderAsync(item.WebDavPath);
                }
                else
                {
                    LoadExplorerFolder(item.FullPath);
                }
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
            if (_isSidebarVisible && !_isFullscreen)
            {
                _SidebarWidth = (int)SidebarColumn.Width.Value > 200 ? (int)SidebarColumn.Width.Value : 320;
            }
            if ((int)SidebarColumn.Width.Value > 200)
            {
                _isSidebarVisible = true;
            }
            _isSidebarVisible = !_isSidebarVisible;
            SidebarGrid.Visibility = _isSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            if (SplitterGrid != null)
            {
                SplitterGrid.Visibility = _isSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            SidebarColumn.Width = _isSidebarVisible ? new GridLength(_SidebarWidth) : new GridLength(0);
        }

        #endregion


        #region Sidebar Resizing

        private bool _isDraggingSplitter = false;
        private double _lastPointerX = 0;

        private void Splitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (SplitterGrid != null && !_isDraggingSplitter)
            {
                SplitterGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray);
            }
        }

        private void Splitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (SplitterGrid != null && !_isDraggingSplitter)
            {
                SplitterGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        private void Splitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = true;
            var pointer = e.GetCurrentPoint(SplitterGrid);
            _lastPointerX = pointer.Position.X;

            if (SplitterGrid != null)
            {
                SplitterGrid.CapturePointer(e.Pointer);
            }
        }

        private void Splitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isDraggingSplitter || SidebarColumn == null)
                return;

            var pointer = e.GetCurrentPoint(SplitterGrid);
            var deltaX = pointer.Position.X - _lastPointerX;

            // Get current width
            if (SidebarColumn.Width.IsAuto)
                return;

            var newWidth = SidebarColumn.Width.Value + deltaX;

            // Clamp between 150 and 800
            if (newWidth < 150)
                newWidth = 150;
            else if (newWidth > 800)
                newWidth = 800;

            SidebarColumn.Width = new GridLength(newWidth);
            _lastPointerX = pointer.Position.X;
        }

        private void Splitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = false;

            if (SplitterGrid != null)
            {
                SplitterGrid.ReleasePointerCapture(e.Pointer);
                SplitterGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        #endregion

    }
}