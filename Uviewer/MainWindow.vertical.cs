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
        private Dictionary<string, CanvasBitmap> _verticalImageCache = new();
        private HashSet<string> _knownMissingVerticalImages = new();
        private int? _pendingVerticalScrollLine = null;
        private int _pendingVerticalStartBlockIndex = -1;

        private struct VerticalPageInfo
        {
            public List<AozoraBindingModel> Blocks;
            public int StartLine;
        }

        // 가상화 렌더링을 위한 단일 페이지 캐시 및 네비게이션 상태
        private VerticalPageInfo _currentVerticalPageInfo;
        private int _currentVerticalStartBlockIndex = 0;
        private int _currentVerticalEndBlockIndex = 0;
        private Stack<int> _verticalNavHistory = new();
        
        // 백그라운드 전체 페이지 계산용 상태
        private int _verticalTotalPages = 0;
        private bool _isVerticalPageCalcCompleted = false;
        private Dictionary<int, int> _verticalBlockToPageMap = new();
        private System.Threading.CancellationTokenSource? _verticalPageCalcCts;
        private int _verticalCalculatedCurrentPage = 1;
        private CancellationTokenSource? _currentVerticalRenderCts;
        
        private void ClearVerticalDisplayState()
        {
            _currentVerticalRenderCts?.Cancel();
            _verticalPageCalcCts?.Cancel();
            _currentVerticalPageInfo = new VerticalPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };
            _currentVerticalStartBlockIndex = 0;
            _currentVerticalEndBlockIndex = 0;
            _verticalNavHistory.Clear();
            _verticalImageCache.Clear();
            _verticalTotalPages = 0;
            _isVerticalPageCalcCompleted = false;
            _pendingVerticalStartBlockIndex = -1;
            _verticalCalculatedCurrentPage = 1;
            ClearBackwardCache();
            VerticalTextCanvas?.Invalidate();
            UpdateVerticalStatusBar();
        }

        private async void VerticalToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Save current position before switching mode
            await AddToRecentAsync(true);
            _isVerticalMode = VerticalToggleButton?.IsChecked ?? false;
            SaveTextSettings();
            ToggleVerticalMode();
        }

        private async void ToggleVerticalMode()
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
                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                if (EpubArea != null) EpubArea.Visibility = Visibility.Collapsed;
                if (TextArea != null) TextArea.Visibility = Visibility.Visible;
                
                int currentLine = 1;

                // [핵심 수정] EPUB 모드 검사를 가장 먼저 수행하여, 전역 설정인 _isAozoraMode가 켜져 있더라도 
                // 무조건 EPUB 챕터 파싱과 페이지 위치 비율 계산을 최우선으로 가져오도록 합니다.
                if (_isEpubMode)
                {
                    var blocks = await GetEpubChapterAsAozoraBlocksAsync(_currentEpubChapterIndex);

                    // [추가] 가로 모드와 동일하게 SideBySide 모드인 경우 다음 챕터의 이미지도 함께 가져옴
                    if (_isSideBySideMode && blocks.Count > 0 && blocks.Any(b => b.HasImage))
                    {
                        int nextIdx = _currentEpubChapterIndex + 1;
                        if (nextIdx < _epubSpine.Count)
                        {
                            var nextBlocks = await GetEpubChapterAsAozoraBlocksAsync(nextIdx);
                            if (nextBlocks.Count > 0 && nextBlocks.Any(b => b.HasImage))
                                blocks.AddRange(nextBlocks);
                        }
                    }
                    _aozoraBlocks = blocks;

                    if (_epubWin2DPages != null && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count)
                    {
                        var page = _epubWin2DPages[_currentEpubPageIndex];
                        if (!page.IsImagePage)
                        {
                            // 텍스트 페이지면 해당 페이지의 시작 줄 번호를 그대로 사용
                            currentLine = page.StartLine;
                            // [추가] 블록 인덱스도 함께 전달하여 정확한 위치 고정
                            _pendingEpubStartBlockIndex = page.StartBlockIndex;
                        }
                        else
                        {
                            // 이미지 페이지면 이미지 블록의 줄 번호를 사용
                            if (_aozoraBlocks != null)
                            {
                                int targetIdx = -1;
                                for (int i = 0; i < _aozoraBlocks.Count; i++)
                                {
                                    if (_aozoraBlocks[i].Inlines.OfType<AozoraImage>().Any(img => img.Source == page.ImagePath))
                                    {
                                        targetIdx = i;
                                        break;
                                    }
                                }
                                
                                if (targetIdx >= 0)
                                {
                                    currentLine = _aozoraBlocks[targetIdx].SourceLineNumber;
                                    // [추가] 이미지 페이지일 때도 블록 인덱스를 정확히 설정
                                    _pendingEpubStartBlockIndex = targetIdx;
                                }
                            }
                        }
                    }
                }
                // EPUB이 아닌 일반 텍스트 모드일 경우
                else if (_isAozoraMode)
                {
                    // [수정] 진행 중인 렌더링이 있다면 해당 목표 라인을 우선 사용
                    if (_aozoraPendingTargetLine > 0) 
                    {
                        currentLine = _aozoraPendingTargetLine;
                        _aozoraPendingTargetLine = 0; // Reset after use
                    }
                    else if (_currentAozoraPageInfo.Blocks != null && _currentAozoraPageInfo.Blocks.Count > 0)
                    {
                        currentLine = _currentAozoraPageInfo.StartLine;
                    }
                    else if (_aozoraBlocks != null && _aozoraBlocks.Count > _currentAozoraStartBlockIndex)
                    {
                        currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                    }
                    else 
                    {
                        currentLine = 1;
                    }
                }
                else if (TextScrollViewer != null) 
                {
                    currentLine = GetTopVisibleLineIndex();
                }

                int targetBlockIdx = _isEpubMode ? _pendingEpubStartBlockIndex : (_isAozoraMode ? _currentAozoraStartBlockIndex : -1);
                await PrepareVerticalTextAsync(currentLine, targetBlockIdx, _globalTextCts?.Token ?? default);
            }
            else
            {
                // [수정] _pendingVerticalScrollLine을 null로 초기화하기 전에 값을 먼저 구출합니다.
                int currentLine = 1;
                int currentBlockIdx = -1;
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

                _pendingVerticalScrollLine = null; // 값을 구출한 뒤에 초기화
                _pendingVerticalStartBlockIndex = -1;

                // Detach vertical key handler
                if (_verticalKeyAttached && RootGrid != null)
                {
                    RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                    _verticalKeyAttached = false;
                }
                
                    if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                if (_isEpubMode)
                {
                    if (EpubArea != null) EpubArea.Visibility = Visibility.Visible;
                    if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                    
                    // [추가] 세로 모드에서 여러 챕터가 섞여 있을 수 있으므로(SBS), 현재 보고 있는 블록의 챕터로 갱신
                    if (_aozoraBlocks != null && currentBlockIdx >= 0 && currentBlockIdx < _aozoraBlocks.Count)
                    {
                        var targetBlock = _aozoraBlocks[currentBlockIdx];
                        if (targetBlock.EpubChapterIndex >= 0)
                        {
                            // 만약 블록의 챕터가 현재 챕터와 다르다면 챕터 인덱스 갱신
                            if (targetBlock.EpubChapterIndex != _currentEpubChapterIndex)
                            {
                                _currentEpubChapterIndex = targetBlock.EpubChapterIndex;
                            }

                            // LoadEpubChapterAsync가 챕터 내 상대 경로 블록 인덱스를 사용하도록 보정
                            int chapterStartIdx = -1;
                            for (int i = 0; i <= currentBlockIdx; i++)
                            {
                                if (_aozoraBlocks[i].EpubChapterIndex == _currentEpubChapterIndex)
                                {
                                    chapterStartIdx = i;
                                    break;
                                }
                            }
                            if (chapterStartIdx >= 0)
                            {
                                currentBlockIdx = currentBlockIdx - chapterStartIdx;
                            }
                        }
                    }

                    double? progress = null;
                    if (_aozoraBlocks != null && _aozoraBlocks.Count > 0)
                    {
                         // 현재 챕터 기준의 진행률을 구하도록 수정 (전체 combined 블록이 아님)
                         var chapterBlocks = _aozoraBlocks.Where(b => b.EpubChapterIndex == _currentEpubChapterIndex).ToList();
                         if (chapterBlocks.Count > 0)
                         {
                             int currentRelIdx = currentBlockIdx >= 0 ? Math.Min(currentBlockIdx, chapterBlocks.Count - 1) : 0;
                             progress = (double)currentRelIdx / chapterBlocks.Count;
                         }
                    }

                    await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, targetBlockIndex: currentBlockIdx, progress: progress);
                }
                else if (_isAozoraMode)
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
            UpdateTextStatusBar();
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
            _verticalNavHistory.Clear();
            _pendingVerticalScrollLine = targetLine;
            _pendingVerticalStartBlockIndex = targetBlockIndex;
            ClearBackwardCache(); // <-- 캐시 초기화 추가

            // [수정] 이전 챕터의 잔여 데이터로 인한 무한 루프 방지를 위해 시작/끝 인덱스만 초기화하고,
            // _currentVerticalPageInfo는 실제 새 페이지 렌더링 직전에 교체하여 깜박임을 방지합니다.
            _currentVerticalStartBlockIndex = 0;
            _currentVerticalEndBlockIndex = 0;

            try
            {
                if (token.IsCancellationRequested) return;
                // [수정] EPUB 모드일 때는 텍스트 모드용 파싱과 MD 감지 로직을 건너뜁니다.
                if (!_isEpubMode && (_aozoraBlocks == null || _aozoraBlocks.Count == 0))
                {
                    _aozoraBlocks = await Task.Run(() => 
                    {
                        List<AozoraBindingModel> blocks;
                        int lineCount;

                        if (_isMarkdownRenderMode)
                        {
                            var result = AozoraParserService.ParseMarkdownContent(_currentTextContent);
                            blocks = result.Blocks;
                            lineCount = result.SourceLineCount;
                        }
                        else
                        {
                            var result = AozoraParserService.ParseAozoraContent(_currentTextContent, _settingsManager.FontSize);
                            blocks = result.Blocks;
                            lineCount = result.SourceLineCount;
                        }

                        DispatcherQueue.TryEnqueue(() => { _textTotalLineCountInSource = lineCount; });
                        return blocks;
                    });
                }

                int startIdx = 0;

                // [수정] 이전 챕터로 돌아올 때(fromEnd: true) 마지막 페이지를 온전하게 계산하여 표시
// [수정] 이전 챕터로 돌아올 때(fromEnd: true) 마지막 페이지를 온전하게 계산하여 표시
                if (targetLine == 999999 && _aozoraBlocks.Count > 0)
                {
                    int targetIdx = _aozoraBlocks.Count;
                    float availWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1000) - 40;
                    float availHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800) - 40;
                    var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    // 기존 while 루프를 지우고 단 한 줄로 교체
                    startIdx = FindPreviousPageStart(targetIdx, _aozoraBlocks, availWidth, availHeight, device, true);
                }
                 else if (targetLine < 0) 
                {
                    startIdx = Math.Max(0, _aozoraBlocks.Count - 15);
                }
                else if (targetBlockIndex >= 0)
                {
                    startIdx = Math.Clamp(targetBlockIndex, 0, _aozoraBlocks.Count - 1);
                }
                else if (_aozoraBlocks.Count > 0)
                {
                    // [수정] O(N) 선형 탐색을 O(log N) 이진 탐색으로 변경하여 즉시 탐색
                    int left = 0;
                    int right = _aozoraBlocks.Count - 1;
                    
                    while (left <= right)
                    {
                        int mid = left + (right - left) / 2;
                        if (_aozoraBlocks[mid].SourceLineNumber == targetLine)
                        {
                            startIdx = mid;
                            // [수정] 동일한 라인의 여러 조각 중 가장 첫 번째 조각을 찾기 위해 탐색을 멈추지 않고 계속 진행
                            right = mid - 1; 
                        }
                        else if (_aozoraBlocks[mid].SourceLineNumber < targetLine)
                        {
                            startIdx = mid; // 현재까지 발견된 가장 가까운 인덱스 기록
                            left = mid + 1;
                        }
                        else
                        {
                            right = mid - 1;
                        }
                    }
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
                _currentVerticalPageInfo = new VerticalPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };
                if (token.IsCancellationRequested) return;
                if (VerticalTextCanvas != null) VerticalTextCanvas.Invalidate();
                UpdateVerticalStatusBar();
                return;
            }

            if (VerticalTextCanvas == null) return;
            
            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentVerticalStartBlockIndex = startIdx;

            // [수정] 상하 20으로 줄여 글줄 길이를 확보하고, 왼쪽 마진(진행 방향)을 10으로 최소화.
            float marginTop = 20, marginBottom = 20, marginRight = 30, marginLeft = 10;
            float availableHeight = (float)VerticalTextCanvas.ActualHeight;
            if (availableHeight < 100) availableHeight = (float)RootGrid.ActualHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            availableHeight -= (marginTop + marginBottom); 
            
            float availableWidth = (float)VerticalTextCanvas.ActualWidth;
            if (availableWidth < 100) availableWidth = (float)RootGrid.ActualWidth - 100;
            if (availableWidth < 100) availableWidth = 1000;
            availableWidth -= (marginRight + marginLeft);

            int index = startIdx;
            
            // [핵심 수정] Device가 null이면 측정 로직이 고장나서 글자가 겹치거나 왼쪽 밖으로 잘립니다.
            // CanvasControl의 Device를 가져오고, 없으면 시스템 공용 Device를 강제로라도 가져와야 합니다.
            var device = VerticalTextCanvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
            
            // 디바이스를 정확히 넘겨주어 페이지를 측정합니다.
            var pageBlocks = PaginateAozoraPage(ref index, _aozoraBlocks, availableWidth, availableHeight, device);
            if (token.IsCancellationRequested) return;

            _currentVerticalEndBlockIndex = index > startIdx ? index - 1 : startIdx;

            // [이미지 프리로딩] EPUB 등에서 이미지 교체 시 깜박임을 방지하기 위해 렌더링 전 이미지를 미리 로드합니다.
            if (pageBlocks.Any(b => b.HasImage))
            {
                var loadTasks = pageBlocks.Where(b => b.HasImage)
                    .Select(async b => 
                    {
                        var src = b.Inlines.OfType<AozoraImage>().FirstOrDefault()?.Source;
                        if (!string.IsNullOrEmpty(src) && !_verticalImageCache.ContainsKey(src))
                        {
                            await LoadVerticalImageAsync(src);
                        }
                    }).ToList();
                await Task.WhenAll(loadTasks);
            }
            if (token.IsCancellationRequested) return;
            
            _currentVerticalPageInfo = new VerticalPageInfo 
            { 
                Blocks = pageBlocks, 
                StartLine = pageBlocks.Count > 0 ? pageBlocks[0].SourceLineNumber : 1 
            };

            VerticalTextCanvas.Invalidate();
            UpdateVerticalStatusBar();
            StartBackwardPageCaching(_currentVerticalStartBlockIndex, true); // <-- 현재 페이지 렌더링 직후 백그라운드 캐싱 시작
        }

        private async void StartVerticalPageCalculationAsync()
        {
            if (!_isVerticalMode || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;
            
            _verticalPageCalcCts?.Cancel();
            _verticalPageCalcCts = new System.Threading.CancellationTokenSource();
            var token = _verticalPageCalcCts.Token;

            _isVerticalPageCalcCompleted = false;
            _verticalTotalPages = 0;
            _verticalCalculatedCurrentPage = 1;

            if (VerticalTextCanvas == null || VerticalTextCanvas.ActualHeight <= 0 || VerticalTextCanvas.ActualWidth <= 0) return;

            // [수정] 백그라운드 계산도 상하 40, 좌우 40 공간 차감으로 맞춥니다.
            float availableHeight = (float)VerticalTextCanvas.ActualHeight - 40;
            float availableWidth = (float)VerticalTextCanvas.ActualWidth - 40;
            var device = VerticalTextCanvas.Device;

            try
            {
                await Task.Run(async () =>
                {
                    int pageCount = 1;
                    float currentPageWidth = 0;
                    var blockToPageMap = new Dictionary<int, int>();
                    AozoraBindingModel? currentMergedBlock = null;
                    float currentMergedBlockWidth = 0;

                    for (int i = 0; i < _aozoraBlocks.Count; i++)
                    {
                        if (token.IsCancellationRequested) return;
                        
                        var block = _aozoraBlocks[i];

                        // 페이지 시작 부분의 빈 줄(공백) 건너뛰기 - 빈 페이지 방지
                        if (currentPageWidth == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                        {
                            blockToPageMap[i] = pageCount;
                            continue;
                        }

                        if (block.HasImage || block.IsPageBreak)
                        {
                            if (currentPageWidth > 0)
                            {
                                pageCount++;
                                currentPageWidth = 0;
                            }

                            blockToPageMap[i] = pageCount;
                            
                            if (block.IsPageBreak)
                            {
                                if (i < _aozoraBlocks.Count - 1)
                                {
                                    pageCount++;
                                }
                                currentMergedBlock = null;
                                currentMergedBlockWidth = 0;
                                continue;
                            }

                            // 이미지 존재 여부를 확인하여 없는 경우 페이지로 계산하지 않음
                            var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                            if (aozoraImg != null && !DoesVerticalImageExist(aozoraImg.Source))
                            {
                                continue;
                            }

                            if (i < _aozoraBlocks.Count - 1)
                            {
                                pageCount++;
                            }
                            currentMergedBlock = null;
                            currentMergedBlockWidth = 0;
                            continue;
                        }

                        // 💡 문단 이어붙이기(Paragraph Continuation) 논리 추가
                        if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                        {
                            var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                            tempMerged.Inlines.AddRange(block.Inlines);

                            float fontSize = (float)(_settingsManager.FontSize * tempMerged.FontSizeScale);
                            float newWidth = MeasureVerticalBlockWidth(device, tempMerged, availableHeight, fontSize);
                            float widthDiff = newWidth - currentMergedBlockWidth;
                            float leftToleranceMerged = fontSize * 0.8f;

                            if (currentPageWidth + widthDiff > (availableWidth + leftToleranceMerged) && currentPageWidth > 0)
                            {
                                pageCount++;
                                currentPageWidth = 0;
                            }
                            else
                            {
                                // 병합 성공
                                blockToPageMap[i] = pageCount;
                                currentPageWidth += widthDiff;
                                currentMergedBlock = tempMerged;
                                currentMergedBlockWidth = newWidth;
                                continue;
                            }
                        }

                        float fontSizeBase = (float)(_settingsManager.FontSize * block.FontSizeScale);
                        float blockWidth = MeasureVerticalBlockWidth(device, block, availableHeight, fontSizeBase);
                        
                        // 💡 테두리(Keigakomi) 마진 논리 추가
                        bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                        bool wasKeigakomi = currentMergedBlock != null && (currentMergedBlock.BorderColor != null || currentMergedBlock.BorderThickness.Top > 0);

                        if (isKeigakomi && !wasKeigakomi) blockWidth += 20f;
                        if (!isKeigakomi && wasKeigakomi) blockWidth += 20f;

                        // [수정] 세로 모드는 Tolerance를 0.8배로 허용
                        float leftTolerance = fontSizeBase * 0.8f;
                        if (currentPageWidth > 0 && currentPageWidth + blockWidth > (availableWidth + leftTolerance))
                        {
                            pageCount++;
                            currentPageWidth = 0;
                        }

                        blockToPageMap[i] = pageCount;
                        currentPageWidth += blockWidth;

                        if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                        {
                            currentMergedBlock = block;
                            currentMergedBlockWidth = blockWidth;
                        }
                        else
                        {
                            currentMergedBlock = null;
                        }
                        
                        if (i % 50 == 0) await Task.Delay(1, token);
                    }
                    
                    if (token.IsCancellationRequested) return;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _verticalBlockToPageMap = blockToPageMap;
                        _verticalTotalPages = pageCount;
                        _isVerticalPageCalcCompleted = true;
                        
                        if (_verticalBlockToPageMap.TryGetValue(_currentVerticalStartBlockIndex, out int cp))
                            _verticalCalculatedCurrentPage = cp;
                        else
                            _verticalCalculatedCurrentPage = 1;
                            
                        UpdateVerticalStatusBar();
                    });
                }, token);
            }
            catch { }
        }

        private List<AozoraBindingModel> PaginateAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, Microsoft.Graphics.Canvas.CanvasDevice? device = null)
        {
            var pageBlocks = new List<AozoraBindingModel>();
            float usedWidth = 0;

            AozoraBindingModel? currentMergedBlock = null;
            float currentMergedBlockWidth = 0;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                // [추가] 페이지 시작 시 빈 줄(공백) 건너뛰기 - 빈 페이지 방지
                if (pageBlocks.Count == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                {
                    index++;
                    continue;
                }

                if (block.HasImage || block.IsPageBreak)
                {
                    // If the current page already has content, break to start a new page.
                    // Otherwise, if it's a page break at the start of a page, just skip it.
                    if (pageBlocks.Count > 0) break;

                    if (block.IsPageBreak)
                    {
                        index++;
                        continue; // Skip the page break block itself if at the start of a page
                    }

                    // Handle image block
                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null && !DoesVerticalImageExist(aozoraImg.Source))
                    {
                        index++;
                        continue;
                    }
                    
                    pageBlocks.Add(block);
                    index++;
                    currentMergedBlock = null;

                    if (_isEpubMode && _isSideBySideMode)
                    {
                        while (index < blocks.Count)
                        {
                            var nextBlock = blocks[index];
                            if (nextBlock.HasImage)
                            {
                                var nextImg = nextBlock.Inlines.OfType<AozoraImage>().FirstOrDefault();
                                if (nextImg != null && DoesVerticalImageExist(nextImg.Source))
                                {
                                    pageBlocks.Add(nextBlock);
                                    index++;
                                    break; 
                                }
                            }
                            bool isWhitespace = nextBlock.Inlines.All(inline => 
                                (inline is string s && string.IsNullOrWhiteSpace(s)) || (inline is AozoraLineBreak));
                            
                            if (isWhitespace) { index++; continue; }
                            break; 
                        }
                    }
                    break;
                }

                // 1. 이어지는 문장 합치기 시도
                if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                {
                    var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                    tempMerged.Inlines.AddRange(block.Inlines); // 기존 문단에 텍스트 이어붙이기

                    float fontSize = (float)(_settingsManager.FontSize * tempMerged.FontSizeScale);
                    float newWidth = MeasureVerticalBlockWidth(device, tempMerged, availableHeight, fontSize);
                    float widthDiff = newWidth - currentMergedBlockWidth;
                    
                    // [수정] Tolerance 0.8배 적용
                    float leftToleranceMerged = fontSize * 0.8f;

                    if (usedWidth + widthDiff > (availableWidth + leftToleranceMerged) && pageBlocks.Count > 0)
                    {
                        break; 
                    }

                    // 병합 성공: 현재 페이지 마지막 블록을 교체
                    pageBlocks[pageBlocks.Count - 1] = tempMerged;
                    currentMergedBlock = tempMerged;
                    usedWidth += widthDiff;
                    currentMergedBlockWidth = newWidth;
                    index++;
                    continue;
                }

                // 2. 새 문단 또는 일반 블록
                float fontSizeBase = (float)(_settingsManager.FontSize * block.FontSizeScale);
                float blockWidth = MeasureVerticalBlockWidth(device, block, availableHeight, fontSizeBase);
                // [수정]
                float leftTolerance = fontSizeBase * 0.8f;

                // [추가] 박스 진입/이탈 시 양옆 여백(Padding)을 페이지 너비에 반영
                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                bool wasKeigakomi = pageBlocks.Count > 0 && (pageBlocks[pageBlocks.Count - 1].BorderColor != null || pageBlocks[pageBlocks.Count - 1].BorderThickness.Top > 0);

                if (isKeigakomi && !wasKeigakomi) blockWidth += 20f; // 박스 시작 우측 여백
                if (!isKeigakomi && wasKeigakomi) blockWidth += 20f; // 박스 종료 좌측 여백

                if (pageBlocks.Count > 0 && usedWidth + blockWidth > (availableWidth + leftTolerance))
                {
                    break; 
                }

                var blockCopy = AozoraParserService.CloneBlockProperties(block, true);
                pageBlocks.Add(blockCopy);
                usedWidth += blockWidth;

                if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                {
                    currentMergedBlock = blockCopy;
                    currentMergedBlockWidth = blockWidth;
                }
                else
                {
                    currentMergedBlock = null;
                }

                index++;
                if (usedWidth >= (availableWidth + leftTolerance)) break;
            }
            return pageBlocks;
        }

        private float MeasureVerticalBlockWidth(CanvasDevice? device, AozoraBindingModel block, float availableHeight, float fontSize)
        {
            // 👉 줄 번호와 내부 요소 개수, 그리고 첫 요소의 해시를 조합해 고유 키 생성
            // 복제된 블록이라도 같은 내용을 담고 있다면 완벽히 캐시 적중됩니다.
            int contentHash = block.Inlines.Count > 0 ? block.Inlines[0].GetHashCode() : 0;
            var cacheKey = new BlockCacheKey(block.SourceLineNumber, block.Inlines.Count, contentHash);
            
            if (_blockMeasureCache.TryGetValue(cacheKey, out float cachedWidth))
                return cachedWidth;

            if (device == null) return fontSize * 2.0f;

            // Build text same as in Draw method
            StringBuilder sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(VerticalRenderer.NormalizeVerticalText(s));
                else if (inline is AozoraRuby ruby)
                {
                    var normBase = VerticalRenderer.NormalizeVerticalText(ruby.BaseText);
                    sb.Append(normBase);
                    if (ruby.IsBold) boldRanges.Add((start, normBase.Length));
                }
                else if (inline is AozoraBold bold)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(bold.Text);
                    sb.Append(normText);
                    boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraItalic italic)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(italic.Text);
                    sb.Append(normText);
                    italicRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraCode code) sb.Append(VerticalRenderer.NormalizeVerticalText(code.Text));
                else if (inline is AozoraTCY tcy)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(tcy.Text);
                    sb.Append(normText);
                    if (tcy.IsBold) boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraLineBreak) sb.Append("\n");
            }
            if (block.IsTable && block.TableRows.Count > 0)
            {
                foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
            }

            string text = sb.ToString();
            if (string.IsNullOrEmpty(text)) text = " ";

            using var format = new CanvasTextFormat
            {
                FontSize = fontSize,
                FontFamily = block.FontFamily ?? _settingsManager.FontFamily,
                FontWeight = GetFontWeightForFamily(block.FontFamily ?? _settingsManager.FontFamily),
                Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                WordWrapping = CanvasWordWrapping.EmergencyBreak,
                LineSpacing = fontSize * 1.8f,
                VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };

            // Using same measureWidth (fontSize * 2f) as in Draw method
            using var layout = new CanvasTextLayout(device, text, format, fontSize * 2.0f, availableHeight);
            
            if (block.IsBold) layout.SetFontWeight(0, text.Length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in boldRanges) layout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in italicRanges) layout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);           
            float boundsWidth = (float)layout.LayoutBounds.Width;
            float spacing = fontSize * (block.IsBlankLine ? 0.2f : 0.6f);
            
            float result = boundsWidth + spacing;
            if (block.IsBlankLine) result = (boundsWidth * 0.5f) + spacing;
            
            // 👉 절대 .Count 검사 없이 바로 저장
            _blockMeasureCache[cacheKey] = result;

            return result;
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
            
            float marginTop = 20;
            float marginBottom = 20;
            float marginRight = 30;
            float marginLeft = 10;

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
                marginTop: marginTop,
                marginBottom: marginBottom,
                marginRight: marginRight,
                marginLeft: marginLeft,
                baseFontSize: _settingsManager.FontSize,
                defaultFontFamily: _settingsManager.FontFamily,
                getFontWeight: GetFontWeightForFamily
            );
        }

        private Color GetVerticalTextColor()
        {
            if (_settingsManager.ThemeIndex == 2) return Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204); // Dark theme matching GetThemeForeground
            if (_settingsManager.ThemeIndex == 3 && _settingsManager.CustomForegroundColor.HasValue) return _settingsManager.CustomForegroundColor.Value;
            return Colors.Black; // Light and Beige themes
        }

        private Color GetVerticalBackgroundColor()
        {
            if (_settingsManager.ThemeIndex == 0) return Colors.White;
            if (_settingsManager.ThemeIndex == 1) return Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235); // Beige
            if (_settingsManager.ThemeIndex == 3 && _settingsManager.CustomBackgroundColor.HasValue) return _settingsManager.CustomBackgroundColor.Value;
            return Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30); // Dark
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
            var blocks = _aozoraBlocks;
            bool isEmpty = blocks == null || blocks.Count == 0;
            if (isEmpty && !_isEpubMode) return;

            if (direction > 0) // 다음 페이지
            {
                if (blocks != null && _currentVerticalEndBlockIndex < blocks.Count - 1)
                {
                    // 💡 History Push 완전히 제거됨
                    await RenderVerticalDynamicPageAsync(_currentVerticalEndBlockIndex + 1);
                    if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage++; UpdateVerticalStatusBar(); }
                }
                else if (_isEpubMode)
                {
                    int maxChapterOnPage = _currentEpubChapterIndex;
                    var pageBlocks = _currentVerticalPageInfo.Blocks;
                    if (pageBlocks != null && pageBlocks.Count > 0)
                        maxChapterOnPage = pageBlocks.Max(b => b.EpubChapterIndex);

                    if (maxChapterOnPage < _epubSpine.Count - 1)
                    {
                        _currentEpubChapterIndex = maxChapterOnPage + 1;
                        await LoadEpubChapterAsync(_currentEpubChapterIndex);
                    }
                }
            }
            else if (direction < 0) // 이전 페이지
            {
                if (blocks != null && _currentVerticalStartBlockIndex > 0)
                {
                    int targetIdx = _currentVerticalStartBlockIndex;
                    int bestStart = 0;

                    float availWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1000) - 40;
                    float availHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800) - 40;
                    var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    // 💡 이동 전 캐시 유효성 철저히 검증
                    ValidateBackwardCache(availWidth, availHeight, _settingsManager.FontSize, true, targetIdx);

                    lock (_backwardPageCache)
                    {
                        if (!_backwardPageCache.TryGetValue(targetIdx, out bestStart))
                        {
                            bestStart = FindPreviousPageStart(targetIdx, blocks, availWidth, availHeight, device, true);
                            _backwardPageCache[targetIdx] = bestStart;
                        }
                    }

                    await RenderVerticalDynamicPageAsync(bestStart);
                    if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage = Math.Max(1, _verticalCalculatedCurrentPage - 1); UpdateVerticalStatusBar(); }
                }
                else if (_isEpubMode && _currentEpubChapterIndex > 0)
                {
                    // 👉 버그 수정: 현재 페이지에 여러 챕터가 섞여 있을 경우 가장 앞선 챕터 기준
                    int minChapterOnPage = _currentEpubChapterIndex;
                    var pageBlocks = _currentVerticalPageInfo.Blocks;
                    if (pageBlocks != null && pageBlocks.Count > 0)
                        minChapterOnPage = pageBlocks.Min(b => b.EpubChapterIndex);

                    if (minChapterOnPage > 0)
                    {
                        // SideBySide 모드인 경우 2챕터 이전으로 이동하여 겹침 방지
                        _currentEpubChapterIndex = _isSideBySideMode ? Math.Max(0, minChapterOnPage - 2) : minChapterOnPage - 1;
                        await LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: true);
                    }
                }
            }
        }

        private void UpdateVerticalStatusBar()
        {
            if (!_isVerticalMode || _currentVerticalPageInfo.Blocks == null) return;

            int totalPages = _isVerticalPageCalcCompleted ? _verticalTotalPages : 0;
            int currentPage = _isVerticalPageCalcCompleted ? _verticalCalculatedCurrentPage : 1;
            int currentLine = _currentVerticalPageInfo.StartLine;
            
            // 기존의 Split 코드를 아래처럼 변경합니다.
            int totalLines = _textTotalLineCountInSource;
            if (totalLines <= 1 && !string.IsNullOrEmpty(_currentTextContent))
            {
                int count = 1;
                foreach (char c in _currentTextContent)
                {
                    if (c == '\n') count++;
                }
                totalLines = count;
                _textTotalLineCountInSource = totalLines;
            }
            
            if (_isEpubMode)
            {
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);
                double totalProgress = 0;
                if (_epubSpine.Count > 0)
                {
                    double chapterProgress = (double)_currentEpubChapterIndex / _epubSpine.Count;
                    double pageProgressInChapter = totalPages > 0 ? (double)(currentPage - 1) / totalPages / _epubSpine.Count : 0;
                    totalProgress = (chapterProgress + pageProgressInChapter) * 100.0;
                    if (totalProgress > 100) totalProgress = 100;
                }
                TextProgressText.Text = $"{totalProgress:F1}%";
                ImageIndexText.Text = totalPages > 0 ? $"{currentPage} / {totalPages} (Ch.{_currentEpubChapterIndex + 1})" : $"계산 중... (Ch.{_currentEpubChapterIndex + 1})";
            }
            else
            {
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);

                // Start-based line progress
                double progress = totalLines > 1 ? (double)(currentLine - 1) / (totalLines - 1) * 100.0 : 100.0;
                if (progress > 100) progress = 100;
                if (progress < 0) progress = 0;
                TextProgressText.Text = $"{progress:F1}%";

                if (_isVerticalPageCalcCompleted)
                {
                    // 점프(Home/End/이동) 시에도 페이지 번호 즉시 반영
                    if (_verticalBlockToPageMap != null && _verticalBlockToPageMap.TryGetValue(_currentVerticalStartBlockIndex, out int mappedPage))
                    {
                        _verticalCalculatedCurrentPage = mappedPage;
                    }

                    ImageIndexText.Text = $"{_verticalCalculatedCurrentPage} / {totalPages}";
                }
                else
                {
                    ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
                }
            }

            if (currentLine != _lastRecentSaveLine)
            {
                _lastRecentSaveLine = currentLine;
                _ = AddToRecentAsync(true);
            }
        }

        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isVerticalMode && (!string.IsNullOrEmpty(_currentTextContent) || _isEpubMode))
            {
                ClearBackwardCache(); // <-- 화면 크기 변경 시 캐시 지우기
                var blocks = _aozoraBlocks;
                if (_isEpubMode && (blocks == null || blocks.Count == 0)) return;

                int currentLine = 1;
                int currentBlockIdx = -1;
                if (_pendingVerticalScrollLine.HasValue)
                {
                    currentLine = _pendingVerticalScrollLine.Value;
                    currentBlockIdx = _pendingVerticalStartBlockIndex;
                }
                else if (_currentVerticalPageInfo.Blocks != null)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                    currentBlockIdx = _currentVerticalStartBlockIndex;
                }
                _ = PrepareVerticalTextAsync(currentLine, currentBlockIdx);
            }
        }

        private async void RootGrid_Vertical_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
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

            if (_verticalImageCache.TryGetValue(path, out var bitmap))
            {
                if (bitmap == null) return;

                float canvasW = (float)rect.Width;
                float canvasH = (float)rect.Height;
                float imgW = (float)bitmap.Size.Width;
                float imgH = (float)bitmap.Size.Height;

                float scale = Math.Min(canvasW / imgW, canvasH / imgH);

                float drawW = imgW * scale;
                float drawH = imgH * scale;

                float drawX = (float)rect.X + (canvasW - drawW) / 2;
                if (align == HorizontalAlignment.Left) drawX = (float)rect.X;
                else if (align == HorizontalAlignment.Right) drawX = (float)rect.X + (canvasW - drawW);

                float drawY = (float)rect.Y + (canvasH - drawH) / 2;

                ds.DrawImage(bitmap, new Rect(drawX, drawY, (float)drawW, (float)drawH), bitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
            }
            else
            {
                _verticalImageCache[path] = null!;
                _ = LoadVerticalImageAsync(path);
            }
        }


        private bool DoesVerticalImageExist(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            try
            {
                if (_isEpubMode && _currentEpubArchive != null)
                {
                    string normPath = relativePath.Replace('\\', '/');
                    return _currentEpubArchive.Entries.Any(e =>
                        e.FullName.Replace('\\', '/') == normPath ||
                        string.Equals(e.FullName.Replace('\\', '/'), normPath, StringComparison.OrdinalIgnoreCase));
                }
                if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    if (_knownMissingVerticalImages.Contains(relativePath)) return false;

                    // If it's in the same folder, we can check _imageEntries for faster initial feedback
                    if (_imageEntries != null && !relativePath.Contains('/') && !relativePath.Contains('\\'))
                    {
                        string? fullRemotePath = ResolveWebDavImagePath(relativePath);
                        if (fullRemotePath != null)
                        {
                            if (!_imageEntries.Any(e => e.WebDavPath == fullRemotePath ||
                                string.Equals(e.WebDavPath, fullRemotePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                return false; // Definitely missing from current folder
                            }
                        }
                    }

                    return true;
                }
                if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextArchiveEntryKey == null)
                {
                    // Local File
                    string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentTextFilePath)!, relativePath);
                    return System.IO.File.Exists(fullPath);
                }
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
                {
                    // Archive
                    string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                    string? baseDir = "";
                    int lastSlash = normKey.LastIndexOf('/');
                    if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                    string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                    string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                    targetKey = targetKey.Replace("/./", "/");

                    if (_currentArchive != null)
                    {
                        return _currentArchive.Entries.Any(e => e.Key != null &&
                               (e.Key.Replace('\\', '/') == targetKey ||
                                string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (_current7zArchive != null)
                    {
                        return _current7zArchive.Entries.Any(e => e.FileName != null &&
                               (e.FileName.Replace('\\', '/') == targetKey ||
                                string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
            catch { }
            return false;
        }

        private async Task LoadVerticalImageAsync(string relativePath)
        {
            try
            {
                byte[]? bytes = null;

                if (_isEpubMode && _currentEpubArchive != null)
                {
                    string normPath = relativePath.Replace('\\', '/');
                    var entry = _currentEpubArchive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == normPath)
                             ?? _currentEpubArchive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), normPath, StringComparison.OrdinalIgnoreCase));

                    if (entry != null)
                    {
                        await _epubArchiveLock.WaitAsync();
                        try
                        {
                            using var s = entry.Open();
                            using var ms = new System.IO.MemoryStream();
                            await s.CopyToAsync(ms);
                            bytes = ms.ToArray();
                        }
                        finally { _epubArchiveLock.Release(); }
                    }
                }
                else if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    string? fullRemotePath = ResolveWebDavImagePath(relativePath);
                    if (fullRemotePath != null)
                    {
                        var tempPath = await _webDavService.DownloadToTempFileAsync(fullRemotePath);
                        if (!string.IsNullOrEmpty(tempPath) && System.IO.File.Exists(tempPath))
                        {
                            bytes = await System.IO.File.ReadAllBytesAsync(tempPath);
                        }
                        else
                        {
                            // Mark as missing to prevent empty page on re-calculation
                            _knownMissingVerticalImages.Add(relativePath);
                            
                            // Re-calculate Vertical pages
                            DispatcherQueue.TryEnqueue(() => 
                            {
                                int currentLine = 1;
                                if (_aozoraBlocks.Count > 0 && _currentVerticalStartBlockIndex >= 0 && _currentVerticalStartBlockIndex < _aozoraBlocks.Count)
                                    currentLine = _aozoraBlocks[_currentVerticalStartBlockIndex].SourceLineNumber;
                                
                                _ = PrepareVerticalTextAsync(currentLine, -1, _globalTextCts?.Token ?? default);
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextArchiveEntryKey == null)
                {
                    // Local File
                    string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentTextFilePath)!, relativePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    }
                }
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
                {
                    // Archive
                    string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                    string? baseDir = "";
                    int lastSlash = normKey.LastIndexOf('/');
                    if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                    string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                    string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                    targetKey = targetKey.Replace("/./", "/");

                    await _archiveLock.WaitAsync();
                    try
                    {
                        if (_currentArchive != null)
                        {
                            var entry = _currentArchive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace('\\', '/') == targetKey)
                                     ?? _currentArchive.Entries.FirstOrDefault(e => e.Key != null && string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase));

                            if (entry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                using var es = entry.OpenEntryStream();
                                es.CopyTo(ms);
                                bytes = ms.ToArray();
                            }
                        }
                        else if (_current7zArchive != null)
                        {
                            var entry = _current7zArchive.Entries.FirstOrDefault(e => e.FileName != null && e.FileName.Replace('\\', '/') == targetKey)
                                     ?? _current7zArchive.Entries.FirstOrDefault(e => e.FileName != null && string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase));

                            if (entry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                entry.Extract(ms);
                                bytes = ms.ToArray();
                            }
                        }
                    }
                    finally { _archiveLock.Release(); }
                }

                if (bytes != null)
                {
                     // We need the CanvasDevice from the canvas control
                     // Usually we should have it since pagination happened.
                     if (VerticalTextCanvas == null) return;

                     var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                     using (var writer = new Windows.Storage.Streams.DataWriter(winrtStream))
                     {
                         writer.WriteBytes(bytes);
                         await writer.StoreAsync();
                         await writer.FlushAsync();
                         writer.DetachStream();
                     }
                     winrtStream.Seek(0);

                     // Switch to UI thread or Canvas session thread? 
                     // CanvasBitmap.LoadAsync can be called anywhere if we have device.
                     var device = VerticalTextCanvas.Device;
                     var originalBitmap = await CanvasBitmap.LoadAsync(device, winrtStream);
                     var finalBitmap = originalBitmap;

                     if (_sharpenEnabled)
                     {
                         var sharpened = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);
                         if (sharpened != null && sharpened != originalBitmap)
                         {
                             finalBitmap = sharpened;
                             // Note: ApplySharpenToBitmapAsync might already dispose original if it returned a new one
                             // but we keep the logic consistent.
                         }
                     }

                     this.DispatcherQueue.TryEnqueue(() => 
                     {
                         _verticalImageCache[relativePath] = finalBitmap;
                         VerticalTextCanvas.Invalidate(); // Redraw with image
                     });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadVerticalImageAsync failed: {ex.Message}");
            }
        }

    }
}
