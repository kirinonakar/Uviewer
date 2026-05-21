using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            if (_isCurrentViewSideBySide && _currentPdfDocument == null) return;

            if (CanvasBitmapHelper.TryGetBitmapSize(_currentBitmap, out var bitmapSize))
            {
                var containerWidth = ImageArea.ActualWidth;
                var containerHeight = ImageArea.ActualHeight;

                if (containerWidth > 0 && containerHeight > 0)
                {
                    _zoomService.CalculateActualZoom(containerWidth, containerHeight, bitmapSize.Width, bitmapSize.Height, MainCanvas.Dpi / 96.0f, _currentPdfDocument != null);
                    ApplyZoom();
                }
            }
        }

        private void ZoomIn()
        {
            if (_isCurrentViewSideBySide && _currentPdfDocument == null) return;
            _zoomService.ZoomIn();
            ApplyZoom();
        }

        private void ZoomOut()
        {
            if (_isCurrentViewSideBySide && _currentPdfDocument == null) return;
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
            if (!CanvasBitmapHelper.IsUsable(_currentBitmap) || ImageArea.ActualWidth <= 0 || ImageArea.ActualHeight <= 0)
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
            MainToolbar.SetZoomLevel(_zoomLevel);

            if (_currentPdfDocument != null && !(_smoothZoomTimer?.IsRunning ?? false))
            {
                _ = RerenderPdfCurrentPageAsync();
            }
        }

        #endregion

        #region Image Display

        private async Task DisplayCurrentImageAsync()
        {
            try
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
                // [안정성 수정] 이전 CTS를 Dispose하여 빠른 페이지 넘김 시 리소스 누수 방지
                var oldCts = _imageLoadingCts;
                _imageLoadingCts = new CancellationTokenSource();
                var token = _imageLoadingCts.Token; // <-- 이 토큰을 전달해야 함
                oldCts?.Cancel();
                oldCts?.Dispose();

                // 아카이브 탐색 시 백그라운드 추출 위치 재조정 신호 전송 (큰 폭의 이동 시에만)
                if (_archiveSession.IsSevenZipArchive)
                {
                    if (_sevenZipExtraction.ShouldSignalJump(_currentIndex, 2))
                    {
                        Signal7zJump();
                    }
                }
                else
                {
                    _sevenZipExtraction.MarkCurrentIndex(_currentIndex);
                }

                _animatedWebpService.Stop();

                var entry = _imageEntries[_currentIndex];

                // PDF 전용 처리 블록: 잔상 제거 및 줌 레벨 맞춤 캐시 적용
                if (entry.IsPdfEntry && _currentPdfDocument != null)
                {
                    SwitchToImageMode();
                    _isCurrentViewSideBySide = false;
                    CanvasBitmap? nextBitmap = null;

                    nextBitmap = _imageCache.GetPreloadedImage(_currentIndex, _zoomLevel);

                    // 2. 캐시에 없어서 새로 렌더링해야 하는 경우 (잔상 제거 로직 포함)
                    if (nextBitmap == null)
                    {
                        // 새 페이지를 그리는 동안 예전 페이지가 멈춰 있는 현상을 막기 위해
                        // 현재 비트맵을 즉시 비우고 화면을 갱신합니다.
                        var tempOldBitmap = _currentBitmap;
                        _currentBitmap = null;
                        MainCanvas?.Invalidate();

                        if (tempOldBitmap != null && !IsBitmapInCache(tempOldBitmap))
                        {
                            _imageCache.SafeDisposeBitmap(tempOldBitmap);
                        }

                        // 렌더링 시작 (토큰 전달로 페이지 이동 시 취소 가능하게 함)
                        nextBitmap = await LoadPdfPageBitmapAsync(entry.PdfPageIndex, MainCanvas!, token);

                        // [핵심] await 후 인덱스가 달라졌으면 버리기 (다른 페이지로 넘어간 것임)
                        if (nextBitmap != null)
                        {
                            if (token.IsCancellationRequested || _currentIndex != capturedIndexAtStart)
                            {
                                _imageCache.SafeDisposeBitmap(nextBitmap);
                                return;
                            }

                            _imageCache.UpdateCache(capturedIndexAtStart, nextBitmap, true, _zoomLevel, _currentBitmap);
                        }
                    }

                    // 3. 확보된 이미지를 화면에 표시
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
                            var canvasSize = MainCanvas!.Size;
                            _pdfPanY = ZoomService.CalculateInitialVerticalPan(
                                canvasSize,
                                nextBitmap.Size,
                                _zoomLevel,
                                _pdfScrollDirection);
                            _pdfPanX = 0;
                            _isPdfTransitioning = false;
                        }

                        MainCanvas?.Invalidate();
                        ShowImageUI();
                        UpdateStatusBar(entry, _currentBitmap);

                        // [수정] PDF 페이지 이동 시 캐시된 이미지가 저해상도일 수 있으므로(예: 이전에 작은 창 크기에서 프리로드됨)
                        // 현재 해상도와 줌 레벨에 맞춰 비동기로 한 번 더 고해상도 렌더링을 수행하여 항상 선명한 환경을 유지합니다.
                        _ = RerenderPdfCurrentPageAsync();

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

                    bool canSideBySide = await _imageDoublePageDecisionService.ShouldUseSideBySideAsync(
                        _imageEntries,
                        _currentIndex,
                        _isSideBySideMode,
                        _autoDoublePageForArchive,
                        _archiveSession.HasArchive,
                        _currentPdfDocument != null,
                        _zoomLevel,
                        _currentBitmap,
                        LoadBitmapForPreloadAsync,
                        token);

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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DisplayCurrentImageAsync: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
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
                    string targetPath = entry.IsArchiveEntry ? (_archiveSession.CurrentPath ?? "") : (entry.FilePath ?? "");
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
            _isAnimatedFrameActive = false;
            _animatedWebpService.Stop(); // 기존 애니메이션 중단

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
                    // 확대 상태가 아니라면 FitToWindow()를 수행하여 초기 상태로 맞춤
                    if (_zoomLevel <= 1.01)
                    {
                        _zoomLevel = 1.0;
                        FitToWindow();
                    }

                    var canvasSize = MainCanvas.Size;
                    _pdfPanY = ZoomService.CalculateInitialVerticalPan(
                        canvasSize,
                        bitmap.Size,
                        _zoomLevel,
                        _pdfScrollDirection);
                    _pdfPanX = 0;
                    ShowImageUI();
                    UpdateStatusBar(entry, _currentBitmap);
                    UpdateSharpenButtonState();
                    MainCanvas?.Invalidate();
 
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
                if (_animatedWebpService.IsAnimationSupported(entry))
                {
                    // 로딩 표시 추가
                    FileNameText.Text += Strings.Loading;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            await _animatedWebpService.StartAsync(entry, MainCanvas!, token, (float)ImageOptions.UpscaleFactor, (float)ImageOptions.SharpenAmount, (float)ImageOptions.SharpenThreshold, (float)ImageOptions.UnsharpAmount, (float)ImageOptions.UnsharpRadius, _sharpenEnabled);
                            
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    UpdateStatusBar(entry, _currentBitmap!);
                                }
                            });
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

            if (_animatedWebpService.IsBitmapInCache(bitmap)) return true;
            return false;
        }

        private void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap)
        {
            _isAnimatedFrameActive = true;
            var oldBitmap = _currentBitmap;
            _currentBitmap = newBitmap;

            if (oldBitmap != null && oldBitmap != newBitmap && !IsBitmapInCache(oldBitmap))
            {
                _imageCache.SafeDisposeBitmap(oldBitmap);
            }

            MainCanvas?.Invalidate();
        }

        private void OnAnimatedWebpAnimationStopped(object? sender, EventArgs e)
        {
            try
            {
                _isAnimatedFrameActive = false;
                var bitmap = _currentBitmap;
                if (bitmap != null && _animatedWebpService.IsBitmapInCache(bitmap))
                {
                    _currentBitmap = null;
                    MainCanvas?.Invalidate();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping animated image: {ex.Message}");
            }
        }

        private async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            try
            {
                var pair = await _sideBySideImageLoadService.LoadAsync(
                    _imageEntries,
                    _currentIndex,
                    _nextImageOnRight,
                    LeftCanvas,
                    RightCanvas,
                    LoadImageBitmapAsync,
                    ReleaseBitmapIfUnused,
                    token);

                if (pair == null || token.IsCancellationRequested)
                {
                    return;
                }

                // 1. 옛날 이미지를 임시 변수에 담아둠
                var oldLeft = _leftBitmap;
                var oldRight = _rightBitmap;

                // 2. 현재 이미지를 새것으로 '먼저' 교체
                _leftBitmap = pair.LeftBitmap;
                _rightBitmap = pair.RightBitmap;
                _currentBitmap = pair.RightBitmap ?? pair.LeftBitmap; // For zoom calculations

                // 3. UI 갱신 요청
                _zoomLevel = 1.0;
                FitToWindow();

                ShowImageUI();

                // 상태바/사이드바에서 "현재 페이지"는 항상 _currentIndex 기준으로 표시되도록 수정
                var primaryEntry = _imageEntries[_currentIndex];
                CanvasBitmap? primaryBitmap = pair.PrimaryBitmap ?? _currentBitmap;

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
                if (oldLeft != null && !IsBitmapInCache(oldLeft) && oldLeft != pair.LeftBitmap && oldLeft != pair.RightBitmap)
                {
                    _imageCache.SafeDisposeBitmap(oldLeft);
                }
                if (oldRight != null && !IsBitmapInCache(oldRight) && oldRight != pair.LeftBitmap && oldRight != pair.RightBitmap)
                {
                    _imageCache.SafeDisposeBitmap(oldRight);
                }
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"이미지 로드 실패: {ex.Message}";
            }
        }

        private void ReleaseBitmapIfUnused(CanvasBitmap? bitmap)
        {
            if (bitmap != null && !IsBitmapInCache(bitmap))
            {
                _imageCache.SafeDisposeBitmap(bitmap);
            }
        }

        private Services.ImageBitmapLoaderContext CreateImageBitmapLoaderContext()
            => new(
                ImageEntries: _imageEntries,
                CurrentIndex: _currentIndex,
                ZoomLevel: _zoomLevel,
                SharpenEnabled: _sharpenEnabled,
                SharpenParams: CreateSharpenParams(),
                IsPdfMode: _currentPdfDocument != null,
                IsWebDavMode: _isWebDavMode,
                ArchiveSession: _archiveSession,
                WebDavService: _webDavService,
                MainCanvas: MainCanvas,
                LoadPdfPageBitmapAsync: (pageIndex, canvas, token, isPreload) =>
                    LoadPdfPageBitmapAsync(pageIndex, canvas, token, isPreload),
                InvalidateCanvas: () => MainCanvas?.Invalidate());

        private Task<CanvasBitmap?> LoadImageBitmapAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token = default)
            => _imageBitmapLoader.LoadImageBitmapAsync(entry, canvas, CreateImageBitmapLoaderContext(), token);

        private async void SharpenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _sharpenEnabled = !_sharpenEnabled;

                // [추가] 샤픈 옵션을 바꿀 때 캐시를 초기화하여 충돌 방지
                _imageCache.ClearSharpenedCache(_currentBitmap, _leftBitmap, _rightBitmap);

                _animatedWebpService.Stop();

                // EPUB 및 텍스트 모드 이미지 캐시 통합 초기화
                _imageResourceService.Clear();

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
                _windowSettingsCoordinator.SaveWindowSettings();

                // 이미지 다시 로드
                await DisplayCurrentImageAsync();

                // [추가] 샤프닝 토글 시 주변 이미지들도 즉시 샤프닝 작업 시작 (스크롤 시 부분적으로 보이는 이미지용)
                if (_imageEntries != null && _imageEntries.Count > 0)
                {
                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: true,
                        requireSharpening: _sharpenEnabled);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SharpenButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal void UpdateSharpenButtonState()
        {
            MainToolbar.SetSharpenState(_sharpenEnabled);
        }

        private void SideBySideButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfDocument != null) return;
            _isSideBySideMode = !_isSideBySideMode;

            UpdateSideBySideButtonState();
            _windowSettingsCoordinator.SaveWindowSettings();

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
            _windowSettingsCoordinator.SaveWindowSettings();

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

        internal void UpdateSideBySideButtonState()
        {
            MainToolbar.SetSideBySideState(_isSideBySideMode);
        }

        internal void UpdateNextImageSideButtonState()
        {
            MainToolbar.SetNextImageSideState(_nextImageOnRight);
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
            var content = _imageStatusBarService.Create(
                entry,
                bitmap,
                _archiveSession.CurrentPath,
                _isWebDavMode ? _currentWebDavItemPath : null,
                _isCurrentViewSideBySide,
                _currentPdfDocument != null,
                _currentIndex,
                _imageEntries.Count);

            FileNameText.Text = content.FileName;
            ImageInfoText.Text = content.ImageInfo;
            ImageIndexText.Text = content.ImageIndex;
            TextProgressText.Text = content.TextProgress;
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
            try
            {
                var properties = e.GetCurrentPoint(ImageArea).Properties;
                var wheelDelta = properties.MouseWheelDelta;
                var isHorizontal = properties.IsHorizontalMouseWheel;

                var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    if (_currentBitmap != null && (!_isCurrentViewSideBySide || _currentPdfDocument != null))
                    {
                        // 마우스 휠이나 터치패드 핀치의 불연속적인 델타값을 스무스하게 보간하기 위해 애니메이션 사용
                        double zoomMultiplier = Math.Exp(wheelDelta * 0.001); 
                        var pt = e.GetCurrentPoint(ImageArea).Position;
                        StartSmoothZoom(zoomMultiplier, pt);

                        e.Handled = true;
                        return;
                    }
                }

                // [핵심] PDF이거나 1장 보기 상태에서 확대된 이미지는 터치패드의 세밀한 델타값을 그대로 반영하여 부드럽게 스크롤되게 합니다.
                if (_currentBitmap != null && (_currentPdfDocument != null || (_zoomLevel > 1.01 && !_isCurrentViewSideBySide)))
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

                if (Math.Abs(wheelDelta) >= 40)
                {
                    if (wheelDelta < 0) await NavigateToNextAsync();
                    else await NavigateToPreviousAsync();
                }

                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerWheelChanged: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void ImageArea_ManipulationStarting(object sender, Microsoft.UI.Xaml.Input.ManipulationStartingRoutedEventArgs e)
        {
            e.Container = ImageArea;
            e.Mode = Microsoft.UI.Xaml.Input.ManipulationModes.All;
        }

        private async void ImageArea_ManipulationDelta(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
                if (_currentBitmap == null || (_isCurrentViewSideBySide && _currentPdfDocument == null)) return;

                // 1. 핀치 줌 처리
                if (e.Delta.Scale != 1.0f)
                {
                    ZoomPdfAtPosition(e.Delta.Scale, e.Position);
                }

                // 2. 드래그/스와이프 스크롤 처리
                await HandlePdfScrollAsync(e.Delta.Translation.X, e.Delta.Translation.Y);

                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_ManipulationDelta: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
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
            var bitmap = _currentBitmap;
            if (bitmap == null) return;
            var canvasSize = MainCanvas.Size;
            if (!CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize)) return;

            var transform = ZoomService.CalculateZoomAtPosition(
                canvasSize,
                imageSize,
                _zoomLevel,
                _pdfPanX,
                _pdfPanY,
                zoomMultiplier,
                position);
            if (!transform.HasValue) return;

            _zoomLevel = transform.Value.ZoomLevel;
            _pdfPanX = transform.Value.PanX;
            _pdfPanY = transform.Value.PanY;
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
            if (_currentBitmap == null)
            {
                _smoothZoomTimer?.Stop();
                return;
            }

            if (Math.Abs(_zoomLevel - _targetZoomLevel) < 0.005)
            {
                ZoomPdfAtPosition(_targetZoomLevel / _zoomLevel, _zoomPivot);
                _smoothZoomTimer?.Stop();
                if (_currentPdfDocument != null) _ = RerenderPdfCurrentPageAsync();
                return;
            }

            // Lerp (목표 줌의 30%씩 부드럽게 접근)
            double nextZoom = _zoomLevel + (_targetZoomLevel - _zoomLevel) * 0.3;
            ZoomPdfAtPosition(nextZoom / _zoomLevel, _zoomPivot);
        }

        private async Task HandlePdfScrollAsync(double deltaX, double deltaY)
        {
            var bitmap = _currentBitmap;
            // PDF가 아니어도 확대된 일반 이미지라면 연속 스크롤 지원
            if ((_currentPdfDocument == null && _zoomLevel <= 1.01) || !CanvasBitmapHelper.TryGetBitmapSize(bitmap, out var imageSize) || _isPdfTransitioning) return;

            try
            {
                var canvasSize = MainCanvas.Size;
                if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;

                var scaledSize = ZoomService.CalculateScaledSize(canvasSize, imageSize, _zoomLevel);

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

                    int targetPrevIndex = FileExplorerService.GetNextImageIndex(_imageEntries, _currentIndex, 1, false);
                    // 이전 페이지로 전환
                    if (_pdfPanY > maxPanY + 1 && targetPrevIndex != _currentIndex)
                    {
                        _isPdfTransitioning = true;
                        CanvasBitmap? prev = (_sharpenEnabled && _currentPdfDocument == null) ? _imageCache.GetSharpenedImage(targetPrevIndex) : null;
                        if (prev == null) prev = _imageCache.GetPreloadedImage(targetPrevIndex, _zoomLevel);

                        var oldPosNextTop = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY;
                        int oldIndex = _currentIndex;
                        _imageCache.UpdateCache(oldIndex, bitmap, true, _zoomLevel, _currentBitmap);

                        _currentIndex = targetPrevIndex;

                        if (CanvasBitmapHelper.TryGetBitmapSize(prev, out var prevSize))
                        {
                            try
                            {
                                var pFit = Math.Min(canvasSize.Width / prevSize.Width, canvasSize.Height / prevSize.Height);
                                var pScaledH = prevSize.Height * pFit * _zoomLevel;
                                _pdfPanY = (oldPosNextTop - gap - pScaledH) - (canvasSize.Height - pScaledH) / 2;

                                _currentBitmap = prev;
                                var prevEntry = _imageEntries[_currentIndex];
                                UpdateStatusBar(prevEntry, _currentBitmap);
                                SyncSidebarSelection(prevEntry);
                                MainCanvas?.Invalidate();

                                _ = _preloadManager.StartPreloadAsync(
                                    _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                    _currentBitmap, _leftBitmap, _rightBitmap,
                                    LoadBitmapForPreloadAsync,
                                    () => MainCanvas?.Invalidate(),
                                    prioritizeNext: false,
                                    requireSharpening: _sharpenEnabled);
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
                                if (_currentPdfDocument != null)
                                {
                                    prev = await LoadPdfPageBitmapAsync((uint)targetPrevIndex, MainCanvas, token);
                                }
                                else
                                {
                                    var entry = _imageEntries[targetPrevIndex];
                                    prev = await LoadImageBitmapAsync(entry, MainCanvas, token);
                                }

                                if (prev != null)
                                {
                                    _imageCache.UpdateCache(targetPrevIndex, prev, true, _zoomLevel, _currentBitmap);

                                    if (!CanvasBitmapHelper.TryGetBitmapSize(prev, out var loadedPrevSize)) return;
                                    var pFit = Math.Min(canvasSize.Width / loadedPrevSize.Width, canvasSize.Height / loadedPrevSize.Height);
                                    var pScaledH = loadedPrevSize.Height * pFit * _zoomLevel;
                                    _pdfPanY = (oldPosNextTop - gap - pScaledH) - (canvasSize.Height - pScaledH) / 2;

                                    _currentBitmap = prev;
                                    var prevEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(prevEntry, _currentBitmap);
                                    SyncSidebarSelection(prevEntry);
                                    MainCanvas?.Invalidate();

                                    _ = _preloadManager.StartPreloadAsync(
                                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                        _currentBitmap, _leftBitmap, _rightBitmap,
                                        LoadBitmapForPreloadAsync,
                                        () => MainCanvas?.Invalidate(),
                                        prioritizeNext: false,
                                        requireSharpening: _sharpenEnabled);
                                }
                            }
                            catch { }
                        }
                        _isPdfTransitioning = false;
                        return;
                    }

                    if (targetPrevIndex == _currentIndex && _pdfPanY > maxPanY) _pdfPanY = maxPanY;
                }
                else if (deltaY < 0) // 아래로 스크롤 (다음 페이지로)
                {
                    _pdfPanY += deltaY;

                    int targetNextIndex = FileExplorerService.GetNextImageIndex(_imageEntries, _currentIndex, 1, true);
                    // 다음 페이지로 전환
                    if (_pdfPanY < -maxPanY - 1 && targetNextIndex != _currentIndex)
                    {
                        _isPdfTransitioning = true;
                        CanvasBitmap? next = (_sharpenEnabled && _currentPdfDocument == null) ? _imageCache.GetSharpenedImage(targetNextIndex) : null;
                        if (next == null) next = _imageCache.GetPreloadedImage(targetNextIndex, _zoomLevel);

                        var oldPosPrevBottom = (canvasSize.Height - scaledSize.Height) / 2 + _pdfPanY + scaledSize.Height;
                        int oldIndex = _currentIndex;
                        _imageCache.UpdateCache(oldIndex, bitmap, true, _zoomLevel, _currentBitmap);

                        _currentIndex = targetNextIndex;

                        if (CanvasBitmapHelper.TryGetBitmapSize(next, out var nextSize))
                        {
                            try
                            {
                                var nFit = Math.Min(canvasSize.Width / nextSize.Width, canvasSize.Height / nextSize.Height);
                                var nScaledH = nextSize.Height * nFit * _zoomLevel;
                                _pdfPanY = (oldPosPrevBottom + gap) - (canvasSize.Height - nScaledH) / 2;

                                _currentBitmap = next;
                                var nextEntry = _imageEntries[_currentIndex];
                                UpdateStatusBar(nextEntry, _currentBitmap);
                                SyncSidebarSelection(nextEntry);
                                MainCanvas?.Invalidate();

                                _ = _preloadManager.StartPreloadAsync(
                                    _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                    _currentBitmap, _leftBitmap, _rightBitmap,
                                    LoadBitmapForPreloadAsync,
                                    () => MainCanvas?.Invalidate(),
                                    prioritizeNext: true,
                                    requireSharpening: _sharpenEnabled);
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
                                if (_currentPdfDocument != null)
                                {
                                    next = await LoadPdfPageBitmapAsync((uint)targetNextIndex, MainCanvas, token);
                                }
                                else
                                {
                                    var entry = _imageEntries[targetNextIndex];
                                    next = await LoadImageBitmapAsync(entry, MainCanvas, token);
                                }

                                if (next != null)
                                {
                                    _imageCache.UpdateCache(targetNextIndex, next, true, _zoomLevel, _currentBitmap);

                                    if (!CanvasBitmapHelper.TryGetBitmapSize(next, out var loadedNextSize)) return;
                                    var nFit = Math.Min(canvasSize.Width / loadedNextSize.Width, canvasSize.Height / loadedNextSize.Height);
                                    var nScaledH = loadedNextSize.Height * nFit * _zoomLevel;
                                    _pdfPanY = (oldPosPrevBottom + gap) - (canvasSize.Height - nScaledH) / 2;

                                    _currentBitmap = next;
                                    var nextEntry = _imageEntries[_currentIndex];
                                    UpdateStatusBar(nextEntry, _currentBitmap);
                                    SyncSidebarSelection(nextEntry);
                                    MainCanvas?.Invalidate();

                                    _ = _preloadManager.StartPreloadAsync(
                                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                                        _currentBitmap, _leftBitmap, _rightBitmap,
                                        LoadBitmapForPreloadAsync,
                                        () => MainCanvas?.Invalidate(),
                                        prioritizeNext: true,
                                        requireSharpening: _sharpenEnabled);
                                }
                            }
                            catch { }
                        }
                        _isPdfTransitioning = false;
                        return;
                    }

                    if (targetNextIndex == _currentIndex && _pdfPanY < -maxPanY) _pdfPanY = -maxPanY;
                }

                ApplyZoom();
            }
            finally
            {
                _isPdfTransitioning = false;
            }
        }

        private async void ImageArea_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                if (_imageEntries.Count <= 1)
                    return;

                var pt = e.GetCurrentPoint(ImageArea);
                if (!pt.Properties.IsLeftButtonPressed)
                    return;

                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
                {
                    // 확대된 상태이거나 PDF 모드인 경우에는 스와이프/팬 제스처를 방해하지 않도록 터치 클릭 내비게이션을 차단합니다.
                    if (_zoomLevel > 1.01 || _currentPdfDocument != null)
                        return;
                }

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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerPressed: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void ClearImageResources()
        {
            _imageLoadingCts?.Cancel();
            _preloadManager.CancelAll();

            _imageCache?.ClearAll();

            _animatedWebpService.Stop();

            _imageViewerState.ClearBitmaps();


            if (MainCanvas != null) MainCanvas.Invalidate();
            if (LeftCanvas != null) LeftCanvas.Invalidate();
            if (RightCanvas != null) RightCanvas.Invalidate();

            if (FileNameText != null) FileNameText.Text = "";
            if (ImageInfoText != null) ImageInfoText.Text = "";
            if (ImageIndexText != null) ImageIndexText.Text = "";
        }

        #endregion


        #region Navigation

        private async Task NavigateToPreviousAsync(bool isManualClick = false)
        {
            await NavigateImageAsync(forward: false, isManualClick);
        }

        private async void OnSharpenParamsChanged()
        {
            try
            {
                // 샤프닝이 켜져있다면 즉시 캐시 지우고 화면 리렌더링
                if (_sharpenEnabled)
                {
                    _imageCache?.ClearSharpenedCache(_currentBitmap, _leftBitmap, _rightBitmap);
                    
                    _animatedWebpService.Stop();

                    // EPUB 및 텍스트 모드 이미지 캐시 통합 초기화
                    _imageResourceService.Clear();
                    
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

                    // 현재 이미지 다시 그리기
                    await DisplayCurrentImageAsync();
                    
                    // [추가] 샤프닝 옵션 변경 시 주변 이미지들도 즉시 새 설정으로 샤프닝 다시 시작
                    if (_imageEntries != null && _imageEntries.Count > 0)
                    {
                        _ = _preloadManager.StartPreloadAsync(
                            _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                            _currentBitmap, _leftBitmap, _rightBitmap,
                            LoadBitmapForPreloadAsync,
                            () => MainCanvas?.Invalidate(),
                            prioritizeNext: true,
                            requireSharpening: _sharpenEnabled);
                    }
                }
                
                // 변경사항 저장
                _windowSettingsCoordinator.SaveWindowSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSharpenParamsChanged: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void SharpenParams_Reset_Click(object sender, RoutedEventArgs e)
        {
            ImageOptions.Reset();
        }

        private async Task NavigateToNextAsync(bool isManualClick = false)
        {
            await NavigateImageAsync(forward: true, isManualClick);
        }

        private async Task NavigateImageAsync(bool forward, bool isManualClick)
        {
            _pdfScrollDirection = forward ? 1 : -1;

            bool canNavigate = forward
                ? _currentIndex < _imageEntries.Count - 1
                : _currentIndex > 0;

            if (canNavigate)
            {
                bool isFast = !isManualClick && _fastNavigationService.DetectFastNavigation(ResetFastNavigation);

                int step = _isCurrentViewSideBySide ? 2 : 1;
                _currentIndex = FileExplorerService.GetNextImageIndex(_imageEntries, _currentIndex, step, forward);

                if (isFast)
                {
                    UpdateFastNavigationUI();
                    return;
                }

                await DisplayCurrentImageAsync();

                await AddToRecentAsync(true);

                // [최적화] 프리로드 재시작 디바운스 (PDF 한정)
                if (_archiveSession.CurrentArchive != null || _currentPdfDocument != null)
                {
                    _ = _preloadManager.StartPreloadAsync(
                        _currentIndex, _imageEntries, _currentPdfDocument != null, _zoomLevel,
                        _currentBitmap, _leftBitmap, _rightBitmap,
                        LoadBitmapForPreloadAsync,
                        () => MainCanvas?.Invalidate(),
                        prioritizeNext: forward,
                        requireSharpening: _sharpenEnabled);
                }
            }

            RootGrid.Focus(FocusState.Programmatic);
        }

        private string? GetCurrentNavigatingPath()
        {
            if (_isWebDavMode) return _currentWebDavItemPath;
            if (_archiveSession.HasArchive && !string.IsNullOrEmpty(_archiveSession.CurrentPath)) return _archiveSession.CurrentPath;
            if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath)) return _currentEpubFilePath;
            if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath)) return _currentTextFilePath;
            if (_imageEntries != null && _imageEntries.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageEntries.Count) return _imageEntries[_currentIndex].FilePath;
            return null;
        }

        private async Task NavigateToFileAsync(bool isNext)
        {
            await AddToRecentAsync(true);
            string? currentPath = GetCurrentNavigatingPath();
            if (string.IsNullOrEmpty(currentPath)) return;

            var nextItem = FileExplorerService.GetNextNavigableFile(_fileItems, currentPath, isNext, _isWebDavMode);
            
            if (nextItem != null)
            {
                // [Optimization] If the next file is already in our loaded image list (same folder), 
                // just jump to its index instead of re-scanning the folder.
                if (_imageEntries != null && _imageEntries.Count > 0 && !nextItem.IsDirectory && !nextItem.IsArchive && !nextItem.IsPdf)
                {
                    int index = _imageEntries.FindIndex(e => e.FilePath == nextItem.FullPath);
                    if (index != -1)
                    {
                        _currentIndex = index;
                        await DisplayCurrentImageAsync();
                        SyncExplorerSelection(nextItem);
                        RootGrid.Focus(FocusState.Programmatic);
                        return;
                    }
                }

                // If it's a different folder, archive, or not in the current list, handle normally (this re-scans if needed)
                await HandleFileSelectionAsync(nextItem);
                SyncExplorerSelection(nextItem);
            }
            
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void SyncExplorerSelection(FileItem item)
        {
            if (_isExplorerGrid)
            {
                FileGridView.SelectedItem = item;
                FileGridView.ScrollIntoView(item);
            }
            else
            {
                FileListView.SelectedItem = item;
                FileListView.ScrollIntoView(item);
            }
        }

        private Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, CancellationToken token)
            => _imageBitmapLoader.LoadBitmapForPreloadAsync(entry, CreateImageBitmapLoaderContext(), token);

        #endregion


    }
}

