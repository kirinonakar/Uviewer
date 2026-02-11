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

            float marginTop = 40;
            float marginBottom = 40;
            float marginRight = 40;
            float marginLeft = 40; 

            float availableHeight = (float)(VerticalTextCanvas?.ActualHeight ?? 800);
            if (availableHeight < 100) availableHeight = (float)RootGrid.ActualHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            availableHeight -= (marginTop + marginBottom); 
            
            float availableWidth = (float)(VerticalTextCanvas?.ActualWidth ?? 1200);
            if (availableWidth < 100) availableWidth = (float)RootGrid.ActualWidth - 100;
            if (availableWidth < 100) availableWidth = 1000;
            availableWidth -= (marginRight + marginLeft);

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
            var device = VerticalTextCanvas?.Device;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                if (block.HasImage)
                {
                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null && !DoesVerticalImageExist(aozoraImg.Source))
                    {
                        index++;
                        continue;
                    }

                    if (pageBlocks.Count > 0) break;
                    
                    pageBlocks.Add(block);
                    index++;

                    // EPUB에서 SideBySide 모드인 경우 이미지를 가로로 한 장 더 배치 (Space 키 토글 지원)
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
                                    break; // Max 2 images reached
                                }
                            }

                            // Skip whitespace blocks between images
                            bool isWhitespace = nextBlock.Inlines.All(inline => 
                                (inline is string s && string.IsNullOrWhiteSpace(s)) || 
                                (inline is AozoraLineBreak));
                            
                            if (isWhitespace)
                            {
                                index++;
                                continue;
                            }
                            break; // Text block found, stop grouping
                        }
                    }
                    break;
                }

                float fontSize = (float)(_textFontSize * block.FontSizeScale);
                
                // [핵심 수정] Pixel-accurate measurement using CanvasTextLayout
                float blockTotalWidth = MeasureVerticalBlockWidth(device, block, availableHeight, fontSize);

                // 공간 체크 (안전 여백 Safety Buffer 적용)
                // HitTest 정밀 계산을 위해 여백을 최소화 (5px)
                float safetyBuffer = 5.0f;

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

        private float MeasureVerticalBlockWidth(CanvasDevice? device, AozoraBindingModel block, float availableHeight, float fontSize)
        {
            if (device == null) return fontSize * 2.0f;

            // Build text same as in Draw method
            StringBuilder sb = new StringBuilder();
            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(NormalizeVerticalText(s));
                else if (inline is AozoraRuby ruby)
                {
                    sb.Append(NormalizeVerticalText(ruby.BaseText));
                }
                else if (inline is AozoraBold bold) sb.Append(NormalizeVerticalText(bold.Text));
                else if (inline is AozoraItalic italic) sb.Append(NormalizeVerticalText(italic.Text));
                else if (inline is AozoraCode code) sb.Append(NormalizeVerticalText(code.Text));
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
                Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                WordWrapping = CanvasWordWrapping.EmergencyBreak,
                LineSpacing = fontSize * 1.8f,
                VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };

            // Using same measureWidth (fontSize * 2f) as in Draw method
            using var layout = new CanvasTextLayout(device, text, format, fontSize * 2.0f, availableHeight);
            
            // [롤백]
            // using var typography = new CanvasTypography();
            // typography.AddFeature(CanvasTypographyFeatureName.ProportionalAlternateWidths, 1);
            // layout.SetTypography(0, text.Length, typography);
            
            float boundsWidth = (float)layout.LayoutBounds.Width;
            float spacing = fontSize * 0.6f;
            
            // 루비 공간은 제외 (오른쪽 여백 사용)
            return boundsWidth + spacing;
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

            float marginTop = 40;
            float marginBottom = 40;
            float marginRight = 40;
            float marginLeft = 40;

            // [좌표 기준] currentX: 현재 줄의 "가장 오른쪽 끝" 좌표
            float currentX = (float)size.Width - marginRight; 
            float startY = marginTop;
            float drawHeight = (float)size.Height - (marginTop + marginBottom);

            if (page.Blocks == null) return;

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

            foreach (var block in page.Blocks)
            {

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
                    if (inline is string s) sb.Append(NormalizeVerticalText(s));
                    else if (inline is AozoraRuby ruby)
                    {
                        var normBase = NormalizeVerticalText(ruby.BaseText);
                        sb.Append(normBase);
                        rubyRanges.Add((start, normBase.Length, ruby.RubyText));
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
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }

                if (block.IsTable && block.TableRows.Count > 0)
                {
                    foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
                }

                string blockText = sb.ToString();

                // 1. 텍스트 레이아웃 생성
                // 1. 텍스트 레이아웃 생성
                using var textLayout = new CanvasTextLayout(ds, blockText, format, measureWidth, drawHeight);

                // [수정] 괄호 간격 수동 조정 (여는/닫는 괄호의 자간을 좁힘)
                ApplyVerticalBracketSpacing(textLayout, blockText, fontSize);
                
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

                // 2. [핵심 수정] 텍스트의 실제 점유 영역(Bounding Box) 계산
                var bounds = textLayout.LayoutBounds;

                // 3. [핵심 수정] 왼쪽 마진 진입 여부 체크 (잘림 방지)
                // 루비 공간을 두께에 포함하지 않음 (루비는 오른쪽 여백 사용)
                float currentLineThickness = (float)bounds.Width;

                // 4. 그리기 위치(drawX) 보정
                // 목표: 텍스트의 "오른쪽 끝(Left + Width)"이 "currentX"에 딱 맞아야 함.
                float drawX = currentX - (float)(bounds.X + bounds.Width);
                
                // Y축 정렬
                float drawY = startY + (float)block.Margin.Top;
                if (block.Alignment == TextAlignment.Center) drawY = (float)((size.Height - bounds.Height) / 2);
                else if (block.Alignment == TextAlignment.Right) drawY = (float)(size.Height - bounds.Height - marginBottom);

                // 5. [안전 장치] 왼쪽 여백 침범 체크 (marginLeft 사용)
                // 여유 공간을 조금 더 타이트하게 잡음
                if (currentX - currentLineThickness < marginLeft) break; 

                // 5. 본문 그리기
                ds.DrawTextLayout(textLayout, drawX, drawY, textColor);

                // 5. 루비 그리기 (개선됨: 겹침 방지 처리)
                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = _textFontFamily,
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };

                // [수정] 루비 겹침 방지를 위해 리스트에 먼저 담음
                var rubyRenderInfos = new List<RubyRenderInfo>();

                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;

                        float rubyX = drawX + (float)charBounds.Left + (float)charBounds.Width + (rubyFontSize * 2.2f);
                        float rubyY = drawY + (float)charBounds.Top; // Base Y position

                        // 루비 레이아웃 생성
                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, rubyFontSize * 1.5f);
                        float rubyHeight = (float)rubyLayout.LayoutBounds.Height;
                        float charHeight = (float)charBounds.Height;

                        // 본문 글자 높이의 중앙에 정렬했을 때의 이상적인 Top 위치
                        float idealTop = rubyY + (charHeight - rubyHeight) / 2;

                        rubyRenderInfos.Add(new RubyRenderInfo
                        {
                            Layout = rubyLayout,
                            IdealY = idealTop,
                            Height = rubyHeight,
                            X = rubyX,
                            Y = idealTop // 초기값은 Ideal position
                        });
                    }
                }

                // [수정] 겹침 해결 로직 적용
                ResolveRubyOverlaps(rubyRenderInfos);

                // [수정] 최종 위치에 그리기
                foreach (var info in rubyRenderInfos)
                {
                    ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor);
                    info.Layout.Dispose(); // 중요: 자원 해제
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
                if (direction > 0)
                {
                    // Find the max chapter index on the current page to skip already seen chapters
                    int maxChapterOnPage = _currentEpubChapterIndex;
                    var currentPage = _verticalPageInfos[_currentVerticalPageIndex];
                    if (currentPage.Blocks.Count > 0)
                        maxChapterOnPage = currentPage.Blocks.Max(b => b.EpubChapterIndex);

                    if (maxChapterOnPage < _epubSpine.Count - 1)
                    {
                        _currentEpubChapterIndex = maxChapterOnPage + 1;
                        await LoadEpubChapterAsync(_currentEpubChapterIndex);
                    }
                }
                else if (direction < 0 && _currentEpubChapterIndex > 0)
                {
                    int prevIndex = _currentEpubChapterIndex - 1;

                    // In SideBySide mode, chapters are often merged (Current + Next). 
                    // Going back 1 chapter loads (Prev + Current), and fromEnd puts us at End of Current.
                    // So we must go back 2 chapters to land at End of Prev.
                    if (_isSideBySideMode && prevIndex > 0)
                    {
                        var currentPage = _verticalPageInfos[_currentVerticalPageIndex];
                        if (currentPage.Blocks.Any(b => b.HasImage))
                        {
                            prevIndex--;
                        }
                    }

                    _currentEpubChapterIndex = prevIndex;
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
                ImageInfoText.Text = $"Line {currentLine} / {totalLines}";

                // Calculate global progress for EPUB
                double totalProgress = 0;
                if (_epubSpine.Count > 0)
                {
                    double chapterProgress = (double)_currentEpubChapterIndex / _epubSpine.Count;
                    double pageProgressInChapter = (double)(currentPage - 1) / totalPages / _epubSpine.Count;
                    totalProgress = (chapterProgress + pageProgressInChapter) * 100.0;
                    if (totalProgress > 100) totalProgress = 100;
                }
                TextProgressText.Text = $"{totalProgress:F1}%";
                ImageIndexText.Text = $"{currentPage} / {totalPages} (Ch.{_currentEpubChapterIndex + 1})";
            }
            else
            {
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);
                ImageIndexText.Text = $"{currentPage} / {totalPages}";
                
                double progress = totalPages > 1 ? (double)_currentVerticalPageIndex / (totalPages - 1) * 100.0 : 100.0;
                if (progress > 100) progress = 100;
                TextProgressText.Text = $"{progress:F1}%";
            }
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

            // Space to toggle SideBySide for images in vertical mode
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                _isSideBySideMode = !_isSideBySideMode;
                UpdateSideBySideButtonState();

                // Preserve current line position when toggling
                int currentLine = 1;
                if (_verticalPageInfos.Count > _currentVerticalPageIndex)
                    currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;

                _ = PrepareVerticalTextAsync(currentLine);
                e.Handled = true;
                return;
            }

            // Handle Home/End navigation (within page range or chapters)

            if (e.Key == Windows.System.VirtualKey.Home)
            {
                if (_currentVerticalPageIndex > 0)
                {
                    _currentVerticalPageIndex = 0;
                    VerticalTextCanvas?.Invalidate();
                    UpdateTextStatusBar();
                    e.Handled = true;
                }
                else if (_isEpubMode && _currentEpubChapterIndex > 0)
                {
                    _currentEpubChapterIndex--;
                    _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                    e.Handled = true;
                }
            }
            else if (e.Key == Windows.System.VirtualKey.End)
            {
                if (_currentVerticalPageIndex < _verticalPageInfos.Count - 1)
                {
                    _currentVerticalPageIndex = _verticalPageInfos.Count - 1;
                    VerticalTextCanvas?.Invalidate();
                    UpdateTextStatusBar();
                    e.Handled = true;
                }
                else if (_isEpubMode && _currentEpubChapterIndex < _epubSpine.Count - 1)
                {
                    _currentEpubChapterIndex++;
                    _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
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
        private string NormalizeVerticalText(string text)
        {
            // [롤백] 글자 자체를 변환하지 않고 그대로 반환 (사용자가 이전과 같다고 하여 스페이싱 조정 방식으로 변경)
            return text;
        }

        private void ApplyVerticalBracketSpacing(CanvasTextLayout layout, string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 조정할 괄호 목록
            string brackets = "()[]{}<>（）「」『』【】〈〉《》";
            float spacingReduction = -fontSize * 0.4f; // 40% 정도 좁힘

            for (int i = 0; i < text.Length; i++)
            {
                if (brackets.Contains(text[i]))
                {
                    // 해당 글자의 자간을 줄임 (Trailing Spacing을 줄임)
                    // Win2D SetCharacterSpacing(idx, count, leading, trailing, minAdvance)
                    layout.SetCharacterSpacing(i, 1, 0, spacingReduction, 0);
                }
            }
        }
    }
}
