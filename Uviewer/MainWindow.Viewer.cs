using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {

        #region Image Display

        private async Task DisplayCurrentImageAsync()
        {
            // 방어 코드: 인덱스가 범위를 벗어나면 현재 이미지 목록 기준으로 자동 보정
            if (_imageEntries == null || _imageEntries.Count == 0)
                return;

            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count)
            {
                _currentIndex = Math.Clamp(_currentIndex, 0, _imageEntries.Count - 1);
            }

            // 이전 로딩 작업 취소
            _imageLoadingCts?.Cancel();
            _imageLoadingCts = new CancellationTokenSource();
            var token = _imageLoadingCts.Token; // <-- 이 토큰을 전달해야 함

            StopAnimatedWebp();

            var entry = _imageEntries[_currentIndex];
            if (IsTextEntry(entry))
            {
                await LoadTextEntryAsync(entry);
                await AddToRecentAsync(false);
            }
            else if (IsEpubEntry(entry))
            {
                await LoadEpubEntryAsync(entry);
                await AddToRecentAsync(false);
            }
            else
            {
                SwitchToImageMode(); // Ensure Image Mode is active

                // 이미지가 1장뿐일 때는 항상 단일 이미지 모드로 렌더링하여
                // 2장 보기 모드에서 발생할 수 있는 NRE를 방지
                bool canSideBySide = _isSideBySideMode &&
                                     _currentPdfDocument == null &&
                                     _imageEntries.Count > 1;

                if (canSideBySide)
                {
                    await DisplaySideBySideImagesAsync(token); // <-- token 전달
                }
                else
                {
                    await DisplaySingleImageAsync(token); // <-- token 전달
                }

                await AddToRecentAsync(false);
            }

            RootGrid.Focus(FocusState.Programmatic);
        }

        private void SyncSidebarSelection(ImageEntry entry)
        {
            try
            {
                if (_fileItems == null || _fileItems.Count == 0) return;

                string targetPath = entry.IsArchiveEntry ? (_currentArchivePath ?? "") : (entry.FilePath ?? "");
                if (string.IsNullOrEmpty(targetPath)) return;

                // Find item in file list
                var item = _fileItems.FirstOrDefault(f => f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    if (_isExplorerGrid)
                    {
                        if (FileGridView.SelectedItem != item)
                        {
                            FileGridView.SelectedItem = item;
                            FileGridView.ScrollIntoView(item);
                        }
                    }
                    else
                    {
                        if (FileListView.SelectedItem != item)
                        {
                            FileListView.SelectedItem = item;
                            FileListView.ScrollIntoView(item);
                        }
                    }
                }
            }
            catch { }
        }

        // 매개변수 추가
        private async Task DisplaySingleImageAsync(CancellationToken token)
        {
            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count) return;

            var entry = _imageEntries[_currentIndex];
            StopAnimatedWebp(); // 기존 애니메이션 중단

            try
            {
                if (token.IsCancellationRequested) return;

                // 1. 이미지 로드 (캐시 또는 파일에서져옴)
                var bitmap = await LoadImageBitmapAsync(entry, MainCanvas, token);

                if (token.IsCancellationRequested)
                {
                    // 취소 요청이 들어왔다면, 로드된 비트맵이 "캐시된 것"인지 "새로 만든 것"인지 따지지 말고
                    // 일단 화면에 안 쓸 거니까 정리해야 합니다.

                    // 단, 캐시에 있는 녀석(프리로딩된 것)은 지우면 안 되므로 IsBitmapInCache 체크
                    if (bitmap != null && !IsBitmapInCache(bitmap))
                    {
                        bitmap.Dispose();
                    }
                    return;
                }

                if (bitmap != null)
                {
                    // 1. 옛날 이미지를 임시 변수에 담아둠
                    var oldBitmap = _currentBitmap;

                    // 2. 현재 이미지를 새것으로 '먼저' 교체 (Draw 스레드가 새것을 보게 함)
                    _currentBitmap = bitmap;

                    // 3. UI 갱신 요청
                    if (_currentPdfDocument == null)
                    {
                        _zoomLevel = 1.0;
                        _pdfPanX = 0;
                        _pdfPanY = 0;
                        FitToWindow();
                    }
                    else
                    {
                        // PDF: Set initial pan to top or bottom of page depending on direction
                        if (!_isSeamlessScroll)
                        {
                            var canvasSize = MainCanvas.Size;
                            var imageSize = bitmap.Size;
                            var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                            var scaledH = imageSize.Height * fitRatio * _zoomLevel;
                            double maxPan = (scaledH > canvasSize.Height) ? (scaledH - canvasSize.Height) / 2 : 0;
                            _pdfPanY = (_pdfScrollDirection == 1) ? maxPan : -maxPan;
                            _pdfPanX = 0;
                            _isPdfTransitioning = false;
                        }
                    }
                    ShowImageUI();
                    UpdateStatusBar(entry, _currentBitmap);
                    UpdateSharpenButtonState();
                    MainCanvas.Invalidate();

                    // Sync sidebar selection (using our new safe method)
                    SyncSidebarSelection(entry);

                    // 4. 이제 안전하게 옛날 이미지를 폐기
                    // (단, 캐시에 있는 건 지우면 안 됨)
                    if (oldBitmap != null && !IsBitmapInCache(oldBitmap) && oldBitmap != bitmap)
                    {
                        oldBitmap.Dispose();
                    }
                }
                else
                {
                    FileNameText.Text = Strings.LoadImageError;
                    return;
                }

                // [애니메이션 WebP 처리 부분] 헤더만 읽어서 애니메이션 여부 확인 후 필요시 로드
                if (IsAnimationSupported(entry))
                {
                    // 로딩 표시 추가
                    FileNameText.Text += Strings.Loading;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            byte[]? imageBytes = null;
                            if (entry.IsArchiveEntry && entry.ArchiveEntryKey != null)
                            {
                                imageBytes = await LoadBytesFromArchiveEntryAsync(entry.ArchiveEntryKey, token);
                            }
                            else if (entry.FilePath != null)
                            {
                                imageBytes = await File.ReadAllBytesAsync(entry.FilePath, token);
                            }

                            if (imageBytes == null || token.IsCancellationRequested) return;

                            // 애니메이션 프레임 초기화 (Win2D GPU 합성 및 바이트 캐싱 방식)
                            var (framePixels, delaysMs, w, h) = await TryLoadAnimatedImageFramesNativeAsync(imageBytes);
                            bool success = framePixels != null;

                            if (token.IsCancellationRequested) return;

                            if (success)
                            {
                                _animatedWebpFramePixels = framePixels;
                                _animatedWebpDelaysMs = delaysMs;
                                _animatedWebpWidth = w;
                                _animatedWebpHeight = h;

                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (token.IsCancellationRequested) return;

                                    // 현재 인덱스가 일치하는지 확인 (다른 파일로 넘어갔을 경우 방지)
                                    bool isStillCurrent = false;
                                    if (entry.IsArchiveEntry)
                                        isStillCurrent = _currentIndex < _imageEntries.Count && _imageEntries[_currentIndex].ArchiveEntryKey == entry.ArchiveEntryKey;
                                    else
                                        isStillCurrent = _currentIndex < _imageEntries.Count && _imageEntries[_currentIndex].FilePath == entry.FilePath;

                                    if (isStillCurrent)
                                    {
                                        // 로딩 완료 후 상태바 복구
                                        UpdateStatusBar(entry, _currentBitmap!);
                                        StartAnimatedWebpTimer();
                                    }
                                    else
                                    {
                                        // 이미 다른 이미지로 넘어갔다면 리소스 해제
                                        StopAnimatedWebp();
                                    }
                                });
                            }
                            else
                            {
                                // 애니메이션이 아니거나 로드 실패 시 상태바 복구
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    UpdateStatusBar(entry, _currentBitmap!);
                                });
                            }
                        }
                        catch
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (token.IsCancellationRequested) return;
                                UpdateStatusBar(entry, _currentBitmap!);
                            });
                        }
                    }, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    FileNameText.Text = $"이미지 로드 오류: {ex.Message}";
            }
        }

        private bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            if (bitmap == _currentBitmap || bitmap == _leftBitmap || bitmap == _rightBitmap) return true;

            lock (_preloadedImages)
            {
                if (_preloadedImages.ContainsValue(bitmap)) return true;
            }
            lock (_sharpenedImageCache)
            {
                if (_sharpenedImageCache.ContainsValue(bitmap)) return true;
            }
            lock (_animatedWebpSharpenedCache)
            {
                if (_animatedWebpSharpenedCache.ContainsValue(bitmap)) return true;
            }
            return false;
        }

        private async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            try
            {
                CanvasBitmap? leftBitmap, rightBitmap;
                ImageEntry leftEntry, rightEntry;

                bool actualNextImageOnRight = _nextImageOnRight;
                if (_currentPdfDocument != null) actualNextImageOnRight = true;

                if (actualNextImageOnRight)
                {
                    // → direction: current image on left, next image on right
                    leftEntry = _imageEntries[_currentIndex];
                    leftBitmap = await LoadImageBitmapAsync(leftEntry, LeftCanvas, token);

                    if (token.IsCancellationRequested)
                    {
                        if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) leftBitmap.Dispose();
                        return; // 취소 확인
                    }

                    if (_currentIndex + 1 < _imageEntries.Count)
                    {
                        rightEntry = _imageEntries[_currentIndex + 1];
                        rightBitmap = await LoadImageBitmapAsync(rightEntry, RightCanvas, token);
                    }
                    else
                    {
                        rightEntry = leftEntry; // Use same entry if no next image
                        rightBitmap = null;
                    }
                }
                else
                {
                    // ← direction: next image (n+1) on left, current image (n) on right
                    if (_currentIndex + 1 < _imageEntries.Count)
                    {
                        leftEntry = _imageEntries[_currentIndex + 1];
                        leftBitmap = await LoadImageBitmapAsync(leftEntry, LeftCanvas, token);
                    }
                    else
                    {
                        leftEntry = _imageEntries[_currentIndex];
                        leftBitmap = null;
                    }

                    if (token.IsCancellationRequested)
                    {
                        if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) leftBitmap.Dispose();
                        return; // 취소 확인
                    }

                    rightEntry = _imageEntries[_currentIndex];
                    rightBitmap = await LoadImageBitmapAsync(rightEntry, RightCanvas, token);
                }

                if (token.IsCancellationRequested)
                {
                    if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) leftBitmap.Dispose();
                    if (rightBitmap != null && !IsBitmapInCache(rightBitmap)) rightBitmap.Dispose();
                    return;
                }

                // 1. 옛날 이미지를 임시 변수에 담아둠
                var oldLeft = _leftBitmap;
                var oldRight = _rightBitmap;

                // 2. 현재 이미지를 새것으로 '먼저' 교체
                _leftBitmap = leftBitmap;
                _rightBitmap = rightBitmap;
                _currentBitmap = rightBitmap ?? leftBitmap; // For zoom calculations

                // 3. UI 갱신 요청
                if (_currentPdfDocument == null)
                {
                    _zoomLevel = 1.0;
                    FitToWindow();
                }
                else
                {
                    // PDF: Set initial pan state based on direction
                    if (!_isSeamlessScroll && leftBitmap != null)
                    {
                        var canvasSize = LeftCanvas.Size;
                        var imageSize = leftBitmap.Size;
                        var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                        var scaledH = imageSize.Height * fitRatio * _zoomLevel;
                        double maxPan = (scaledH > canvasSize.Height) ? (scaledH - canvasSize.Height) / 2 : 0;
                        _pdfPanY = (_pdfScrollDirection == 1) ? maxPan : -maxPan;
                    }
                }

                ShowImageUI();

                // 상태바/사이드바에서 "현재 페이지"는 항상 _currentIndex 기준으로 표시되도록 수정
                var primaryEntry = _imageEntries[_currentIndex];
                CanvasBitmap? primaryBitmap;

                if (actualNextImageOnRight)
                {
                    // 현재 페이지가 왼쪽에 있는 경우
                    primaryBitmap = _leftBitmap ?? _rightBitmap ?? _currentBitmap;
                }
                else
                {
                    // 현재 페이지가 오른쪽에 있는 경우
                    primaryBitmap = _rightBitmap ?? _leftBitmap ?? _currentBitmap;
                }

                if (primaryBitmap != null)
                {
                    UpdateStatusBar(primaryEntry, primaryBitmap);
                }
                else if (_currentBitmap != null)
                {
                    UpdateStatusBar(primaryEntry, _currentBitmap);
                }

                SyncSidebarSelection(primaryEntry);


                // 4. 이제 안전하게 옛날 이미지를 폐기
                if (oldLeft != null && !IsBitmapInCache(oldLeft) && oldLeft != leftBitmap && oldLeft != rightBitmap)
                {
                    oldLeft.Dispose();
                }
                if (oldRight != null && !IsBitmapInCache(oldRight) && oldRight != leftBitmap && oldRight != rightBitmap)
                {
                    oldRight.Dispose();
                }
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"이미지 로드 실패: {ex.Message}";
            }
        }

        private async Task<CanvasBitmap?> LoadImageBitmapAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token = default)
        {
            try
            {
                if (token.IsCancellationRequested) return null;
                // 1. 캐시 확인 (압축 파일 프리로딩 등)
                if (entry.IsArchiveEntry && _currentArchive != null)
                {
                    var entryIndex = _imageEntries.IndexOf(entry);
                    CanvasBitmap? preloadedBitmap = null;

                    lock (_preloadedImages)
                    {
                        if (_preloadedImages.TryGetValue(entryIndex, out var bitmap))
                        {
                            preloadedBitmap = bitmap;
                        }
                    }

                    if (preloadedBitmap != null)
                    {
                        if (_sharpenEnabled)
                        {
                            // 샤픈 캐시 확인 및 적용 로직...
                            lock (_sharpenedImageCache)
                            {
                                if (_sharpenedImageCache.TryGetValue(entryIndex, out var sharpenedBitmap))
                                    return sharpenedBitmap;
                            }
                            var sharpened = await ApplySharpenToBitmapAsync(preloadedBitmap, canvas, skipUpscale: false);
                            if (sharpened != null)
                            {
                                CacheSharpenedImage(entryIndex, sharpened);
                                return sharpened;
                            }
                        }
                        return preloadedBitmap;
                    }
                }

                CanvasBitmap? originalBitmap = null;

                // 2. 이미지 소스에 따라 로드
                if (entry.IsPdfEntry && _currentPdfDocument != null)
                {
                    originalBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, canvas, token);
                }
                else if (entry.IsArchiveEntry && _currentArchive != null)
                {
                    // [중요] 압축 파일 내 이미지는 WebP 여부 상관없이 여기서 로드 (Win2D LoadAsync 사용)
                    originalBitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, canvas, token);
                }
                else if (entry.FilePath != null)
                {
                    // 로컬 파일 (애니메이션 WebP가 아닌 경우 여기로 옴)
                    originalBitmap = await LoadImageFromPathAsync(entry.FilePath, canvas);
                }

                // 3. 로드 실패 시 null 반환
                if (originalBitmap == null) return null;

                // 4. 샤픈 효과 적용
                if (_sharpenEnabled && !entry.IsPdfEntry)
                {
                    var entryIndex = _imageEntries.IndexOf(entry);
                    lock (_sharpenedImageCache)
                    {
                        if (_sharpenedImageCache.TryGetValue(entryIndex, out var cached))
                            return cached;
                    }

                    var sharpened = await ApplySharpenToBitmapAsync(originalBitmap, canvas, skipUpscale: false);
                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        CacheSharpenedImage(entryIndex, sharpened);
                        originalBitmap.Dispose(); // Dispose original as we now have sharpened version
                        return sharpened;
                    }
                }

                return originalBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image bitmap: {ex.Message}");
                return null;
            }
        }

        private void SharpenButton_Click(object sender, RoutedEventArgs e)
        {
            _sharpenEnabled = !_sharpenEnabled;

            // [추가] 샤픈 옵션을 바꿀 때 캐시를 초기화하여 충돌 방지
            lock (_sharpenedImageCache)
            {
                foreach (var bmp in _sharpenedImageCache.Values)
                {
                    // 현재 화면에 떠있는 이미지가 아닐 때만 Dispose
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        bmp.Dispose();
                    }
                }
                _sharpenedImageCache.Clear();
            }

            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values)
                {
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        bmp.Dispose();
                    }
                }
                _animatedWebpSharpenedCache.Clear();
            }

            UpdateSharpenButtonState();
            SaveWindowSettings();

            // 이미지 다시 로드
            _ = DisplayCurrentImageAsync();
        }

        private void UpdateSharpenButtonState()
        {
            // UI 동기화
            SharpenButton.IsChecked = _sharpenEnabled;

            // 내부 변수 기준으로 UI 스타일 변경
            if (_sharpenEnabled)
            {
                SharpenIcon.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) &&
                    accent is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    SharpenButton.Foreground = brush;
                }
            }
            else
            {
                SharpenIcon.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                SharpenButton.ClearValue(Control.ForegroundProperty);
            }
        }

        private void SideBySideButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfDocument != null) return;
            _isSideBySideMode = !_isSideBySideMode;

            UpdateSideBySideButtonState();
            SaveWindowSettings();

            if (_isVerticalMode)
            {
                int currentLine = 1;
                if (_verticalPageInfos.Count > _currentVerticalPageIndex)
                    currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
                _ = PrepareVerticalTextAsync(currentLine);
            }
            else if (_isEpubMode)
            {
                SetEpubPageIndex(_currentEpubPageIndex);
            }
            else
            {
                _ = DisplayCurrentImageAsync();
            }
        }

        private void NextImageSideButton_Click(object sender, RoutedEventArgs e)
        {
            _nextImageOnRight = !_nextImageOnRight;
            UpdateNextImageSideButtonState();
            SaveWindowSettings();

            if (_isVerticalMode)
            {
                int currentLine = 1;
                if (_verticalPageInfos.Count > _currentVerticalPageIndex)
                    currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
                _ = PrepareVerticalTextAsync(currentLine);
            }
            else if (_isEpubMode)
            {
                SetEpubPageIndex(_currentEpubPageIndex);
            }
            else
            {
                _ = DisplayCurrentImageAsync();
            }
        }

        private void UpdateSideBySideButtonState()
        {
            if (_isSideBySideMode)
            {
                SideBySideText.Text = "2";
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Microsoft.UI.Xaml.Media.Brush brush)
                    SideBySideButton.Foreground = brush;
            }
            else
            {
                SideBySideText.Text = "1";
                SideBySideButton.ClearValue(Button.ForegroundProperty);
            }
        }

        private void UpdateNextImageSideButtonState()
        {
            if (_nextImageOnRight)
            {
                NextImageSideText.Text = "→"; // Right arrow (left to right)
            }
            else
            {
                NextImageSideText.Text = "←"; // Left arrow (right to left)
            }
        }

        private void MatchControlDirectionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _matchControlDirection = MatchControlDirectionMenuItem.IsChecked;
            SaveWindowSettings();
        }

        private void AllowMultipleInstancesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _allowMultipleInstances = AllowMultipleInstancesMenuItem.IsChecked;
            SaveWindowSettings();
        }

        private async Task<CanvasBitmap?> LoadImageFromPathAsync(string filePath, CanvasControl canvas)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                return await CanvasBitmap.LoadAsync(canvas, stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image from path: {ex.Message}");
                return null;
            }
        }

        private async Task<CanvasBitmap?> LoadImageFromArchiveEntryAsync(string entryKey, CanvasControl canvas, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            using var memoryStream = new MemoryStream();

            // 1. [Lock 구간] 아카이브에서 데이터만 빠르게 메모리로 복사
            await _archiveLock.WaitAsync(token);
            try
            {
                if (_currentArchive == null) return null;
                var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                if (archiveEntry == null) return null;

                using var entryStream = archiveEntry.OpenEntryStream();
                // [핵심 수정] CopyToAsync에 토큰을 전달하여 스트림 복사 강제 중단
                await entryStream.CopyToAsync(memoryStream, token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Archive Stream Error: {ex.Message}");
                return null;
            }
            finally
            {
                _archiveLock.Release();
            }

            memoryStream.Position = 0;
            if (token.IsCancellationRequested) return null;

            // 2. [Lock 해제 후] 디코딩 수행 (여기가 CPU를 많이 쓰므로 락 밖에서 해야 함)
            try
            {
                return await CanvasBitmap.LoadAsync(canvas, memoryStream.AsRandomAccessStream());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win2D Load Error: {ex.Message}");
                return null;
            }
        }


        private async Task<byte[]?> LoadBytesFromArchiveEntryAsync(string entryKey, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            await _archiveLock.WaitAsync(token);
            try
            {
                if (_currentArchive == null) return null;
                var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                if (archiveEntry == null) return null;

                using var entryStream = archiveEntry.OpenEntryStream();
                using var memoryStream = new MemoryStream();
                await entryStream.CopyToAsync(memoryStream, token);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Archive Byte Load Error: {ex.Message}");
                return null;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        private void ShowImageUI()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;

            bool shouldShowSideBySide = _isSideBySideMode && 
                                        _currentPdfDocument == null && 
                                        _imageEntries.Count > 1;

            if (shouldShowSideBySide)
            {
                MainCanvas.Visibility = Visibility.Collapsed;
                SideBySideGrid.Visibility = Visibility.Visible;
            }
            else
            {
                MainCanvas.Visibility = Visibility.Visible;
                SideBySideGrid.Visibility = Visibility.Collapsed;
            }
        }

        private string GetFormattedDisplayName(string displayName, bool isArchiveEntry)
        {
            if (isArchiveEntry && !string.IsNullOrEmpty(_currentArchivePath))
            {
                string archivePath = _currentArchivePath;
                if (archivePath.StartsWith("WebDAV:"))
                {
                    archivePath = archivePath.Substring("WebDAV:".Length);
                }
                string archiveName = Path.GetFileName(archivePath);
                return $"{archiveName} - {displayName}";
            }

            if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
            {
                string realName = Path.GetFileName(_currentWebDavItemPath);
                
                if (!string.IsNullOrEmpty(displayName))
                {
                    // If displayName contains " - ", preserve the suffix (e.g., PDF pages)
                    int dashIndex = displayName.IndexOf(" - ");
                    if (dashIndex > 0)
                    {
                        return realName + displayName.Substring(dashIndex);
                    }
                    
                    return realName;
                }
            }

            return displayName;
        }

        private void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap)
        {
            FileNameText.Text = GetFormattedDisplayName(entry.DisplayName, entry.IsArchiveEntry);
            ImageInfoText.Text = $"{(int)bitmap.Size.Width} × {(int)bitmap.Size.Height}";
            TextProgressText.Text = ""; // Clear for image mode

            if (_isSideBySideMode && _currentPdfDocument == null)
            {
                int displayIndex = (_currentIndex / 2) + 1;
                int totalPairs = (_imageEntries.Count + 1) / 2;
                ImageIndexText.Text = $"{displayIndex} / {totalPairs} (B)";
            }
            else
            {
                ImageIndexText.Text = $"{_currentIndex + 1} / {_imageEntries.Count}";
            }
        }

        private void ImageArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-apply fit when window is resized
            if (_currentBitmap != null &&
                (MainCanvas.Visibility == Visibility.Visible || SideBySideGrid.Visibility == Visibility.Visible))
            {
                ApplyZoom();
            }
        }

        private async void ImageArea_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint(ImageArea).Properties;
            var wheelDelta = properties.MouseWheelDelta;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                if (_currentPdfDocument != null && _currentBitmap != null)
                {
                    double zoomDelta = (wheelDelta / 120.0) * 0.1;
                    _zoomLevel += zoomDelta;
                    _zoomLevel = Math.Clamp(_zoomLevel, MinZoom, MaxZoom);
                    ApplyZoom();
                    e.Handled = true;
                    return;
                }
            }

            if (_currentPdfDocument != null && _currentBitmap != null)
            {
                // PDF 연속 스크롤 처리
                double step = 80;
                double deltaY = (wheelDelta > 0) ? step : -step;
                await HandlePdfScrollAsync(0, deltaY);
                e.Handled = true;
                return;
            }

            // 일반 이미지 내비게이션
            if (wheelDelta < 0) await NavigateToNextAsync();
            else if (wheelDelta > 0) await NavigateToPreviousAsync();

            e.Handled = true;
        }

        private async void ImageArea_ManipulationDelta(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            if (_currentPdfDocument == null || _currentBitmap == null) return;

            // 1. 핀치 줌 처리
            if (e.Delta.Scale != 1.0f)
            {
                _zoomLevel *= e.Delta.Scale;
                _zoomLevel = Math.Clamp(_zoomLevel, MinZoom, MaxZoom);
            }

            // 2. 드래그/스와이프 스크롤 처리
            await HandlePdfScrollAsync(e.Delta.Translation.X, e.Delta.Translation.Y);

            e.Handled = true;
        }

        private void ImageArea_ManipulationCompleted(object sender, Microsoft.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
        {
            _isPdfTransitioning = false;
        }

        private async Task HandlePdfScrollAsync(double deltaX, double deltaY)
        {
            if (_currentPdfDocument == null || _currentBitmap == null || _isPdfTransitioning) return;

            try
            {
                var canvasSize = MainCanvas.Size;
                if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;

                var imageSize = _currentBitmap.Size;
                var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                var scaledSize = new Windows.Foundation.Size(imageSize.Width * fitRatio * _zoomLevel, imageSize.Height * fitRatio * _zoomLevel);

                // 가로 스크롤/팬 처리
                _pdfPanX += deltaX;
                double maxPanX = Math.Max(0, (scaledSize.Width - canvasSize.Width) / 2);
                if (_pdfPanX > maxPanX) _pdfPanX = maxPanX;
                if (_pdfPanX < -maxPanX) _pdfPanX = -maxPanX;

                // 세로 스크롤 및 페이지 전환 처리
                double gap = 20 * _zoomLevel;
                double maxPanY = Math.Max(0, (scaledSize.Height - canvasSize.Height) / 2);

                if (deltaY > 0) // 위로 스크롤 (이전 페이지로)
                {
                    _pdfPanY += deltaY;

                    // 이전 페이지로 전환
                    if (_pdfPanY > maxPanY + 1 && _currentIndex > 0)
                    {
                        _isPdfTransitioning = true;
                        CanvasBitmap? prev = null;
                        int targetPrevIndex = _currentIndex - 1;
                        lock (_preloadedImages) { _preloadedImages.TryGetValue(targetPrevIndex, out prev); }

                        var oldPosNextTop = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;

                        int oldIndex = _currentIndex;
                        lock (_preloadedImages)
                        {
                            if (!_preloadedImages.ContainsKey(oldIndex))
                                _preloadedImages[oldIndex] = _currentBitmap;
                        }

                        _currentIndex = targetPrevIndex;

                        if (prev != null && prev.Device != null)
                        {
                            try
                            {
                                var pFit = Math.Min(canvasSize.Width / prev.Size.Width, canvasSize.Height / prev.Size.Height);
                                var pScaledH = prev.Size.Height * pFit * _zoomLevel;
                                _pdfPanY = (oldPosNextTop - gap - pScaledH) - (canvasSize.Height - pScaledH) / 2;

                                _currentBitmap = prev;
                                var prevEntry = _imageEntries[_currentIndex];
                                // Do not call ShowImageUI here to avoid flickering
                                UpdateStatusBar(prevEntry, _currentBitmap);
                                MainCanvas.Invalidate();

                                // ONLY cancel if we are jumping far, for smooth scroll just fire and forget.
                                // However, we can use the same token so it just adds to queue.
                                // We do NOT cancel the token here!
                                var token = _preloadCts?.Token ?? default;
                                _ = Task.Run(() => PreloadPreviousImagesAsync(token));
                            }
                            catch
                            {
                                _isPdfTransitioning = false;
                                return;
                            }
                        }
                        else
                        {
                            // Page not preloaded yet! Don't do a full DisplayCurrentImageAsync which flickers.
                            // Wait for it inline.
                            try
                            {
                                prev = await LoadPdfPageBitmapAsync((uint)targetPrevIndex, MainCanvas, default);
                                if (prev != null)
                                {
                                    lock (_preloadedImages) { _preloadedImages[targetPrevIndex] = prev; }

                                    var pFit = Math.Min(canvasSize.Width / prev.Size.Width, canvasSize.Height / prev.Size.Height);
                                    var pScaledH = prev.Size.Height * pFit * _zoomLevel;
                                    _pdfPanY = (oldPosNextTop - gap - pScaledH) - (canvasSize.Height - pScaledH) / 2;

                                    _currentBitmap = prev;
                                    var prevEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(prevEntry, _currentBitmap);
                                    MainCanvas.Invalidate();

                                    var token = _preloadCts?.Token ?? default;
                                    _ = Task.Run(() => PreloadPreviousImagesAsync(token));
                                }
                            }
                            catch { }
                        }
                        _isPdfTransitioning = false;
                        return;
                    }

                    if (_currentIndex == 0 && _pdfPanY > maxPanY) _pdfPanY = maxPanY;
                }
                else if (deltaY < 0) // 아래로 스크롤 (다음 페이지로)
                {
                    _pdfPanY += deltaY;

                    // 다음 페이지로 전환
                    if (_pdfPanY < -maxPanY - 1 && _currentIndex < _imageEntries.Count - 1)
                    {
                        _isPdfTransitioning = true;
                        CanvasBitmap? next = null;
                        int targetNextIndex = _currentIndex + 1;
                        lock (_preloadedImages) { _preloadedImages.TryGetValue(targetNextIndex, out next); }

                        var oldPosPrevBottom = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY + scaledSize.Height;

                        int oldIndex = _currentIndex;
                        lock (_preloadedImages)
                        {
                            if (!_preloadedImages.ContainsKey(oldIndex))
                                _preloadedImages[oldIndex] = _currentBitmap;
                        }

                        _currentIndex = targetNextIndex;

                        if (next != null && next.Device != null)
                        {
                            try
                            {
                                var nFit = Math.Min(canvasSize.Width / next.Size.Width, canvasSize.Height / next.Size.Height);
                                var nScaledH = next.Size.Height * nFit * _zoomLevel;
                                _pdfPanY = (oldPosPrevBottom + gap) - (canvasSize.Height - nScaledH) / 2;

                                _currentBitmap = next;
                                var nextEntry = _imageEntries[_currentIndex];
                                // Do not call ShowImageUI here to avoid flickering
                                UpdateStatusBar(nextEntry, _currentBitmap);
                                MainCanvas.Invalidate();

                                // DO NOT cancel token to avoid stuttering and throwing away in-progress loads
                                var token = _preloadCts?.Token ?? default;
                                _ = Task.Run(() => PreloadNextImagesAsync(token));
                            }
                            catch
                            {
                                _isPdfTransitioning = false;
                                return;
                            }
                        }
                        else
                        {
                            // Page not preloaded yet! 
                            try
                            {
                                next = await LoadPdfPageBitmapAsync((uint)targetNextIndex, MainCanvas, default);
                                if (next != null)
                                {
                                    lock (_preloadedImages) { _preloadedImages[targetNextIndex] = next; }

                                    var nFit = Math.Min(canvasSize.Width / next.Size.Width, canvasSize.Height / next.Size.Height);
                                    var nScaledH = next.Size.Height * nFit * _zoomLevel;
                                    _pdfPanY = (oldPosPrevBottom + gap) - (canvasSize.Height - nScaledH) / 2;

                                    _currentBitmap = next;
                                    var nextEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(nextEntry, _currentBitmap);
                                    MainCanvas.Invalidate();

                                    var token = _preloadCts?.Token ?? default;
                                    _ = Task.Run(() => PreloadNextImagesAsync(token));
                                }
                            }
                            catch { }
                        }
                        _isPdfTransitioning = false;
                        return;
                    }

                    if (_currentIndex >= _imageEntries.Count - 1 && _pdfPanY < -maxPanY) _pdfPanY = -maxPanY;
                }

                ApplyZoom();
            }
            finally
            {
                _isPdfTransitioning = false;
            }
        }

        private async void ImageArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_imageEntries.Count <= 1)
                return;

            var pt = e.GetCurrentPoint(ImageArea);
            if (!pt.Properties.IsLeftButtonPressed)
                return;

            if (_currentPdfDocument != null && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
                return; // Prevent touch click from navigating pages in PDF (interferes with swipe)

            double half = ImageArea.ActualWidth * 0.5;
            if (pt.Position.X < half)
            {
                if (ShouldInvertControls) await NavigateToNextAsync(true);
                else await NavigateToPreviousAsync(true);
            }
            else
            {
                if (ShouldInvertControls) await NavigateToPreviousAsync(true);
                else await NavigateToNextAsync(true);
            }
            e.Handled = true;
            RootGrid.Focus(FocusState.Programmatic);
        }

        #endregion


    }
}

