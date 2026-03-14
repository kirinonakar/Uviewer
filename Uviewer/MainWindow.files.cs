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
            foreach (var ext in SupportedFileExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                var extension = Path.GetExtension(file.Path).ToLowerInvariant();
                if (SupportedArchiveExtensions.Contains(extension))
                {
                    await LoadImagesFromArchiveAsync(file.Path);
                }
                else if (SupportedPdfExtensions.Contains(extension))
                {
                    await LoadImagesFromPdfAsync(file.Path);
                }
                else if (SupportedEpubExtensions.Contains(extension))
                {
                    await LoadEpubFileAsync(file);
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
            CloseCurrentArchive();
            await CloseCurrentPdfAsync();
            CloseCurrentEpub();

            // Cancel any ongoing preloading and clear cache
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = null;
            
            lock (_preloadedImages)
            {
                foreach (var bitmap in _preloadedImages.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _preloadedImages.Clear();
            }

            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _sharpenedImageCache.Clear();
            }

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
                            .Where(f => SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                            .OrderBy(f => f.Name, StringComparer.CurrentCulture)
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
                        .Where(f => SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                        .OrderBy(f => f.Name, StringComparer.CurrentCulture)
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
            CloseCurrentArchive();
            await CloseCurrentPdfAsync();
            CloseCurrentEpub();

            // Cancel any ongoing preloading and clear cache
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = null;
            
            lock (_preloadedImages)
            {
                foreach (var bitmap in _preloadedImages.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _preloadedImages.Clear();
            }

            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _sharpenedImageCache.Clear();
            }

            var files = await folder.GetFilesAsync();
            _imageEntries = files
                .Where(f => SupportedFileExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
                .OrderBy(f => f.Name, StringComparer.CurrentCulture)
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
            // Close other formats first
            await CloseCurrentPdfAsync();
            CloseCurrentEpub();

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
                        .OrderBy(e => e.Key, StringComparer.CurrentCulture)
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
 
                    // 7z 파일인 경우 백그라운드 압축 해제 시작
                    string extension = Path.GetExtension(archivePath).ToLowerInvariant();
                    if (extension == ".7z")
                    {
                        _7zExtractCts = new CancellationTokenSource();
                        var extractToken = _7zExtractCts.Token;
                        _ = Start7zBackgroundExtractionAsync(archivePath, extractToken);
                    }

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
            if (_currentArchive == null) return;

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
                    SafeDisposeBitmap(bitmap);
                }
                _preloadedImages.Clear();
            }

            // Clear sharpened image cache
            lock (_sharpenedImageCache)
            {
                foreach (var bitmap in _sharpenedImageCache.Values)
                {
                    SafeDisposeBitmap(bitmap);
                }
                _sharpenedImageCache.Clear();
            }

            // Clean up fast navigation
            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts?.Dispose();
            _fastNavigationResetCts = null;

            _currentBitmap = null;
            _leftBitmap = null;
            _rightBitmap = null;

            Cleanup7zTempData();
        }

        private async Task Start7zBackgroundExtractionAsync(string archivePath, CancellationToken token)
        {
            try
            {
                // 이전 데이터 정리 및 새 템프 폴더 생성
                string baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
                _current7zTempFolder = Path.Combine(baseTemp, "7z_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_current7zTempFolder);

                var total = _imageEntries.Count;
                var extracted = new bool[total];
                var lockObj = new object();

                // [멀티스레드 압축 해제]
                // 7z의 경우 SharpCompress의 IArchive 인스턴스가 스레드 세이프하지 않을 수 있으므로
                // 스레드별로 별도의 아카이브 인스턴스를 열어 성능을 극대화합니다.
                int threadCount = Math.Min(Environment.ProcessorCount, 4); // 최대 4개 스레드 사용
                var tasks = new List<Task>();

                for (int t = 0; t < threadCount; t++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var archive = ArchiveFactory.Open(archivePath);
                            var entries = archive.Entries
                                .Where(e => !e.IsDirectory && SupportedImageExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant()))
                                .ToList();
                            var entryMap = entries.ToDictionary(e => e.Key!, e => e);

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
                                    try
                                    {
                                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _7zJumpCts.Token);
                                        var linkedToken = linkedCts.Token;

                                        string ext = Path.GetExtension(imageEntry.ArchiveEntryKey ?? "") ?? "";
                                        outputPath = Path.Combine(_current7zTempFolder!, Guid.NewGuid().ToString("N") + ext);
                                        
                                        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                                        using (var entryStream = archiveEntry.OpenEntryStream())
                                        {
                                            await entryStream.CopyToAsync(fs, linkedToken);
                                        }
                                        imageEntry.FilePath = outputPath;
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        // 취소된 경우 생성 중이던 불완전한 파일 삭제
                                        if (outputPath != null && File.Exists(outputPath))
                                        {
                                            try { File.Delete(outputPath); } catch { }
                                        }

                                        lock (lockObj)
                                        {
                                            extracted[targetIndex] = false;
                                        }
                                    }
                                    catch 
                                    {
                                        if (outputPath != null && File.Exists(outputPath))
                                        {
                                            try { File.Delete(outputPath); } catch { }
                                        }
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

        private void Cleanup7zTempData(bool immediate = false)
        {
            try
            {
                _7zExtractCts?.Cancel();
                _7zExtractCts?.Dispose();
                _7zExtractCts = null;

                if (_current7zTempFolder != null)
                {
                    string folderToDelete = _current7zTempFolder;
                    if (Directory.Exists(folderToDelete))
                    {
                        if (immediate)
                        {
                            TryDeleteDirectoryRecursive(folderToDelete);
                        }
                        else
                        {
                            _current7zTempFolder = null;
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(1000);
                                for (int i = 0; i < 3; i++)
                                {
                                    try
                                    {
                                        if (Directory.Exists(folderToDelete))
                                            Directory.Delete(folderToDelete, true);
                                        break;
                                    }
                                    catch { await Task.Delay(2000); }
                                }
                                CleanupUviewerTempRoot();
                            });
                        }
                    }
                }
                
                if (immediate) CleanupUviewerTempRoot();
            }
            catch { }
        }

        private void CleanupUviewerTempRoot()
        {
            try
            {
                var baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
                if (Directory.Exists(baseTemp) && !Directory.EnumerateFileSystemEntries(baseTemp).Any())
                {
                    Directory.Delete(baseTemp);
                }
            }
            catch { }
        }

        private void CleanupZeroByteTempFiles()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    string baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
                    if (Directory.Exists(baseTemp))
                    {
                        // 0바이트 파일 삭제
                        var files = Directory.GetFiles(baseTemp, "*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                if (fi.Length == 0)
                                {
                                    fi.Delete();
                                }
                            }
                            catch { }
                        }

                        // 빈 하위 폴더 삭제 (하위부터 상위로)
                        var dirs = Directory.GetDirectories(baseTemp, "*", SearchOption.AllDirectories)
                                            .OrderByDescending(d => d.Length);
                        foreach (var dir in dirs)
                        {
                            try
                            {
                                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                                {
                                    Directory.Delete(dir);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }

        private void TryDeleteDirectoryRecursive(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;

                // 먼저 내부 파일들을 개별적으로 삭제 시도 (잠긴 파일 확인용 및 부하 분산)
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (Directory.Exists(path))
                            Directory.Delete(path, true);
                        return;
                    }
                    catch
                    {
                        if (i < 4) Thread.Sleep(100);
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Folder Explorer
        
        private CancellationTokenSource? _thumbnailLoadingCts;

        private void LoadExplorerFolder(string path)
        {
            // WebDAV 모드에서 로컬 폴더로 이동 시 모드 해제
            if (_isWebDavMode)
            {
                DisconnectWebDav();
                _currentWebDavItemPath = null; 
            }

            _currentExplorerPath = path;
            CurrentPathText.Text = path;

            // Gather folder contents in background
            _ = Task.Run(() =>
            {
                try
                {
                    var newItems = new List<FileItem>();

                    // Add parent directory entry
                    var parentDir = Directory.GetParent(path);
                    if (parentDir != null)
                    {
                        newItems.Add(new FileItem
                        {
                            Name = "..",
                            FullPath = parentDir.FullName,
                            IsDirectory = true,
                            IsParentDirectory = true
                        });
                    }

                    // Add directories (Smart sort)
                    var directories = Directory.GetDirectories(path)
                        .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCulture);

                    foreach (var dir in directories)
                    {
                        var name = Path.GetFileName(dir);
                        if (!name.StartsWith(".")) // Hide hidden folders
                        {
                            newItems.Add(new FileItem
                            {
                                Name = name,
                                FullPath = dir,
                                IsDirectory = true
                            });
                        }
                    }

                    // Add files (images and archives)
                    var files = Directory.GetFiles(path)
                        .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCulture);

                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        var isImage = SupportedImageExtensions.Contains(ext);
                        var isArchive = SupportedArchiveExtensions.Contains(ext);
                        var isText = SupportedTextExtensions.Contains(ext);
                        var isEpub = SupportedEpubExtensions.Contains(ext);
                        var isPdf = SupportedPdfExtensions.Contains(ext);

                        if (isImage || isArchive || isText || isEpub || isPdf)
                        {
                            newItems.Add(new FileItem
                            {
                                Name = Path.GetFileName(file),
                                FullPath = file,
                                IsDirectory = false,
                                IsImage = isImage,
                                IsArchive = isArchive,
                                IsText = isText,
                                IsEpub = isEpub,
                                IsPdf = isPdf
                            });
                        }
                    }

                    // Finalize on UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // Protect against late updates if path changed
                        if (_currentExplorerPath != path) return;

                        _fileItems.Clear();
                        foreach (var item in newItems)
                        {
                            _fileItems.Add(item);
                        }

                        // Sync selection if an image is already loaded
                        if (_currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
                        {
                            SyncSidebarSelection(_imageEntries[_currentIndex]);
                        }

                        // Start loading thumbnails
                        _thumbnailLoadingCts?.Cancel();
                        _thumbnailLoadingCts = new CancellationTokenSource();
                        _ = LoadThumbnailsAsync(_thumbnailLoadingCts.Token);
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CurrentPathText.Text = $"오류: {ex.Message}";
                    });
                }
            });
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

            // WebDAV 모드인 경우 원격 경로 비교 추가
            if (_isWebDavMode && _currentIndex >= 0 && _imageEntries != null && _currentIndex < _imageEntries.Count)
            {
                var entry = _imageEntries[_currentIndex];
                if (entry.IsWebDavEntry && entry.WebDavPath == path) return true;
                if (entry.IsArchiveEntry && _currentArchivePath != null && 
                    (_currentArchivePath == path || _currentArchivePath == $"WebDAV:{path}")) return true;
            }

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
            else if (item.IsPdf)
            {
                await AddToRecentAsync(true);
                await LoadImagesFromPdfAsync(item.FullPath);
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
            SaveWindowSettings();
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