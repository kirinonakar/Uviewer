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

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isVerticalMode = false;
        private bool _verticalKeyAttached = false;
        private Dictionary<string, CanvasBitmap> _verticalImageCache = new();
        private int? _pendingVerticalScrollLine = null;

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
        private System.Threading.CancellationTokenSource? _verticalPageCalcCts;
        private int _verticalCalculatedCurrentPage = 1;

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
                    _aozoraBlocks = await GetEpubChapterAsAozoraBlocksAsync(_currentEpubChapterIndex);

                    if (_epubPages != null && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubPages.Count)
                    {
                        var page = _epubPages[_currentEpubPageIndex];
                        if (page is Grid g)
                        {
                            if (g.Tag is EpubPageInfoTag tag)
                            {
                                if (tag.TotalLinesInChapter > 0 && _aozoraBlocks.Count > 0)
                                {
                                    int maxSourceLine = _aozoraBlocks[_aozoraBlocks.Count - 1].SourceLineNumber;
                                    double ratio = (double)tag.StartLine / tag.TotalLinesInChapter;
                                    if (ratio > 1.0) ratio = 1.0;
                                    
                                    currentLine = (int)(maxSourceLine * ratio);
                                    if (currentLine < 1) currentLine = 1;
                                }
                                else
                                {
                                    currentLine = tag.StartLine;
                                }
                            }
                            else if (g.Tag is EpubImageTag imgTag)
                            {
                                if (_aozoraBlocks != null)
                                {
                                    var targetBlock = _aozoraBlocks.FirstOrDefault(b => 
                                        b.Inlines.OfType<AozoraImage>().Any(img => img.Source == imgTag.FullPath));
                                    
                                    if (targetBlock != null)
                                    {
                                        currentLine = targetBlock.SourceLineNumber;
                                    }
                                }
                            }
                        }
                    }
                }
                // EPUB이 아닌 일반 텍스트 모드일 경우
                else if (_isAozoraMode)
                {
                    currentLine = (_aozoraBlocks != null && _aozoraBlocks.Count > _currentAozoraStartBlockIndex) ? _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber : 1;
                }
                else if (TextScrollViewer != null) 
                {
                    currentLine = GetTopVisibleLineIndex();
                }

                await PrepareVerticalTextAsync(currentLine, _globalTextCts?.Token ?? default);
            }
            else
            {
                _pendingVerticalScrollLine = null;
                // Detach vertical key handler
                if (_verticalKeyAttached && RootGrid != null)
                {
                    RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                    _verticalKeyAttached = false;
                }
                
                // [안전 장치] Blocks가 비어있을 때 발생하는 오류 방지
                int currentLine = _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0 ? _currentVerticalPageInfo.StartLine : 1;

                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                if (_isEpubMode)
                {
                    if (EpubArea != null) EpubArea.Visibility = Visibility.Visible;
                    if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                    
                    double? progress = null;
                    if (_aozoraBlocks != null && _aozoraBlocks.Count > 0)
                    {
                         int maxSource = _aozoraBlocks[_aozoraBlocks.Count - 1].SourceLineNumber;
                         if (maxSource > 0) progress = (double)currentLine / maxSource;
                         if (progress > 1.0) progress = 1.0;
                    }

                    await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, progress: progress);
                }
                else if (_isAozoraMode)
                {
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;
                    await PrepareAozoraDisplayAsync(_currentTextContent, currentLine, _globalTextCts?.Token ?? default);
                }
                else
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                    await LoadTextLinesProgressivelyAsync(_currentTextContent, currentLine);
                }
            }
            UpdateTextStatusBar();
        }


        private async Task PrepareVerticalTextAsync(int targetLine = 1, CancellationToken externalToken = default)
        {
            if (string.IsNullOrEmpty(_currentTextContent) && !_isEpubMode) return;

            // 로딩 오버레이 켬
            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
            if (ImageInfoText != null) ImageInfoText.Text = Strings.Paginating;

            _verticalPageCalcCts?.Cancel();
            _verticalImageCache.Clear();
            _verticalNavHistory.Clear();
            _pendingVerticalScrollLine = targetLine;

            // [수정] 이전 챕터의 잔여 데이터로 인한 무한 루프 방지를 위해 상태 초기화
            _currentVerticalPageInfo = new VerticalPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };
            _currentVerticalStartBlockIndex = 0;
            _currentVerticalEndBlockIndex = 0;

            try
            {
                // [수정] EPUB 모드일 때는 텍스트 모드용 파싱을 건너뜁니다.
                if (!_isEpubMode && (_aozoraBlocks == null || _aozoraBlocks.Count == 0))
                {
                    _aozoraBlocks = await Task.Run(() => 
                    {
                        var blocks = ParseAozoraContent(_currentTextContent);
                        
                        int lineCount = 1;
                        for (int i = 0; i < _currentTextContent.Length; i++)
                        {
                            if (_currentTextContent[i] == '\n') lineCount++;
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
                    int bestStart = Math.Max(0, targetIdx - 1);
                    int currentTest = bestStart;

                    float availWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1000) - 80;
                    float availHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800) - 80;
                    var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    int safetyLimit = Math.Max(0, targetIdx - 1000);

                    while (currentTest >= safetyLimit)
                    {
                        int tempIdx = currentTest;
                        PaginateAozoraPage(ref tempIdx, _aozoraBlocks, availWidth, availHeight, device);
                        
                        // 화면을 꽉 채우고 남을 때까지 1줄씩 역추산
                        if (tempIdx < targetIdx && currentTest < bestStart)
                        {
                            break;
                        }

                        bestStart = currentTest;
                        if (currentTest == 0) break;
                        currentTest--;
                    }
                    startIdx = bestStart;
                }
                else if (targetLine < 0) 
                {
                    startIdx = Math.Max(0, _aozoraBlocks.Count - 15);
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
                            break;
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
                RenderVerticalDynamicPage(startIdx);
                
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
                _pendingVerticalScrollLine = null;

                // 전체 페이지 수와 스크롤바 세팅은 백그라운드에 던짐
                StartVerticalPageCalculationAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Vertical Pagination Error: {ex.Message}");
            }
        }

        private void RenderVerticalDynamicPage(int startIdx)
        {
            // [수정] 블록이 없더라도 화면을 갱신(Invalidate)해야 이전 페이지의 잔상이 지워지고 스턱된 느낌이 사라집니다.
            if (_aozoraBlocks == null || _aozoraBlocks.Count == 0)
            {
                _currentVerticalPageInfo = new VerticalPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };
                if (VerticalTextCanvas != null) VerticalTextCanvas.Invalidate();
                UpdateVerticalStatusBar();
                return;
            }

            if (VerticalTextCanvas == null) return;
            
            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentVerticalStartBlockIndex = startIdx;

            float marginTop = 40, marginBottom = 40, marginRight = 40, marginLeft = 40;
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
            
            _currentVerticalEndBlockIndex = index > startIdx ? index - 1 : startIdx;
            
            _currentVerticalPageInfo = new VerticalPageInfo 
            { 
                Blocks = pageBlocks, 
                StartLine = pageBlocks.Count > 0 ? pageBlocks[0].SourceLineNumber : 1 
            };

            VerticalTextCanvas.Invalidate();
            UpdateVerticalStatusBar();
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

            float availableHeight = (float)VerticalTextCanvas.ActualHeight - 80;
            float availableWidth = (float)VerticalTextCanvas.ActualWidth - 80;
            var device = VerticalTextCanvas.Device;

            try
            {
                await Task.Run(async () =>
                {
                    int pageCount = 1;
                    float currentPageWidth = 0;
                    float safetyBuffer = 5.0f;
                    var blockToPageMap = new Dictionary<int, int>();

                    for (int i = 0; i < _aozoraBlocks.Count; i++)
                    {
                        if (token.IsCancellationRequested) return;
                        blockToPageMap[i] = pageCount;
                        
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
                            
                            if (block.IsPageBreak)
                            {
                                blockToPageMap[i] = pageCount; // Map the page break to the new page it starts
                                continue; // Skip the page break block itself
                            }

                            // If it's an image (and not a page break)
                            pageCount++;
                            currentPageWidth = 0;
                            blockToPageMap[i] = pageCount; // Map the image to its new page
                            continue;
                        }

                        float fontSize = (float)(_textFontSize * block.FontSizeScale);
                        float blockWidth = MeasureVerticalBlockWidth(device, block, availableHeight, fontSize);
                        
                        if (currentPageWidth > 0 && currentPageWidth + blockWidth > (availableWidth - safetyBuffer))
                        {
                            pageCount++;
                            currentPageWidth = 0;
                            blockToPageMap[i] = pageCount;
                        }
                        
                        currentPageWidth += blockWidth;
                        
                        if (i % 50 == 0) await Task.Delay(1, token);
                    }
                    
                    if (token.IsCancellationRequested) return;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _verticalTotalPages = pageCount;
                        _isVerticalPageCalcCompleted = true;
                        
                        if (blockToPageMap.TryGetValue(_currentVerticalStartBlockIndex, out int cp))
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
                    var tempMerged = CloneBlockProperties(currentMergedBlock, true);
                    tempMerged.Inlines.AddRange(block.Inlines); // 기존 문단에 텍스트 이어붙이기

                    float fontSize = (float)(_textFontSize * tempMerged.FontSizeScale);
                    float newWidth = MeasureVerticalBlockWidth(device, tempMerged, availableHeight, fontSize);
                    float widthDiff = newWidth - currentMergedBlockWidth;
                    float safetyBuffer = 5.0f;

                    // 합친 넓이가 페이지를 초과하면 이번 문장은 병합 취소하고 다음 페이지로 이월
                    if (usedWidth + widthDiff > (availableWidth - safetyBuffer) && pageBlocks.Count > 0)
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
                float fontSizeBase = (float)(_textFontSize * block.FontSizeScale);
                float blockWidth = MeasureVerticalBlockWidth(device, block, availableHeight, fontSizeBase);
                float safetyBuf = 5.0f;

                // [추가] 박스 진입/이탈 시 양옆 여백(Padding)을 페이지 너비에 반영
                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                bool wasKeigakomi = pageBlocks.Count > 0 && (pageBlocks[pageBlocks.Count - 1].BorderColor != null || pageBlocks[pageBlocks.Count - 1].BorderThickness.Top > 0);

                if (isKeigakomi && !wasKeigakomi) blockWidth += 20f; // 박스 시작 우측 여백
                if (!isKeigakomi && wasKeigakomi) blockWidth += 20f; // 박스 종료 좌측 여백

                if (pageBlocks.Count > 0 && usedWidth + blockWidth > (availableWidth - safetyBuf))
                {
                    break; 
                }

                var blockCopy = CloneBlockProperties(block, true);
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
                if (usedWidth >= (availableWidth - safetyBuf)) break;
            }
            return pageBlocks;
        }

        private float MeasureVerticalBlockWidth(CanvasDevice? device, AozoraBindingModel block, float availableHeight, float fontSize)
        {
            if (device == null) return fontSize * 2.0f;

            // Build text same as in Draw method
            StringBuilder sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(NormalizeVerticalText(s));
                else if (inline is AozoraRuby ruby)
                {
                    var normBase = NormalizeVerticalText(ruby.BaseText);
                    sb.Append(normBase);
                    if (ruby.IsBold) boldRanges.Add((start, normBase.Length));
                }
                else if (inline is AozoraBold bold)
                {
                    var normText = NormalizeVerticalText(bold.Text);
                    sb.Append(normText);
                    boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraItalic italic)
                {
                    var normText = NormalizeVerticalText(italic.Text);
                    sb.Append(normText);
                    italicRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraCode code) sb.Append(NormalizeVerticalText(code.Text));
                else if (inline is AozoraTCY tcy)
                {
                    var normText = NormalizeVerticalText(tcy.Text);
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
                FontFamily = block.FontFamily ?? _textFontFamily,
                FontWeight = GetFontWeightForFamily(block.FontFamily ?? _textFontFamily),
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
            
            // [롤백]
            // using var typography = new CanvasTypography();
            // typography.AddFeature(CanvasTypographyFeatureName.ProportionalAlternateWidths, 1);
            // layout.SetTypography(0, text.Length, typography);
            
            float boundsWidth = (float)layout.LayoutBounds.Width;
            float spacing = fontSize * (block.IsBlankLine ? 0.2f : 0.6f);
            
            if (block.IsBlankLine) return (boundsWidth * 0.5f) + spacing;
            return boundsWidth + spacing;
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
            
            // [수정] 블록 존재 여부와 무관하게 무조건 캔버스를 먼저 비워서 이전 화면의 잔상을 제거합니다.
            ds.Clear(GetVerticalBackgroundColor());

            if (_currentVerticalPageInfo.Blocks == null || _currentVerticalPageInfo.Blocks.Count == 0) return;

            var page = _currentVerticalPageInfo;
            
            float marginTop = 40;
            float marginBottom = 40;
            float marginRight = 40;

            // [좌표 기준] currentX: 현재 줄의 "가장 오른쪽 끝" 좌표
            float currentX = (float)size.Width - marginRight; 
            float startY = marginTop;
            float drawHeight = (float)size.Height - (marginTop + marginBottom);

            // [추가] 이미지 모드 체크 (SideBySide일 때 한 페이지에 여러 블록(이미지)이 있을 수 있음)
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
                return; // 이미지 페이지는 텍스트를 그리지 않음
            }

            // [罫囲み] 박스 렌더링을 위한 상태 변수
            bool isBoxing = false;
            float boxRight = 0f;
            float boxLeft = float.MaxValue;
            float boxTop = float.MaxValue;
            float boxBottom = float.MinValue;
            Color boxColor = Colors.Gray;
            float boxPad = 20f; // 박스 안팎 여백

            // 기존 foreach를 for 문으로 교체합니다.
            for (int i = 0; i < page.Blocks.Count; i++)
            {
                var block = page.Blocks[i];

                float fontSize = (float)(_textFontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;
                float measureWidth = fontSize * 2f;

                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? _textFontFamily,
                    FontWeight = GetFontWeightForFamily(block.FontFamily ?? _textFontFamily),
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    WordWrapping = CanvasWordWrapping.EmergencyBreak,
                    LineSpacing = fontSize * 1.8f, 
                    VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                    VerticalAlignment = CanvasVerticalAlignment.Center 
                };

                // --- 텍스트 빌더 및 범위 기록 ---
                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var tcyRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(NormalizeVerticalText(s));
                    else if (inline is AozoraRuby ruby)
                    {
                        var normBase = NormalizeVerticalText(ruby.BaseText);
                        sb.Append(normBase);
                        rubyRanges.Add((start, normBase.Length, ruby.RubyText));
                        if (ruby.IsBold) boldRanges.Add((start, normBase.Length));
                    }
                    else if (inline is AozoraBold bold)
                    {
                        var normText = NormalizeVerticalText(bold.Text);
                        sb.Append(normText);
                        boldRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraItalic italic)
                    {
                        var normText = NormalizeVerticalText(italic.Text);
                        sb.Append(normText);
                        italicRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraCode code) sb.Append(NormalizeVerticalText(code.Text));
                    else if (inline is AozoraTCY tcy)
                    {
                        var normText = NormalizeVerticalText(tcy.Text);
                        sb.Append(normText);
                        tcyRanges.Add((start, normText.Length));
                        if (tcy.IsBold) boldRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }

                if (block.IsTable && block.TableRows.Count > 0)
                {
                    foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
                }

                string blockText = sb.ToString();

                using var textLayout = new CanvasTextLayout(ds, blockText, format, measureWidth, drawHeight);
                ApplyVerticalBracketSpacing(ds, format, textLayout, blockText, fontSize);
                
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

                var bounds = textLayout.LayoutBounds;
                float currentLineThickness = (float)bounds.Width;
                if (block.IsBlankLine) currentLineThickness *= 0.5f;

                // --- 罫囲み (박스) 처리 ---
                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                
                float drawY = startY + (float)block.Margin.Top;
                if (block.Alignment == TextAlignment.Center) drawY = (float)((size.Height - bounds.Height) / 2);
                else if (block.Alignment == TextAlignment.Right) drawY = (float)(size.Height - bounds.Height - marginBottom);

                // 빈 줄일 경우 텍스트 높이가 너무 작아 박스가 찌그러지는 것을 방지
                float currentH = (float)bounds.Height;
                if (block.IsBlankLine && currentH < fontSize) currentH = fontSize;

                if (isKeigakomi)
                {
                    if (!isBoxing)
                    {
                        // 박스 시작: 오른쪽 여백 확보
                        currentX -= boxPad;
                        isBoxing = true;
                        boxRight = currentX;
                        boxLeft = currentX - currentLineThickness;
                        boxTop = drawY + (float)bounds.Y;
                        boxBottom = drawY + (float)bounds.Y + currentH;
                        boxColor = block.BorderColor ?? Colors.Gray;
                    }
                    else
                    {
                        // 박스 진행 중: 가장 넓은 상하좌우 영역으로 갱신
                        boxLeft = currentX - currentLineThickness;
                        boxTop = Math.Min(boxTop, drawY + (float)bounds.Y);
                        boxBottom = Math.Max(boxBottom, drawY + (float)bounds.Y + currentH);
                    }
                }
                else if (!isKeigakomi && isBoxing)
                {
                    // 박스 종료: 텍스트 뒤에 박스 테두리 그리기
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                    
                    // 박스 종료 후 왼쪽 여백 확보
                    currentX -= boxPad;
                }

                // 4. 그리기 위치(drawX) 보정
                float drawX = currentX - (float)(bounds.X + bounds.Width);

                // 5. 본문 그리기
                ds.DrawTextLayout(textLayout, drawX, drawY, textColor);

                // 6. 루비 그리기 
                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = _textFontFamily,
                    FontWeight = GetFontWeightForFamily(_textFontFamily),
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };

                var rubyRenderInfos = new List<RubyRenderInfo>();
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;
                        float rubyX = drawX + (float)charBounds.Left + (float)charBounds.Width + (rubyFontSize * 2.2f);
                        float rubyY = drawY + (float)charBounds.Top; 

                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, rubyFontSize * 1.5f);
                        if (block.IsBold || boldRanges.Any(br => ruby.start >= br.start && ruby.start < br.start + br.length))
                        {
                            rubyLayout.SetFontWeight(0, ruby.rubyText.Length, Microsoft.UI.Text.FontWeights.Bold);
                        }

                        float rubyHeight = (float)rubyLayout.LayoutBounds.Height;
                        float charHeight = (float)charBounds.Height;
                        float idealTop = rubyY + (charHeight - rubyHeight) / 2;

                        rubyRenderInfos.Add(new RubyRenderInfo
                        {
                            Layout = rubyLayout,
                            IdealY = idealTop,
                            Height = rubyHeight,
                            X = rubyX,
                            Y = idealTop 
                        });
                    }
                }

                ResolveRubyOverlaps(rubyRenderInfos);

                foreach (var info in rubyRenderInfos)
                {
                    ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor);
                    info.Layout.Dispose(); 
                }

                // 7. 다음 줄 위치 계산
                float spacing = fontSize * (block.IsBlankLine ? 0.2f : 0.6f); 
                currentX -= (currentLineThickness + spacing);

                // 루프가 끝났는데(페이지의 끝) 박스가 닫히지 않은 경우 이어서 그리기
                if (i == page.Blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }

        private Color GetVerticalTextColor()
        {
            if (_themeIndex == 2) return Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204); // Dark theme matching GetThemeForeground
            if (_themeIndex == 3 && _customForegroundColor.HasValue) return _customForegroundColor.Value;
            return Colors.Black; // Light and Beige themes
        }

        private Color GetVerticalBackgroundColor()
        {
            if (_themeIndex == 0) return Colors.White;
            if (_themeIndex == 1) return Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235); // Beige
            if (_themeIndex == 3 && _customBackgroundColor.HasValue) return _customBackgroundColor.Value;
            return Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30); // Dark
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
                    _verticalNavHistory.Push(_currentVerticalStartBlockIndex);
                    RenderVerticalDynamicPage(_currentVerticalEndBlockIndex + 1);
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
                if (_verticalNavHistory.Count > 0)
                {
                    int prevIdx = _verticalNavHistory.Pop();
                    RenderVerticalDynamicPage(prevIdx);
                    if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage = Math.Max(1, _verticalCalculatedCurrentPage - 1); UpdateVerticalStatusBar(); }
                }
                else if (blocks != null && _currentVerticalStartBlockIndex > 0)
                {
                    int targetIdx = _currentVerticalStartBlockIndex;
                    int bestStart = Math.Max(0, targetIdx - 1);
                    int currentTest = bestStart;

                    float availWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1000) - 80;
                    float availHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800) - 80;
                    var device = VerticalTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    // 💡 [수정] 박스나 긴 단락 대비 탐색 한계치를 1000블록으로 넉넉하게 잡습니다.
                    int safetyLimit = Math.Max(0, targetIdx - 1000); 

                    while (currentTest >= safetyLimit)
                    {
                        int tempIdx = currentTest;
                        PaginateAozoraPage(ref tempIdx, blocks, availWidth, availHeight, device);
                        
                        // tempIdx가 targetIdx에 도달하지 못했다면, 
                        // currentTest부터 시작하여 채운 페이지가 targetIdx 직전에 꽉 차버렸음을 의미합니다.
                        if (tempIdx < targetIdx && currentTest < bestStart)
                        {
                            break;
                        }

                        bestStart = currentTest;
                        if (currentTest == 0) break;
                        
                        // 💡 [핵심] 단락 처음으로 무조건 건너뛰는 코드를 삭제하고 1줄씩 뒤로 이동합니다.
                        currentTest--;
                    }

                    RenderVerticalDynamicPage(bestStart);
                    if (_isVerticalPageCalcCompleted) { _verticalCalculatedCurrentPage = Math.Max(1, _verticalCalculatedCurrentPage - 1); UpdateVerticalStatusBar(); }
                }
                else if (_isEpubMode && _currentEpubChapterIndex > 0)
                {
                    int prevIndex = _currentEpubChapterIndex - 1;
                    var pageBlocks = _currentVerticalPageInfo.Blocks;
                    if (pageBlocks != null && pageBlocks.Any(b => b.HasImage) && prevIndex > 0)
                    {
                         prevIndex--;
                    }
                    _currentEpubChapterIndex = prevIndex;
                    await LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: true);
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
                if (_isVerticalPageCalcCompleted)
                {
                    ImageIndexText.Text = $"{currentPage} / {totalPages}";
                    double progress = totalPages > 1 ? (double)(currentPage - 1) / (totalPages - 1) * 100.0 : 100.0;
                    if (progress > 100) progress = 100;
                    TextProgressText.Text = $"{progress:F1}%";
                }
                else
                {
                    ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
                    var blocks = _aozoraBlocks;
                    double progress = (blocks != null && blocks.Count > 0) ? (double)_currentVerticalStartBlockIndex / blocks.Count * 100.0 : 0;
                    TextProgressText.Text = $"{progress:F1}%";
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
                var blocks = _aozoraBlocks;
                if (_isEpubMode && (blocks == null || blocks.Count == 0)) return;

                int currentLine = 1;
                if (_pendingVerticalScrollLine.HasValue)
                {
                    currentLine = _pendingVerticalScrollLine.Value;
                }
                else if (_currentVerticalPageInfo.Blocks != null)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                }
                _ = PrepareVerticalTextAsync(currentLine);
            }
        }

        private void RootGrid_Vertical_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
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
                // [수정]
                var pageBlocks = _currentVerticalPageInfo.Blocks;
                int currentLine = pageBlocks != null ? _currentVerticalPageInfo.StartLine : 1;
                _ = PrepareVerticalTextAsync(currentLine);
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
                    _verticalNavHistory.Clear();
                    RenderVerticalDynamicPage(0);
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
                    _verticalNavHistory.Clear();
                    int lastIdx = Math.Max(0, blocks.Count - 15); // 끝부분 추정
                    RenderVerticalDynamicPage(lastIdx);
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

                ds.DrawImage(bitmap, new Rect(drawX, drawY, drawW, drawH));
            }
            else
            {
                _verticalImageCache[path] = null!;
                _ = LoadVerticalImageAsync(path);
            }
        }

        private void ResolveRubyOverlaps(List<RubyRenderInfo> rubies)
        {
            if (rubies.Count == 0) return;

            // X 좌표가 같은 그룹끼리 처리 (줄바꿈이 발생할 경우 X가 다름)
            int startIndex = 0;
            while (startIndex < rubies.Count)
            {
                int endIndex = startIndex;
                float currentX = rubies[startIndex].X;

                // 같은 X 좌표 라인 찾기 (오차 범위 2px)
                while (endIndex + 1 < rubies.Count && Math.Abs(rubies[endIndex + 1].X - currentX) < 2.0f)
                {
                    endIndex++;
                }

                // 해당 라인 내에서 충돌 해결
                ResolveRubyOverlapsInColumn(rubies, startIndex, endIndex);
                startIndex = endIndex + 1;
            }
        }

        private void ResolveRubyOverlapsInColumn(List<RubyRenderInfo> rubies, int start, int end)
        {
            float prevBottom = -10000f; // 초기값 (충분히 작은 값)

            int i = start;
            while (i <= end)
            {
                // 클러스터 시작 (현재 루비)
                float clusterSumCenter = rubies[i].IdealY + rubies[i].Height / 2.0f;
                float clusterTotalHeight = rubies[i].Height;
                int clusterCount = 1;
                int clusterEnd = i;

                // 다음 루비들과 충돌 체크 및 병합
                while (clusterEnd + 1 <= end)
                {
                    var next = rubies[clusterEnd + 1];
                    
                    // 현재까지 클러스터의 가상 Top/Bottom 계산 (중심 기준 재배치 시뮬레이션)
                    float currentHypotheticalTop = (clusterSumCenter / clusterCount) - (clusterTotalHeight / 2.0f);
                    float currentHypotheticalBottom = currentHypotheticalTop + clusterTotalHeight;

                    // 겹침 여부 확인 (Bottom > Next.IdealTop)
                    // 주의: next.IdealY는 "이상적인 위치"의 Top입니다.
                    // 클러스터가 확장되면서 아래로 밀려날 수 있으므로, 현재 클러스터의 Bottom이 다음 녀석의 Ideal Top을 침범하면 병합 대상입니다.
                    if (currentHypotheticalBottom > next.IdealY)
                    {
                        // 병합
                        clusterEnd++;
                        clusterSumCenter += (next.IdealY + next.Height / 2.0f);
                        clusterTotalHeight += next.Height;
                        clusterCount++;
                    }
                    else
                    {
                        break; // 겹치지 않으면 중단
                    }
                }

                // 병합된 클러스터를 재배치 (중심 유지 전략)
                float finalTop = (clusterSumCenter / clusterCount) - (clusterTotalHeight / 2.0f);

                // [수정] 위쪽 방향으로의 이동 제한 (이전 루비와 겹치지 않도록 함)
                // 만약 계산된 위치가 이전 루비의 끝보다 위라면(작다면), 이전 루비 끝에 맞춤 (Push Down 효과)
                if (finalTop < prevBottom)
                {
                    finalTop = prevBottom;
                }

                // 실제 위치 적용
                for (int k = i; k <= clusterEnd; k++)
                {
                    rubies[k].Y = finalTop;
                    finalTop += rubies[k].Height;
                }

                // 다음 루비를 위한 prevBottom 갱신
                prevBottom = finalTop;

                i = clusterEnd + 1;
            }
        }

        private class RubyRenderInfo
        {
            public required CanvasTextLayout Layout;
            public float IdealY;
            public float Height;
            public float X;
            public float Y;
        }

        private bool DoesVerticalImageExist(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            try
            {
                if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    return true;
                }
                if (_isEpubMode && _currentEpubArchive != null)
                {
                    string normPath = relativePath.Replace('\\', '/');
                    return _currentEpubArchive.Entries.Any(e =>
                        e.FullName.Replace('\\', '/') == normPath ||
                        string.Equals(e.FullName.Replace('\\', '/'), normPath, StringComparison.OrdinalIgnoreCase));
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

                if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    string? fullRemotePath = ResolveWebDavImagePath(relativePath);
                    if (fullRemotePath != null)
                    {
                        var tempPath = await _webDavService.DownloadToTempFileAsync(fullRemotePath);
                        if (!string.IsNullOrEmpty(tempPath) && System.IO.File.Exists(tempPath))
                        {
                            bytes = await System.IO.File.ReadAllBytesAsync(tempPath);
                        }
                    }
                }
                else if (_isEpubMode && _currentEpubArchive != null)
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
                     var bitmap = await CanvasBitmap.LoadAsync(device, winrtStream);

                     this.DispatcherQueue.TryEnqueue(() => 
                     {
                         _verticalImageCache[relativePath] = bitmap;
                         VerticalTextCanvas.Invalidate(); // Redraw with image
                     });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadVerticalImageAsync failed: {ex.Message}");
            }
        }

        private string NormalizeVerticalText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 세로 모드 전용: !!, ??, ?!, !? 를 하나의 TCY(세로중짜) 유니코드 문자로 치환하여
            // 세로 쓰기 레이아웃에서 나란히 바르게 서도록 처리합니다. (전각/반각 모두 지원)
            text = text.Replace("!!", "‼").Replace("！！", "‼");
            text = text.Replace("??", "⁇").Replace("？？", "⁇");
            text = text.Replace("?!", "⁈").Replace("？！", "⁈");
            text = text.Replace("!?", "⁉").Replace("！？", "⁉");

            return text;
        }

        private void ApplyVerticalBracketSpacing(ICanvasResourceCreator resourceCreator, CanvasTextFormat format, CanvasTextLayout layout, string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 조정할 괄호 목록
            string brackets = "()[]{}<>（）「」『』【】〈〉《》";
            float baseReduction = -fontSize * 0.4f;

            for (int i = 0; i < text.Length; i++)
            {
                if (brackets.Contains(text[i]))
                {
                    // 폰트에 따라 괄호의 잉크 위치가 다르므로 실제 측정을 통해 공백 여부 판단
                    using var tmpFormat = new CanvasTextFormat
                    {
                        FontFamily = format.FontFamily,
                        FontSize = fontSize,
                        Direction = format.Direction,
                        VerticalAlignment = CanvasVerticalAlignment.Top // 측정을 위해 상단 정렬
                    };
                    
                    using var tmpLayout = new CanvasTextLayout(resourceCreator, text[i].ToString(), tmpFormat, fontSize * 2, fontSize * 2);
                    var drawBounds = tmpLayout.DrawBounds;
                    var layoutBounds = tmpLayout.LayoutBounds;

                    // 세로 모드(TopToBottom)에서 자간 조정은 아래쪽 글자를 끌어올리는 방식임.
                    // 따라서 현재 글자의 하단(slotBottom)과 실제 잉크의 하단(inkBottom) 사이에 여백이 있을 때만 안전하게 적용 가능.
                    float slotBottom = (float)(layoutBounds.Y + layoutBounds.Height);
                    float inkBottom = (float)(drawBounds.Y + drawBounds.Height);
                    float gapBelow = slotBottom - inkBottom;

                    // 하단 여백이 폰트 크기의 15% 이상일 때만 조정을 적용하여 겹침 방지
                    if (gapBelow > fontSize * 0.15f)
                    {
                        // 여백보다 과하게 겹치지 않도록 실제 여백 크기에 맞춰 조정값 결정
                        float actualReduction = Math.Max(baseReduction, -gapBelow * 0.85f);
                        layout.SetCharacterSpacing(i, 1, 0, actualReduction, 0);
                    }
                }
            }
        }
    }
}
