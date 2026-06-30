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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Uviewer.Models;
using Uviewer.Services;
using Uviewer.Renderers;

namespace Uviewer
{
    internal sealed partial class DocumentReaderController
    {
        internal bool _isAozoraMode = true;
        internal bool _isMarkdownRenderMode = false;
        internal List<AozoraBindingModel> _aozoraBlocks = new();
        internal int _aozoraTotalLineCount = 0;
        internal int _aozoraTotalLineCountInSource = 0;

        // Win2D Rendering State
        internal readonly ReaderPageState _aozoraPageState = new();
        internal ReaderPageInfo _currentAozoraPageInfo
        {
            get => _aozoraPageState.CurrentPage;
            set => _aozoraPageState.CurrentPage = value;
        }
        internal int _currentAozoraStartBlockIndex
        {
            get => _aozoraPageState.StartBlockIndex;
            set => _aozoraPageState.StartBlockIndex = value;
        }
        internal int _currentAozoraEndBlockIndex
        {
            get => _aozoraPageState.EndBlockIndex;
            set => _aozoraPageState.EndBlockIndex = value;
        }
        // 이미지 캐시는 _imageResourceService 로 통합됨 (접두어 "text:")
        internal Dictionary<int, int> _aozoraBlockToPageMap
        {
            get => _aozoraPageState.BlockToPageMap;
            set => _aozoraPageState.BlockToPageMap = value;
        }

        internal void ClearBackwardCache() => _aozoraPreviousPageCache.Clear();

        internal AozoraBlockPaginationContext CreatePreviousPageContext(
            Microsoft.Graphics.Canvas.ICanvasResourceCreator? device,
            float maxWidth,
            float availHeight,
            bool isVertical)
        {
            return new AozoraBlockPaginationContext(
                device as CanvasDevice,
                maxWidth,
                availHeight,
                _settingsManager.FontSize,
                _settingsManager.FontFamily,
                GetFontWeightForFamily,
                isVertical ? DoesVerticalImageExist : DoesAozoraImageExist,
                isVertical && (_isSideBySideMode || _autoDoublePageForArchive),
                isVertical ? new Func<string, bool>(ShouldPairTextImage) : null);
        }

        internal int FindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, Microsoft.Graphics.Canvas.ICanvasResourceCreator device, bool isVertical)
        {
            var context = CreatePreviousPageContext(device as CanvasDevice, maxWidth, availHeight, isVertical);
            return _aozoraPreviousPageCache.FindPreviousPageStart(
                targetIdx,
                blocks,
                context,
                isVertical ? AozoraPageOrientation.Vertical : AozoraPageOrientation.Horizontal);
        }

        internal int GetOrFindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, Microsoft.Graphics.Canvas.ICanvasResourceCreator device, bool isVertical)
        {
            var context = CreatePreviousPageContext(device as CanvasDevice, maxWidth, availHeight, isVertical);
            return _aozoraPreviousPageCache.GetOrFindPreviousPageStart(
                targetIdx,
                blocks,
                context,
                isVertical ? AozoraPageOrientation.Vertical : AozoraPageOrientation.Horizontal);
        }

        // 백그라운드에서 최대 10페이지 분량을 미리 계산하여 캐시에 적재합니다.
        internal void StartBackwardPageCaching(int currentStartIdx, bool isVertical)
        {
            if (currentStartIdx <= 0 || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

            float availWidth = isVertical ? (float)(VerticalTextCanvas?.ActualWidth ?? 0) : (float)(AozoraTextCanvas?.ActualWidth ?? 0);
            float availHeight = isVertical ? (float)(VerticalTextCanvas?.ActualHeight ?? 0) : (float)(AozoraTextCanvas?.ActualHeight ?? 0);
            var layout = isVertical
                ? _readerLayoutService.CreateVerticalTextLayout(availWidth, availHeight, RootGrid?.ActualWidth ?? 0, RootGrid?.ActualHeight ?? 0)
                : _readerLayoutService.CreateHorizontalTextLayout(availWidth, availHeight, RootGrid?.ActualWidth ?? 0, RootGrid?.ActualHeight ?? 0, _isMarkdownRenderMode, GetUrlMaxWidth());

            var device = isVertical ? VerticalTextCanvas?.Device : AozoraTextCanvas?.Device;
            device ??= Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
            
            var context = CreatePreviousPageContext(device, layout.MaxWidth, layout.AvailableHeight, isVertical);
            _aozoraPreviousPageCache.StartCaching(
                currentStartIdx,
                _aozoraBlocks,
                context,
                isVertical ? AozoraPageOrientation.Vertical : AozoraPageOrientation.Horizontal);
        }

        // Page Calculation
        internal int _aozoraTotalPages
        {
            get => _aozoraPageState.TotalPages;
            set => _aozoraPageState.TotalPages = value;
        }
        internal bool _isAozoraPageCalcCompleted
        {
            get => _aozoraPageState.IsPageCalculationCompleted;
            set => _aozoraPageState.IsPageCalculationCompleted = value;
        }
        internal System.Threading.CancellationTokenSource? _aozoraPageCalcCts;
        internal int _aozoraCalculatedCurrentPage
        {
            get => _aozoraPageState.CalculatedCurrentPage;
            set => _aozoraPageState.CalculatedCurrentPage = value;
        }

        // Settings
        public class AozoraSettings
        {
            public bool IsAozoraModeEnabled { get; set; } = true;
        }

        internal const string AozoraSettingsFilePath = "aozora_settings.json";
        internal string GetAozoraSettingsFilePath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", AozoraSettingsFilePath);

        [System.Text.Json.Serialization.JsonSerializable(typeof(AozoraSettings))]
        public partial class AozoraSettingsContext : System.Text.Json.Serialization.JsonSerializerContext;

        internal void LoadAozoraSettings()
        {
            try
            {
                var file = GetAozoraSettingsFilePath();
                if (System.IO.File.Exists(file))
                {
                    var json = System.IO.File.ReadAllText(file);
                    var settings = System.Text.Json.JsonSerializer.Deserialize(json, typeof(AozoraSettings), AozoraSettingsContext.Default) as AozoraSettings;
                    if (settings != null)
                    {
                        _isAozoraMode = settings.IsAozoraModeEnabled;
                    }
                }

                MainToolbar.SetAozoraToggleChecked(_isAozoraMode);
            }
            catch { }
        }

        internal void SaveAozoraSettings()
        {
            try
            {
                var settings = new AozoraSettings { IsAozoraModeEnabled = _isAozoraMode };
                var json = System.Text.Json.JsonSerializer.Serialize(settings, typeof(AozoraSettings), AozoraSettingsContext.Default);

                var file = GetAozoraSettingsFilePath();
                var dir = System.IO.Path.GetDirectoryName(file);
                if (dir != null) System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(file, json);
            }
            catch { }
        }

        internal void AozoraToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAozoraMode();
        }

        internal async void ToggleAozoraMode()
        {
            try
            {
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
                await Task.Delay(10);
                
                int currentLine = 1;

                if (_isAozoraMode)
                {
                    // [추가] 수직 모드가 활성화되어 있다면 수직 모드의 현재 라인을 가장 먼저 우선시해야 함
                    if (_isVerticalMode && _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0)
                    {
                        currentLine = _currentVerticalPageInfo.StartLine;
                        _aozoraPendingTargetBlockIndex = _currentVerticalStartBlockIndex;
                    }
                    // [수정] 가로 렌더링 대기 라인을 우선 확인
                    else if (_aozoraPendingTargetLine > 0)
                    {
                        currentLine = _aozoraPendingTargetLine;
                    }
                    else if (_currentAozoraPageInfo.Blocks != null && _currentAozoraPageInfo.Blocks.Count > 0)
                    {
                        currentLine = _currentAozoraPageInfo.StartLine;
                    }
                }
                else
                {
                    // [수정] 세로 모드에서 넘어올 때의 방어 로직 추가
                    if (_isVerticalMode && _pendingVerticalScrollLine.HasValue)
                    {
                        currentLine = _pendingVerticalScrollLine.Value;
                    }
                    else if (_isVerticalMode && _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0)
                    {
                        currentLine = _currentVerticalPageInfo.StartLine;
                    }
                    else if (TextScrollViewer != null)
                    {
                        currentLine = GetTopVisibleLineIndex();
                    }
                }

                _aozoraPendingTargetLine = currentLine > 0 ? currentLine : 1;
                _isAozoraMode = !_isAozoraMode;

                MainToolbar.SetAozoraToggleChecked(_isAozoraMode);
                SaveAozoraSettings();

                if (!string.IsNullOrEmpty(_currentTextContent))
                {
                    CancelAndResetGlobalTextCts();
                    string displayName = string.IsNullOrEmpty(_currentTextFilePath) ? "Document" : System.IO.Path.GetFileName(_currentTextFilePath);
                    await DisplayLoadedText(_currentTextContent, displayName, _currentTextFilePath, _globalTextCts!.Token);
                }
                else
                {
                    if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleAozoraMode: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal async Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine)
        {
            try
            {
                _aozoraPageCalcCts?.Cancel();
                _textReaderState.CancelPageCalculation();
                
                _aozoraPendingTargetLine = targetLine;
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                if (_isVerticalMode)
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                    if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;

                    await PrepareVerticalTextAsync(targetLine, -1, token);
                    FileNameText.Text = FileExplorerService.GetFormattedDisplayName(fileName, _currentTextArchiveEntryKey != null, _archiveSession.CurrentPath);
                }
                else if (_isAozoraMode)
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;
                    if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;

                    await PrepareAozoraDisplayAsync(_currentTextContent, targetLine, -1, token);
                    FileNameText.Text = FileExplorerService.GetFormattedDisplayName(fileName, _currentTextArchiveEntryKey != null, _archiveSession.CurrentPath);
                }
                else
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                    if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;

                    if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                    {
                        TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                    }

                    await LoadTextLinesProgressivelyAsync(_currentTextContent, targetLine, token);
                    UpdateTextStatusBar(fileName, _textTotalLineCountInSource, 1);

                    if (targetLine > 1)
                    {
                        await Task.Delay(50);
                        ScrollToLine(targetLine);
                        UpdateTextStatusBar();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReloadTextDisplayFromCacheAsync error: {ex.Message}");
            }
        }

        internal int _aozoraPendingTargetLine = 0;
        internal int _aozoraPendingTargetBlockIndex = -1;

        internal async Task PrepareAozoraDisplayAsync(string rawContent, int targetLine = 1, int targetBlockIndex = -1, CancellationToken token = default)
{
    int startIdx = 0;
    try
    {
        if (_aozoraPendingTargetLine > 0)
        {
            targetLine = _aozoraPendingTargetLine;
            // _aozoraPendingTargetLine = 0; // <--- 이 줄을 삭제하세요! (섣불리 지우면 위치를 잃어버립니다)
        }

        // 화면 전환 전 불필요한 UI 숨기기 (로딩 뷰가 있다면 여기서 띄워도 좋습니다)
        if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
        if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
        if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;

        if (TextArea != null)
        {
            TextArea.Visibility = Visibility.Visible;
            TextArea.Background = _settingsManager.GetThemeBackground();
        }

        ClearBackwardCache(); // <-- 파일/챕터 변경 시 캐시 지우기 추가

        // [핵심 추가] 이미 파싱된 블록이 존재한다면 불필요한 재파싱을 생략하여 즉시 렌더링 (폰트 크기가 달라도 블록은 재사용 가능)
        if (_aozoraBlocks != null && _aozoraBlocks.Count > 0)
        {
            startIdx = _textBlockDocumentService.FindStartBlockIndex(
                _aozoraBlocks,
                targetLine,
                targetBlockIndex);

            _currentAozoraStartBlockIndex = startIdx;

            if (AozoraTextCanvas != null)
            {
                AozoraTextCanvas.Visibility = Visibility.Visible;
                if (AozoraTextCanvas.ActualHeight == 0 || AozoraTextCanvas.ActualWidth == 0)
                {
                    await Task.Delay(50);
                }
            }

            await RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
            _aozoraPendingTargetLine = 0;
            StartAozoraPageCalculationAsync();
            UpdateAozoraStatusBar();
            return; // 재파싱 없이 함수 즉시 종료
        }

        // 2. 전체 데이터 백그라운드 파싱 (UI 프리징 방지)
        await Task.Run(() =>
        {
            if (token.IsCancellationRequested) return;

            var document = _textBlockDocumentService.Parse(rawContent, _isMarkdownRenderMode, _settingsManager.FontSize);

            if (token.IsCancellationRequested) return;

            // 3. 파싱 완료 후 UI 스레드에서 화면 업데이트
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (token.IsCancellationRequested) return;

                _aozoraBlocks = document.Blocks;
                _aozoraTotalLineCount = _aozoraBlocks.Count;
                _aozoraTotalLineCountInSource = document.SourceLineCount;
                _textTotalLineCountInSource = document.SourceLineCount;

                // Update TOC with parsed blocks
                _tocService.SetProvider(new TextTocProvider(_currentTextContent, _aozoraBlocks));
                _ = _tocService.LoadTocAsync(token);

                // 목표 라인(TargetLine) 또는 블록 인덱스 탐색
                int startIdx = _textBlockDocumentService.FindStartBlockIndex(
                    _aozoraBlocks,
                    targetLine,
                    targetBlockIndex);

                _currentAozoraStartBlockIndex = startIdx;

                if (AozoraTextCanvas != null)
                {
                    AozoraTextCanvas.Visibility = Visibility.Visible;
                    // 캔버스 크기가 아직 잡히지 않았다면 잠시 대기
                    if (AozoraTextCanvas.ActualHeight == 0 || AozoraTextCanvas.ActualWidth == 0)
                    {
                        await Task.Delay(50);
                    }
                }

                // 현재 화면에 보이는 만큼만 가상화 렌더링
                if (_aozoraBlocks.Count > 0)
                {
                    await RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                }

                _aozoraPendingTargetLine = 0; // <--- 렌더링이 화면에 반영된 직후인 이 위치에서 초기화해 줍니다!

                // 렌더링 완료 후 남은 전체 페이지 계산 백그라운드 시작
                StartAozoraPageCalculationAsync();
                UpdateAozoraStatusBar();
            });

        }, token);
    }
    catch (TaskCanceledException)
    {
        // 토큰 취소 시 무시
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Aozora Load Error: {ex.Message}");
    }
}

        internal void AozoraTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isTextMode || !_isAozoraMode) return;
            if (_aozoraBlocks.Count == 0) return;

            ClearBackwardCache(); // <-- 화면 크기 변경 시 캐시 지우기
            // 크기 변경 시 현재 위치 다시 렌더링
            _ = RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
            StartAozoraPageCalculationAsync();
        }

        internal async Task RenderAozoraDynamicPage(int startIdx)
        {
            if (AozoraTextCanvas == null || _aozoraBlocks == null || _aozoraBlocks.Count == 0)
            {
                _aozoraPageState.SetEmptyPage();
                if (AozoraTextCanvas != null) AozoraTextCanvas.Invalidate();
                UpdateAozoraStatusBar();
                return;
            }

            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentAozoraStartBlockIndex = startIdx;

            var layout = _readerLayoutService.CreateHorizontalTextLayout(
                AozoraTextCanvas.ActualWidth,
                AozoraTextCanvas.ActualHeight,
                RootGrid?.ActualWidth ?? 0,
                RootGrid?.ActualHeight ?? 0,
                _isMarkdownRenderMode,
                GetUrlMaxWidth());

            int index = startIdx;
            var device = AozoraTextCanvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

            var pageBlocks = PaginateHorizontalAozoraPage(ref index, _aozoraBlocks, layout.MaxWidth, layout.AvailableHeight, device);

            // [이미지 프리로딩] 깜박임 방지를 위해 렌더링 전 이미지를 미리 로드합니다.
            if (pageBlocks.Any(b => b.HasImage))
            {
                var ctx = CreateViewingContext();
                var sp = CreateSharpenParams();
                var preloadDevice = AozoraTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
                await _imageResourceService.PreloadTextImagesAsync(pageBlocks, preloadDevice, ctx, _sharpenEnabled, sp);
            }

            _aozoraPageState.SetPage(pageBlocks, startIdx, index);

            AozoraTextCanvas?.Invalidate();
            UpdateAozoraStatusBar();
            StartBackwardPageCaching(_currentAozoraStartBlockIndex, false); // <-- 현재 페이지 렌더링 직후 백그라운드 캐싱 시작
        }

        internal async void StartAozoraPageCalculationAsync()
        {
            try
            {
                if (!_isAozoraMode || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

                _aozoraPageCalcCts?.Cancel();
                _aozoraPageCalcCts = new System.Threading.CancellationTokenSource();
                var token = _aozoraPageCalcCts.Token;

                _aozoraPageState.ResetPageCalculation();
                UpdateAozoraStatusBar();

                if (AozoraTextCanvas == null || AozoraTextCanvas.ActualHeight <= 0 || AozoraTextCanvas.ActualWidth <= 0) return;

                var layout = _readerLayoutService.CreateHorizontalPageMapLayout(
                    AozoraTextCanvas.ActualWidth,
                    AozoraTextCanvas.ActualHeight,
                    _isMarkdownRenderMode,
                    GetUrlMaxWidth());
                var device = AozoraTextCanvas.Device;

                bool calculated = await _readerPageMapCalculationService.CalculateAsync(
                    _aozoraPageState,
                    _aozoraPageMapCalculator,
                    _aozoraBlocks,
                    new AozoraBlockPaginationContext(
                        device,
                        layout.MaxWidth,
                        layout.AvailableHeight,
                        _settingsManager.FontSize,
                        _settingsManager.FontFamily,
                        GetFontWeightForFamily,
                        DoesAozoraImageExist),
                    AozoraPageOrientation.Horizontal,
                    token);

                if (!calculated || token.IsCancellationRequested) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;

                    UpdateAozoraStatusBar();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in StartAozoraPageCalculationAsync: {ex.Message}");
            }
        }

        internal List<AozoraBindingModel> PaginateHorizontalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null)
        {
            return _aozoraBlockPaginator.PaginateHorizontalPage(
                ref index,
                blocks,
                new Services.AozoraBlockPaginationContext(
                    device,
                    availableWidth,
                    availableHeight,
                    _settingsManager.FontSize,
                    _settingsManager.FontFamily,
                    GetFontWeightForFamily,
                    DoesAozoraImageExist));
        }

        internal void AozoraTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
        }

        internal void AozoraTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isAozoraMode) return;

            var ds = args.DrawingSession;
            var size = sender.Size;
            Color textColor = GetVerticalTextColor();

            ds.Clear(GetVerticalBackgroundColor());

            if (_currentAozoraPageInfo.Blocks == null || _currentAozoraPageInfo.Blocks.Count == 0) return;

            var page = _currentAozoraPageInfo;

            var margins = ReaderPageMargins.HorizontalText;
            float availableWidth = (float)size.Width - margins.Horizontal;
            float maxWidth = _isMarkdownRenderMode ? availableWidth : Math.Min(availableWidth, (float)GetUrlMaxWidth());

            var imgBlocks = page.Blocks.Where(b => b.HasImage).ToList();
            if (imgBlocks.Count > 0)
            {
                var src = imgBlocks[0].Inlines.OfType<AozoraImage>().First().Source;
                DrawHorizontalImage(ds, size, src);
                return;
            }

            // ⭐ 새로 분리된 통합 렌더러 호출!
            HorizontalRenderer.RenderBlocks(
                ds: ds,
                blocks: page.Blocks,
                textColor: textColor,
                marginLeft: margins.Left,
                marginTop: margins.Top,
                maxWidth: maxWidth,
                baseFontSize: _settingsManager.FontSize,
                defaultFontFamily: _settingsManager.FontFamily,
                getFontWeight: GetFontWeightForFamily,
                searchQuery: _activeSearchQuery,
                currentSearchMatch: GetActiveSearchMatchFor(DocumentSearchKind.Text),
                renderedSearchKind: DocumentSearchKind.Text,
                firstBlockIndex: _currentAozoraStartBlockIndex
            );
        }

        internal void AozoraTextCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (AozoraTextCanvas == null || !_isAozoraMode) return;
            var pt = e.GetCurrentPoint(AozoraTextCanvas).Position;
            var width = AozoraTextCanvas.ActualWidth;

            // 가로 모드는 좌클릭/터치 시 오른쪽 화면이 다음 페이지
            if (pt.X > width / 2) NavigateAozoraPage(1);
            else NavigateAozoraPage(-1);

            e.Handled = true;
            RootGrid.Focus(FocusState.Programmatic);
        }

        internal void AozoraTextCanvas_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (AozoraTextCanvas == null || !_isAozoraMode) return;
            var delta = e.GetCurrentPoint(AozoraTextCanvas).Properties.MouseWheelDelta;
            if (delta > 0) NavigateAozoraPage(-1);
            else NavigateAozoraPage(1);
            e.Handled = true;
        }

        internal void NavigateAozoraPage(int direction)
        {
            if (_aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

            int? targetIndex = _readerPageNavigationService.GetTargetStartIndex(
                _aozoraPageState,
                _aozoraBlocks.Count,
                direction,
                () =>
                {
                    var layout = _readerLayoutService.CreateHorizontalTextLayout(
                        AozoraTextCanvas?.ActualWidth ?? 0,
                        AozoraTextCanvas?.ActualHeight ?? 0,
                        RootGrid?.ActualWidth ?? 0,
                        RootGrid?.ActualHeight ?? 0,
                        _isMarkdownRenderMode,
                        GetUrlMaxWidth());
                    var device = AozoraTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    return GetOrFindPreviousPageStart(
                        _currentAozoraStartBlockIndex,
                        _aozoraBlocks,
                        layout.MaxWidth,
                        layout.AvailableHeight,
                        device,
                        false);
                });

            if (targetIndex.HasValue)
            {
                _ = RenderAozoraDynamicPage(targetIndex.Value);
                _readerPageNavigationService.AdvanceCalculatedPage(_aozoraPageState, direction);
                UpdateAozoraStatusBar();
            }
        }

        internal void UpdateAozoraStatusBar()
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;

            int startLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
            int totalLines = _aozoraTotalLineCountInSource;
            _readingProgressController.UpdatePagedReader(
                _aozoraPageState,
                startLine,
                totalLines,
                _textReaderState,
                RecentSavePolicy.Always);
        }

        public void JumpToAozoraLine(int targetLine)
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;

            if (_isVerticalMode)
            {
                _ = PrepareVerticalTextAsync(targetLine);
                return;
            }

            int startIdx = _textBlockDocumentService.FindStartBlockIndex(_aozoraBlocks, targetLine);

            _ = RenderAozoraDynamicPage(startIdx);
            StartAozoraPageCalculationAsync(); 
        }


        internal bool DoesAozoraImageExist(string relativePath)
            => _imageResourceService.DoesImageExist(relativePath, CreateViewingContext());


        internal void DrawHorizontalImage(CanvasDrawingSession ds, Size canvasSize, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            string cacheKey = Services.ImageResourceService.GetTextCacheKey(relativePath);
            var bitmap = _imageResourceService.TryGetCached(cacheKey);

            if (bitmap != null)
            {
                try
                {
                    ImageCanvasRenderer.DrawBitmapFit(ds, bitmap, new Rect(0, 0, canvasSize.Width, canvasSize.Height));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Aozora image draw skipped: {ex.Message}");
                }
            }
            else
            {
                _ = LoadAozoraImageAsync(relativePath);
            }
        }

        internal async Task LoadAozoraImageAsync(string relativePath)
        {
            string cacheKey = Services.ImageResourceService.GetTextCacheKey(relativePath);
            var device = AozoraTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

            await LoadImageResourceAndInvalidateAsync(
                relativePath,
                cacheKey,
                device,
                () => AozoraTextCanvas?.Invalidate(),
                () =>
                {
                    if (!_isWebDavMode) return;

                    // WebDAV 누락 이미지: 페이지 재렌더링으로 누락 표시 제거
                    this.DispatcherQueue.TryEnqueue(() =>
                        _ = RenderAozoraDynamicPage(_currentAozoraStartBlockIndex));
                });
        }


        // TOC Handlers

        internal async void TocButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isTextMode && !_isEpubMode) return;

                MainToolbar.SetTextTocTitle(Strings.TocTitle);

                var items = _tocService.CurrentToc;

                // Highlight current item and scroll
                int currentIndex = -1;

                if (_isEpubMode)
                {
                    if (_currentEpubChapterIndex >= 0 && _currentEpubChapterIndex < _epubSpine.Count)
                    {
                        string currentSpinePath = _epubSpine[_currentEpubChapterIndex];
                        for (int i = 0; i < items.Count; i++)
                        {
                            string linkPath = items[i].EpubLink;
                            if (string.IsNullOrEmpty(linkPath)) continue;

                            int hashIndex = linkPath.IndexOf('#');
                            if (hashIndex >= 0) linkPath = linkPath.Substring(0, hashIndex);

                            if (string.Equals(linkPath, currentSpinePath, StringComparison.OrdinalIgnoreCase))
                            {
                                currentIndex = i;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Text / Aozora Mode
                    int currentLine = 1;
                    if (_isVerticalMode)
                    {
                        currentLine = _currentVerticalPageInfo.StartLine;
                    }
                    else if (_isAozoraMode)
                    {
                        if (_currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                            currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                    }
                    else
                    {
                        currentLine = GetTopVisibleLineIndex();
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].SourceLineNumber > 0 && items[i].SourceLineNumber <= currentLine)
                            currentIndex = i;
                        else if (items[i].SourceLineNumber > currentLine)
                            break;
                    }
                }

                // Create a display-only list to avoid modifying the original service list
                var displayItems = items.Select(item => new TocItem 
                { 
                    HeadingText = item.HeadingText, 
                    HeadingLevel = item.HeadingLevel, 
                    SourceLineNumber = item.SourceLineNumber,
                    EpubLink = item.EpubLink,
                    Tag = item.Tag
                }).ToList();

                if (currentIndex >= 0 && currentIndex < displayItems.Count)
                {
                    displayItems[currentIndex].HeadingText = "⮕ " + displayItems[currentIndex].HeadingText;
                }

                if (displayItems.Count == 0)
                {
                    displayItems.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
                }

                MainToolbar.SetTextTocItems(displayItems);

                if (currentIndex >= 0)
                {
                    MainToolbar.ScrollTextTocIntoView(displayItems[currentIndex]);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TocButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal void TocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                MainToolbar.HideTextTocFlyout();

                if (_isEpubMode)
                {
                    if (!string.IsNullOrEmpty(item.EpubLink))
                    {
                        JumpToEpubTocItem(new EpubTocItem { Title = item.HeadingText, Link = item.EpubLink });
                    }
                }
                else if (item.Tag?.ToString() == "PDF")
                {
                    if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageEntries.Count)
                    {
                        _currentIndex = item.SourceLineNumber;
                        _ = DisplayCurrentImageAsync();
                    }
                }
                else if (item.SourceLineNumber > 0)
                {
                    if (_isVerticalMode)
                    {
                        _ = PrepareVerticalTextAsync(item.SourceLineNumber);
                    }
                    else if (_isAozoraMode)
                    {
                        JumpToAozoraLine(item.SourceLineNumber);
                    }
                    else
                    {
                        ScrollToLine(item.SourceLineNumber);
                    }
                }
            }
        }
    }
}
