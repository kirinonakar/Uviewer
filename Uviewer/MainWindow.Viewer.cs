using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SevenZipExtractor;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {

        #region Fast Navigation

        private void UpdateFastNavigationUI()
        {
            if (_currentIndex < 0 || _imageEntries == null || _imageEntries.Count == 0)
                return;

            var currentEntry = _imageEntries[_currentIndex];
            string displayName = FileExplorerService.GetFormattedDisplayName(currentEntry.DisplayName, currentEntry.IsArchiveEntry);

            _fastNavigationService.UpdateState(_currentIndex, _imageEntries.Count, displayName, _isCurrentViewSideBySide);

            Signal7zJump(); // 빠른 탐색 중에도 추출 위치를 계속 업데이트

            _fastNavigationService.ShowOverlay(
                showCallback: () =>
                {
                    FastNavText.Text = _fastNavigationService.GetOverlayMessage();
                    FastNavOverlay.Visibility = Visibility.Visible;
                },
                hideCallback: () =>
                {
                    FastNavOverlay.Visibility = Visibility.Collapsed;
                }
            );

            // Don't hide images during fast navigation - just update text
            // Images will stay visible showing the last loaded image

            // Update the UI via service formatted messages
            FileNameText.Text = _fastNavigationService.DisplayName;
            ImageIndexText.Text = _fastNavigationService.GetImageIndexMessage();
            TextProgressText.Text = ""; // Clear for image mode
            ImageInfoText.Text = "빠르게 넘어가는 중...";
        }

        private async Task ResetFastNavigation()
        {
            _fastNavigationService.StopOverlayTimer();
            if (_currentIndex >= 0 && _currentIndex < (_imageEntries?.Count ?? 0))
            {
                Signal7zJump(); // Fast Navigation 종료 시 해당 위치로 추출 순위 재조정
                await DisplayCurrentImageAsync();
            }
            // 화면 로딩이 완전히 끝난 후 오버레이를 닫고 그리기 허용
            FastNavOverlay.Visibility = Visibility.Collapsed;
            MainCanvas?.Invalidate();
        }

        #endregion

        #region Zoom

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            FitToWindow();
        }

        private void ZoomActualButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap != null && _currentBitmap.Device != null)
            {
                var containerWidth = ImageArea.ActualWidth;
                var containerHeight = ImageArea.ActualHeight;

                if (containerWidth > 0 && containerHeight > 0)
                {
                    _zoomService.CalculateActualZoom(containerWidth, containerHeight, _currentBitmap.Size.Width, _currentBitmap.Size.Height, MainCanvas.Dpi / 96.0f, _currentPdfDocument != null);
                    ApplyZoom();
                }
            }
        }

        private void ZoomIn()
        {
            _zoomService.ZoomIn();
            ApplyZoom();
        }

        private void ZoomOut()
        {
            _zoomService.ZoomOut();
            ApplyZoom();
        }

        private void FitToWindow()
        {
            _zoomService.FitToWindow();
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_currentBitmap == null || _currentBitmap.Device == null || ImageArea.ActualWidth <= 0 || ImageArea.ActualHeight <= 0)
                return;

            // Trigger canvas redraw for new zoom level
            if (!_isCurrentViewSideBySide || _currentPdfDocument != null)
            {
                MainCanvas?.Invalidate();
            }
            else
            {
                LeftCanvas?.Invalidate();
                RightCanvas?.Invalidate();
            }

            // Update zoom level display (relative to fit size)
            ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";

            if (_currentPdfDocument != null && !(_smoothZoomTimer?.IsRunning ?? false))
            {
                _ = RerenderPdfCurrentPageAsync();
            }
        }

        #endregion

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

            // [Important] Capture index before await to avoid race condition if _currentIndex changes during render
            int capturedIndexAtStart = _currentIndex;

            // 이전 로딩 작업 취소
            _imageLoadingCts?.Cancel();
            _imageLoadingCts = new CancellationTokenSource();
            var token = _imageLoadingCts.Token; // <-- 이 토큰을 전달해야 함

            // 아카이브 탐색 시 백그라운드 추출 위치 재조정 신호 전송 (큰 폭의 이동 시에만)
            if (_current7zArchive != null)
            {
                if (Math.Abs(_currentIndex - _lastIndexFor7zJump) > 2)
                {
                    Signal7zJump();
                    _lastIndexFor7zJump = _currentIndex;
                }
            }
            else
            {
                _lastIndexFor7zJump = _currentIndex;
            }

            StopAnimatedWebp();

            var entry = _imageEntries[_currentIndex];

            // PDF 전용 처리 블록: 잔상 제거 및 줌 레벨 맞춤 캐시 적용
            if (entry.IsPdfEntry && _currentPdfDocument != null)
            {
                SwitchToImageMode();
                CanvasBitmap? nextBitmap = null;

                nextBitmap = _imageCache.GetPreloadedImage(_currentIndex, _zoomLevel);

                // 2. 캐시에 없어서 새로 렌더링해야 하는 경우 (잔상 제거 로직 포함)
                if (nextBitmap == null)
                {
                    // 새 페이지를 그리는 동안 예전 페이지가 멈춰 있는 현상을 막기 위해
                    // 현재 비트맵을 즉시 비우고 화면을 갱신합니다.
                    var tempOldBitmap = _currentBitmap;
                    _currentBitmap = null;
                    MainCanvas.Invalidate();

                    if (tempOldBitmap != null && !IsBitmapInCache(tempOldBitmap))
                    {
                        _imageCache.SafeDisposeBitmap(tempOldBitmap);
                    }

                    // 렌더링 시작 (토큰 전달로 페이지 이동 시 취소 가능하게 함)
                    nextBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas, token);

                    // [핵심] await 후 인덱스가 달라졌으면 버리기 (다른 페이지로 넘어간 것임)
                    if (nextBitmap != null)
                    {
                        if (token.IsCancellationRequested || _currentIndex != capturedIndexAtStart)
                        {
                            _imageCache.SafeDisposeBitmap(nextBitmap);
                            return;
                        }

                        _imageCache.UpdateCache(capturedIndexAtStart, nextBitmap, true, _zoomLevel, false, _currentBitmap);
                    }
                }

                // 3. 확보된 고해상도 이미지를 화면에 표시
                if (nextBitmap != null && !token.IsCancellationRequested && _currentIndex == capturedIndexAtStart)
                {
                    var oldBitmap = _currentBitmap;
                    _currentBitmap = nextBitmap;

                    // PDF 단일 페이지 모드이므로 사이드 바이 사이드용 비트맵 초기화
                    _leftBitmap = null;
                    _rightBitmap = null;

                    // PDF: Set initial pan position to top or bottom of page depending on direction
                    if (!_isSeamlessScroll)
                    {
                        var canvasSize = MainCanvas.Size;
                        var imageSize = nextBitmap.Size;
                        var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
                        var scaledH = imageSize.Height * fitRatio * _zoomLevel;
                        double maxPan = (scaledH > canvasSize.Height) ? (scaledH - canvasSize.Height) / 2 : 0;
                        _pdfPanY = (_pdfScrollDirection == 1) ? maxPan : -maxPan;
                        _pdfPanX = 0;
                        _isPdfTransitioning = false;
                    }

                    MainCanvas.Invalidate();
                    ShowImageUI();
                    UpdateStatusBar(entry, _currentBitmap);

                    if (oldBitmap != null && oldBitmap != nextBitmap && !IsBitmapInCache(oldBitmap))
                    {
                        _imageCache.SafeDisposeBitmap(oldBitmap);
                    }
                }
                
                // 페이지 이동 정보 기록
                await AddToRecentAsync(false);
                RootGrid.Focus(FocusState.Programmatic);
                return;
            }

            if (FileExplorerService.IsTextEntry(entry))
            {
                if (!_isTextMode || _currentTextFilePath != entry.FilePath || _currentTextArchiveEntryKey != entry.ArchiveEntryKey)
                {
                    await LoadTextEntryAsync(entry);
                }
                else
                {
                    // Already in text mode and same file: update UI
                    if (_aozoraPendingTargetLine != 0)
                    {
                        string fileName = System.IO.Path.GetFileName(_currentTextFilePath ?? "");
                        await ReloadTextDisplayFromCacheAsync(fileName, _aozoraPendingTargetLine);
                    }
                    else
                    {
                        if (_isVerticalMode) VerticalTextCanvas?.Invalidate();
                        else if (_isAozoraMode) AozoraTextCanvas?.Invalidate();
                        else TextScrollViewer?.InvalidateArrange(); // Or similar refresh
                    }
                }
                await AddToRecentAsync(false);
            }
            else if (FileExplorerService.IsEpubEntry(entry))
            {
                if (!_isEpubMode || _currentEpubFilePath != entry.FilePath)
                {
                    await LoadEpubEntryAsync(entry, token);
                }
                else
                {
                    // Already in EPUB mode and same file: update UI
                    if (PendingEpubChapterIndex >= 0 || _aozoraPendingTargetLine != 0)
                    {
                        int targetCh = (PendingEpubChapterIndex >= 0) ? PendingEpubChapterIndex : _currentEpubChapterIndex;
                        await LoadEpubChapterAsync(targetCh, targetLine: _aozoraPendingTargetLine, targetBlockIndex: _pendingEpubStartBlockIndex, targetPage: PendingEpubPageIndex, token: token);
                        
                        // Reset pending values
                        PendingEpubChapterIndex = -1;
                        PendingEpubPageIndex = -1;
                        _aozoraPendingTargetLine = 0;
                        _pendingEpubStartBlockIndex = -1;
                    }
                    else
                    {
                        if (_isVerticalMode) VerticalTextCanvas?.Invalidate();
                        else if (CurrentEpubWin2DPage?.IsImagePage == true) ShowEpubImagePage(CurrentEpubWin2DPage);
                        else EpubTextCanvas?.Invalidate();
                    }
                }
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

// [추가] 압축파일 자동 2장보기 옵션
                if (!canSideBySide && _autoDoublePageForArchive && 
                    (_currentArchivePath != null || _current7zArchive != null) &&
                    _currentPdfDocument == null && _imageEntries.Count > 1)
                {
                    // 현재 이미지를 미리 로드하여 가로세로 비율 확인 (샤픈 적용을 피하기 위해 원본 로드 사용)
                    CanvasBitmap? firstBitmap = _imageCache.GetPreloadedImage(_currentIndex, _zoomLevel);
                    
                    if (firstBitmap == null)
                    {
                        firstBitmap = await LoadBitmapForPreloadAsync(entry, false, token);
                        if (firstBitmap != null)
                        {
                            // 원본 이미지를 캐시에 넣어두어 나중에 다시 로드되는 것을 방지
                            _imageCache.UpdateCache(_currentIndex, firstBitmap, false, _zoomLevel, false, _currentBitmap);
                        }
                    }

                    if (firstBitmap != null)
                    {
                        if (firstBitmap.Size.Height >= firstBitmap.Size.Width * 1.2)
                        {
                            canSideBySide = true;
                        }
                    }
                }

                _isCurrentViewSideBySide = canSideBySide;

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

                FileItem? item = null;

                if (_isWebDavMode && entry.IsWebDavEntry && !entry.IsArchiveEntry)
                {
                    // Match by WebDAV remote path for files in a folder
                    item = _fileItems.FirstOrDefault(f => f.IsWebDav && f.WebDavPath == entry.WebDavPath);
                }
                else
                {
                    string targetPath = entry.IsArchiveEntry ? (_currentArchivePath ?? "") : (entry.FilePath ?? "");
                    if (string.IsNullOrEmpty(targetPath)) return;

                    // Match by local full path or check if it's a WebDAV archive (WebDAV: prefix)
                    item = _fileItems.FirstOrDefault(f => 
                        f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase) ||
                        (f.IsWebDav && targetPath.Equals($"WebDAV:{f.WebDavPath}", StringComparison.OrdinalIgnoreCase)));
                }

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
                        _imageCache.SafeDisposeBitmap(bitmap); // 일반 Dispose() 대신 사용
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
                        // PDF: Handle initial pan state (handled in DisplayCurrentImageAsync now)
                        _pdfPanX = 0;
                        _pdfPanY = 0;
                        _isPdfTransitioning = false;
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
                        _imageCache.SafeDisposeBitmap(oldBitmap); // 일반 Dispose() 대신 사용
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

            if (_imageCache.IsBitmapInCache(bitmap)) return true;

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
                        // [수정] 직접 Dispose() 대신 SafeDisposeBitmap 사용
                        if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) 
                            _imageCache.SafeDisposeBitmap(leftBitmap);
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
                        // [수정] 직접 Dispose() 대신 SafeDisposeBitmap 사용
                        if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) 
                            _imageCache.SafeDisposeBitmap(leftBitmap);
                        return; // 취소 확인
                    }

                    rightEntry = _imageEntries[_currentIndex];
                    rightBitmap = await LoadImageBitmapAsync(rightEntry, RightCanvas, token);
                }

                if (token.IsCancellationRequested)
                {
                    if (leftBitmap != null && !IsBitmapInCache(leftBitmap)) _imageCache.SafeDisposeBitmap(leftBitmap);
                    if (rightBitmap != null && !IsBitmapInCache(rightBitmap)) _imageCache.SafeDisposeBitmap(rightBitmap);
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
                    _imageCache.SafeDisposeBitmap(oldLeft);
                }
                if (oldRight != null && !IsBitmapInCache(oldRight) && oldRight != leftBitmap && oldRight != rightBitmap)
                {
                    _imageCache.SafeDisposeBitmap(oldRight);
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
                var entryIndex = _imageEntries.IndexOf(entry);
                if (entryIndex >= 0)
                {
                    var cachedBitmap = _imageCache.GetPreloadedImage(entryIndex, _zoomLevel);
                    if (cachedBitmap != null)
                    {
                        if (_sharpenEnabled)
                        {
                            var sharpenedBitmap = _imageCache.GetSharpenedImage(entryIndex);
                            if (sharpenedBitmap != null) return sharpenedBitmap;

                            var sharpened = await _sharpeningService.ApplySharpenToBitmapAsync(cachedBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);
                            if (sharpened != null)
                            {
                                _imageCache.CacheSharpenedImage(entryIndex, sharpened, _currentIndex);
                                return sharpened;
                            }
                        }
                        return cachedBitmap;
                    }
                }

                CanvasBitmap? originalBitmap = null;

                // 2. 이미지 소스에 따라 로드
                if (entry.IsPdfEntry && _currentPdfDocument != null)
                {
                    originalBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, canvas, token);
                }
                else if (entry.FilePath != null)
                {
                    // 로컬 파일 (애니메이션 WebP가 아닌 경우 여기로 옴)
                    originalBitmap = await LoadImageFromPathAsync(entry.FilePath, canvas);
                }
                else if (entry.IsArchiveEntry && (_currentArchive != null || _current7zArchive != null))
                {
                    // [중요] 압축 파일 내 이미지는 WebP 여부 상관없이 여기서 로드 (Win2D LoadAsync 사용)
                    originalBitmap = await LoadImageFromArchiveEntryAsync(entry.ArchiveEntryKey!, canvas, token);
                }
                else if (entry.IsWebDavEntry && _isWebDavMode)
                {
                    // WebDAV file not yet downloaded
                    try
                    {
                        var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            entry.FilePath = tempPath;
                            originalBitmap = await LoadImageFromPathAsync(tempPath, canvas);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error downloading WebDAV image for display: {ex.Message}");
                    }
                }

                // 3. 로드 실패 시 null 반환
                if (originalBitmap == null) return null;

                // 4. 샤픈 효과 적용
                if (_sharpenEnabled && !entry.IsPdfEntry)
                {
                    var sharpened = _imageCache.GetSharpenedImage(entryIndex);
                    if (sharpened != null) return sharpened;

                    sharpened = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);
                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        _imageCache.CacheSharpenedImage(entryIndex, sharpened, _currentIndex);
                        _imageCache.SafeDisposeBitmap(originalBitmap); // Dispose original as we now have sharpened version
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
            _imageCache.ClearSharpenedCache(_currentBitmap, _leftBitmap, _rightBitmap);

            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values)
                {
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        _imageCache.SafeDisposeBitmap(bmp);
                    }
                }
                _animatedWebpSharpenedCache.Clear();
            }

            // EPUB 및 텍스트 모드 이미지 캐시 초기화
            foreach (var bmp in _epubImageCache.Values)
                if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
            _epubImageCache.Clear();

            foreach (var bmp in _verticalImageCache.Values)
                if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
            _verticalImageCache.Clear();

            foreach (var bmp in _aozoraImageCache.Values)
                if (bmp != null) _imageCache?.SafeDisposeBitmap(bmp);
            _aozoraImageCache.Clear();

            if (_isEpubMode)
            {
                if (CurrentEpubWin2DPage?.IsImagePage == true)
                {
                    ShowEpubImagePage(CurrentEpubWin2DPage);
                }
                else
                {
                    EpubTextCanvas?.Invalidate();
                }
            }

            if (_isVerticalMode) VerticalTextCanvas?.Invalidate();
            if (_isAozoraMode) AozoraTextCanvas?.Invalidate();

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
                // 활성화 시 글자를 흰색으로 설정하여 가독성 확보
                SharpenButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                
                // 배경색은 Accent 색상으로 강조 (Win2D 연산량을 고려하여 UI로만 표시)
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) &&
                    accent is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    SharpenButton.Background = brush;
                }
            }
            else
            {
                SharpenIcon.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                SharpenButton.ClearValue(Control.ForegroundProperty);
                SharpenButton.ClearValue(Control.BackgroundProperty);
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
                if (_isEpubMode)
                {
                    _ = LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: _currentVerticalPageInfo.StartLine);
                }
                else
                {
                    int currentLine = _currentVerticalPageInfo.StartLine;
                    _ = PrepareVerticalTextAsync(currentLine);
                }
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
                int currentLine = _currentVerticalPageInfo.StartLine;
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
                NextImageSideText.Glyph = "\uE111"; // Next/Forward glyph (left to right)
            }
            else
            {
                NextImageSideText.Glyph = "\uE112"; // Back/Previous glyph (right to left)
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

        private void AutoDoublePageForArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _autoDoublePageForArchive = AutoDoublePageForArchiveMenuItem.IsChecked;
            SaveWindowSettings();
            _ = DisplayCurrentImageAsync();
        }

        private async Task<CanvasBitmap?> LoadImageFromPathAsync(string filePath, CanvasControl canvas)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                return await CanvasBitmap.LoadAsync(canvas, stream, 96.0f);
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

    // [스크롤 버벅임 최적화] 이미 백그라운드에서 압축 해제하여 파일이 존재한다면 Lock 없이 즉시 로드
    var imageEntry = _imageEntries.FirstOrDefault(e => e.ArchiveEntryKey == entryKey);
    if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
    {
        return await LoadImageFromPathAsync(imageEntry.FilePath, canvas);
    }

    using var memoryStream = new MemoryStream();

    // 1. [Lock 구간] 아카이브에서 데이터만 빠르게 메모리로 복사
    await _archiveLock.WaitAsync(token);
    try
    {
        // [Race Condition 방지] Lock을 기다리는 사이에 백그라운드 스레드가 압축을 풀었을 수 있음
        if (imageEntry != null && !string.IsNullOrEmpty(imageEntry.FilePath) && File.Exists(imageEntry.FilePath))
        {
            return await LoadImageFromPathAsync(imageEntry.FilePath, canvas);
        }

        if (_currentArchive != null)
        {
            var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
            if (archiveEntry == null) return null;

            using var entryStream = archiveEntry.OpenEntryStream();
            await entryStream.CopyToAsync(memoryStream, token);
        }
        else if (_current7zArchive != null)
        {
            var archiveEntry = _current7zArchive.Entries.FirstOrDefault(e => e.FileName == entryKey);
            if (archiveEntry == null) return null;

            archiveEntry.Extract(memoryStream);
        }
        else
        {
            return null;
        }
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
        return await CanvasBitmap.LoadAsync(canvas, memoryStream.AsRandomAccessStream(), 96.0f);
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
                if (_currentArchive != null)
                {
                    var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                    if (archiveEntry == null) return null;

                    using var entryStream = archiveEntry.OpenEntryStream();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream, token);
                    return memoryStream.ToArray();
                }
                else if (_current7zArchive != null)
                {
                    var archiveEntry = _current7zArchive.Entries.FirstOrDefault(e => e.FileName == entryKey);
                    if (archiveEntry == null) return null;

                    using var memoryStream = new MemoryStream();
                    archiveEntry.Extract(memoryStream);
                    return memoryStream.ToArray();
                }
                return null;
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

            bool shouldShowSideBySide = _isCurrentViewSideBySide && 
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


        private void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap)
        {
            FileNameText.Text = FileExplorerService.GetFormattedDisplayName(entry.DisplayName, entry.IsArchiveEntry, _currentArchivePath, _isWebDavMode ? _currentWebDavItemPath : null);
            if (bitmap != null && bitmap.Device != null)
                ImageInfoText.Text = $"{(int)bitmap.Size.Width} × {(int)bitmap.Size.Height}";
            else
                ImageInfoText.Text = "";
            TextProgressText.Text = ""; // Clear for image mode

            if (_isCurrentViewSideBySide && _currentPdfDocument == null)
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
            _lastCanvasWidth = e.NewSize.Width;

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
            var isHorizontal = properties.IsHorizontalMouseWheel;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                if (_currentPdfDocument != null && _currentBitmap != null)
                {
                    // 마우스 휠이나 터치패드 핀치의 불연속적인 델타값을 스무스하게 보간하기 위해 애니메이션 사용
                    double zoomMultiplier = Math.Exp(wheelDelta * 0.001); 
                    var pt = e.GetCurrentPoint(ImageArea).Position;
                    StartSmoothZoom(zoomMultiplier, pt);

                    e.Handled = true;
                    return;
                }
            }

            if (_currentPdfDocument != null && _currentBitmap != null)
            {
                // [핵심] 강제로 80 단위로 움직이던 코드를 제거하고, 터치패드의 부드러운 실제 값을 그대로 전달합니다.
                // 가로 스크롤(스와이프)도 함께 지원하도록 분기합니다.
                if (isHorizontal)
                {
                    await HandlePdfScrollAsync(wheelDelta, 0);
                }
                else
                {
                    await HandlePdfScrollAsync(0, wheelDelta);
                }
                e.Handled = true;
                return;
            }

            // 일반 이미지 내비게이션 (단일 이미지 모드)
            // 터치패드의 미세한 움직임에 의해 페이지가 너무 훅훅 넘어가는 것을 막기 위한 안전장치(Threshold)
            if (Math.Abs(wheelDelta) >= 40)
            {
                if (wheelDelta < 0) await NavigateToNextAsync();
                else await NavigateToPreviousAsync();
            }

            e.Handled = true;
        }

        private async void ImageArea_ManipulationDelta(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            if (_currentPdfDocument == null || _currentBitmap == null) return;

            // 1. 핀치 줌 처리
            if (e.Delta.Scale != 1.0f)
            {
                ZoomPdfAtPosition(e.Delta.Scale, e.Position);
            }

            // 2. 드래그/스와이프 스크롤 처리
            await HandlePdfScrollAsync(e.Delta.Translation.X, e.Delta.Translation.Y);

            e.Handled = true;
        }

        private void ImageArea_ManipulationCompleted(object sender, Microsoft.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
        {
            _isPdfTransitioning = false;
            if (_currentPdfDocument != null)
            {
                _ = RerenderPdfCurrentPageAsync();
            }
        }

        private void ZoomPdfAtPosition(double zoomMultiplier, Windows.Foundation.Point position)
        {
            if (_currentPdfDocument == null || _currentBitmap == null) return;
            var canvasSize = MainCanvas.Size;
            var imageSize = _currentBitmap.Size;
            if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;

            var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);

            var oldScaledW = imageSize.Width * fitRatio * _zoomLevel;
            var oldScaledH = imageSize.Height * fitRatio * _zoomLevel;

            double oldVisualLeft = (canvasSize.Width - oldScaledW) / 2 + _pdfPanX;
            double oldVisualTop = (canvasSize.Height - oldScaledH) / 2 + _pdfPanY;

            double normX = (position.X - oldVisualLeft) / _zoomLevel;
            double normY = (position.Y - oldVisualTop) / _zoomLevel;

            double newZoom = Math.Clamp(_zoomLevel * zoomMultiplier, Services.ZoomService.MinZoom, Services.ZoomService.MaxZoom);
            if (newZoom == _zoomLevel) return;
            _zoomLevel = newZoom;

            var newScaledW = imageSize.Width * fitRatio * _zoomLevel;
            var newScaledH = imageSize.Height * fitRatio * _zoomLevel;

            double newVisualLeft = position.X - (normX * _zoomLevel);
            double newVisualTop = position.Y - (normY * _zoomLevel);

            _pdfPanX = newVisualLeft - (canvasSize.Width - newScaledW) / 2;
            _pdfPanY = newVisualTop - (canvasSize.Height - newScaledH) / 2;

            double maxPanX = Math.Max(0, (newScaledW - canvasSize.Width) / 2);
            double maxPanY = Math.Max(0, (newScaledH - canvasSize.Height) / 2);

            _pdfPanX = Math.Clamp(_pdfPanX, -maxPanX, maxPanX);
            _pdfPanY = Math.Clamp(_pdfPanY, -maxPanY, maxPanY);

            ApplyZoom();
        }

        private DispatcherQueueTimer? _smoothZoomTimer;
        private double _targetZoomLevel = 1.0;
        private Windows.Foundation.Point _zoomPivot;

        private void StartSmoothZoom(double targetMultiplier, Windows.Foundation.Point pivot)
        {
            if (_smoothZoomTimer == null)
            {
                _smoothZoomTimer = DispatcherQueue.CreateTimer();
                _smoothZoomTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
                _smoothZoomTimer.Tick += SmoothZoomTimer_Tick;
            }

            // 첫 줌 시작이거나 타이머가 멈춰있으면 목표값을 현재 줌부터 시작
            if (!_smoothZoomTimer.IsRunning || Math.Abs(_zoomLevel - _targetZoomLevel) < 0.001)
            {
                _targetZoomLevel = _zoomLevel;
            }

            _targetZoomLevel = Math.Clamp(_targetZoomLevel * targetMultiplier, Services.ZoomService.MinZoom, Services.ZoomService.MaxZoom);
            _zoomPivot = pivot;

            if (!_smoothZoomTimer.IsRunning)
            {
                _smoothZoomTimer.Start();
            }
        }

        private void SmoothZoomTimer_Tick(object? sender, object e)
        {
            if (_currentPdfDocument == null || _currentBitmap == null)
            {
                _smoothZoomTimer?.Stop();
                return;
            }

            if (Math.Abs(_zoomLevel - _targetZoomLevel) < 0.005)
            {
                ZoomPdfAtPosition(_targetZoomLevel / _zoomLevel, _zoomPivot);
                _smoothZoomTimer?.Stop();
                _ = RerenderPdfCurrentPageAsync();
                return;
            }

            // Lerp (목표 줌의 30%씩 부드럽게 접근)
            double nextZoom = _zoomLevel + (_targetZoomLevel - _zoomLevel) * 0.3;
            ZoomPdfAtPosition(nextZoom / _zoomLevel, _zoomPivot);
        }

        private async Task HandlePdfScrollAsync(double deltaX, double deltaY)
        {
            var bitmap = _currentBitmap;
            if (_currentPdfDocument == null || bitmap == null || bitmap.Device == null || _isPdfTransitioning) return;

            try
            {
                var canvasSize = MainCanvas.Size;
                if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;

                var imageSize = bitmap.Size;
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
                        int targetPrevIndex = _currentIndex - 1;
                        CanvasBitmap? prev = _imageCache.GetPreloadedImage(targetPrevIndex, _zoomLevel);

                        var oldPosNextTop = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;
                        int oldIndex = _currentIndex;
                        _imageCache.UpdateCache(oldIndex, bitmap, true, _zoomLevel, false, _currentBitmap);

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
                                UpdateStatusBar(prevEntry, _currentBitmap);
                                MainCanvas.Invalidate();

                                _ = _preloadManager.StartPreloadAsync(
                                    _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                    _currentBitmap, _leftBitmap, _rightBitmap,
                                    LoadBitmapForPreloadAsync,
                                    () => MainCanvas?.Invalidate(),
                                    prioritizeNext: false);
                            }
                            catch
                            {
                                _isPdfTransitioning = false;
                                return;
                            }
                        }
                        else
                        {
                            try
                            {
                                var token = _imageLoadingCts?.Token ?? default;
                                prev = await LoadPdfPageBitmapAsync((uint)targetPrevIndex, MainCanvas, token);
                                if (prev != null)
                                {
                                    _imageCache.UpdateCache(targetPrevIndex, prev, true, _zoomLevel, false, _currentBitmap);

                                    var pFit = Math.Min(canvasSize.Width / prev.Size.Width, canvasSize.Height / prev.Size.Height);
                                    var pScaledH = prev.Size.Height * pFit * _zoomLevel;
                                    _pdfPanY = (oldPosNextTop - gap - pScaledH) - (canvasSize.Height - pScaledH) / 2;

                                    _currentBitmap = prev;
                                    var prevEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(prevEntry, _currentBitmap);
                                    MainCanvas.Invalidate();

                                    _ = _preloadManager.StartPreloadAsync(
                                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                        _currentBitmap, _leftBitmap, _rightBitmap,
                                        LoadBitmapForPreloadAsync,
                                        () => MainCanvas?.Invalidate(),
                                        prioritizeNext: false);
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
                        int targetNextIndex = _currentIndex + 1;
                        CanvasBitmap? next = _imageCache.GetPreloadedImage(targetNextIndex, _zoomLevel);

                        var oldPosPrevBottom = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY + scaledSize.Height;
                        int oldIndex = _currentIndex;
                        _imageCache.UpdateCache(oldIndex, bitmap, true, _zoomLevel, false, _currentBitmap);

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
                                UpdateStatusBar(nextEntry, _currentBitmap);
                                MainCanvas.Invalidate();

                                _ = _preloadManager.StartPreloadAsync(
                                    _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                    _currentBitmap, _leftBitmap, _rightBitmap,
                                    LoadBitmapForPreloadAsync,
                                    () => MainCanvas?.Invalidate(),
                                    prioritizeNext: true);
                            }
                            catch
                            {
                                _isPdfTransitioning = false;
                                return;
                            }
                        }
                        else
                        {
                            try
                            {
                                var token = _imageLoadingCts?.Token ?? default;
                                next = await LoadPdfPageBitmapAsync((uint)targetNextIndex, MainCanvas, token);
                                if (next != null)
                                {
                                    _imageCache.UpdateCache(targetNextIndex, next, true, _zoomLevel, false, _currentBitmap);

                                    var nFit = Math.Min(canvasSize.Width / next.Size.Width, canvasSize.Height / next.Size.Height);
                                    var nScaledH = next.Size.Height * nFit * _zoomLevel;
                                    _pdfPanY = (oldPosPrevBottom + gap) - (canvasSize.Height - nScaledH) / 2;

                                    _currentBitmap = next;
                                    var nextEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(nextEntry, _currentBitmap);
                                    MainCanvas.Invalidate();

                                    _ = _preloadManager.StartPreloadAsync(
                                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                        _currentBitmap, _leftBitmap, _rightBitmap,
                                        LoadBitmapForPreloadAsync,
                                        () => MainCanvas?.Invalidate(),
                                        prioritizeNext: true);
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

        private void ClearImageResources()
        {
            _imageLoadingCts?.Cancel();
            _preloadManager.CancelAll();

            _imageCache?.ClearAll();

            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values) _imageCache?.SafeDisposeBitmap(bmp);
                _animatedWebpSharpenedCache.Clear();
            }

            _currentBitmap = null;
            _leftBitmap = null;
            _rightBitmap = null;

            StopAnimatedWebp();

            if (MainCanvas != null) MainCanvas.Invalidate();
            if (LeftCanvas != null) LeftCanvas.Invalidate();
            if (RightCanvas != null) RightCanvas.Invalidate();

            if (FileNameText != null) FileNameText.Text = "";
            if (ImageInfoText != null) ImageInfoText.Text = "";
            if (ImageIndexText != null) ImageIndexText.Text = "";
        }

        #endregion


    }
}

