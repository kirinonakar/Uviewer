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
        private List<string> _verticalPages = new();
        private int _currentVerticalPageIndex = 0;
        private System.Threading.CancellationTokenSource? _verticalPaginationCts;
        private Dictionary<string, CanvasBitmap> _verticalImageCache = new();

        private void VerticalToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isVerticalMode = VerticalToggleButton?.IsChecked ?? false;
            SaveTextSettings();
            ToggleVerticalMode();
        }

        private async void ToggleVerticalMode()
        {
            if (_isVerticalMode)
            {
                // Attach vertical key handler
                if (!_verticalKeyAttached && RootGrid != null)
                {
                    RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                    _verticalKeyAttached = true;
                }
                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Collapsed;
                if (EpubArea != null) EpubArea.Visibility = Visibility.Collapsed;
                if (TextArea != null) TextArea.Visibility = Visibility.Visible;
                
                int currentLine = 1;
                if (_isAozoraMode) currentLine = (_aozoraBlocks != null && _aozoraBlocks.Count > _currentAozoraStartBlockIndex) ? _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber : 1;
                else if (_isEpubMode)
                {
                    if (_epubPages != null && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubPages.Count)
                    {
                        if (_epubPages[_currentEpubPageIndex] is Grid g && g.Tag is EpubPageInfoTag tag)
                            currentLine = tag.StartLine;
                    }
                    _aozoraBlocks = await GetEpubChapterAsAozoraBlocksAsync(_currentEpubChapterIndex);
                }
                else if (TextScrollViewer != null) currentLine = GetTopVisibleLineIndex();

                await PrepareVerticalTextAsync(currentLine, _globalTextCts?.Token ?? default);
            }
            else
            {
                // Detach vertical key handler
                if (_verticalKeyAttached && RootGrid != null)
                {
                    RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                    _verticalKeyAttached = false;
                }
                int currentLine = _verticalPageInfos.Count > 0 && _currentVerticalPageIndex < _verticalPageInfos.Count 
                    ? _verticalPageInfos[_currentVerticalPageIndex].StartLine : 1;

                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                if (_isEpubMode)
                {
                    if (EpubArea != null) EpubArea.Visibility = Visibility.Visible;
                    if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                    await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine);
                }
                else if (_isAozoraMode)
                {
                    if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Visible;
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

        private struct VerticalPageInfo
        {
            public List<AozoraBindingModel> Blocks;
            public int StartLine;
        }
        private List<VerticalPageInfo> _verticalPageInfos = new();

        private async Task PrepareVerticalTextAsync(int targetLine = 1, CancellationToken externalToken = default)
        {
            if (string.IsNullOrEmpty(_currentTextContent) && !_isEpubMode) return;

            // 로딩 상태 표시 (Status Bar)
            FileNameText.Text = Strings.Paginating;

            _verticalPaginationCts?.Cancel();
            _verticalPaginationCts = new System.Threading.CancellationTokenSource();
            
            // 링크된 토큰 생성 (외부 토큰이나 내부 토큰 중 하나라도 취소되면 중단)
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _verticalPaginationCts.Token);
            var token = linkedCts.Token;

            _verticalPageInfos.Clear();
            _currentVerticalPageIndex = 0;
            _verticalImageCache.Clear();

            float availableHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800);
            if (availableHeight < 100) availableHeight = (float)RootGrid.ActualHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            
            float availableWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1200);
            if (availableWidth < 100) availableWidth = (float)RootGrid.ActualWidth - 100;
            if (availableWidth < 100) availableWidth = 1000;
            availableWidth -= 120; // Total horizontal margin (60 * 2)

            try
            {
                // Ensure blocks are available (Always use Aozora parsing for Vertical mode)
                if (_aozoraBlocks == null || _aozoraBlocks.Count == 0)
                {
                    // Case for Plain Text Mode or First Time Vertical Mode with Aozora File
                    _aozoraBlocks = await Task.Run(() => ParseAozoraContent(_currentTextContent), token);
                    _textTotalLineCountInSource = _currentTextContent.Split('\n').Length;
                }

                // 2. Immediate Pagination (up to target or reasonable limit)
                int limit = Math.Min(_aozoraBlocks.Count, 1000); 
                // Search for target line block index
                int targetBlockIdx = 0;
                for (int n = 0; n < _aozoraBlocks.Count; n++)
                {
                    if (_aozoraBlocks[n].SourceLineNumber >= targetLine)
                    {
                        targetBlockIdx = n;
                        break;
                    }
                }
                if (targetBlockIdx > limit) limit = Math.Min(_aozoraBlocks.Count, targetBlockIdx + 500);

                int i = 0;
                bool foundTargetPage = false;

                // Initial sync chunk
                while (i < limit)
                {
                    if (token.IsCancellationRequested) return;
                    var pageBlocks = PaginateAozoraPage(ref i, _aozoraBlocks, availableWidth, availableHeight);
                    if (pageBlocks.Count > 0)
                    {
                        var info = new VerticalPageInfo { Blocks = pageBlocks, StartLine = pageBlocks[0].SourceLineNumber };
                        _verticalPageInfos.Add(info);
                        
                        if (!foundTargetPage && (info.StartLine >= targetLine || i >= targetBlockIdx))
                        {
                            int idx = _verticalPageInfos.Count - 1;
                            if (idx > 0 && info.StartLine > targetLine) idx--;
                            _currentVerticalPageIndex = idx;
                            foundTargetPage = true;
                        }
                    }
                    if (i >= limit) break;
                }

                // Initial Render
                VerticalTextCanvas?.Invalidate();
                UpdateTextStatusBar();

                // 3. Background Pagination for the rest
                if (i < _aozoraBlocks.Count)
                {
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            int bgIdx = i;
                            while (bgIdx < _aozoraBlocks.Count)
                            {
                                if (token.IsCancellationRequested) return;

                                var batch = new List<VerticalPageInfo>();
                                for (int b = 0; b < 20 && bgIdx < _aozoraBlocks.Count; b++)
                                {
                                    var pBlocks = PaginateAozoraPage(ref bgIdx, _aozoraBlocks, availableWidth, availableHeight);
                                    if (pBlocks.Count > 0) batch.Add(new VerticalPageInfo { Blocks = pBlocks, StartLine = pBlocks[0].SourceLineNumber });
                                }

                                if (batch.Count > 0)
                                {
                                    this.DispatcherQueue.TryEnqueue(() => 
                                    {
                                        if (token.IsCancellationRequested) return;
                                        _verticalPageInfos.AddRange(batch);
                                        UpdateTextStatusBar();
                                    });
                                }
                                await Task.Delay(1, token);
                            }
                        }
                        catch { }
                    }, token);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Vertical Pagination Error: {ex.Message}");
            }
        }

        private List<AozoraBindingModel> PaginateAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight)
        {
            var pageBlocks = new List<AozoraBindingModel>();
            float usedWidth = 0;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                // [추가] 이미지 블록은 무조건 한 페이지를 차지하게 함
                if (block.HasImage)
                {
                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null)
                    {
                        // 이미지 파일 존재 여부 확인
                        if (!DoesVerticalImageExist(aozoraImg.Source))
                        {
                            index++; // 파일이 없으면 이 블록은 건너뜀
                            continue;
                        }
                    }

                    if (pageBlocks.Count > 0) break; // 이미 다른 내용이 있으면 다음 페이지로 넘김
                    
                    pageBlocks.Add(block);
                    index++;
                    break; // 이미지 페이지는 이미지 하나만 넣고 종료
                }

                float fontSize = (float)(_textFontSize * block.FontSizeScale);
                
                // 1. 블록의 전체 텍스트 길이 계산
                int textLength = 0;
                if (block.Inlines != null)
                {
                    foreach (var inline in block.Inlines)
                    {
                        if (inline is string s) textLength += s.Length;
                        else if (inline is AozoraRuby ruby) textLength += ruby.BaseText.Length;
                        else if (inline is AozoraBold bold) textLength += bold.Text.Length;
                        else if (inline is AozoraItalic italic) textLength += italic.Text.Length;
                        else if (inline is AozoraCode code) textLength += code.Text.Length;
                        else if (inline is AozoraLineBreak) textLength += 1;
                    }
                }
                
                if (block.IsTable && block.TableRows.Count > 0)
                {
                    foreach (var row in block.TableRows) textLength += string.Join(" | ", row).Length + 1;
                }

                if (textLength == 0) textLength = 1;

                // 2. 한 줄(세로)에 들어갈 수 있는 글자 수 추정 (여백 90% 고려해 더 안전하게 계산)
                int charsPerCol = (int)((availableHeight * 0.90f) / fontSize);
                if (charsPerCol < 1) charsPerCol = 1;

                // 3. 이 블록이 차지할 예상 줄(Column) 수 계산
                int estimatedCols = (int)Math.Ceiling((double)textLength / charsPerCol);
                if (estimatedCols < 1) estimatedCols = 1;

                // 4. 예상 너비 계산 (줄 수 * (본문두께 + 줄간격 + 루비여유))
                // Draw 로직의 LineSpacing(1.8f) 및 Spacing(0.6f)을 고려해 넉넉하게 2.0f로 설정
                float colWidth = fontSize * 2.0f; 
                float blockTotalWidth = (estimatedCols * colWidth) + (fontSize * 0.2f); 

                // 5. 공간 체크 (안전 여백 Safety Buffer 적용)
                // 화면 왼쪽 끝에 딱 붙지 않도록, 폰트 크기의 1.5배 정도 여유를 둠
                float safetyBuffer = fontSize * 1.5f;

                if (pageBlocks.Count > 0 && usedWidth + blockTotalWidth > (availableWidth - safetyBuffer))
                {
                    break; 
                }

                pageBlocks.Add(block);
                usedWidth += blockTotalWidth;
                index++;

                if (usedWidth >= (availableWidth - safetyBuffer)) break;
            }
            return pageBlocks;
        }

        private void VerticalTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
        }

        private void VerticalTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isVerticalMode || _verticalPageInfos.Count == 0 || _currentVerticalPageIndex >= _verticalPageInfos.Count) return;

            var ds = args.DrawingSession;
            var size = sender.Size;
            var page = _verticalPageInfos[_currentVerticalPageIndex];
            Color textColor = GetVerticalTextColor();
            
            ds.Clear(GetVerticalBackgroundColor());

            float margin = 60;
            // [좌표 기준] currentX: 현재 줄의 "가장 오른쪽 끝" 좌표
            float currentX = (float)size.Width - margin; 
            float startY = margin;

            if (page.Blocks == null) return;

            foreach (var block in page.Blocks)
            {
                if (block.HasImage)
                {
                    // Draw Image Page
                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null)
                    {
                        DrawVerticalImage(ds, size, aozoraImg.Source);
                    }
                    continue;
                }

                float fontSize = (float)(_textFontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;

                // [레이아웃 너비] 폰트 크기보다 약간 여유 있게 잡음 (줄바꿈 방지 및 루비 공간 확보)
                float measureWidth = fontSize * 2f;

                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? _textFontFamily,
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    WordWrapping = CanvasWordWrapping.EmergencyBreak,
                    LineSpacing = fontSize * 1.8f, 
                    VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                    // [수정 1] 내부 정렬을 제거하거나 Center로 둡니다. (좌표 계산으로 직접 정렬할 것이므로)
                    VerticalAlignment = CanvasVerticalAlignment.Center 
                };

                // --- 텍스트 빌더 및 범위 기록 ---
                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(s);
                    else if (inline is AozoraRuby ruby)
                    {
                        sb.Append(ruby.BaseText);
                        rubyRanges.Add((start, ruby.BaseText.Length, ruby.RubyText));
                    }
                    else if (inline is AozoraBold bold)
                    {
                        sb.Append(bold.Text);
                        boldRanges.Add((start, bold.Text.Length));
                    }
                    else if (inline is AozoraItalic italic)
                    {
                        sb.Append(italic.Text);
                        italicRanges.Add((start, italic.Text.Length));
                    }
                    else if (inline is AozoraCode code) sb.Append(code.Text);
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }

                if (block.IsTable && block.TableRows.Count > 0)
                {
                    foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
                }

                string blockText = sb.ToString();

                // 1. 텍스트 레이아웃 생성
                using var textLayout = new CanvasTextLayout(ds, blockText, format, measureWidth, (float)size.Height - margin * 2);
                
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

                // 2. [핵심 수정] 텍스트의 실제 점유 영역(Bounding Box) 계산
                var bounds = textLayout.LayoutBounds;

                // 3. [핵심 수정] 왼쪽 마진 진입 여부 체크 (잘림 방지)
                // 현재 줄 두께와 루비 공간을 합친 너비 계산
                float currentLineThickness = (float)bounds.Width + (rubyRanges.Count > 0 ? rubyFontSize : 0);
                // [수정] 아래 break를 제거하여 페이지네이션에서 계산된 줄이 그리기 단계에서 누락되지 않도록 함.
                // if (currentX - currentLineThickness < margin - 5) break; 

                // 4. 그리기 위치(drawX) 보정
                // 목표: 텍스트의 "오른쪽 끝(Left + Width)"이 "currentX"에 딱 맞아야 함.
                float drawX = currentX - (float)(bounds.X + bounds.Width);
                
                // Y축 정렬
                float drawY = startY + (float)block.Margin.Top;
                if (block.Alignment == TextAlignment.Center) drawY = (float)((size.Height - bounds.Height) / 2);
                else if (block.Alignment == TextAlignment.Right) drawY = (float)(size.Height - bounds.Height - margin);

                // 5. 본문 그리기
                ds.DrawTextLayout(textLayout, drawX, drawY, textColor);

                // 5. 루비 그리기 (수정됨)
                // 루비용 포맷: 루비도 세로쓰기 방향이어야 함
                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = _textFontFamily,
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    VerticalAlignment = CanvasVerticalAlignment.Top, // [수정] 세로쓰기에서 Top은 오른쪽 정렬을 의미
                    WordWrapping = CanvasWordWrapping.NoWrap // [중요] 루비가 길어도 줄바꿈되지 않도록 설정
                };

                foreach (var ruby in rubyRanges)
                {
                    // [핵심] 본문 글자의 실제 좌표 영역을 가져옵니다.
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        // firstRegion은 textLayout 내부의 상대 좌표(0,0 기준)를 가집니다.
                        var charBounds = regions[0].LayoutBounds;

                        // 루비 X 위치:
                        // (레이아웃 시작점 drawX) + (글자의 왼쪽 여백 charBounds.Left) + (글자 너비 charBounds.Width)
                        // 여기에 약간의 여백(2.2fontSize)을 더해 오른쪽으로 더 띄웁니다.
                        float rubyX = drawX + (float)charBounds.Left + (float)charBounds.Width + (rubyFontSize * 2.2f);

                        // 루비 Y 위치: 글자의 시작 높이
                        float rubyY = drawY + (float)charBounds.Top;
                        
                        // [수정] 루비 레이아웃 생성
                        using var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, rubyFontSize * 1.5f);
                        
                        // 루비를 본문 글자 높이(세로쓰기니까 Height가 길이)의 중앙에 오도록 보정
                        float rubyHeight = (float)rubyLayout.LayoutBounds.Height;
                        float charHeight = (float)charBounds.Height;
                        
                        // 본문 글자보다 루비가 길면 위아래로 삐져나가게(음수), 짧으면 중앙에 오게(양수) 계산됨
                        float yOffset = (charHeight - rubyHeight) / 2;

                        // 루비 그리기
                        ds.DrawTextLayout(rubyLayout, rubyX, rubyY + yOffset, textColor);
                    }
                }

                // 7. 다음 줄 위치 계산
                float spacing = fontSize * 0.6f; 
                currentX -= (currentLineThickness + spacing);
            }
        }

        private Color GetVerticalTextColor()
        {
            if (_themeIndex == 2) return Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204); // Dark theme matching GetThemeForeground
            return Colors.Black; // Light and Beige themes
        }

        private Color GetVerticalBackgroundColor()
        {
            if (_themeIndex == 0) return Colors.White;
            if (_themeIndex == 1) return Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235); // Beige
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
            if (_verticalPageInfos.Count == 0) return;

            int nextIndex = _currentVerticalPageIndex + direction;
            if (nextIndex >= 0 && nextIndex < _verticalPageInfos.Count)
            {
                _currentVerticalPageIndex = nextIndex;
                VerticalTextCanvas?.Invalidate();
                UpdateTextStatusBar();
            }
            else if (_isEpubMode)
            {
                if (direction > 0 && _currentEpubChapterIndex < _epubSpine.Count - 1)
                {
                    _currentEpubChapterIndex++;
                    await LoadEpubChapterAsync(_currentEpubChapterIndex);
                }
                else if (direction < 0 && _currentEpubChapterIndex > 0)
                {
                    _currentEpubChapterIndex--;
                    await LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: true);
                }
            }
        }

        private void UpdateVerticalStatusBar()
        {
            if (!_isVerticalMode || _verticalPageInfos == null || _verticalPageInfos.Count == 0) return;
            if (_currentVerticalPageIndex < 0 || _currentVerticalPageIndex >= _verticalPageInfos.Count) return;

            int totalPages = _verticalPageInfos.Count;
            int currentPage = _currentVerticalPageIndex + 1;
            int currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
            
            // Use cached total line count if available
            int totalLines = _textTotalLineCountInSource;
            if (totalLines <= 1 && !string.IsNullOrEmpty(_currentTextContent))
            {
                // Fallback for first run or if not set
                totalLines = _currentTextContent.Split('\n').Length;
                _textTotalLineCountInSource = totalLines;
            }
            
            if (_isEpubMode)
            {
                ImageInfoText.Text = $"Ch. {_currentEpubChapterIndex + 1} / {_epubSpine.Count} | Line {currentLine} / {totalLines}";
            }
            else
            {
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);
            }

            ImageIndexText.Text = $"{currentPage} / {totalPages}";
            
            double progress = totalPages > 1 ? (double)_currentVerticalPageIndex / (totalPages - 1) * 100.0 : 100.0;
            if (progress > 100) progress = 100;
            TextProgressText.Text = $"{progress:F1}%";
        }

        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isVerticalMode && (!string.IsNullOrEmpty(_currentTextContent) || _isEpubMode))
            {
                int currentLine = 1;
                if (_verticalPageInfos.Count > 0 && _currentVerticalPageIndex >= 0 && _currentVerticalPageIndex < _verticalPageInfos.Count)
                {
                    currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
                }
                _ = PrepareVerticalTextAsync(currentLine, _globalTextCts?.Token ?? default);
            }
        }

        private void RootGrid_Vertical_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Handled) return;
            if (!_isVerticalMode) return;

            // Only handle chapter navigation for EPUB in vertical mode
            if (!_isEpubMode) return;

            if (e.Key == Windows.System.VirtualKey.Home)
            {
                if (_currentEpubChapterIndex > 0)
                {
                    _currentEpubChapterIndex--;
                    _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.End)
            {
                if (_currentEpubChapterIndex < _epubSpine.Count - 1)
                {
                    _currentEpubChapterIndex++;
                    _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                }
                e.Handled = true;
            }
        }

        private async void DrawVerticalImage(CanvasDrawingSession ds, Size canvasSize, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            // Try to get from cache
            if (_verticalImageCache.TryGetValue(relativePath, out var bitmap))
            {
                if (bitmap == null) return; // Loading failed before

                // Draw centered and scaled
                float canvasW = (float)canvasSize.Width;
                float canvasH = (float)canvasSize.Height;
                float imgW = (float)bitmap.Size.Width;
                float imgH = (float)bitmap.Size.Height;

                float scale = Math.Min((canvasW - 80) / imgW, (canvasH - 80) / imgH);
                if (scale > 1.0f) scale = 1.0f; // Don't upscale too much if small

                float drawW = imgW * scale;
                float drawH = imgH * scale;
                float drawX = (canvasW - drawW) / 2;
                float drawY = (canvasH - drawH) / 2;

                ds.DrawImage(bitmap, new Rect(drawX, drawY, drawW, drawH));
            }
            else
            {
                // Trigger Load
                _verticalImageCache[relativePath] = null!; // Mark as loading (placeholder)
                _ = LoadVerticalImageAsync(relativePath);
            }
        }

        private bool DoesVerticalImageExist(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            try
            {
                if (_isEpubMode && _currentEpubArchive != null)
                {
                    return _currentEpubArchive.GetEntry(relativePath) != null;
                }
                if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextArchiveEntryKey == null)
                {
                    // Local File
                    string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentTextFilePath)!, relativePath);
                    return System.IO.File.Exists(fullPath);
                }
                else if (_currentArchive != null && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
                {
                    // Archive
                    string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                    string? baseDir = "";
                    int lastSlash = normKey.LastIndexOf('/');
                    if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                    string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                    string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                    targetKey = targetKey.Replace("/./", "/");

                    return _currentArchive.Entries.Any(e => e.Key != null && 
                           (e.Key.Replace('\\', '/') == targetKey || 
                            string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
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
                    var entry = _currentEpubArchive.GetEntry(relativePath);
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
                else if (_currentArchive != null && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
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
    }
}
