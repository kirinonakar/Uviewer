using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isAozoraMode = true;
        private bool _isMarkdownRenderMode = false;
        private List<AozoraBindingModel> _aozoraBlocks = new();
        private int _aozoraTotalLineCount = 0; // Total visual blocks
        private int _aozoraTotalLineCountInSource = 0; // Total text lines in source file

        // On-demand rendering state
        private int _currentAozoraStartBlockIndex = 0;
        private int _currentAozoraEndBlockIndex = 0;
        private Stack<int> _aozoraNavHistory = new();
        
        // Page Calculation
        private int _aozoraTotalPages = 0;
        private bool _isAozoraPageCalcCompleted = false;
        private System.Threading.CancellationTokenSource? _aozoraPageCalcCts;
        private int _aozoraCalculatedCurrentPage = 1;



        public class AozoraBindingModel
        {
            public List<object> Inlines { get; set; } = new(); // String (for text), AozoraBold (for bold), AozoraRuby (for ruby)
            public double FontSizeScale { get; set; } = 1.0;
            public TextAlignment Alignment { get; set; } = TextAlignment.Left;
            public Thickness Margin { get; set; } = new Thickness(0);
            public Thickness Padding { get; set; } = new Thickness(0);
            public Windows.UI.Color? BorderColor { get; set; } = null;
            public Thickness BorderThickness { get; set; } = new Thickness(0);
            public Windows.UI.Color? BackgroundColor { get; set; } = null;
            public string? FontFamily { get; set; } = null; // Override font family (e.g. for code)
            public bool IsTable { get; set; } = false;
            public List<List<string>> TableRows { get; set; } = new();
            public int SourceLineNumber { get; set; } = 0; // Original line number in source text
            public int HeadingLevel { get; set; } = 0; // 0=None, 1=Large/H1, 2=Medium/H2, 3=Small/H3...
            public string HeadingText { get; set; } = "";
            public bool HasImage => Inlines.Any(i => i is AozoraImage);
            public int EpubChapterIndex { get; set; } = -1;
            public bool IsPageBreak { get; set; } = false;
            public bool IsBold { get; set; } = false;
            public double BlockIndent { get; set; } = 0;
            public bool IsBlankLine { get; set; } = false;
            public bool IsParagraphContinuation { get; set; } = false; 
        }

        public class AozoraBold { public string Text { get; set; } = ""; }
        public class AozoraItalic { public string Text { get; set; } = ""; }
        public class AozoraCode { public string Text { get; set; } = ""; }
        public class AozoraLineBreak { }
        public class AozoraRuby { public string BaseText { get; set; } = ""; public string RubyText { get; set; } = ""; public bool IsBold { get; set; } = false; }
        public class AozoraTCY { public string Text { get; set; } = ""; public bool IsBold { get; set; } = false; }
        public class AozoraImage { public string Source { get; set; } = ""; }

        // Settings
        public class AozoraSettings
        {
            public bool IsAozoraModeEnabled { get; set; } = true;
        }
        
        private const string AozoraSettingsFilePath = "aozora_settings.json";
        private string GetAozoraSettingsFilePath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", AozoraSettingsFilePath);
        
        [System.Text.Json.Serialization.JsonSerializable(typeof(AozoraSettings))]
        public partial class AozoraSettingsContext : System.Text.Json.Serialization.JsonSerializerContext;


        private void LoadAozoraSettings()
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
                
                // Sync UI
                if (AozoraToggleButton != null)
                {
                    AozoraToggleButton.IsChecked = _isAozoraMode;
                }
            }
            catch { }
        }

        private void SaveAozoraSettings()
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

        private void AozoraToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAozoraMode();
        }

        private async void ToggleAozoraMode()
        {
            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
            await Task.Delay(10); 
            // 1. 모드 전환 전, 현재 보고 있는 줄 번호를 캡처합니다.
            int currentLine = 1;
            
            if (_isAozoraMode)
            {
                // 아오조라 모드 -> 일반 텍스트 모드로 갈 때: 현재 아오조라 페이지의 첫 줄 캡처
                if (_aozoraBlocks != null && _aozoraBlocks.Count > 0 && 
                    _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                {
                    currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }
            }
            else
            {
                // 일반 텍스트 모드 -> 아오조라 모드로 갈 때: 현재 스크롤에 보이는 첫 줄 캡처
                if (TextScrollViewer != null)
                {
                    currentLine = GetTopVisibleLineIndex();
                }
            }

            // 2. 캡처한 줄 번호를 대기열(Pending) 변수에 넣습니다. 
            // (DisplayLoadedText가 실행될 때 이 값을 읽어 해당 줄로 이동시킵니다)
            _aozoraPendingTargetLine = currentLine > 0 ? currentLine : 1;

            // 3. 모드 토글
            _isAozoraMode = !_isAozoraMode;
            
            if (AozoraToggleButton != null)
            {
                AozoraToggleButton.IsChecked = _isAozoraMode;
            }
            
            SaveAozoraSettings();

            // 4. 새 모드에 맞춰 텍스트 다시 렌더링
            if (!string.IsNullOrEmpty(_currentTextContent))
            {
                CancelAndResetGlobalTextCts();
                // 파일 이름이 없으면 임시 이름 제공
                string displayName = string.IsNullOrEmpty(_currentTextFilePath) ? "Document" : System.IO.Path.GetFileName(_currentTextFilePath);
                
                await DisplayLoadedText(_currentTextContent, displayName, _currentTextFilePath, _globalTextCts!.Token);
            }
        }
        
        /// <summary>
        /// Reload text display using cached content (no disk I/O)
        /// </summary>
        private async Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine)
        {
            try
            {
                // CRITICAL: Cancel ALL pending background calculations immediately
                // This prevents the old mode's heavy background work from blocking UI
                _aozoraPageCalcCts?.Cancel();
                _pageCalcCts?.Cancel();
                _aozoraResizeCts?.Cancel();
                
                // Set pending target
                _aozoraPendingTargetLine = targetLine;
                
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                if (_isAozoraMode)
                {
                    // Switch to Aozora mode display
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                    if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Visible;
                    
                    await PrepareAozoraDisplayAsync(_currentTextContent, targetLine, token);
                    FileNameText.Text = GetFormattedDisplayName(fileName, _currentTextArchiveEntryKey != null);
                }
                else
                {
                    // Switch to Simple Text mode display
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                    if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Collapsed;
                    
                    // Ensure default template
                    if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                    {
                         TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                    }
                    
                    // Progressive loading using cached content
                    await LoadTextLinesProgressivelyAsync(_currentTextContent, targetLine, token);
                    
                    // Update Text Status
                    UpdateTextStatusBar(fileName, _textTotalLineCountInSource, 1);
                    
                    // Scroll to target line
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

        private async void StartAozoraPageCalculationAsync()
        {
            // Early exit if not in Aozora mode
            if (!_isAozoraMode) return;
            
            _aozoraPageCalcCts?.Cancel();
            _aozoraPageCalcCts = new System.Threading.CancellationTokenSource();
            var token = _aozoraPageCalcCts.Token;

            _isAozoraPageCalcCompleted = false;
            _aozoraTotalPages = 0;
            _aozoraCalculatedCurrentPage = 1;
            UpdateAozoraStatusBar();

            if (AozoraPageContainer == null || AozoraPageContainer.ActualHeight <= 0 || AozoraPageContainer.ActualWidth <= 0)
            {
                 // Wait a bit or return. PrepareAozoraDisplayAsync waits for container, so we should be good mostly.
                 // But if resized to 0?
                 return;
            }

            double availableWidth = AozoraPageContainer.ActualWidth;
            if (availableWidth < 100) availableWidth = 800;
            double innerWidth = availableWidth - 40; // Grid Padding (20+20)

            double availableHeight = AozoraPageContainer.ActualHeight;
            if (availableHeight < 200) availableHeight = 800;
            availableHeight -= 45; // 40 (Grid Padding) + 5 (Ruby safety gap)

            double maxWidth = _isMarkdownRenderMode ? innerWidth : Math.Min(innerWidth, GetUrlMaxWidth());
            
            try
            {
                var dummyRTB = new RichTextBlock
                {
                    FontFamily = new FontFamily(_textFontFamily),
                    FontSize = _textFontSize,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = maxWidth
                };

                double currentPageHeight = 0;
                int pageCount = 1;
                
                // Track start indices of pages to find current page later? 
                // Or just count total. For Current Page, we need to map _currentAozoraStartBlockIndex to a page number.
                // We'll store a map: BlockIndex -> PageNumber
                var blockToPageMap = new Dictionary<int, int>();

                var sw = System.Diagnostics.Stopwatch.StartNew();

                AozoraBindingModel? currentMergedBlock = null;
                double currentMergedBlockHeight = 0;

                for (int i = 0; i < _aozoraBlocks.Count; i++)
                {
                    if (token.IsCancellationRequested) return;
                    blockToPageMap[i] = pageCount;
                    var block = _aozoraBlocks[i];

                    // --- [추가: 罫囲み 및 테두리 블록 그룹화 페이지 측정] ---
                    if (block.BorderColor != null || block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 || block.BorderThickness.Bottom > 0)
                    {
                        double boxWidth = maxWidth - 10;
                        if (boxWidth < 100) boxWidth = maxWidth;

                        var border = new Border
                        {
                            HorizontalAlignment = HorizontalAlignment.Left,
                            MaxWidth = boxWidth,
                            BorderBrush = new SolidColorBrush(block.BorderColor ?? Colors.Gray),
                            BorderThickness = block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 ? block.BorderThickness : new Thickness(1.5),
                            Padding = block.Padding.Left > 0 ? block.Padding : new Thickness(15),
                            Margin = new Thickness(0, 10, 0, 10)
                        };

                        var innerRtb = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
                        border.Child = innerRtb;
                        
                        var pWrapper = new Paragraph();
                        pWrapper.Inlines.Add(new InlineUIContainer { Child = border });

                        dummyRTB.Blocks.Clear();
                        dummyRTB.Blocks.Add(pWrapper);

                        int k = i;
                        bool forcedRender = false;

                        // 💡 [수정] 페이지 계산 시에도 화면을 초과하면 분할하여 측정합니다.
                        while (k < _aozoraBlocks.Count && 
                               _aozoraBlocks[k].BorderColor == block.BorderColor && 
                               _aozoraBlocks[k].BorderThickness == block.BorderThickness)
                        {
                            var kb = _aozoraBlocks[k];
                            var innerP = CreateParagraphFromBlock(kb, availableHeight, boxWidth);
                            innerRtb.Blocks.Add(innerP);

                            dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                            double currentBoxHeight = dummyRTB.DesiredSize.Height;

                            if (currentPageHeight + currentBoxHeight > availableHeight)
                            {
                                if (innerRtb.Blocks.Count > 1)
                                {
                                    innerRtb.Blocks.Remove(innerP);
                                    break;
                                }
                                else if (currentPageHeight > 0)
                                {
                                    // 현재 페이지에 자리가 부족하면 박스를 새 페이지로 이동
                                    pageCount++;
                                    currentPageHeight = 0;
                                    dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                                    if (dummyRTB.DesiredSize.Height > availableHeight)
                                    {
                                        forcedRender = true;
                                        blockToPageMap[k] = pageCount;
                                        k++;
                                        break;
                                    }
                                }
                                else
                                {
                                    forcedRender = true;
                                    blockToPageMap[k] = pageCount;
                                    k++;
                                    break;
                                }
                            }
                            
                            blockToPageMap[k] = pageCount;
                            k++;
                        }

                        dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                        double finalBoxHeight = dummyRTB.DesiredSize.Height;

                        currentPageHeight += finalBoxHeight;
                        currentMergedBlockHeight = finalBoxHeight;

                        bool willBreakPage = false;
                        if (k < _aozoraBlocks.Count && 
                            _aozoraBlocks[k].BorderColor == block.BorderColor && 
                            _aozoraBlocks[k].BorderThickness == block.BorderThickness)
                        {
                            willBreakPage = true; // 아직 렌더링할 박스 내용이 남음
                        }

                        if (currentPageHeight > availableHeight || forcedRender || willBreakPage)
                        {
                            currentPageHeight = 0;
                            pageCount++;
                        }

                        i = k - 1;
                        currentMergedBlock = null;
                        continue;
                    }
                    // --- [罫囲み 그룹화 끝] ---
                    
                    if (block.HasImage || block.IsPageBreak)
                    {
                        if (currentPageHeight > 0)
                        {
                            pageCount++;
                            currentPageHeight = 0;
                            blockToPageMap[i] = pageCount;
                        }
                        pageCount++;
                        currentPageHeight = 0;
                        currentMergedBlock = null;
                        continue;
                    }

                    if (block.IsParagraphContinuation && currentMergedBlock != null)
                    {
                        currentMergedBlock.Inlines.AddRange(block.Inlines);
                        
                        dummyRTB.Blocks.Clear();
                        dummyRTB.Blocks.Add(CreateParagraphFromBlock(currentMergedBlock, availableHeight));
                        dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                        
                        double newHeight = dummyRTB.DesiredSize.Height;
                        double heightDiff = newHeight - currentMergedBlockHeight;

                        if (currentPageHeight + heightDiff > availableHeight)
                        {
                            pageCount++;
                            currentPageHeight = 0;
                            
                            // 초과한 문장은 새 페이지의 시작 문단이 됨
                            currentMergedBlock = CloneBlockProperties(block, true);
                            dummyRTB.Blocks.Clear();
                            dummyRTB.Blocks.Add(CreateParagraphFromBlock(currentMergedBlock, availableHeight));
                            dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                            
                            currentMergedBlockHeight = dummyRTB.DesiredSize.Height;
                            currentPageHeight = currentMergedBlockHeight;
                            blockToPageMap[i] = pageCount;
                        }
                        else
                        {
                            currentPageHeight += heightDiff;
                            currentMergedBlockHeight = newHeight;
                        }
                        continue;
                    }

                    currentMergedBlock = CloneBlockProperties(block, true);
                    dummyRTB.Blocks.Clear();
                    dummyRTB.Blocks.Add(CreateParagraphFromBlock(block, availableHeight));
                    dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                    double blockHeight = dummyRTB.DesiredSize.Height;
                    
                    if (currentPageHeight + blockHeight > availableHeight && currentPageHeight > 0)
                    {
                        pageCount++;
                        currentPageHeight = 0;
                        blockToPageMap[i] = pageCount;
                    }
                    
                    currentPageHeight += blockHeight;
                    currentMergedBlockHeight = blockHeight;
                    
                    if (currentPageHeight > availableHeight)
                    {
                         currentPageHeight = 0; 
                         pageCount++;
                    }

                    if (sw.ElapsedMilliseconds > 3) 
                    {
                        await Task.Delay(1, token);
                        sw.Restart();
                    }
                }

                _aozoraTotalPages = pageCount;
                _isAozoraPageCalcCompleted = true;
                
                // Find current page based on _currentAozoraStartBlockIndex
                if (blockToPageMap.TryGetValue(_currentAozoraStartBlockIndex, out int cp))
                {
                    _aozoraCalculatedCurrentPage = cp;
                }
                else
                {
                    _aozoraCalculatedCurrentPage = 1;
                }

                UpdateAozoraStatusBar();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Aozora Page Calc Error: {ex}");
            }
        }
        
        // Current page index for virtualized navigation

        private int _aozoraPendingTargetLine = 1; // Used to carry target position during load (positive=line, negative=page)
        private double _lastAozoraContainerHeight = 0;
        private double _lastAozoraContainerWidth = 0;
        private System.Threading.CancellationTokenSource? _aozoraResizeCts;
        
        private async Task PrepareAozoraDisplayAsync(string rawContent, int targetLine = 1, CancellationToken token = default)
        {
            try
            {
                // Reset state immediately to prevent stale index usage during async parsing
                _currentAozoraStartBlockIndex = 0;
                _currentAozoraEndBlockIndex = 0;
                // Do not clear _aozoraBlocks here if we need them for transition, 
                // but since we are replacing content, safer to clear or let it be replaced.
                // Clearing might cause UI flicker if binding is live? 
                // But we act on _aozoraBlocks in other threads. 
                // Ideally, we shouldn't touch _aozoraBlocks until we have new ones.
                // But _currentAozoraStartBlockIndex MUST be reset.

                // Priority: Explicit pending target from Favorite/Recent navigation overrides automatic restoration
                if (_aozoraPendingTargetLine != 1)
                {
                    targetLine = _aozoraPendingTargetLine;
                    _aozoraPendingTargetLine = 1; // Reset after use
                }
                // Check if current file is Markdown
                bool isMarkdown = false;
                if (!string.IsNullOrEmpty(_currentTextFilePath))
                {
                    var ext = System.IO.Path.GetExtension(_currentTextFilePath).ToLower();
                    if (ext == ".md" || ext == ".markdown")
                    {
                        isMarkdown = true;
                    }
                }

                if (isMarkdown)
                {
                    _isMarkdownRenderMode = true;
                    _aozoraBlocks = await Task.Run(() => ParseMarkdownContent(rawContent));
                    _aozoraTotalLineCountInSource = _aozoraBlocks.Count; // Markdown line count approximation
                    _textTotalLineCountInSource = _aozoraBlocks.Count; 
                }
                else
                {
                    _isMarkdownRenderMode = false;

                    // [Bold Preprocessing] Handle "last start wins" logic globally across lines
                    string boldStartTag = @"［＃(?:ここから太字)］";
                    string boldEndTag = @"［＃(?:ここで太字終わり)］";
                    rawContent = Regex.Replace(rawContent, $"{boldStartTag}(.*?){boldEndTag}", (m) => {
                        string inner = m.Groups[1].Value;
                        var startRegex = new Regex(boldStartTag);
                        var parts = startRegex.Split(inner);
                        if (parts.Length <= 1) return $"@@BOLD_START@@{inner}@@BOLD_END@@";
                        string prefix = string.Join("", parts.Take(parts.Length - 1));
                        string boldContent = parts.Last();
                        return $"{prefix}@@BOLD_START@@{boldContent}@@BOLD_END@@";
                    }, RegexOptions.Singleline);
                    
                    // Optimized Loading for Large Files
                    // User Request: "File first open, then TOC background calculate"
                    // TOC is derived from _aozoraBlocks structure. Parsing creates blocks.
                    // We split parsing: First Chunk (Immediate) -> Render -> Rest (Background)
                    
                    var lines = rawContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                    _aozoraTotalLineCountInSource = lines.Length;
                    _textTotalLineCountInSource = lines.Length; 

                    int initialLimit = 2000;
                    if (targetLine > initialLimit - 500) initialLimit = targetLine + 500;

                    if (lines.Length > initialLimit)
                    {
                        // 1. Initial Load (First initialLimit lines)
                        var initialLines = lines.Take(initialLimit).ToArray();
                        _aozoraBlocks = await Task.Run(() => ParseAozoraLines(initialLines, 1));
                        
                        // Proceed to render first page immediately below...
                        
                        // 2. Queue Background Load for the rest
                        var activeBlocksRef = _aozoraBlocks; 
                        int expectedContentHash = rawContent.GetHashCode(); // [추가] 텍스트의 고유 해시값 캡처
                        int expectedSourceCount = lines.Length; // [추가] 원본 텍스트의 실제 줄 수 캡처
                        
                        _ = Task.Run(() => 
                        {
                            try
                            {
                                if (token.IsCancellationRequested) return;
                                var restLines = lines.Skip(initialLimit).ToArray();
                                var restBlocks = ParseAozoraLines(restLines, initialLimit + 1);
                                
                                if (token.IsCancellationRequested) return;
                                this.DispatcherQueue.TryEnqueue(() => 
                                {
                                    if (token.IsCancellationRequested) return;
                                    
                                    // [핵심 해결책] 화면에 로드된 텍스트가 로딩을 시작했던 텍스트와 다르면 즉시 폐기 (다른 파일로 넘어간 경우 완벽 차단)
                                    if (string.IsNullOrEmpty(_currentTextContent) || _currentTextContent.GetHashCode() != expectedContentHash) return;
                                    
                                    // 모드 전환 등으로 객체가 새로 생성되었다면 병합 중단
                                    if (_aozoraBlocks != activeBlocksRef) return;
                                    
                                    if (!_isAozoraMode && !_isVerticalMode) return; 
                                    
                                    _aozoraBlocks.AddRange(restBlocks);
                                    _aozoraTotalLineCount = _aozoraBlocks.Count;
                                    _aozoraTotalLineCountInSource = expectedSourceCount; // [추가] 전체 줄 수 강제 동기화
                                    _textTotalLineCountInSource = expectedSourceCount; 
                                    
                                    if (_isAozoraMode)
                                    {
                                        UpdateAozoraStatusBar();
                                        StartAozoraPageCalculationAsync();
                                    }
                                    else if (_isVerticalMode)
                                    {
                                        try
                                        {
                                            int currentLine = _currentVerticalPageInfo.StartLine;
                                            if (currentLine <= 0) currentLine = 1;
                                            _ = PrepareVerticalTextAsync(currentLine);
                                        }
                                        catch { }
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Background Parse Error: {ex.Message}");
                            }
                        }, token);
                    }
                    else
                    {
                        // Small file, load all
                        _aozoraBlocks = await Task.Run(() => ParseAozoraLines(lines, 1));
                    }
                }

                _aozoraTotalLineCount = _aozoraBlocks.Count; // Actual block count (visual lines)
                // SourceLineNumber is already set in parsing
                
                // ... (Rest of function)

                _aozoraNavHistory.Clear();
                int startIdx = 0;
                if (targetLine > 1)
                {
                    // Find block by line number
                    for (int i = 0; i < _aozoraBlocks.Count; i++)
                    {
                        // Default to current index as fallback (for lines beyond end)
                        startIdx = i;

                        if (_aozoraBlocks[i].SourceLineNumber >= targetLine)
                        {
                            if (_aozoraBlocks[i].SourceLineNumber == targetLine)
                            {
                                startIdx = i;
                            }
                            else
                            {
                                // Target is between previous block and this block
                                startIdx = i > 0 ? i - 1 : 0;
                            }
                            break;
                        }
                    }
                }
                else if (targetLine < 0)
                {
                    // Legacy support: targetLine is -SavedPage
                    int targetPage = -targetLine;
                    // We can't jump to EXACT page without calculating.
                    // Let's just estimate or start from beginning?
                    // Actually, let's keep it simple: 1 page = ~50 blocks? 
                    // No, let's just start at the beginning for legacy bookmarks 
                    // or try to guess.
                    startIdx = Math.Min((targetPage - 1) * 30, _aozoraBlocks.Count - 1);
                }

                _currentAozoraStartBlockIndex = startIdx;

                // UI 설정 및 가시성 확보
                if (AozoraPageContainer != null)
                {
                    AozoraPageContainer.Background = GetThemeBackground();
                    AozoraPageContainer.Visibility = Visibility.Visible;
                }
                if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                if (TextArea != null)
                {
                    TextArea.Visibility = Visibility.Visible;
                    TextArea.Background = GetThemeBackground();
                    
                    // On-demand rendering requires Container size
                    if (AozoraPageContainer != null && (AozoraPageContainer.ActualHeight == 0 || AozoraPageContainer.ActualWidth == 0))
                    {
                        // Wait for a proper size
                        await Task.Delay(50);
                    }
                }

                if (_aozoraBlocks.Count == 0) return;

                // 즉시 현재 페이지만 렌더링
                RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                
                // 로딩 오버레이 제거
                if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
                
                // Start background page calculation
                StartAozoraPageCalculationAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Aozora Load Error: {ex.Message}");
            }

            UpdateAozoraStatusBar();
        }
        
        private void AozoraPageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            HandleAozoraContainerResize(e.NewSize.Width, e.NewSize.Height);
        }
        
        private void TextArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Also handle TextArea resize for window drag resizing
            if (!_isAozoraMode) return;
            HandleAozoraContainerResize(e.NewSize.Width, e.NewSize.Height);
        }
        
        private void HandleAozoraContainerResize(double newWidth, double newHeight)
        {
            if (!_isTextMode || !_isAozoraMode) return;
            if (_aozoraBlocks.Count == 0) return;
            
            // Debounce: Only recalculate after size change settles
            // Check both width and height changes (width affects text wrapping)
            bool heightChanged = Math.Abs(newHeight - _lastAozoraContainerHeight) >= 10;
            bool widthChanged = Math.Abs(newWidth - _lastAozoraContainerWidth) >= 10;
            
            if (!heightChanged && !widthChanged) return; // Ignore small changes
            
            _lastAozoraContainerHeight = newHeight;
            _lastAozoraContainerWidth = newWidth;
            
            // Cancel previous resize task
            _aozoraResizeCts?.Cancel();
            _aozoraResizeCts = new System.Threading.CancellationTokenSource();
            var token = _aozoraResizeCts.Token;
            
            // Delay recalculation to avoid excessive updates during drag
            _ = RecalculateAozoraPagesDelayedAsync(token);
        }
        
        private async Task RecalculateAozoraPagesDelayedAsync(System.Threading.CancellationToken token)
        {
            try
            {
                await Task.Delay(300, token); // Wait for resize to settle
                if (token.IsCancellationRequested) return;
                
                // On-demand rendering: Just re-render the same starting block
                RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                StartAozoraPageCalculationAsync(); // Recalc pages on resize
                UpdateAozoraStatusBar();
            }
            catch (TaskCanceledException) { }
        }
        
        private void RenderAozoraDynamicPage(int startIdx)
        {
            if (AozoraPageContent == null || _aozoraBlocks.Count == 0) return;
            
            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentAozoraStartBlockIndex = startIdx;
            
            AozoraPageContent.Blocks.Clear();
            AozoraPageContent.Padding = new Thickness(0, 15, 0, 0); // Add top padding to prevent first-line ruby clipping

            // Reflow fix: Calculate available width for proper measurement and wrapping
            double availableWidth = AozoraPageContainer?.ActualWidth ?? 800;
            if (availableWidth < 100) availableWidth = 800;
            double innerWidth = availableWidth - 40; // Grid Padding (20+20)

            double currentMaxWidth = _isMarkdownRenderMode ? innerWidth : Math.Min(innerWidth, GetUrlMaxWidth());
            AozoraPageContent.MaxWidth = currentMaxWidth;
            
            AozoraPageContent.FontFamily = new FontFamily(_textFontFamily);
            AozoraPageContent.FontSize = _textFontSize;
            AozoraPageContent.Foreground = GetThemeForeground();

            double availableHeight = AozoraPageContainer?.ActualHeight ?? 800;
            if (availableHeight < 200) availableHeight = 800;
            
            // Image pages should NOT have top padding for ruby safety as it might push the image down unnecessarily
            bool isImagePage = startIdx < _aozoraBlocks.Count && _aozoraBlocks[startIdx].HasImage && AozoraPageContent.Blocks.Count == 0;
            
            if (isImagePage)
            {
                if (AozoraPageContainer != null) AozoraPageContainer.Padding = new Thickness(0);
                AozoraPageContent.Padding = new Thickness(0);
                availableWidth = AozoraPageContainer?.ActualWidth ?? 800; // Recalculate full width without padding
                innerWidth = availableWidth; // No grid padding
                availableHeight = AozoraPageContainer?.ActualHeight ?? 800; // Reset height to full container
                
                AozoraPageContent.MaxWidth = innerWidth; // Bypass GetUrlMaxWidth() for images to allow full screen width
                AozoraPageContent.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                if (AozoraPageContainer != null) AozoraPageContainer.Padding = new Thickness(20);
                AozoraPageContent.Padding = new Thickness(0, 15, 0, 0);
                availableHeight -= 55; // Grid Padding + Ruby safety gap
                AozoraPageContent.MaxWidth = _isMarkdownRenderMode ? innerWidth : Math.Min(innerWidth, GetUrlMaxWidth());
                AozoraPageContent.VerticalAlignment = VerticalAlignment.Top;
            }

            double currentHeight = 0;
            int endIdx = startIdx;
            Paragraph? currentParagraph = null;

            for (int i = startIdx; i < _aozoraBlocks.Count; i++)
            {
                var block = _aozoraBlocks[i];

                // --- [추가: 罫囲み 및 테두리 블록 그룹화 렌더링] ---
                if (block.BorderColor != null || block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 || block.BorderThickness.Bottom > 0)
                {
                    double boxWidth = currentMaxWidth - 10;
                    if (boxWidth < 100) boxWidth = currentMaxWidth;

                    var border = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MaxWidth = boxWidth,
                        BorderBrush = new SolidColorBrush(block.BorderColor ?? Colors.Gray),
                        BorderThickness = block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 ? block.BorderThickness : new Thickness(1.5),
                        Padding = block.Padding.Left > 0 ? block.Padding : new Thickness(15),
                        Margin = new Thickness(0, 10, 0, 10),
                        CornerRadius = new CornerRadius(4),
                        Background = block.BackgroundColor != null ? new SolidColorBrush(block.BackgroundColor.Value) : null
                    };

                    var rtb = new RichTextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = GetThemeForeground()
                    };

                    border.Child = rtb;
                    
                    var pWrapper = new Paragraph();
                    pWrapper.Inlines.Add(new InlineUIContainer { Child = border });
                    AozoraPageContent.Blocks.Add(pWrapper);

                    int k = i;
                    bool forcedRender = false;

                    // 💡 [핵심 수정] 박스 안의 내용을 하나씩 추가하며 높이를 재고, 초과하면 루프를 중단합니다.
                    while (k < _aozoraBlocks.Count && 
                           _aozoraBlocks[k].BorderColor == block.BorderColor && 
                           _aozoraBlocks[k].BorderThickness == block.BorderThickness)
                    {
                        var kb = _aozoraBlocks[k];
                        var innerPara = CreateParagraphFromBlock(kb, availableHeight, boxWidth);
                        rtb.Blocks.Add(innerPara);

                        AozoraPageContent.Measure(new Windows.Foundation.Size(AozoraPageContent.MaxWidth, double.PositiveInfinity));
                        
                        if (AozoraPageContent.DesiredSize.Height > availableHeight)
                        {
                            if (rtb.Blocks.Count > 1)
                            {
                                // 현재 줄을 추가했더니 화면을 넘어감 -> 이 줄부터는 다음 페이지로
                                rtb.Blocks.Remove(innerPara);
                                break;
                            }
                            else if (AozoraPageContent.Blocks.Count > 1)
                            {
                                // 박스의 첫 줄조차 화면에 다 안 들어감 -> 박스 전체를 다음 페이지로 이동
                                AozoraPageContent.Blocks.Remove(pWrapper);
                                break;
                            }
                            else
                            {
                                // 한 줄짜리인데 화면보다 큰 경우 -> 강제 렌더링 (무한루프 방지)
                                forcedRender = true;
                                k++;
                                break;
                            }
                        }
                        k++;
                    }

                    if (!AozoraPageContent.Blocks.Contains(pWrapper))
                    {
                        // 박스 전체가 다음 페이지로 밀린 경우 루프 종료
                        break;
                    }

                    AozoraPageContent.Measure(new Windows.Foundation.Size(AozoraPageContent.MaxWidth, double.PositiveInfinity));
                    currentHeight = AozoraPageContent.DesiredSize.Height;
                    endIdx = k - 1;
                    i = endIdx; 
                    currentParagraph = null; 
                    
                    // 💡 아직 렌더링하지 못한 남은 박스 내용이 있다면, 이 시점에서 페이지를 끊고 다음 페이지로 넘깁니다.
                    if (k < _aozoraBlocks.Count && 
                        _aozoraBlocks[k].BorderColor == block.BorderColor && 
                        _aozoraBlocks[k].BorderThickness == block.BorderThickness) 
                    {
                        break;
                    }
                    if (forcedRender || (currentHeight > availableHeight && AozoraPageContent.Blocks.Count == 1)) break;
                    continue;
                }
                // --- [罫囲み 그룹화 끝] ---

                // 1. 이어지는 문장인 경우 현재 문단에 합치기 시도
                if (block.IsParagraphContinuation && currentParagraph != null && !block.HasImage && !block.IsTable && block.HeadingLevel == 0)
                {
                    var pTemp = CreateParagraphFromBlock(block, availableHeight, innerWidth);
                    var inlinesToMove = pTemp.Inlines.ToList();
                    pTemp.Inlines.Clear();
                    
                    foreach (var inline in inlinesToMove) currentParagraph.Inlines.Add(inline);
                    
                    AozoraPageContent.Measure(new Windows.Foundation.Size(AozoraPageContent.MaxWidth, double.PositiveInfinity));
                    double newHeight = AozoraPageContent.DesiredSize.Height;
                    
                    // 합쳤는데 페이지를 초과하면 원상복구하고 다음 페이지로 넘김
                    if (newHeight > availableHeight)
                    {
                        foreach (var inline in inlinesToMove) currentParagraph.Inlines.Remove(inline);
                        break;
                    }
                    
                    currentHeight = newHeight;
                    endIdx = i;
                    continue; // 덧붙이기 성공했으므로 다음 블록으로
                }

                // 2. 새 문단이거나 분리가 안 되는 블록 처리
                var p = CreateParagraphFromBlock(block, availableHeight, innerWidth);

                if (p.Inlines.Count == 0)
                {
                    endIdx = i;
                    continue;
                }
                
                if (block.HasImage || block.IsPageBreak)
                {
                    if (AozoraPageContent.Blocks.Count > 0) break;
                    else
                    {
                        if (block.HasImage)
                        {
                            p.TextAlignment = TextAlignment.Center;
                            if (AozoraPageContainer != null) AozoraPageContainer.Padding = new Thickness(0);
                            AozoraPageContent.MaxWidth = innerWidth;
                            AozoraPageContent.Padding = new Thickness(0);
                            AozoraPageContent.VerticalAlignment = VerticalAlignment.Center;
                        }
                        AozoraPageContent.Blocks.Add(p);
                        endIdx = i;
                        break;
                    }
                }

                AozoraPageContent.Blocks.Add(p);
                AozoraPageContent.Measure(new Windows.Foundation.Size(AozoraPageContent.MaxWidth, double.PositiveInfinity));
                double measuredHeight = AozoraPageContent.DesiredSize.Height;
                
                if (AozoraPageContent.Blocks.Count > 1 && measuredHeight > availableHeight)
                {
                    AozoraPageContent.Blocks.Remove(p);
                    break;
                }
                
                currentHeight = measuredHeight;
                endIdx = i;

                // 다음 블록이 덧붙일 수 있도록 기준 문단 캐싱
                if (!block.HasImage && !block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                    currentParagraph = p;
                else
                    currentParagraph = null;
                
                if (currentHeight > availableHeight && AozoraPageContent.Blocks.Count == 1) break;
            }
            
            _currentAozoraEndBlockIndex = endIdx;

            // Scroll to top
            if (AozoraPageScroll != null)
            {
                AozoraPageScroll.ChangeView(null, 0, null, true);
            }
        }

        private Paragraph CreateParagraphFromBlock(AozoraBindingModel block, double availableHeight = 0, double targetWidth = -1)
        {
            if (block.IsTable)
            {
                var tablePara = new Paragraph();
                tablePara.Inlines.Add(CreateTableInline(block.TableRows));
                return tablePara;
            }
            
            var p = new Paragraph();
            if (block.HasImage)
            {
                // Images shouldn't have forced line height which might cause clipping/offsetting
                p.LineHeight = 0;
                p.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            }
            else
            {
                p.LineHeight = _textFontSize * block.FontSizeScale * (block.IsBlankLine ? 1.0 : 2.0); // Reduced multiplier for blank lines
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            p.Margin = new Thickness(block.BlockIndent > 0 ? block.BlockIndent : 0, block.Margin.Top, 0, block.Margin.Bottom);
            p.TextAlignment = block.Alignment;
            p.FontFamily = block.FontFamily != null ? new FontFamily(block.FontFamily) : new FontFamily(_textFontFamily);
            p.FontWeight = block.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
            
            foreach (var item in block.Inlines)
            {
                if (item is string text)
                {
                    p.Inlines.Add(new Run { Text = text, FontSize = _textFontSize * block.FontSizeScale });
                }
                else if (item is AozoraBold bold)
                {
                    p.Inlines.Add(new Run { Text = bold.Text, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = _textFontSize * block.FontSizeScale });
                }
                else if (item is AozoraItalic italic)
                {
                    p.Inlines.Add(new Run { Text = italic.Text, FontStyle = Windows.UI.Text.FontStyle.Italic, FontSize = _textFontSize * block.FontSizeScale });
                }
                else if (item is AozoraLineBreak)
                {
                    p.Inlines.Add(new LineBreak());
                }
                else if (item is AozoraCode code)
                {
                    p.Inlines.Add(new Run { Text = code.Text, FontFamily = new FontFamily("Consolas, Courier New, Monospace"), Foreground = new SolidColorBrush(Colors.DarkSlateGray), FontSize = _textFontSize * block.FontSizeScale });
                }
                else if (item is AozoraRuby ruby)
                {
                    var weight = (ruby.IsBold || block.IsBold) ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, _textFontSize * block.FontSizeScale, weight));
                }
                else if (item is AozoraTCY tcy)
                {
                    var weight = (tcy.IsBold || block.IsBold) ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    p.Inlines.Add(new Run { Text = tcy.Text, FontSize = _textFontSize * block.FontSizeScale, FontWeight = weight });
                }
                else if (item is AozoraImage img)
                {
                    var ui = CreateImageInline(img.Source, availableHeight, targetWidth);
                    if (ui != null) p.Inlines.Add(ui);
                }
            }
            return p;
        }
        


        private void UpdateRichTextBlockForMeasurement(RichTextBlock rtb, AozoraBindingModel block, double availableHeight = 0)
        {
            rtb.Blocks.Clear();
            rtb.FontSize = _textFontSize * block.FontSizeScale;
            rtb.FontFamily = block.FontFamily != null ? new FontFamily(block.FontFamily) : new FontFamily(_textFontFamily);
            
            if (block.IsTable)
            {
                 var tablePara = new Paragraph();
                 tablePara.Inlines.Add(CreateTableInline(block.TableRows));
                 rtb.Blocks.Add(tablePara);
                 return;
            }

            var p = new Paragraph();
            // 실제 렌더링 시 사용하는 배수와 동일하게 설정
            p.LineHeight = rtb.FontSize * (block.IsBlankLine ? 1.1 : 2.2);
            p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

            foreach (var item in block.Inlines)
            {
                if (item is string text) 
                {
                    p.Inlines.Add(new Run { Text = text });
                }
                else if (item is AozoraBold bold) 
                {
                    p.Inlines.Add(new Run { Text = bold.Text, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                }
                else if (item is AozoraRuby ruby) 
                {
                    var weight = (ruby.IsBold || block.IsBold) ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, rtb.FontSize, weight));
                }
                else if (item is AozoraItalic italic)
                {
                    p.Inlines.Add(new Run { Text = italic.Text, FontStyle = Windows.UI.Text.FontStyle.Italic });
                }
                else if (item is AozoraLineBreak)
                {
                    p.Inlines.Add(new LineBreak());
                }
                else if (item is AozoraCode code)
                {
                    p.Inlines.Add(new Run 
                    { 
                        Text = code.Text, 
                        FontFamily = new FontFamily("Consolas, Courier New, Monospace"), 
                        Foreground = new SolidColorBrush(Colors.DarkSlateGray)
                    });
                }
                else if (item is AozoraImage img)
                {
                    var ui = CreateImageInline(img.Source, availableHeight);
                    if (ui != null) p.Inlines.Add(ui);
                }
            }
            rtb.Blocks.Add(p);
        }
        
        private void AozoraPageContainer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isTextMode || !_isAozoraMode) return;
            
            var ptr = e.GetCurrentPoint(RootGrid);
            if (ptr.Properties.IsLeftButtonPressed)
            {
                HandleSmartTouchNavigation(e,
                    () => NavigateAozoraPage(-1),
                    () => NavigateAozoraPage(1));
                
                e.Handled = true;
                RootGrid.Focus(FocusState.Programmatic);
            }
        }
        
        private void AozoraPageContainer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isTextMode || !_isAozoraMode) return;
            
            var ptr = e.GetCurrentPoint(AozoraPageContainer);
            var delta = ptr.Properties.MouseWheelDelta;
            
            if (delta > 0) NavigateAozoraPage(-1);
            else NavigateAozoraPage(1);
            
            e.Handled = true;
        }
        
        private void NavigateAozoraPage(int direction)
        {
            if (_aozoraBlocks.Count == 0) return;

            if (direction > 0)
            {
                // Next Page
                if (_currentAozoraEndBlockIndex < _aozoraBlocks.Count - 1)
                {
                    _aozoraNavHistory.Push(_currentAozoraStartBlockIndex);
                    RenderAozoraDynamicPage(_currentAozoraEndBlockIndex + 1);
                    UpdateAozoraStatusBar();
                }
            }
            else if (direction < 0)
            {
                // Previous Page
                if (_aozoraNavHistory.Count > 0)
                {
                    int prevIdx = _aozoraNavHistory.Pop();
                    RenderAozoraDynamicPage(prevIdx);
                    UpdateAozoraStatusBar();
                }
                else if (_currentAozoraStartBlockIndex > 0)
                {
                    int targetIdx = _currentAozoraStartBlockIndex;
                    int bestStart = Math.Max(0, targetIdx - 1);
                    int testStart = bestStart;

                    double availableWidth = AozoraPageContainer?.ActualWidth ?? 800;
                    if (availableWidth < 100) availableWidth = 800;
                    double innerWidth = availableWidth - 40;
                    double currentMaxWidth = _isMarkdownRenderMode ? innerWidth : Math.Min(innerWidth, GetUrlMaxWidth());

                    double availableHeight = AozoraPageContainer?.ActualHeight ?? 800;
                    if (availableHeight < 200) availableHeight = 800;
                    availableHeight -= 55;

                    var dummyRTB = new RichTextBlock
                    {
                        FontFamily = new FontFamily(_textFontFamily),
                        FontSize = _textFontSize,
                        MaxWidth = currentMaxWidth,
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(0, 15, 0, 0),
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight // 실제 렌더링과 동일한 줄간격 강제 적용
                    };

                    // 💡 [수정] 박스나 단락이 길 경우를 대비해 탐색 한계치를 1000으로 넉넉하게 잡습니다.
                    int safetyLimit = Math.Max(0, targetIdx - 1000);

                    while (testStart >= safetyLimit)
                    {
                        dummyRTB.Blocks.Clear();
                        Paragraph? currentParagraph = null;
                        bool forcedBreak = false;

                        for (int i = testStart; i < targetIdx; i++)
                        {
                            var block = _aozoraBlocks[i];

                            if (block.HasImage || block.IsPageBreak)
                            {
                                if (dummyRTB.Blocks.Count > 0) 
                                {
                                    forcedBreak = true;
                                    break; 
                                }
                            }

                            // 💡 [수정] 박스(罫囲み)도 실제 화면에 그려지는 것과 100% 동일하게 그룹화하여 측정
                            if (block.BorderColor != null || block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 || block.BorderThickness.Bottom > 0)
                            {
                                var keigakomiBlocks = new List<AozoraBindingModel>();
                                int k = i;
                                while (k < targetIdx && 
                                    _aozoraBlocks[k].BorderColor == block.BorderColor && 
                                    _aozoraBlocks[k].BorderThickness == block.BorderThickness)
                                {
                                    keigakomiBlocks.Add(_aozoraBlocks[k]);
                                    k++;
                                }

                                double boxWidth = currentMaxWidth - 10;
                                if (boxWidth < 100) boxWidth = currentMaxWidth;

                                var border = new Border
                                {
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    MaxWidth = boxWidth,
                                    BorderBrush = new SolidColorBrush(block.BorderColor ?? Colors.Gray),
                                    BorderThickness = block.BorderThickness.Top > 0 || block.BorderThickness.Left > 0 ? block.BorderThickness : new Thickness(1.5),
                                    Padding = block.Padding.Left > 0 ? block.Padding : new Thickness(15),
                                    Margin = new Thickness(0, 10, 0, 10),
                                    CornerRadius = new CornerRadius(4),
                                    Background = block.BackgroundColor != null ? new SolidColorBrush(block.BackgroundColor.Value) : null
                                };

                                var innerRtb = new RichTextBlock
                                {
                                    TextWrapping = TextWrapping.Wrap,
                                    FontFamily = new FontFamily(_textFontFamily),
                                    FontSize = _textFontSize
                                };

                                foreach (var kb in keigakomiBlocks)
                                {
                                    innerRtb.Blocks.Add(CreateParagraphFromBlock(kb, availableHeight, boxWidth));
                                }
                                border.Child = innerRtb;
                                
                                var pWrapper = new Paragraph();
                                pWrapper.Inlines.Add(new InlineUIContainer { Child = border });
                                dummyRTB.Blocks.Add(pWrapper);

                                i = k - 1;
                                currentParagraph = null;
                                continue;
                            }

                            // 일반 문단 덧붙이기
                            if (block.IsParagraphContinuation && currentParagraph != null && !block.HasImage && !block.IsTable && block.HeadingLevel == 0)
                            {
                                var pTemp = CreateParagraphFromBlock(block, availableHeight, innerWidth);
                                var inlinesToMove = pTemp.Inlines.ToList();
                                pTemp.Inlines.Clear(); 
                                foreach (var inline in inlinesToMove) currentParagraph.Inlines.Add(inline);
                                continue;
                            }

                            var p = CreateParagraphFromBlock(block, availableHeight, innerWidth);
                            if (p.Inlines.Count > 0) dummyRTB.Blocks.Add(p);

                            if (!block.HasImage && !block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                                currentParagraph = p;
                            else
                                currentParagraph = null;
                        }

                        if (forcedBreak && testStart < bestStart) break;

                        dummyRTB.Measure(new Windows.Foundation.Size((float)currentMaxWidth, double.PositiveInfinity));

                        if (dummyRTB.DesiredSize.Height > availableHeight && testStart < bestStart)
                        {
                            break;
                        }

                        bestStart = testStart;
                        if (testStart == 0) break;

                        // 💡 [핵심 해결 포인트] 이전처럼 문단이나 박스 시작점으로 강제로 건너뛰지 않고 무조건 1줄씩 뒤로 갑니다.
                        // 이로써 화면보다 큰 문단이나 박스도 자연스럽게 페이지가 나뉘어 스킵 없이 정확하게 측정됩니다.
                        testStart--;
                    }

                    RenderAozoraDynamicPage(bestStart);
                    UpdateAozoraStatusBar();
                }
            }
        }
        
        private void UpdateAozoraStatusBar()
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;
            
            double progress = (_currentAozoraEndBlockIndex + 1) * 100.0 / _aozoraBlocks.Count;
            if (progress > 100) progress = 100;
            
            int startLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
            
            ImageInfoText.Text = Strings.LineInfo(startLine, _aozoraTotalLineCountInSource);
            _ = AddToRecentAsync(true);
            TextProgressText.Text = $"{progress:F1}%";
            
            if (_isAozoraPageCalcCompleted)
            {
                int curPage = (int)((double)_currentAozoraStartBlockIndex / _aozoraBlocks.Count * _aozoraTotalPages) + 1;
                if (curPage > _aozoraTotalPages) curPage = _aozoraTotalPages;
                ImageIndexText.Text = $"{curPage} / {_aozoraTotalPages}";
            }
            else
            {
                ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
            }
        }

        public void JumpToAozoraLine(int targetLine)
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;

            if (_isVerticalMode)
            {
                _ = PrepareVerticalTextAsync(targetLine);
                return;
            }

            int startIdx = 0;
            for (int i = 0; i < _aozoraBlocks.Count; i++)
            {
                if (_aozoraBlocks[i].SourceLineNumber >= targetLine)
                {
                    if (_aozoraBlocks[i].SourceLineNumber == targetLine)
                    {
                        startIdx = i;
                    }
                    else
                    {
                        startIdx = i > 0 ? i - 1 : 0;
                    }
                    break;
                }
                startIdx = i;
            }
            
            // 💡 [수정] 점프하기 전의 위치를 '이전 페이지'로 오해하지 않도록 스택을 비워줍니다.
            // 기존 _aozoraNavHistory.Push(_currentAozoraStartBlockIndex); 코드를 아래로 교체하세요.
            _aozoraNavHistory.Clear();
            
            RenderAozoraDynamicPage(startIdx);
            UpdateAozoraStatusBar();
        }

        private string PreprocessAozoraBold(string text)
        {
            string boldStartTag = @"［＃(?:ここから太字)］";
            string boldEndTag = @"［＃(?:ここで太字終わり)］";
            return Regex.Replace(text, $"{boldStartTag}(.*?){boldEndTag}", (m) => {
                string inner = m.Groups[1].Value;
                var startRegex = new Regex(boldStartTag);
                var parts = startRegex.Split(inner);
                if (parts.Length <= 1) return $"@@BOLD_START@@{inner}@@BOLD_END@@";
                string prefix = string.Join("", parts.Take(parts.Length - 1));
                string boldContent = parts.Last();
                return $"{prefix}@@BOLD_START@@{boldContent}@@BOLD_END@@";
            }, RegexOptions.Singleline);
        }

        private List<AozoraBindingModel> ParseAozoraContent(string text)
        {
            text = PreprocessAozoraBold(text);
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            _aozoraTotalLineCountInSource = lines.Length;
            return ParseAozoraLines(lines, 1);
        }

        private List<AozoraBindingModel> ParseAozoraLines(string[] lines, int startLineOffset)
        {
            var blocks = new List<AozoraBindingModel>();
            bool lastWasEmpty = false;

            // Flags for multi-line tags
            bool currentBold = false;
            double currentIndentEm = 0;
            bool inKeigakomi = false;
            int smallTextLevel = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Basic clean
                var content = line.Replace('\u3000', ' ').TrimEnd(); 
                
                if (string.IsNullOrEmpty(content)) 
                {
                     if (lastWasEmpty) continue; // Collapse consecutive empty lines
                     
                     var blankModel = new AozoraBindingModel { 
                         Inlines = { "" }, 
                         Margin = new Thickness(0),
                         SourceLineNumber = startLineOffset + i,
                         BlockIndent = currentIndentEm * _textFontSize,
                         IsBlankLine = true
                     };

                     // [핵심 추가] 빈 줄이라도 罫囲み(테두리) 내부에 있다면 테두리 속성 상속
                     if (inKeigakomi)
                     {
                         blankModel.BorderColor = Colors.Gray;
                         blankModel.BorderThickness = new Thickness(1);
                         blankModel.Padding = new Thickness(10);
                     }

                     blocks.Add(blankModel);
                     lastWasEmpty = true;
                     continue;
                }
                lastWasEmpty = false;

                // --- State Updates and Tag Support ---
                // 1. Page Break
                bool isPageBreak = false;
                if (Regex.IsMatch(content, @"［＃(?:改ページ|改頁)］"))
                {
                    isPageBreak = true;
                    content = Regex.Replace(content, @"［＃(?:改ページ|改頁)］", "");
                }

                // 2. Multi-line Bold (Markers handled by tokenizer)


                // 3. Indents
                var indentMatch = Regex.Match(content, @"［＃ここから(?:(\d+)|([０-９]+))字下げ］");
                if (indentMatch.Success)
                {
                    string val = indentMatch.Groups[1].Value;
                    if (string.IsNullOrEmpty(val)) val = indentMatch.Groups[2].Value;
                    currentIndentEm = ConvertFullWidthToDouble(val);
                    content = content.Replace(indentMatch.Value, "");
                }
                if (content.Contains("［＃ここで字下げ終わり］"))
                {
                    currentIndentEm = 0;
                    content = content.Replace("［＃ここで字下げ終わり］", "");
                }

                // 4. Keigakomi
                if (content.Contains("［＃ここから罫囲み］"))
                {
                    inKeigakomi = true;
                    content = content.Replace("［＃ここから罫囲み］", "");
                }
                bool justExitedKeigakomi = false;
                if (content.Contains("［＃ここで罫囲み終わり］"))
                {
                    inKeigakomi = false;
                    justExitedKeigakomi = true;
                    content = content.Replace("［＃ここで罫囲み終わり］", "");
                }

                // 5. Small text
                if (content.Contains("［＃ここから２段階小さな文字］"))
                {
                    smallTextLevel = 2;
                    content = content.Replace("［＃ここから２段階小さな文字］", "");
                }
                if (content.Contains("［＃ここで小さな文字終わり］"))
                {
                    smallTextLevel = 0;
                    content = content.Replace("［＃ここで小さな文字終わり］", "");
                }

                // --- Specific Tag Support (Bouten, TCY, BoldSpecific) ---
                // Bouten
                var boutenMatches = Regex.Matches(content, @"［＃「(.+?)」に傍点］");
                foreach (Match m in boutenMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(targetWord)) targetWord = m.Groups[2].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, (match) => {
                        StringBuilder sb = new StringBuilder();
                        foreach (char c in targetWord) sb.Append($"{{{{RUBY|{c}|﹅}}}}");
                        return sb.ToString();
                    });
                }

                // TCY
                var tcyMatches = Regex.Matches(content, @"［＃「(.+?)」は縦中横］");
                foreach (Match m in tcyMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, $"{{{{TCY|{targetWord}}}}}");
                }

                // Bold Specific
                var boldSpecificMatches = Regex.Matches(content, @"［＃「(.+?)」[は]太字］");
                foreach (Match m in boldSpecificMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, $"@@BOLD_START@@{targetWord}@@BOLD_END@@");
                }

                var model = new AozoraBindingModel { 
                    SourceLineNumber = startLineOffset + i,
                    IsPageBreak = isPageBreak,
                    BlockIndent = currentIndentEm * _textFontSize
                };
                model.Margin = new Thickness(0);

                if (inKeigakomi)
                {
                     model.BorderColor = Colors.Gray;
                     model.BorderThickness = new Thickness(1);
                     model.Padding = new Thickness(10);
                }
                if (smallTextLevel == 2) model.FontSizeScale = 0.85;

                // --- Aozora Tag Parsing ---
                // Headers
                if (content.Contains("［＃大見出し］") || content.StartsWith("# "))
                {
                    model.FontSizeScale = 1.5;
                    content = content.Replace("［＃大見出し］", "").TrimStart('#', ' ');
                    model.HeadingLevel = 1;
                    model.HeadingText = Regex.Replace(content, @"［＃[^］]+］|\[.*?\]", "").Trim();
                }
                else if (content.Contains("［＃中見出し］") || content.StartsWith("## "))
                {
                    model.FontSizeScale = 1.25;
                    content = content.Replace("［＃中見出し］", "").TrimStart('#', ' ');
                    model.HeadingLevel = 2;
                    model.HeadingText = Regex.Replace(content, @"［＃[^］]+］|\[.*?\]", "").Trim();
                }
                
                // Alignments
                if (content.Contains("［＃センター］"))
                {
                    model.Alignment = TextAlignment.Center;
                    content = content.Replace("［＃センター］", "");
                }
                else if (content.Contains("［＃地から３字上げ］"))
                {
                    model.Alignment = TextAlignment.Right;
                    model.Margin = new Thickness(0, 0, 60, 0);
                    content = content.Replace("［＃地から３字上げ］", "");
                }

                // 4. Image tags: <img src="file.jpg"> or ［＃挿絵（img/file.jpg）入る］
                // IMPORTANT: Parse images BEFORE cleaning up all ［＃...］ tags
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                // Cleanup other tags
                content = Regex.Replace(content, @"［＃[^］]+］", "");

                // --- Inline Parsing (Ruby & Bold) ---
                // 1. Aozora Ruby with pipe: ｜漢字《かんじ》
                content = Regex.Replace(content, @"｜(.+?)《(.+?)》", (m) => {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });
                
                // 2. Aozora Ruby for emphasis dots (any character + 《'》): 한자가 아닌 경우에도 방점으로 인식
                content = Regex.Replace(content, @"(.)《'》", "{{RUBY|$1|・}}");
                content = Regex.Replace(content, @"(.)《’》", "{{RUBY|$1|・}}");

                // 3. Aozora Ruby without pipe (Kanji + Ruby): 漢字《かんじ》
                content = Regex.Replace(content, @"([\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF々]+)《(.+?)》", (m) => {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });
                
                // Markdown Bold **...** 
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "@@BOLD_START@@$2@@BOLD_END@@");

                // Tokenize
                string pattern = @"(\{\{RUBY\|.*?\|.*?\}\}|\{\{IMG\|.*?\}\}|\{\{TCY\|.*?\}\}|@@BOLD_START@@|@@BOLD_END@@)";
                var parts = Regex.Split(content, pattern);
                bool inlineBold = currentBold;
                
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    
                    if (part == "@@BOLD_START@@") { inlineBold = true; continue; }
                    if (part == "@@BOLD_END@@") { inlineBold = false; continue; }

                    if (part.StartsWith("{{RUBY|"))
                    {
                        var inner = part.Trim('{', '}'); // RUBY|Base|Ruby
                        var p = inner.Split('|');
                        if (p.Length >= 3)
                        {
                            model.Inlines.Add(new AozoraRuby { BaseText = p[1], RubyText = p[2], IsBold = inlineBold });
                        }
                    }
                    else if (part.StartsWith("{{IMG|"))
                    {
                        var src = part.Substring(6, part.Length - 8);
                        int commaIdx = src.IndexOfAny(new[] { ',', '，', '、' });
                        if (commaIdx >= 0) src = src.Substring(0, commaIdx).Trim();
                        
                        model.Inlines.Add(new AozoraImage { Source = src });
                    }
                    else if (part.StartsWith("{{TCY|"))
                    {
                        var textStr = part.Substring(6, part.Length - 8);
                        model.Inlines.Add(new AozoraTCY { Text = textStr, IsBold = inlineBold });
                    }
                    else
                    {
                        if (inlineBold) model.Inlines.Add(new AozoraBold { Text = part });
                        else model.Inlines.Add(part); // String
                    }
                }
                
                currentBold = inlineBold;
                blocks.Add(model);

                // 💡 [추가] 罫囲み(박스)가 끝난 직후 여백을 위해 빈 줄을 하나 삽입
                if (justExitedKeigakomi)
                {
                    blocks.Add(new AozoraBindingModel {
                        Inlines = { "" },
                        Margin = new Thickness(0),
                        SourceLineNumber = startLineOffset + i,
                        BlockIndent = 0,
                        IsBlankLine = true
                    });
                    
                    // 다음 줄이 원래 빈 줄이더라도 중복으로 너무 넓어지지 않도록 플래그 처리
                    lastWasEmpty = true; 
                }
            }

            // ===== [추가된 부분] 문단이 긴 경우 문장 단위로 블록 분리 =====
            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(SplitBlockBySentences(block));
            }

            return splitBlocks;
        }

        private List<AozoraBindingModel> SplitBlockBySentences(AozoraBindingModel originalBlock)
        {
            // 💡 [유지] 박스(罫囲み) 여부 판별
            bool isKeigakomi = originalBlock.BorderColor != null || 
                               originalBlock.BorderThickness.Top > 0 || 
                               originalBlock.BorderThickness.Left > 0 || 
                               originalBlock.BorderThickness.Bottom > 0 || 
                               originalBlock.BorderThickness.Right > 0;

            if (originalBlock.HeadingLevel > 0 || originalBlock.HasImage || originalBlock.IsTable || originalBlock.IsPageBreak || originalBlock.IsBlankLine || isKeigakomi)
            {
                return new List<AozoraBindingModel> { originalBlock };
            }

            // ==========================================
            // [버그 수정] 쉼표('、', ',') 추가
            // 문장을 구(Clause) 단위로 더 잘게 쪼개어, 긴 문단이 다음 페이지로 넘어갈 때 
            // 이전 페이지 하단에 큰 공백이 남는 현상을 해결합니다.
            // ==========================================
            char[] terminators = { '。', '！', '？', '.', '!', '?', '、', ',' };
            char[] quotes = { '」', '』', '"', '\'', '”', '’', '〉', '》', '】', '］', ')' };

            var result = new List<AozoraBindingModel>();
            var currentBlock = CloneBlockProperties(originalBlock);
            bool isFirst = true;

            for (int i = 0; i < originalBlock.Inlines.Count; i++)
            {
                var inline = originalBlock.Inlines[i];

                if (inline is string text)
                {
                    int start = 0;
                    for (int j = 0; j < text.Length; j++)
                    {
                        char c = text[j];
                        if (Array.IndexOf(terminators, c) >= 0)
                        {
                            if (c == '.' && j > 0 && j < text.Length - 1 && char.IsDigit(text[j - 1]) && char.IsDigit(text[j + 1])) continue;
                            if (j < text.Length - 1 && Array.IndexOf(terminators, text[j + 1]) >= 0) continue;

                            int splitPos = j + 1;
                            while (splitPos < text.Length && Array.IndexOf(quotes, text[splitPos]) >= 0) splitPos++;

                            bool isLastInText = (splitPos == text.Length);
                            bool isLastInline = (i == originalBlock.Inlines.Count - 1);

                            if (isLastInText && isLastInline) continue;

                            string part = text.Substring(start, splitPos - start);
                            if (currentBlock.Inlines.Count == 0) part = part.TrimStart();
                            if (!string.IsNullOrEmpty(part)) currentBlock.Inlines.Add(part);

                            if (currentBlock.Inlines.Count > 0)
                            {
                                if (!isFirst) currentBlock.IsParagraphContinuation = true;
                                result.Add(currentBlock);
                                isFirst = false;
                            }

                            currentBlock = CloneBlockProperties(originalBlock);
                            currentBlock.BlockIndent = 0; 
                            currentBlock.Margin = new Thickness(0);

                            start = splitPos;
                            j = splitPos - 1;
                        }
                    }

                    if (start < text.Length)
                    {
                        string left = text.Substring(start);
                        if (currentBlock.Inlines.Count == 0) left = left.TrimStart();
                        if (!string.IsNullOrEmpty(left)) currentBlock.Inlines.Add(left);
                    }
                }
                else
                {
                    currentBlock.Inlines.Add(inline);
                }
            }

            if (currentBlock.Inlines.Count > 0)
            {
                if (!isFirst) currentBlock.IsParagraphContinuation = true;
                result.Add(currentBlock);
            }

            return result;
        }

        private AozoraBindingModel CloneBlockProperties(AozoraBindingModel source, bool copyInlines = false)
        {
            var clone = new AozoraBindingModel
            {
                FontSizeScale = source.FontSizeScale,
                Alignment = source.Alignment,
                Margin = source.Margin,
                Padding = source.Padding,
                BorderColor = source.BorderColor,
                BorderThickness = source.BorderThickness,
                BackgroundColor = source.BackgroundColor,
                FontFamily = source.FontFamily,
                SourceLineNumber = source.SourceLineNumber,
                IsBold = source.IsBold,
                BlockIndent = source.BlockIndent,
                HeadingLevel = source.HeadingLevel,
                HeadingText = source.HeadingText,
                IsTable = source.IsTable,
                IsBlankLine = source.IsBlankLine,
                IsPageBreak = source.IsPageBreak,
                IsParagraphContinuation = source.IsParagraphContinuation,
                
                // [버그 수정 1] EPUB 챕터 인덱스 복사 누락 해결 (이것이 챕터 1로 순환되던 원인입니다)
                EpubChapterIndex = source.EpubChapterIndex 
            };

            // [버그 수정 2] 테이블 데이터가 있을 경우 참조 오류를 막기 위해 깊은 복사(Deep Copy) 처리
            if (source.IsTable && source.TableRows != null)
            {
                clone.TableRows = source.TableRows.Select(row => new List<string>(row)).ToList();
            }

            if (copyInlines) clone.Inlines = new List<object>(source.Inlines);
            return clone;
        }

        private List<AozoraBindingModel> ParseMarkdownContent(string text)
        {
            var blocks = new List<AozoraBindingModel>();
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            bool inCodeBlock = false;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                string content = line; 
                int sourceLine = i + 1;
                
                // Code Block Handling
                if (content.Trim().StartsWith("```"))
                {
                     inCodeBlock = !inCodeBlock;
                     continue; // Skip the fence line
                }
                
                if (inCodeBlock)
                {
                    var model = new AozoraBindingModel();
                    model.FontFamily = "Consolas, Courier New, Monospace";
                    model.Inlines.Add(content); 
                    model.BackgroundColor = Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128);
                    model.Padding = new Thickness(4, 0, 4, 0);
                    model.Margin = new Thickness(20, 0, 20, 0);
                    model.SourceLineNumber = sourceLine;
                    blocks.Add(model);
                    continue;
                }
                
                // Table Parsing
                if (content.Trim().StartsWith("|"))
                {
                    // Lookahead: consecutive lines starting with |
                    var tableLines = new List<string>();
                    int k = i;
                    while (k < lines.Length && lines[k].Trim().StartsWith("|"))
                    {
                        tableLines.Add(lines[k].Trim());
                        k++;
                    }
                    
                    if (tableLines.Count >= 2) // Header + Separator at minimum
                    {
                        var tableModel = new AozoraBindingModel { IsTable = true };
                        bool isValidTable = false;
                        
                        foreach (var tLine in tableLines)
                        {
                             // Check separator line |---|---|
                             var trimLine = tLine.Trim('|');
                             // If it contains only -, :, | and whitespace, treat as separator
                             if (Regex.IsMatch(trimLine, @"^[\-:\|\s]+$") && trimLine.Contains("-"))
                             {
                                 isValidTable = true;
                                 continue; // Skip separator row
                             }
                             
                             // Parse cells
                             // naive split by |
                             // | A | B | -> ["", "A", "B", ""]
                             var cells = tLine.Split('|').Select(c => c.Trim()).ToList();
                             
                             // Remove empty first/last if empty
                             if (cells.Count > 0 && string.IsNullOrEmpty(cells[0])) cells.RemoveAt(0);
                             if (cells.Count > 0 && string.IsNullOrEmpty(cells[cells.Count - 1])) cells.RemoveAt(cells.Count - 1);
                             
                             tableModel.TableRows.Add(cells);
                        }
                        
                        if (isValidTable || tableLines.Count > 1) 
                        {
                            tableModel.SourceLineNumber = sourceLine;
                            blocks.Add(tableModel);
                            i = k - 1; // Advance loop
                            continue;
                        }
                    }
                }

                content = content.TrimEnd();
                
                if (string.IsNullOrEmpty(content)) 
                {
                     // Spacer
                     blocks.Add(new AozoraBindingModel { Inlines = { "" }, Margin = new Thickness(0), IsBlankLine = true });
                     continue;
                }

                var blockModel = new AozoraBindingModel();
                blockModel.Margin = new Thickness(0);

                // --- Markdown Block Parsing ---
                
                // Headers
                if (content.StartsWith("#"))
                {
                    int level = 0;
                    while (level < content.Length && content[level] == '#') level++;
                    
                    if (level > 0 && level <= 6)
                    {
                         if (level == 1) blockModel.FontSizeScale = 2.0;
                         else if (level == 2) blockModel.FontSizeScale = 1.5;
                         else if (level == 3) blockModel.FontSizeScale = 1.25;
                         else blockModel.FontSizeScale = 1.1;
                         
                         content = content.Substring(level).TrimStart();
                         blockModel.HeadingLevel = level;
                         blockModel.HeadingText = Regex.Replace(content, @"[#\[\]]", "").Trim();

                         if (level == 1 || level == 2) 
                         {
                              blockModel.BorderColor = Colors.LightGray;
                              blockModel.BorderThickness = new Thickness(0, 0, 0, 1); // Bottom border for H1/H2
                         }
                    }
                }
                // Quote
                else if (content.StartsWith(">"))
                {
                    content = content.TrimStart('>', ' ');
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                    blockModel.BorderColor = Colors.Gray;
                    blockModel.BorderThickness = new Thickness(4, 0, 0, 0); // Left border
                    blockModel.Padding = new Thickness(10, 0, 0, 0);
                    blockModel.Inlines.Add(new AozoraItalic { Text = "" }); // Force italic style logic if we had it, but for now just indent
                }
                // List (Unordered)
                else if (Regex.IsMatch(content, @"^[\*\-]\s"))
                {
                    content = "• " + content.Substring(2);
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                }
                // List (Ordered)
                else if (Regex.IsMatch(content, @"^\d+\.\s"))
                {
                     // Keep number
                     blockModel.Margin = new Thickness(20, 0, 0, 0);
                }
                // HR
                 else if (Regex.IsMatch(content, @"^(\*{3,}|-{3,})$"))
                {
                     blockModel.Inlines.Add("");
                     blockModel.BorderColor = Colors.Gray;
                     blockModel.BorderThickness = new Thickness(0, 1, 0, 0);
                     blockModel.Margin = new Thickness(0, 10, 0, 10);
                     blocks.Add(blockModel);
                     continue;
                }

                // --- Inline Parsing ---
                // 1. Code `...` -> {{CODE|...}}
                content = Regex.Replace(content, @"`(.+?)`", "{{CODE|$1}}");
                
                // 1.5 <br> -> {{BR}} (Case insensitive, supports <br>, <br/>, <br />)
                content = Regex.Replace(content, @"<br\s*/?>", "{{BR}}", RegexOptions.IgnoreCase);

                // 1.6 Image <img src="..."> -> {{IMG|...}}
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                // 2. Bold **...** or __...__ (Support spaces inside: ** text **)
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "{{BOLD|$2}}");
                
                // 3. Italic *...* or _..._
                content = Regex.Replace(content, @"(\*|_)(.*?)\1", "{{ITALIC|$2}}");
                
                // Tokenize
                string pattern = @"(\{\{CODE\|.*?\}\}|\{\{BOLD\|.*?\}\}|\{\{ITALIC\|.*?\}\}|\{\{BR\}\}|\{\{IMG\|.*?\}\})";
                var parts = Regex.Split(content, pattern);
                
                foreach (var part in parts)
                {
                     if (string.IsNullOrEmpty(part)) continue;
                     
                     if (part.StartsWith("{{CODE|"))
                     {
                         var inner = part.Substring(7, part.Length - 9);
                         blockModel.Inlines.Add(new AozoraCode { Text = inner });
                     }
                     else if (part.StartsWith("{{IMG|"))
                     {
                         var src = part.Substring(6, part.Length - 8);
                         blockModel.Inlines.Add(new AozoraImage { Source = src });
                     }
                     else if (part == "{{BR}}")
                     {
                         blockModel.Inlines.Add(new AozoraLineBreak());
                     }
                     else if (part.StartsWith("{{BOLD|"))
                     {
                         var inner = part.Substring(7, part.Length - 9);
                         blockModel.Inlines.Add(new AozoraBold { Text = inner });
                     }
                     else if (part.StartsWith("{{ITALIC|"))
                     {
                          var inner = part.Substring(9, part.Length - 11);
                          blockModel.Inlines.Add(new AozoraItalic { Text = inner });
                     }
                     else
                     {
                         blockModel.Inlines.Add(part);
                     }
                }
                
                blocks.Add(blockModel);
            }
            
            return blocks;
        }

        private double ConvertFullWidthToDouble(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
                else sb.Append(c);
            }
            double.TryParse(sb.ToString(), out double result);
            return result;
        }

        private void PrepareAozoraElement(RichTextBlock rtb, int index, double availableHeight = 0)
        {
            if (index < 0 || index >= _aozoraBlocks.Count) return;
            var block = _aozoraBlocks[index];

            // Setup Container Properties
            rtb.FontSize = _textFontSize * block.FontSizeScale;
            rtb.FontFamily = block.FontFamily != null ? new FontFamily(block.FontFamily) : new FontFamily(_textFontFamily);
            rtb.Foreground = GetThemeForeground();
            rtb.TextAlignment = block.Alignment;
            rtb.Margin = new Thickness(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left, block.Margin.Top, block.Margin.Right, block.Margin.Bottom);
            rtb.Padding = block.Padding;
            rtb.FontWeight = block.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
            
            if (_isMarkdownRenderMode)
            {
                rtb.MaxWidth = double.PositiveInfinity; // No limit for Markdown
            }
            else
            {
                rtb.MaxWidth = GetUrlMaxWidth();
            }
            rtb.Blocks.Clear();

            if (block.IsTable)
            {
                 var ui = CreateTableInline(block.TableRows);
                 var para = new Paragraph();
                 para.Inlines.Add(ui);
                 rtb.Blocks.Add(para);
                 return;
            }
            // Border (Background moved to Paragraph/Inline level? No, RichTextBlock doesn't support background easily per block/line unless we wrap it)
            // But we can check if we want to support Background.
            // ItemsRepeater Template for Aozora likely looks like:
            // <DataTemplate>
            //    <RichTextBlock ... />
            // </DataTemplate>
            // We can't bind Background to RichTextBlock (it doesn't have it).
            // We would need to wrap RichTextBlock in a Border/Grid in the XAML template to support efficient background/border.
            // Since I cannot edit XAML template easily in this context (it is in Resources in MainWindow.xaml likely),
            // I will assume simple rendering for now. 
            // However, TextItemsRepeater uses ElementPrepared.
            // Does ElementPrepared get the Container? The sender is Repeater. The element is the UIElement created.
            // 'PrepareAozoraElement' is called from ElementPrepared.
            // if 'element' is RichTextBlock, we can't add Border around it easily here.
            
            // BUT, if the template IS just RichTextBlock, we are stuck.
            // Let's check if we can change the element created? No.
            
            // Let's assume we can only modify RichTextBlock properties. It doesn't have Background or BorderBrush.
            // We might have to sacrifice visual box for code blocks unless we change ItemTemplate in Code Behind?
            // "TextItemsRepeater.ItemTemplate = (DataTemplate)template;"
            // We can construct a DataTemplate in code? Or use XamlReader.
            
            // For now, let's just stick to Text properties.
            // If Border is critical (like for Code), we might be limited.
            // Code Blocks as Monospace font is a good start.
            
            // Check border?? RichTextBlock doesn't handle border well directly.
            // But we can check if the visual parent is a Border if we modified the template. 
            // Current template: just RichTextBlock. 
            // Adding Border programmatically is hard in ItemsRepeater without rewriting template.
            // We'll skip Border visual for now unless necessary.
            
            rtb.Blocks.Clear();
            var p = new Paragraph(); // We put everything in one paragraph per "Block" (Line)
            p.LineHeight = rtb.FontSize * 2.2; // Increased multiplier for better ruby spacing
            p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; // Enforce consistent line height
            p.FontWeight = block.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

            // Build Inlines
            foreach (var item in block.Inlines)
            {
                if (item is string text)
                {
                    p.Inlines.Add(new Run { Text = text });
                }
                else if (item is AozoraBold bold)
                {
                    p.Inlines.Add(new Run { Text = bold.Text, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                }
                else if (item is AozoraItalic italic)
                {
                    p.Inlines.Add(new Run { Text = italic.Text, FontStyle = Windows.UI.Text.FontStyle.Italic });
                }
                else if (item is AozoraLineBreak)
                {
                    p.Inlines.Add(new LineBreak());
                }
                else if (item is AozoraCode code)
                {
                    // Inline Code: Monospace, maybe slightly different color?
                    p.Inlines.Add(new Run { Text = code.Text, FontFamily = new FontFamily("Consolas, Courier New, Monospace"), Foreground = new SolidColorBrush(Colors.DarkSlateGray) });
                }
                else if (item is AozoraRuby ruby)
                {
                    var weight = (ruby.IsBold || block.IsBold) ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, rtb.FontSize, weight));
                }
                else if (item is AozoraTCY tcy)
                {
                    var weight = (tcy.IsBold || block.IsBold) ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                    p.Inlines.Add(new Run { Text = tcy.Text, FontWeight = weight });
                }
                else if (item is AozoraImage img)
                {
                    var ui = CreateImageInline(img.Source, availableHeight);
                    if (ui != null) p.Inlines.Add(ui);
                }
            }
            
            rtb.Blocks.Add(p);
        }

        private InlineUIContainer CreateRubyInline(string baseText, string rubyText, double baseFontSize, FontWeight? fontWeight = null)
        {
            var grid = new Grid();
            
            // Auto 높이를 사용하여 내용물 크기에 딱 맞게 설정
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ruby (0행)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Base (1행)
            
            // 루비 텍스트 (윗첨자)
            var rt = new TextBlock
            {
                Text = (rubyText == "'" || rubyText == "’") ? "・" : rubyText,
                FontSize = baseFontSize * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                Opacity = 1,
                TextLineBounds = TextLineBounds.Tight, // 여백 없이 텍스트 영역만 차지
                IsHitTestVisible = false,
                Margin = new Thickness(0, 0, 0, 4),
                FontWeight = fontWeight ?? Microsoft.UI.Text.FontWeights.Normal
            };

            // [추가] 루비가 너무 길어지는 경우 장평(ScaleX)을 75%로 설정
            bool shouldScale = (baseText != null && baseText.Length == 1 && rubyText != null && rubyText.Length >= 3) || 
                               (baseText != null && baseText.Length == 2 && rubyText != null && rubyText.Length >= 5) ||
                               (baseText != null && baseText.Length == 3 && rubyText != null && rubyText.Length >= 7);
            if (shouldScale)
            {
                rt.RenderTransform = new ScaleTransform { ScaleX = 0.75 };
                rt.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }
            Grid.SetRow(rt, 0);
            
            // 본문 텍스트 (베이스)
            var rb = new TextBlock
            {
                Text = baseText,
                FontSize = baseFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                TextLineBounds = TextLineBounds.Tight, // 주변 텍스트와 높이 맞춤을 위해 Tight 유지
                Margin = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(0),
                FontWeight = fontWeight ?? Microsoft.UI.Text.FontWeights.Normal
            };
            Grid.SetRow(rb, 1);
            
            grid.Children.Add(rt);
            grid.Children.Add(rb);
            
            // [수정] 중요: Grid 자체의 수직 정렬을 Bottom으로 설정하여
            // 본문 텍스트(rb)의 하단이 주변 텍스트의 기준선(Baseline)에 맞도록 유도
            grid.VerticalAlignment = VerticalAlignment.Bottom;

            // [수정] 루비가 3자인 경우 자간이 넓어지는 것을 방지하기 위해 왼쪽/오른쪽 마진을 음수로 설정
            double sideMargin = 0;
            if (rubyText != null && rubyText.Length == 3 && baseText != null && baseText.Length == 1)
            {
                sideMargin = -(baseFontSize * 0.25);
            }
            grid.Margin = new Thickness(sideMargin, 0, sideMargin, 0); 
                // Grid의 아래쪽(BaseText의 바닥)이 기준선에 오게 되므로, 주변 텍스트와 높이가 맞게 됩니다.
            return new InlineUIContainer { Child = grid }; 
        }

        private InlineUIContainer? CreateImageInline(string relativePath, double maxHeight = 0, double targetWidth = -1)
        {
            if (string.IsNullOrEmpty(_currentTextFilePath) && (_currentArchive == null && _current7zArchive == null || string.IsNullOrEmpty(_currentTextArchiveEntryKey))) return null;
            
            relativePath = relativePath.Trim().TrimStart('/', '\\');

            var img = new Image();
            img.Stretch = Stretch.Uniform;
            img.Margin = new Thickness(0);
            
            double maxWidth = targetWidth > 0 ? targetWidth : (AozoraPageContainer?.ActualWidth ?? 800);
            if (targetWidth <= 0 && maxWidth > 40) maxWidth -= 40; // Safety padding only if not explicit targetWidth
            if (maxWidth < 100) maxWidth = 800; // Minimal width
            
            img.Width = maxWidth;
            if (maxHeight > 0) img.Height = maxHeight * 0.98; // 위아래 크기를 화면보다 약간 줄임 (약 90% 수준)
            
            img.HorizontalAlignment = HorizontalAlignment.Center;
            img.VerticalAlignment = VerticalAlignment.Center;

            if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
            {
                // WebDAV Mode Case: Download relative images from the same remote directory
                try
                {
                    // Calculate remote path relative to the current file
                    string normItemPath = _currentWebDavItemPath.Replace('\\', '/');
                    int lastSlash = normItemPath.LastIndexOf('/');
                    string remoteDir = (lastSlash >= 0) ? normItemPath.Substring(0, lastSlash) : "";
                    
                    string normRelativePath = relativePath.Replace('\\', '/');
                    if (normRelativePath.StartsWith("/")) normRelativePath = normRelativePath.Substring(1);
                    
                    string remoteFullPath = remoteDir + "/" + normRelativePath;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Use a separate CTS if available or just the global one
                            var tempPath = await _webDavService.DownloadToTempFileAsync(remoteFullPath);
                            if (string.IsNullOrEmpty(tempPath)) return;

                            using var fs = new System.IO.FileStream(tempPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                            using var ms = new System.IO.MemoryStream();
                            await fs.CopyToAsync(ms);
                            var bytes = ms.ToArray();

                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                                    using (var writer = new Windows.Storage.Streams.DataWriter(winrtStream))
                                    {
                                        writer.WriteBytes(bytes);
                                        await writer.StoreAsync();
                                        await writer.FlushAsync();
                                        writer.DetachStream();
                                    }
                                    winrtStream.Seek(0);
                                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                    await bitmap.SetSourceAsync(winrtStream);
                                    img.Source = bitmap;
                                }
                                catch { }
                            });
                        }
                        catch { }
                    });
                    return new InlineUIContainer { Child = img };
                }
                catch { }
            }
            else if (!string.IsNullOrEmpty(_currentTextFilePath))
            {
                // Local File Case
                try
                {
                    string? dir = System.IO.Path.GetDirectoryName(_currentTextFilePath);
                    if (dir == null) return null;
                    
                    string normPath = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
                    string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, normPath));

                    if (System.IO.File.Exists(fullPath))
                    {
                        // Use stream to avoid UI thread issues and ensure better compatibility
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var fs = new System.IO.FileStream(fullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                                using var ms = new System.IO.MemoryStream();
                                await fs.CopyToAsync(ms);
                                var bytes = ms.ToArray();

                                this.DispatcherQueue.TryEnqueue(async () =>
                                {
                                    try
                                    {
                                        var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                                        using (var writer = new Windows.Storage.Streams.DataWriter(winrtStream))
                                        {
                                            writer.WriteBytes(bytes);
                                            await writer.StoreAsync();
                                            await writer.FlushAsync();
                                            writer.DetachStream();
                                        }
                                        winrtStream.Seek(0);
                                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                        await bitmap.SetSourceAsync(winrtStream);
                                        img.Source = bitmap;
                                    }
                                    catch { }
                                });
                            }
                            catch { }
                        });
                        return new InlineUIContainer { Child = img };
                    }
                }
                catch { }
            }
            else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
            {
                // Synchronously check if entry exists to avoid empty placeholder
                // This prevents blank pages when an image tag refers to a missing file
                string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                string? baseDir = "";
                int lastSlash = normKey.LastIndexOf('/');
                if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                
                // Remove redundant ./ if present
                targetKey = targetKey.Replace("/./", "/");

                // Manual search to match the logic inside the task
                bool exists = false;
                if (_currentArchive != null)
                {
                    exists = _currentArchive.Entries.Any(e => e.Key != null && 
                             (e.Key.Replace('\\', '/') == targetKey || 
                              string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                }
                else if (_current7zArchive != null)
                {
                    exists = _current7zArchive.Entries.Any(e => e.FileName != null && 
                             (e.FileName.Replace('\\', '/') == targetKey || 
                              string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                }

                if (!exists) return null;

                // Archive Case (Async Load)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _archiveLock.WaitAsync();
                        byte[]? bytes = null;
                        try
                        {
                        if (_currentArchive != null)
                        {
                            // Re-normalization inside task to be safe and consistent
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

                        if (bytes != null)
                        {
                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                                    using (var writer = new Windows.Storage.Streams.DataWriter(winrtStream))
                                    {
                                        writer.WriteBytes(bytes);
                                        await writer.StoreAsync();
                                        await writer.FlushAsync();
                                        writer.DetachStream();
                                    }
                                    winrtStream.Seek(0);
                                    
                                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                                    await bitmap.SetSourceAsync(winrtStream);
                                    img.Source = bitmap;
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }
                });
                return new InlineUIContainer { Child = img };
            }

            return null;
        }

        private InlineUIContainer CreateTableInline(List<List<string>> rows)
        {
            var grid = new Grid();
            grid.HorizontalAlignment = HorizontalAlignment.Left;
            // Use Border to wrap Grid for outer border
            grid.BorderBrush = new SolidColorBrush(Colors.LightGray);
            grid.BorderThickness = new Thickness(1, 1, 0, 0); // Top Left

            if (rows.Count == 0) return new InlineUIContainer { Child = grid };

            int maxCols = rows.Max(r => r.Count);
            
            for (int c = 0; c < maxCols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            for (int r = 0; r < rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rowData = rows[r];
                for (int c = 0; c < rowData.Count; c++)
                {
                    if (c >= maxCols) break;
                    
                    var cellText = rowData[c];
                    bool isHeader = (r == 0); 
                    
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.LightGray),
                        BorderThickness = new Thickness(0, 0, 1, 1), // Right Bottom
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = isHeader ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(50, 200, 200, 200)) : null
                    };
                    
                    // Parse inline formatting in cell
                    var rtb = new RichTextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 300
                    };
                    
                    var para = new Paragraph();
                    
                    // Parse cell content for inline formatting
                    string content = cellText;
                    
                    // 1. <br> -> {{BR}}
                    content = Regex.Replace(content, @"<br\s*/?>", "{{BR}}", RegexOptions.IgnoreCase);
                    
                    // 2. Bold **...** or __...__
                    content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "{{BOLD|$2}}");
                    
                    // Tokenize
                    string pattern = @"(\{\{BOLD\|.*?\}\}|\{\{BR\}\})";
                    var parts = Regex.Split(content, pattern);
                    
                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part)) continue;
                        
                        if (part == "{{BR}}")
                        {
                            para.Inlines.Add(new LineBreak());
                        }
                        else if (part.StartsWith("{{BOLD|"))
                        {
                            var inner = part.Substring(7, part.Length - 9);
                            para.Inlines.Add(new Run { Text = inner, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                        }
                        else
                        {
                            para.Inlines.Add(new Run 
                            { 
                                Text = part, 
                                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal 
                            });
                        }
                    }
                    
                    rtb.Blocks.Add(para);
                    border.Child = rtb;
                    
                    Grid.SetRow(border, r);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }
            }
            
            return new InlineUIContainer { Child = grid };
        }

        // TOC Handlers

        public class TocItem
        {
            public string HeadingText { get; set; } = "";
            public int SourceLineNumber { get; set; }
            public Thickness Margin => new Thickness((HeadingLevel - 1) * 16, 0, 0, 0);
            public int HeadingLevel { get; set; }
            public object? Tag { get; set; }
        }

        private async void TocButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTextMode && !_isEpubMode) return;

            // Ensure TOC Title
            if (TocFlyout.Content is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock tb)
            {
                tb.Text = Strings.TocTitle;
            }

            List<TocItem> items = new();

            if (_currentPdfDocument != null && _pdfToc.Count > 0)
            {
                items = _pdfToc.ToList();
            }
            else if ((_isAozoraMode || _isVerticalMode) && _aozoraBlocks.Count > 0)
            {
                items = _aozoraBlocks
                    .Where(b => b.HeadingLevel > 0)
                    .Select(b => new TocItem 
                    { 
                        HeadingText = b.HeadingText, 
                        SourceLineNumber = b.SourceLineNumber,
                        HeadingLevel = b.HeadingLevel
                    })
                    .ToList();
            }

            else if (_isEpubMode)
            {
                 // EPUB Mode
                 if (_epubToc != null && _epubToc.Count > 0)
                 {
                     items = _epubToc.Select(t => new TocItem 
                     { 
                         HeadingText = t.Title, 
                         HeadingLevel = 1, // Simplify level for now
                         SourceLineNumber = -1,
                         Tag = t 
                     }).ToList();
                 }
            }
            else if (!string.IsNullOrEmpty(_currentTextContent))
            {
                 // Scan raw text on demand for Normal Mode or if blocks are empty
                 items = await Task.Run(() => 
                 {
                     var list = new List<TocItem>();
                     var lines = _textLines.Count > 0 ? _textLines : SplitTextToLines(_currentTextContent); // Prefer split lines if available
                     
                     // In standard mode, finding exact source line number might be tricky if _textLines is wrapped.
                     // But _textLines usually stores 1:1 if no wrap? No, MainWindow.text.cs implementation of SplitTextToLines:
                     // Wait, SplitTextToLines splits by wrapping width? 
                     // Let's check SplitTextToLines implementation in MainWindow.text.cs.
                     // If SplitTextToLines wraps connected lines, SourceLineNumber logic is complex.
                     // A safe bet is scanning the raw source lines.
                     var rawLines = _currentTextContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                     
                     for (int i = 0; i < rawLines.Length; i++)
                     {
                         var line = rawLines[i].Trim();
                         int level = 0;
                         string text = "";

                         // Check Aozora
                         if (line.Contains("［＃大見出し］") || line.StartsWith("# ")) { level = 1; text = line.Replace("［＃大見出し］", "").TrimStart('#', ' '); }
                         else if (line.Contains("［＃中見出し］") || line.StartsWith("## ")) { level = 2; text = line.Replace("［＃中見出し］", "").TrimStart('#', ' '); }
                         
                         if (level > 0)
                         {
                             // Clean tags
                             text = Regex.Replace(text, @"［＃[^］]+］|\[.*?\]|[#]", "").Trim();
                             list.Add(new TocItem { HeadingText = text, SourceLineNumber = i + 1, HeadingLevel = level });
                         }
                     }
                     return list;
                 });
            }

            // Highlight current item and scroll
            int currentIndex = -1;

            if (_isEpubMode)
            {
                if (_currentEpubChapterIndex >= 0 && _currentEpubChapterIndex < _epubSpine.Count)
                {
                    string currentSpinePath = _epubSpine[_currentEpubChapterIndex];
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Tag is EpubTocItem epi)
                        {
                            string linkPath = epi.Link;
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
            }
            else if (_currentPdfDocument != null && items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= _currentIndex)
                        currentIndex = i;
                    else
                        break;
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
                    if (items[i].SourceLineNumber <= currentLine)
                        currentIndex = i;
                    else
                        break;
                }
            }

            if (currentIndex >= 0 && currentIndex < items.Count)
            {
                items[currentIndex].HeadingText = "⮕ " + items[currentIndex].HeadingText;
            }

            if (items.Count == 0)
            {
                items.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
            }

            TocListView.ItemsSource = items;
            
            if (currentIndex >= 0)
            {
                // Ensure layout updated before scrolling
                this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        TocListView.ScrollIntoView(items[currentIndex], ScrollIntoViewAlignment.Leading);
                    }
                    catch { }
                });
            }
        }

        private void TocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                TocFlyout.Hide();
                
                if (_isEpubMode)
                {
                     if (item.Tag is EpubTocItem epubItem)
                     {
                         JumpToEpubTocItem(epubItem);
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
