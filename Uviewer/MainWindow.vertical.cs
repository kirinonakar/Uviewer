using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Uviewer.Models;
using Uviewer.Services;
using Uviewer.Renderers;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isVerticalMode = false;
        private bool _verticalKeyAttached = false;
        // 이미지 캐시는 _imageResourceService 로 통합됨 (접두어 "text:")
        private int? _pendingVerticalScrollLine = null;
        private int _pendingVerticalStartBlockIndex = -1;

        // 가상화 렌더링을 위한 단일 페이지 캐시 및 네비게이션 상태
        private readonly ReaderPageState _verticalPageState = new();
        private ReaderPageInfo _currentVerticalPageInfo
        {
            get => _verticalPageState.CurrentPage;
            set => _verticalPageState.CurrentPage = value;
        }
        private int _currentVerticalStartBlockIndex
        {
            get => _verticalPageState.StartBlockIndex;
            set => _verticalPageState.StartBlockIndex = value;
        }
        private int _currentVerticalEndBlockIndex
        {
            get => _verticalPageState.EndBlockIndex;
            set => _verticalPageState.EndBlockIndex = value;
        }
        
        // 백그라운드 전체 페이지 계산용 상태
        private int _verticalTotalPages
        {
            get => _verticalPageState.TotalPages;
            set => _verticalPageState.TotalPages = value;
        }
        private bool _isVerticalPageCalcCompleted
        {
            get => _verticalPageState.IsPageCalculationCompleted;
            set => _verticalPageState.IsPageCalculationCompleted = value;
        }
        private Dictionary<int, int> _verticalBlockToPageMap
        {
            get => _verticalPageState.BlockToPageMap;
            set => _verticalPageState.BlockToPageMap = value;
        }
        private System.Threading.CancellationTokenSource? _verticalPageCalcCts;
        private int _verticalCalculatedCurrentPage
        {
            get => _verticalPageState.CalculatedCurrentPage;
            set => _verticalPageState.CalculatedCurrentPage = value;
        }
        private CancellationTokenSource? _currentVerticalRenderCts;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _verticalResizeTimer;

        public void TriggerVerticalResize()
        {
            if (!_isVerticalMode) return;

            if (_verticalResizeTimer == null)
            {
                _verticalResizeTimer = this.DispatcherQueue.CreateTimer();
                _verticalResizeTimer.Interval = TimeSpan.FromMilliseconds(300);
                _verticalResizeTimer.IsRepeating = false;
                _verticalResizeTimer.Tick += (s, e) =>
                {
                     if (_isVerticalMode)
                     {
                         // [핵심] 글자 크기나 창 크기가 바뀌면 측정 캐시와 이전 페이지 캐시를 모두 비워야 정확한 재배치가 가능합니다.
                         ClearBackwardCache(); 
                         
                         // [수정] 이미 펜딩 중인 위치가 있다면 그것을 우선시하여 레이스 컨디션 방어
                         int currentLine = _pendingVerticalScrollLine ?? 1;
                         int currentBlockIdx = _pendingVerticalStartBlockIndex;

                         if (_pendingVerticalScrollLine == null)
                         {
                             if (_aozoraBlocks != null && _currentVerticalStartBlockIndex >= 0 && _currentVerticalStartBlockIndex < _aozoraBlocks.Count)
                             {
                                 currentLine = _aozoraBlocks[_currentVerticalStartBlockIndex].SourceLineNumber;
                                 currentBlockIdx = _currentVerticalStartBlockIndex;
                             }
                         }
                         
                         // [수정] 블록을 강제로 비우지 않습니다. PrepareVerticalTextAsync 내부에서 필요시 재파싱합니다.
                         // if (_isAozoraMode && !_isEpubMode) _aozoraBlocks = new List<AozoraBindingModel>(); 

                         _ = PrepareVerticalTextAsync(currentLine, currentBlockIdx, _globalTextCts?.Token ?? default);
                     }
                };
            }

            _verticalResizeTimer.Stop();
            _verticalResizeTimer.Start();
        }
        
        private void ClearVerticalDisplayState()
        {
            _currentVerticalRenderCts?.Cancel();
            _verticalPageCalcCts?.Cancel();
            _verticalPageState.Clear();
            _imageResourceService.ClearTextEntries(); // vertical 이미지 캐시/누락 목록 초기화
            _pendingVerticalStartBlockIndex = -1;
            ClearBackwardCache();
            VerticalTextCanvas?.Invalidate();
            UpdateVerticalStatusBar();
        }

        private async void VerticalToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save current position before switching mode
                await AddToRecentAsync(true);
                _isVerticalMode = VerticalToggleButton?.IsChecked ?? false;
                SaveTextSettings();
                ToggleVerticalMode();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in VerticalToggleButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void ToggleVerticalMode()
        {
            try
            {
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
                await Task.Delay(10); 

                if (_isVerticalMode)
                {
                    if (!_verticalKeyAttached && RootGrid != null)
                    {
                        RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                        _verticalKeyAttached = true;
                    }
                    
                    if (_isEpubMode)
                    {
                        if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                        if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                        if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                        if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                        if (EpubArea != null) EpubArea.Visibility = Visibility.Visible;
                        
                        int currentLine = 1;
                        int currentBlockIdx = -1;
                        if (_epubWin2DPages != null && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count)
                        {
                            var page = _epubWin2DPages[_currentEpubPageIndex];
                            currentLine = page.StartLine;
                            currentBlockIdx = page.StartBlockIndex;
                        }

                        _epubPreloadCache.Clear();
                        _imageResourceService.ClearEpubEntries();
                        ClearBackwardCache();
                        await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, targetBlockIndex: currentBlockIdx);
                    }
                    else
                    {
                        if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;
                        if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                        if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                        if (EpubArea != null) EpubArea.Visibility = Visibility.Collapsed;
                        if (TextArea != null) TextArea.Visibility = Visibility.Visible;

                        int currentLine = 1;
                        if (_isAozoraMode)
                        {
                            if (_aozoraPendingTargetLine > 0) 
                            {
                                currentLine = _aozoraPendingTargetLine;
                                _aozoraPendingTargetLine = 0;
                            }
                            else if (_currentAozoraPageInfo.Blocks != null && _currentAozoraPageInfo.Blocks.Count > 0)
                            {
                                currentLine = _currentAozoraPageInfo.StartLine;
                            }
                            else if (_aozoraBlocks != null && _aozoraBlocks.Count > _currentAozoraStartBlockIndex)
                            {
                                currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                            }
                        }
                        else if (TextScrollViewer != null) 
                        {
                            currentLine = GetTopVisibleLineIndex();
                        }

                        int targetBlockIdx = _isAozoraMode ? _currentAozoraStartBlockIndex : -1;
                        await PrepareVerticalTextAsync(currentLine, targetBlockIdx, _globalTextCts?.Token ?? default);
                    }
                }
                else
                {
                    if (_verticalKeyAttached && RootGrid != null)
                    {
                        RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                        _verticalKeyAttached = false;
                    }
                    
                    int currentLine = 1;
                    int currentBlockIdx = -1;

                    if (_isEpubMode)
                    {
                        if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                        if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                        if (EpubArea != null) EpubArea.Visibility = Visibility.Visible;

                        if (_epubWin2DPages != null && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count)
                        {
                            var page = _epubWin2DPages[_currentEpubPageIndex];
                            currentLine = page.StartLine;
                            currentBlockIdx = page.StartBlockIndex;
                        }

                        _epubPreloadCache.Clear();
                        _imageResourceService.ClearEpubEntries();
                        ClearBackwardCache();
                        await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, targetBlockIndex: currentBlockIdx);
                    }
                    else
                    {
                        if (_pendingVerticalStartBlockIndex >= 0)
                        {
                            currentBlockIdx = _pendingVerticalStartBlockIndex;
                            if (_aozoraBlocks != null && currentBlockIdx < _aozoraBlocks.Count)
                                currentLine = _aozoraBlocks[currentBlockIdx].SourceLineNumber;
                        }
                        else if (_pendingVerticalScrollLine.HasValue)
                        {
                            currentLine = _pendingVerticalScrollLine.Value;
                        }
                        else if (_currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0)
                        {
                            currentLine = _currentVerticalPageInfo.StartLine;
                            currentBlockIdx = _currentVerticalStartBlockIndex;
                        }

                        _pendingVerticalScrollLine = null;
                        _pendingVerticalStartBlockIndex = -1;

                        if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                        if (EpubArea != null) EpubArea.Visibility = Visibility.Collapsed;
                        if (TextArea != null) TextArea.Visibility = Visibility.Visible;

                        if (_isAozoraMode)
                        {
                            if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;
                            await PrepareAozoraDisplayAsync(_currentTextContent, currentLine, currentBlockIdx, _globalTextCts?.Token ?? default);
                        }
                        else
                        {
                            if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                            await LoadTextLinesProgressivelyAsync(_currentTextContent, currentLine);
                        }
                    }
                }
                UpdateTextStatusBar();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleVerticalMode: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }


        private async Task PrepareVerticalTextAsync(int targetLine = 1, int targetBlockIndex = -1, CancellationToken externalToken = default)
        {
            if (string.IsNullOrEmpty(_currentTextContent) && !_isEpubMode)
            {
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // 로딩 오버레이 켬 (EPUB 모드에서는 즉각적인 반응을 위해 오버레이를 표시하지 않습니다)
            if (!_isEpubMode && TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
            if (!_isEpubMode && ImageInfoText != null) ImageInfoText.Text = Strings.Paginating;

            _verticalPageCalcCts?.Cancel();
            
            _currentVerticalRenderCts?.Cancel();
            _currentVerticalRenderCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = _currentVerticalRenderCts.Token;

            // _verticalImageCache.Clear(); // <-- 깜박임 방지를 위해 즉시 지우지 않고 새 페이지 준비 시 덮어씀
            _pendingVerticalScrollLine = targetLine;
            _pendingVerticalStartBlockIndex = targetBlockIndex;
            ClearBackwardCache(); // <-- 캐시 초기화 추가

            try
            {
                if (token.IsCancellationRequested) return;
                // [수정] EPUB 모드일 때는 텍스트 모드용 파싱과 MD 감지 로직을 건너뜁니다.
                if (!_isEpubMode && (_aozoraBlocks == null || _aozoraBlocks.Count == 0))
                {
                    _aozoraBlocks = await Task.Run(() => 
                    {
                        var document = _textBlockDocumentService.Parse(
                            _currentTextContent,
                            _isMarkdownRenderMode,
                            _settingsManager.FontSize);

                        DispatcherQueue.TryEnqueue(() => { _textTotalLineCountInSource = document.SourceLineCount; });
                        return document.Blocks;
                    });
                }

                int startIdx = 0;

                // [수정] 이전 챕터로 돌아올 때(fromEnd: true) 마지막 페이지를 온전하게 계산하여 표시
// [수정] 이전 챕터로 돌아올 때(fromEnd: true) 마지막 페이지를 온전하게 계산하여 표시
                if (targetLine == 999999 && _aozoraBlocks.Count > 0)
                {
                    int targetIdx = _aozoraBlocks.Count;
                    var layout = _readerLayoutService.CreateVerticalTextLayout(
                        VerticalTextCanvas?.ActualWidth ?? 0,
                        VerticalTextCanvas?.ActualHeight ?? 0,
                        RootGrid?.ActualWidth ?? 0,
                        RootGrid?.ActualHeight ?? 0);
                    var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    // 기존 while 루프를 지우고 단 한 줄로 교체
                    startIdx = FindPreviousPageStart(targetIdx, _aozoraBlocks, layout.AvailableWidth, layout.AvailableHeight, device, true);
                }
                else
                {
                    startIdx = _textBlockDocumentService.FindStartBlockIndex(
                        _aozoraBlocks,
                        targetLine,
                        targetBlockIndex,
                        NegativeLineTargetBehavior.NearEnd);
                }

                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;
                
                // [핵심] 수천 페이지를 기다리지 않고, 현재 화면에 그릴 '단 1페이지'만 즉시 렌더링
                await RenderVerticalDynamicPageAsync(startIdx, token);
                
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
                _pendingVerticalScrollLine = null;
                _pendingVerticalStartBlockIndex = -1; // <-- 이 줄을 추가하여 이전 위치 캐시를 비웁니다.

                // 전체 페이지 수와 스크롤바 세팅은 백그라운드에 던짐
                StartVerticalPageCalculationAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Vertical Pagination Error: {ex.Message}");
            }
        }

        private async Task RenderVerticalDynamicPageAsync(int startIdx, CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return;

            // [수정] 블록이 없더라도 화면을 갱신(Invalidate)해야 이전 페이지의 잔상이 지워지고 스턱된 느낌이 사라집니다.
            if (_aozoraBlocks == null || _aozoraBlocks.Count == 0)
            {
                _verticalPageState.SetEmptyPage();
                if (token.IsCancellationRequested) return;
                if (VerticalTextCanvas != null) VerticalTextCanvas.Invalidate();
                UpdateVerticalStatusBar();
                return;
            }

            if (VerticalTextCanvas == null) return;
            
            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentVerticalStartBlockIndex = startIdx;

            var layout = _readerLayoutService.CreateVerticalTextLayout(
                VerticalTextCanvas.ActualWidth,
                VerticalTextCanvas.ActualHeight,
                RootGrid?.ActualWidth ?? 0,
                RootGrid?.ActualHeight ?? 0);

            int index = startIdx;
            
            // [핵심 수정] Device가 null이면 측정 로직이 고장나서 글자가 겹치거나 왼쪽 밖으로 잘립니다.
            // CanvasControl의 Device를 가져오고, 없으면 시스템 공용 Device를 강제로라도 가져와야 합니다.
            var device = VerticalTextCanvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
            
            // 디바이스를 정확히 넘겨주어 페이지를 측정합니다.
            var pageBlocks = PaginateAozoraPage(ref index, _aozoraBlocks, layout.AvailableWidth, layout.AvailableHeight, device);
            if (token.IsCancellationRequested) return;

            // [이미지 프리로딩] EPUB 등에서 이미지 교체 시 깜박임을 방지하기 위해 렌더링 전 이미지를 미리 로드합니다.
            if (pageBlocks.Any(b => b.HasImage))
            {
                var ctx = CreateViewingContext();
                var sp = CreateSharpenParams();
                var preloadDevice = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
                await _imageResourceService.PreloadTextImagesAsync(pageBlocks, preloadDevice, ctx, _sharpenEnabled, sp);
            }
            if (token.IsCancellationRequested) return;
            
            _verticalPageState.SetPage(pageBlocks, startIdx, index);

            VerticalTextCanvas?.Invalidate();
            UpdateVerticalStatusBar();
            StartBackwardPageCaching(_currentVerticalStartBlockIndex, true); // <-- 현재 페이지 렌더링 직후 백그라운드 캐싱 시작
        }

        private async void StartVerticalPageCalculationAsync()
        {
            try
            {
                if (!_isVerticalMode || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;
                
                _verticalPageCalcCts?.Cancel();
                _verticalPageCalcCts = new System.Threading.CancellationTokenSource();
                var token = _verticalPageCalcCts.Token;

                _verticalPageState.ResetPageCalculation();

                if (VerticalTextCanvas == null || VerticalTextCanvas.ActualHeight <= 0 || VerticalTextCanvas.ActualWidth <= 0) return;

                var layout = _readerLayoutService.CreateVerticalPageMapLayout(
                    VerticalTextCanvas.ActualWidth,
                    VerticalTextCanvas.ActualHeight);
                var device = VerticalTextCanvas.Device;

                var result = await _aozoraPageMapCalculator.CalculateAsync(
                    _aozoraBlocks,
                    new AozoraBlockPaginationContext(
                        device,
                        layout.AvailableWidth,
                        layout.AvailableHeight,
                        _settingsManager.FontSize,
                        _settingsManager.FontFamily,
                        GetFontWeightForFamily,
                        DoesVerticalImageExist,
                        _isSideBySideMode || _autoDoublePageForArchive,
                        ShouldPairTextImage),
                    AozoraPageOrientation.Vertical,
                    token);

                if (result == null || token.IsCancellationRequested) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;

                    _verticalPageState.SetPageMap(result.BlockToPageMap, result.TotalPages);
                    if (!_verticalPageState.SyncCalculatedCurrentPageFromMap())
                        _verticalCalculatedCurrentPage = 1;

                    UpdateVerticalStatusBar();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in StartVerticalPageCalculationAsync: {ex.Message}");
            }
        }

        private List<AozoraBindingModel> PaginateAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, Microsoft.Graphics.Canvas.CanvasDevice? device = null)
        {
            return _aozoraBlockPaginator.PaginateVerticalPage(
                ref index,
                blocks,
                new AozoraBlockPaginationContext(
                    device,
                    availableWidth,
                    availableHeight,
                    _settingsManager.FontSize,
                    _settingsManager.FontFamily,
                    GetFontWeightForFamily,
                    DoesVerticalImageExist,
                    _isSideBySideMode || _autoDoublePageForArchive,
                    ShouldPairTextImage));
        }

        private bool ShouldPairTextImage(string source)
        {
            bool isTall = !_autoDoublePageForArchive;
            var bitmap = _imageResourceService.TryGetCached(ImageResourceService.GetTextCacheKey(source));
            if (bitmap != null)
            {
                isTall = _autoDoublePageForArchive
                    ? IsAutoDoublePageTallCandidate(bitmap.Size.Width, bitmap.Size.Height)
                    : bitmap.Size.Width < bitmap.Size.Height * 1.2f;
            }

            return isTall;
        }
        private void VerticalTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
        }

        private void VerticalTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isVerticalMode) return;

            var ds = args.DrawingSession;
            var size = sender.Size;
            Color textColor = GetVerticalTextColor();
            
            bool isImgPage = _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Any(b => b.HasImage);
            bool isEmptyPage = _currentVerticalPageInfo.Blocks == null || _currentVerticalPageInfo.Blocks.Count == 0;

            if (_isEpubMode && (isImgPage || isEmptyPage)) ds.Clear(GetVerticalAppBackgroundColor());
            else ds.Clear(GetVerticalBackgroundColor());

            if (_currentVerticalPageInfo.Blocks == null || _currentVerticalPageInfo.Blocks.Count == 0) return;

            var page = _currentVerticalPageInfo;
            var margins = ReaderPageMargins.VerticalText;

            var imgBlocks = page.Blocks.Where(b => b.HasImage).ToList();
            if (imgBlocks.Count > 0)
            {
                if (imgBlocks.Count >= 2)
                {
                    var src1 = imgBlocks[0].Inlines.OfType<AozoraImage>().First().Source;
                    var src2 = imgBlocks[1].Inlines.OfType<AozoraImage>().First().Source;
                    DrawVerticalImagesSBS(ds, size, src1, src2);
                }
                else
                {
                    var src = imgBlocks[0].Inlines.OfType<AozoraImage>().First().Source;
                    DrawVerticalImage(ds, size, src);
                }
                return; 
            }

            // ⭐ 세로 모드 통합 렌더러 호출!
            VerticalRenderer.RenderBlocks(
                ds: ds,
                blocks: page.Blocks,
                textColor: textColor,
                canvasSize: size,
                marginTop: margins.Top,
                marginBottom: margins.Bottom,
                marginRight: margins.Right,
                marginLeft: margins.Left,
                baseFontSize: _settingsManager.FontSize,
                defaultFontFamily: _settingsManager.FontFamily,
                getFontWeight: GetFontWeightForFamily,
                searchQuery: _activeSearchQuery
            );
        }

        private Color GetVerticalTextColor()
        {
            return _settingsManager.GetThemeForegroundColor();
        }

        private Color GetVerticalBackgroundColor()
        {
            return _settingsManager.GetThemeBackgroundColor();
        }

        private Color GetVerticalAppBackgroundColor()
        {
            // 사용자의 요청에 따라 다크 모드 시 완전한 검은색 대신 일반 이미지 보기 배경색(#202020)을 사용합니다.
            if (RootGrid?.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark) 
                return Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32); // #202020 (SolidBackgroundFillColorBase)

            // 라이트 모드인 경우 텍스트 읽기용 베이지 대신 앱 표준 배경색(#F3F3F3)을 적용합니다.
            return Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243); // #F3F3F3
        }

        private void VerticalTextCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (VerticalTextCanvas == null) return;
            var pt = e.GetCurrentPoint(VerticalTextCanvas).Position;
            var width = VerticalTextCanvas.ActualWidth;

            // Click left half to go forward (next page), right half to go backward
            if (pt.X < width / 2)
            {
                NavigateVerticalPage(1);
            }
            else
            {
                NavigateVerticalPage(-1);
            }
            e.Handled = true;
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void VerticalTextCanvas_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (VerticalTextCanvas == null) return;
            RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            var delta = e.GetCurrentPoint(VerticalTextCanvas).Properties.MouseWheelDelta;
            if (delta > 0) NavigateVerticalPage(-1);
            else NavigateVerticalPage(1);
            e.Handled = true;
        }

        private async void NavigateVerticalPage(int direction)
        {
            try
            {
                var blocks = _aozoraBlocks;
                bool isEmpty = blocks == null || blocks.Count == 0;
                if (isEmpty && !_isEpubMode) return;

                if (direction > 0) // 다음 페이지
                {
                    if (blocks != null && _currentVerticalEndBlockIndex < blocks.Count - 1)
                    {
                        // 💡 History Push 완전히 제거됨
                        await RenderVerticalDynamicPageAsync(_currentVerticalEndBlockIndex + 1);
                        if (_isVerticalPageCalcCompleted) { _verticalPageState.AdvanceCalculatedPage(1); UpdateVerticalStatusBar(); }
                    }
                    else if (_isEpubMode)
                    {
                        // [수정] EPUB 모드일 때는 세로 모드여도 NavigateEpubAsync를 통해 내부 페이지를 정교하게 이동 (2장보기 등 대응)
                        await NavigateEpubAsync(direction);
                    }
                }
                else if (direction < 0) // 이전 페이지
                {
                    if (blocks != null && _currentVerticalStartBlockIndex > 0)
                    {
                        int targetIdx = _currentVerticalStartBlockIndex;

                        var layout = _readerLayoutService.CreateVerticalTextLayout(
                            VerticalTextCanvas?.ActualWidth ?? 0,
                            VerticalTextCanvas?.ActualHeight ?? 0,
                            RootGrid?.ActualWidth ?? 0,
                            RootGrid?.ActualHeight ?? 0);
                        var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                        int bestStart = GetOrFindPreviousPageStart(targetIdx, blocks, layout.AvailableWidth, layout.AvailableHeight, device, true);

                        await RenderVerticalDynamicPageAsync(bestStart);
                        if (_isVerticalPageCalcCompleted) { _verticalPageState.AdvanceCalculatedPage(-1); UpdateVerticalStatusBar(); }
                    }
                    else if (_isEpubMode)
                    {
                        // [수정] EPUB 모드일 때는 세로 모드여도 NavigateEpubAsync를 통해 내부 페이지를 정교하게 이동
                        await NavigateEpubAsync(direction);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NavigateVerticalPage: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void UpdateVerticalStatusBar()
        {
            if (!_isVerticalMode) return;
            if (_isEpubMode)
            {
                UpdateEpubStatus();
                return;
            }
            if (_currentVerticalPageInfo.Blocks == null) return;

            int totalPages = _isVerticalPageCalcCompleted ? _verticalTotalPages : 0;
            int currentPage = _isVerticalPageCalcCompleted ? _verticalCalculatedCurrentPage : 1;
            int currentLine = _currentVerticalPageInfo.StartLine;
            
            // 기존의 Split 코드를 아래처럼 변경합니다.
            int totalLines = _textTotalLineCountInSource;
            if (totalLines <= 1 && !string.IsNullOrEmpty(_currentTextContent))
            {
                totalLines = _textBlockDocumentService.CountNormalizedLines(_currentTextContent);
                _textTotalLineCountInSource = totalLines;
            }
            
            ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);

            double progress = _readingProgressService.CalculateLineProgress(currentLine, totalLines);
            TextProgressText.Text = _readingProgressService.FormatPercent(progress);

            if (_isVerticalPageCalcCompleted)
            {
                // 점프(Home/End/이동) 시에도 페이지 번호 즉시 반영
                _verticalPageState.SyncCalculatedCurrentPageFromMap();

                currentPage = _readingProgressService.ClampPage(_verticalCalculatedCurrentPage, totalPages);
                ImageIndexText.Text = $"{currentPage} / {totalPages}";
            }
            else
            {
                ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
            }

            if (currentLine != _lastRecentSaveLine)
            {
                _lastRecentSaveLine = currentLine;
                _ = AddToRecentAsync(true);
            }
        }

        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isVerticalMode && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                TriggerVerticalResize();
            }
        }

        private async void RootGrid_Vertical_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Handled) return;
                if (!_isVerticalMode) return;
                var blocks = _aozoraBlocks;

                // Space to toggle SideBySide for images in vertical mode
                // 스페이스 키 부분:
                if (e.Key == Windows.System.VirtualKey.Space)
                {
                    _isSideBySideMode = !_isSideBySideMode;
                    UpdateSideBySideButtonState();
                    // [수정] EPUB 모드일 때는 챕터 통합 로직을 위해 다시 로드 (SideBySide 모드 반영)
                    var pageBlocks = _currentVerticalPageInfo.Blocks;
                    int currentLine = pageBlocks != null ? _currentVerticalPageInfo.StartLine : 1;
                    
                    if (_isEpubMode)
                    {
                        _ = LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine);
                    }
                    else
                    {
                        _ = PrepareVerticalTextAsync(currentLine);
                    }
                    e.Handled = true;
                    return;
                }

                // Home 키 부분:
                if (e.Key == Windows.System.VirtualKey.Home)
                {
                    // [수정] EPUB 모드일 때는 무조건 이전 챕터로 이동
                    if (_isEpubMode)
                    {
                        if (_currentEpubChapterIndex > 0)
                        {
                            _currentEpubChapterIndex--;
                            _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                        }
                        e.Handled = true;
                    }
                    // 일반 텍스트 모드일 때는 텍스트의 처음으로 이동
                    else if (_currentVerticalStartBlockIndex > 0)
                    {
                        await RenderVerticalDynamicPageAsync(0);
                        if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage = 1; UpdateVerticalStatusBar(); }
                        e.Handled = true;
                    }
                }
                // End 키 부분:
                else if (e.Key == Windows.System.VirtualKey.End)
                {
                    // [수정] EPUB 모드일 때는 무조건 다음 챕터로 이동
                    if (_isEpubMode)
                    {
                        if (_currentEpubChapterIndex < _epubSpine.Count - 1)
                        {
                            _currentEpubChapterIndex++;
                            _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                        }
                        e.Handled = true;
                    }
                    // 일반 텍스트 모드일 때는 텍스트의 끝으로 이동
                    else if (blocks != null && _currentVerticalEndBlockIndex < blocks.Count - 1)
                    {
                        int lastIdx = Math.Max(0, blocks.Count - 15); // 끝부분 추정
                        await RenderVerticalDynamicPageAsync(lastIdx);
                        if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage = _verticalTotalPages; UpdateVerticalStatusBar(); }
                        e.Handled = true;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RootGrid_Vertical_PreviewKeyDown: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void DrawVerticalImagesSBS(CanvasDrawingSession ds, Size canvasSize, string path1, string path2)
        {
            float halfW = (float)canvasSize.Width / 2;
            float canvasH = (float)canvasSize.Height;

            if (_nextImageOnRight)
            {
                // Left-to-Right layout: Current on Left (Right-align), Next on Right (Left-align)
                DrawImageInRect(ds, path1, new Rect(0, 0, halfW, canvasH), HorizontalAlignment.Right);
                DrawImageInRect(ds, path2, new Rect(halfW, 0, halfW, canvasH), HorizontalAlignment.Left);
            }
            else
            {
                // Right-to-Left layout: Current on Right (Left-align), Next on Left (Right-align)
                DrawImageInRect(ds, path1, new Rect(halfW, 0, halfW, canvasH), HorizontalAlignment.Left);
                DrawImageInRect(ds, path2, new Rect(0, 0, halfW, canvasH), HorizontalAlignment.Right);
            }
        }

        private void DrawVerticalImage(CanvasDrawingSession ds, Size canvasSize, string relativePath)
        {
            DrawImageInRect(ds, relativePath, new Rect(0, 0, canvasSize.Width, canvasSize.Height));
        }

        private void DrawImageInRect(CanvasDrawingSession ds, string path, Rect rect, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            if (string.IsNullOrEmpty(path)) return;

            string cacheKey = Services.ImageResourceService.GetTextCacheKey(path);
            var bitmap = _imageResourceService.TryGetCached(cacheKey);

            if (bitmap != null)
            {
                try
                {
                    ImageCanvasRenderer.DrawBitmapFit(ds, bitmap, rect, align);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Vertical image draw skipped: {ex.Message}");
                }
            }
            else
            {
                _ = LoadVerticalImageAsync(path);
            }
        }


        private bool DoesVerticalImageExist(string relativePath)
            => _imageResourceService.DoesImageExist(relativePath, CreateViewingContext());

        private async Task LoadVerticalImageAsync(string relativePath)
        {
            string cacheKey = Services.ImageResourceService.GetTextCacheKey(relativePath);
            var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

            await LoadImageResourceAndInvalidateAsync(
                relativePath,
                cacheKey,
                device,
                () => VerticalTextCanvas?.Invalidate(),
                () =>
                {
                    if (!_isWebDavMode) return;

                    // WebDAV 누락 이미지: 현재 페이지에서 다시 계산
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        int currentLine = 1;
                        if (_aozoraBlocks.Count > 0 && _currentVerticalStartBlockIndex >= 0 && _currentVerticalStartBlockIndex < _aozoraBlocks.Count)
                            currentLine = _aozoraBlocks[_currentVerticalStartBlockIndex].SourceLineNumber;

                        _ = PrepareVerticalTextAsync(currentLine, -1, _globalTextCts?.Token ?? default);
                    });
                });
        }

    }
}

