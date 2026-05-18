using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpCompress.Archives;
using SevenZipExtractor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        // Current archive (if viewing from archive)
        private IArchive? _currentArchive;
        private SevenZipExtractor.ArchiveFile? _current7zArchive;
        private string? _currentArchivePath;
        // 아카이브 동시 접근 방지를 위한 Semaphore 추가
        private readonly SemaphoreSlim _archiveLock = new(1, 1);

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

            try
            {
                // Lock을 사용하여 아카이브를 닫고 새로 여는 과정 보호
                await _archiveLock.WaitAsync();
                try
                {
                    CloseCurrentArchiveInternal(); // Lock이 걸린 상태에서 내부 메서드 호출

                    _currentArchivePath = archivePath;
                    string extension = Path.GetExtension(archivePath).ToLowerInvariant();

                    if (extension == ".7z")
                    {
                        string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                        _current7zArchive = new SevenZipExtractor.ArchiveFile(archivePath, libraryPath);

                        _imageEntries = _current7zArchive.Entries
                            .Where(e => !e.IsFolder &&
                                FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.FileName ?? "").ToLowerInvariant()))
                            .OrderBy(e => e.FileName, NaturalSortComparer.Default)
                            .Select(e => new ImageEntry
                            {
                                DisplayName = Path.GetFileName(e.FileName ?? "Unknown"),
                                ArchiveEntryKey = e.FileName
                            })
                            .ToList();
                    }
                    else
                    {
                        _currentArchive = ArchiveFactory.OpenArchive(archivePath);

                        _imageEntries = _currentArchive.Entries
                            .Where(e => !e.IsDirectory &&
                                FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant()))
                            .OrderBy(e => e.Key, NaturalSortComparer.Default)
                            .Select(e => new ImageEntry
                            {
                                DisplayName = Path.GetFileName(e.Key ?? "Unknown"),
                                ArchiveEntryKey = e.Key
                            })
                            .ToList();
                    }
                }
                finally
                {
                    _archiveLock.Release();
                }

                if (_imageEntries.Count > 0)
                {
                    _currentIndex = 0;

                    await DisplayCurrentImageAsync();
 
                    // 7z 파일인 경우 백그라운드 압축 해제 시작
                    string extension = Path.GetExtension(archivePath).ToLowerInvariant();
                    if (extension == ".7z")
                    {
                        var extractToken = _sevenZipExtraction.StartNewExtraction();
                        _ = Start7zBackgroundExtractionAsync(archivePath, extractToken);
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
            // [Fix] _currentArchive와 _current7zArchive 둘 다 체크
            if (_currentArchive == null && _current7zArchive == null) return;

            // [Immediate Stop] 락을 기다리기 전에 추출 작업 즉시 취소
            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();

            // 외부에서 호출될 때 Lock 대기 (타임아웃 설정으로 데드락 방지)
            if (_archiveLock.Wait(TimeSpan.FromSeconds(2)))
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
                // 락을 잡지 못한 상태에서 강제 Dispose하면 로딩/프리로드 중인 스레드와 충돌할 수 있습니다.
                // 비동기 호출 경로에서 안전하게 재시도하도록 남기고, 여기서는 추가 파괴 작업을 하지 않습니다.
                System.Diagnostics.Debug.WriteLine("Archive lock timeout - cleanup deferred to avoid disposing while in use");
            }
        }

        private async Task<bool> CloseCurrentArchiveAsync()
        {
            if (_currentArchive == null && _current7zArchive == null) return true;

            _sevenZipExtraction.CancelExtraction();
            _preloadManager.CancelAll();

            if (!await _archiveLock.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                System.Diagnostics.Debug.WriteLine("Archive lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseCurrentArchiveInternal();
                return true;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        // Lock 내부에서 호출되거나, 단독으로 사용되는 해제 로직
        private void CloseCurrentArchiveInternal()
        {
            if (_currentArchive != null)
            {
                _currentArchive.Dispose();
                _currentArchive = null;
            }

            if (_current7zArchive != null)
            {
                _current7zArchive.Dispose();
                _current7zArchive = null;
            }

            // WebDAV에서 다운로드한 임시 아카이브 파일인 경우 삭제
            if (_currentArchivePath != null && _currentArchivePath.Contains(Path.Combine("Uviewer", "WebDav")))
            {
                try
                {
                    if (File.Exists(_currentArchivePath))
                    {
                        File.Delete(_currentArchivePath);
                    }
                }
                catch { }
            }

            _currentArchivePath = null;

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

        private async Task Start7zBackgroundExtractionAsync(string archivePath, CancellationToken token)
{
    try
    {
        // 이전 데이터 정리 및 새 템프 폴더 생성
        string tempFolder = _sevenZipExtraction.CreateTempFolder();

        var total = _imageEntries.Count;
        var extracted = new bool[total];
        var lockObj = new object();

        // [멀티스레드 압축 해제]
        int threadCount = Math.Min(Environment.ProcessorCount, 6); // 최대 6개 스레드 사용
        var tasks = new List<Task>();

        for (int t = 0; t < threadCount; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                    using var archive = new SevenZipExtractor.ArchiveFile(archivePath, libraryPath);
                    var entries = archive.Entries
                        .Where(e => !e.IsFolder && FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.FileName ?? "").ToLowerInvariant()))
                        .ToList();
                    var entryMap = entries.ToDictionary(e => e.FileName!, e => e);

                    while (!token.IsCancellationRequested)
                    {
                        int targetIndex = -1;
                        lock (lockObj)
                        {
                            int current = _currentIndex;
                            int bestDist = int.MaxValue;
                            for (int i = 0; i < total; i++)
                            {
                                if (extracted[i]) continue;
                                int dist = Math.Abs(i - current);
                                if (dist < bestDist)
                                {
                                    bestDist = dist;
                                    targetIndex = i;
                                }
                            }

                            if (targetIndex != -1) extracted[targetIndex] = true;
                        }

                        if (targetIndex == -1) break;

                        var imageEntry = _imageEntries[targetIndex];
                        if (entryMap.TryGetValue(imageEntry.ArchiveEntryKey!, out var archiveEntry))
                        {
                            string? outputPath = null;
                            string? tempExtractPath = null;
                            
                            try
                            {
                                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _sevenZipExtraction.JumpToken);
                                var linkedToken = linkedCts.Token;

                                string ext = Path.GetExtension(imageEntry.ArchiveEntryKey ?? "") ?? "";
                                string fileId = Guid.NewGuid().ToString("N");
                                
                                outputPath = Path.Combine(tempFolder, fileId + ext);
                                tempExtractPath = Path.Combine(tempFolder, fileId + ".tmp"); // 안전한 임시 파일명
                                
                                // 1. 임시 파일로 먼저 압축 풀기
                                archiveEntry.Extract(tempExtractPath);
                                
                                // 2. 0바이트 파일 방지 및 무결성 검증 후 이동
                                var fi = new FileInfo(tempExtractPath);
                                if (fi.Exists && fi.Length > 0)
                                {
                                    File.Move(tempExtractPath, outputPath, true);
                                    imageEntry.FilePath = outputPath; // 이 순간부터 UI가 로컬 파일로 인식
                                }
                                else
                                {
                                    if (fi.Exists) fi.Delete();
                                    lock (lockObj) extracted[targetIndex] = false; // 추출 실패 시 재시도 대기열로 복귀
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                if (tempExtractPath != null && File.Exists(tempExtractPath))
                                    try { File.Delete(tempExtractPath); } catch { }
                                lock (lockObj) extracted[targetIndex] = false;
                            }
                            catch 
                            {
                                if (tempExtractPath != null && File.Exists(tempExtractPath))
                                    try { File.Delete(tempExtractPath); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }, token));
        }

        await Task.WhenAll(tasks);
    }
    catch { }
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
            if (!string.IsNullOrEmpty(_currentArchivePath) && _currentArchivePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(_currentEpubFilePath) && _currentEpubFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;

            // WebDAV 모드인 경우 원격 경로 비교 추가
            if (_isWebDavMode && _currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (entry.IsWebDavEntry && entry.WebDavPath == path) return true;
                if (entry.IsArchiveEntry && _currentArchivePath != null && 
                    (_currentArchivePath == path || _currentArchivePath == $"WebDAV:{path}")) return true;
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

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            _explorerController.SetSortMode(ExplorerSortMode.Name);
            UpdateSortIcon();
            RefreshExplorer();
        }

        private void SortByDateDesc_Click(object sender, RoutedEventArgs e)
        {
            _explorerController.SetSortMode(ExplorerSortMode.DateDesc);
            UpdateSortIcon();
            RefreshExplorer();
        }

        private void SortByDateAsc_Click(object sender, RoutedEventArgs e)
        {
            _explorerController.SetSortMode(ExplorerSortMode.DateAsc);
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
                    if (SortByDateDescMenu != null) SortByDateDescMenu.IsChecked = true;
                    break;
                case ExplorerSortMode.DateAsc:
                    SortIcon.Glyph = "\uE110"; // Up arrow
                    ToolTipService.SetToolTip(SortByDateButton, Strings.SortByDateAscTooltip);
                    if (SortByDateAscMenu != null) SortByDateAscMenu.IsChecked = true;
                    break;
                default:
                    SortIcon.Glyph = "\uE174"; // Default Sort (Name)
                    ToolTipService.SetToolTip(SortByDateButton, Strings.SortByNameTooltip);
                    if (SortByNameMenu != null) SortByNameMenu.IsChecked = true;
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

        private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the flyout
            var flyout = FavoritesButton.Flyout as MenuFlyout;
            flyout?.ShowAt(FavoritesButton);
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
