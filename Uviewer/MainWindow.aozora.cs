using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
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
        }

        public class AozoraBold { public string Text { get; set; } = ""; }
        public class AozoraItalic { public string Text { get; set; } = ""; }
        public class AozoraCode { public string Text { get; set; } = ""; }
        public class AozoraLineBreak { }
        public class AozoraRuby { public string BaseText { get; set; } = ""; public string RubyText { get; set; } = ""; }
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

        private void ToggleAozoraMode()
        {
            // Capture current position BEFORE toggling logic
            int currentLine = 1;
            if (_isAozoraMode)
            {
                if (_aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                {
                    currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }
            }
            else
            {
                // In Text Mode
                currentLine = GetTopVisibleLineIndex();
            }

            // Simplest way: Logic Toggle, then Force UI Sync.
            _isAozoraMode = !_isAozoraMode;
            
            // Set pending target for the reload
            _aozoraPendingTargetLine = currentLine;
            
            if (AozoraToggleButton != null)
            {
                AozoraToggleButton.IsChecked = _isAozoraMode;
            }
            
            SaveAozoraSettings();
            
            // Use cached content directly instead of re-reading from disk
            if (!string.IsNullOrEmpty(_currentTextContent))
            {
                string fileName = _currentTextFilePath != null ? System.IO.Path.GetFileName(_currentTextFilePath) : "Text";
                _ = ReloadTextDisplayFromCacheAsync(fileName, currentLine);
            }
            else if (_currentTextFilePath != null)
            {
                // Fallback: reload from file if cache is empty
                var entry = _imageEntries.FirstOrDefault(x => x.FilePath == _currentTextFilePath);
                if (entry != null)
                {
                    _ = LoadTextEntryAsync(entry);
                }
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

                for (int i = 0; i < _aozoraBlocks.Count; i++)
                {
                    if (token.IsCancellationRequested) return;

                    blockToPageMap[i] = pageCount;

                    var block = _aozoraBlocks[i];
                    
                    if (block.HasImage)
                    {
                        if (currentPageHeight > 0)
                        {
                            pageCount++;
                            currentPageHeight = 0;
                            blockToPageMap[i] = pageCount;
                        }

                        // Image block takes its own page
                        pageCount++;
                        currentPageHeight = 0;
                        continue;
                    }

                    // Measure Block
                    dummyRTB.Blocks.Clear();
                    var p = CreateParagraphFromBlock(block, availableHeight);
                    dummyRTB.Blocks.Add(p);
                    
                    dummyRTB.Measure(new Windows.Foundation.Size((float)maxWidth, double.PositiveInfinity));
                    double blockHeight = dummyRTB.DesiredSize.Height;
                    
                    if (currentPageHeight + blockHeight > availableHeight)
                    {
                        // New Page
                        if (currentPageHeight > 0) // If not empty page already
                        {
                            pageCount++;
                            currentPageHeight = 0;
                            blockToPageMap[i] = pageCount; // This block starts new page
                        }
                    }
                    
                    currentPageHeight += blockHeight;
                    
                    // Very large single block handling?
                    if (currentPageHeight > availableHeight)
                    {
                         currentPageHeight = 0; 
                         pageCount++;
                    }

                    if (sw.ElapsedMilliseconds > 15)
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
                }
                else
                {
                    _isMarkdownRenderMode = false;
                    
                    // Optimized Loading for Large Files
                    // User Request: "File first open, then TOC background calculate"
                    // TOC is derived from _aozoraBlocks structure. Parsing creates blocks.
                    // We split parsing: First Chunk (Immediate) -> Render -> Rest (Background)
                    
                    var lines = rawContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                    _aozoraTotalLineCountInSource = lines.Length;

                    int initialLimit = 2000;
                    if (targetLine > initialLimit - 500) initialLimit = targetLine + 500;

                    if (lines.Length > initialLimit)
                    {
                        // 1. Initial Load (First initialLimit lines)
                        var initialLines = lines.Take(initialLimit).ToArray();
                        _aozoraBlocks = await Task.Run(() => ParseAozoraLines(initialLines, 1));
                        
                        // Proceed to render first page immediately below...
                        
                        // 2. Queue Background Load for the rest
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
                                    if (!_isAozoraMode) return; // Mode switched?
                                    
                                    _aozoraBlocks.AddRange(restBlocks);
                                    
                                    // Trigger status update and full page recalc
                                    UpdateAozoraStatusBar();
                                    StartAozoraPageCalculationAsync();
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
                        if (_aozoraBlocks[i].SourceLineNumber >= targetLine)
                        {
                            startIdx = i;
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

            // 임시 측정을 위한 RichTextBlock 설정 (기존 UI 객체 활용)
            // WinUI에서는 LayoutCycle을 유발할 수 있으므로, 이미 시각적 트리에 있는 AozoraPageContent를 
            // 직접 채워가며 Measure하는 것이 가장 정확합니다.
            
            for (int i = startIdx; i < _aozoraBlocks.Count; i++)
            {
                var block = _aozoraBlocks[i];
                var p = CreateParagraphFromBlock(block, availableHeight, innerWidth);

                // If the block is empty (e.g., missing image), skip it entirely
                if (p.Inlines.Count == 0)
                {
                    endIdx = i;
                    continue;
                }
                
                // If this block has an image, it should be isolated on its own page
                if (block.HasImage)
                {
                    if (AozoraPageContent.Blocks.Count > 0)
                    {
                        // Finish current page before image
                        break;
                    }
                    else
                    {
                        // First block is image, add ONLY this block and stop
                        p.TextAlignment = TextAlignment.Center;
                        
                        // [Fix] Ensure container allows full width for the image
                        if (AozoraPageContainer != null) AozoraPageContainer.Padding = new Thickness(0);
                        AozoraPageContent.MaxWidth = innerWidth;
                        AozoraPageContent.Padding = new Thickness(0);
                        AozoraPageContent.VerticalAlignment = VerticalAlignment.Center;

                        AozoraPageContent.Blocks.Add(p);
                        endIdx = i;
                        break;
                    }
                }

                AozoraPageContent.Blocks.Add(p);
                
                // Measure the container to see current total height
                AozoraPageContent.Measure(new Windows.Foundation.Size(AozoraPageContent.MaxWidth, double.PositiveInfinity));
                double newHeight = AozoraPageContent.DesiredSize.Height;
                
                if (AozoraPageContent.Blocks.Count > 1 && newHeight > availableHeight)
                {
                    // Too high, remove last and stop
                    AozoraPageContent.Blocks.Remove(p);
                    break;
                }
                
                currentHeight = newHeight;
                endIdx = i;
                
                // Very long single blocks are allowed (they will be scrollable)
                if (currentHeight > availableHeight && AozoraPageContent.Blocks.Count == 1)
                {
                    break;
                }
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
                p.LineHeight = _textFontSize * block.FontSizeScale * 2; // Increased multiplier for better ruby spacing
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            p.Margin = new Thickness(0, block.Margin.Top, 0, block.Margin.Bottom);
            p.TextAlignment = block.Alignment;
            p.FontFamily = block.FontFamily != null ? new FontFamily(block.FontFamily) : new FontFamily(_textFontFamily);
            
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
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, _textFontSize * block.FontSizeScale));
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
            p.LineHeight = rtb.FontSize * 2.2;
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
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, rtb.FontSize));
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
                    // Fallback if history empty but not at start
                    // Guess a previous starting point (estimated backward)
                    int guess = Math.Max(0, _currentAozoraStartBlockIndex - 20); 
                    RenderAozoraDynamicPage(guess);
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
                    startIdx = i;
                    break;
                }
            }
            
            _aozoraNavHistory.Push(_currentAozoraStartBlockIndex);
            RenderAozoraDynamicPage(startIdx);
            UpdateAozoraStatusBar();
        }

        private List<AozoraBindingModel> ParseAozoraContent(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            _textTotalLineCountInSource = lines.Length;
            return ParseAozoraLines(lines, 1);
        }

        private List<AozoraBindingModel> ParseAozoraLines(string[] lines, int startLineOffset)
        {
            var blocks = new List<AozoraBindingModel>();
            bool lastWasEmpty = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Basic clean
                var content = line.Replace('\u3000', ' ').TrimEnd(); 
                
                if (string.IsNullOrEmpty(content)) 
                {
                     if (lastWasEmpty) continue; // Collapse consecutive empty lines
                     
                     // Empty line -> Spacer
                     blocks.Add(new AozoraBindingModel { 
                         Inlines = { "" }, 
                         Margin = new Thickness(0, 0, 0, _textFontSize),
                         SourceLineNumber = startLineOffset + i
                     });
                     lastWasEmpty = true;
                     continue;
                }
                lastWasEmpty = false;

                var model = new AozoraBindingModel { SourceLineNumber = startLineOffset + i };
                model.Margin = new Thickness(0);

                // --- Aozora Tag Parsing --- (Rest is same)
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
                
                // Indents
                if (content.Contains("［＃ここから２字下げ］"))
                {
                    model.Margin = new Thickness(40, 0, 0, 0); // Accumulate?
                    content = content.Replace("［＃ここから２字下げ］", "");
                }
                
                // Decorations
                if (content.Contains("［＃ここから罫囲み］"))
                {
                     model.BorderColor = Colors.Gray;
                     model.BorderThickness = new Thickness(1);
                     model.Padding = new Thickness(10);
                     content = content.Replace("［＃ここから罫囲み］", "");
                }

                // 4. Image tags: <img src="file.jpg"> or ［＃挿絵（img/file.jpg）入る］
                // IMPORTANT: Parse images BEFORE cleaning up all ［＃...］ tags
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                // Cleanup other tags
                content = Regex.Replace(content, @"［＃[^］]+］", "");

                // --- Inline Parsing (Ruby & Bold) ---
                // Pattern: 
                // Ruby: ｜Base《Ruby》 or Base《Ruby》 (Complex regex needed to avoid partial matches)
                // Bold: **Text**
                
                // We tokenize carefully.
                // Pre-process Ruby into a temp unique format like {{RUBY|Base|Text}} to simplify regex splitting
                
                // 1. Aozora Ruby with pipe: ｜漢字《かんじ》
                content = Regex.Replace(content, @"｜(.+?)《(.+?)》", "{{RUBY|$1|$2}}");
                
                // 2. Aozora Ruby without pipe (Kanji + Ruby): 漢字《かんじ》
                // Heuristic: Take all adjacent CJK characters before 《
                // Broadened range to include Japanese/Korean/Chinese ideographs
                content = Regex.Replace(content, @"([\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF々]+)《(.+?)》", "{{RUBY|$1|$2}}");
                
                // 3. Fallback for mixed/other scripts? Aozora spec usually strictly defines this. 
                // Some files use 《》 for other things? Assuming Ruby for now.
                
                // (Image parsing moved above cleanup)

                // Tokenize
                // Split by {{RUBY|...}}, {{IMG|...}} AND **...**
                // Regex split captures delimiters if in parenthesis.
                
                string pattern = @"(\{\{RUBY\|.*?\|.*?\}\}|\{\{IMG\|.*?\}\}|\*\*.*?\*\*)";
                var parts = Regex.Split(content, pattern);
                
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    
                    if (part.StartsWith("{{RUBY|"))
                    {
                        var inner = part.Trim('{', '}'); // RUBY|Base|Ruby
                        var p = inner.Split('|');
                        if (p.Length >= 3)
                        {
                            model.Inlines.Add(new AozoraRuby { BaseText = p[1], RubyText = p[2] });
                        }
                    }
                    else if (part.StartsWith("{{IMG|"))
                    {
                        var src = part.Substring(6, part.Length - 8);
                        // Strip parameters after comma if present (e.g., img.jpg,横50％)
                        int commaIdx = src.IndexOfAny(new[] { ',', '，', '、' });
                        if (commaIdx >= 0) src = src.Substring(0, commaIdx).Trim();
                        
                        model.Inlines.Add(new AozoraImage { Source = src });
                    }
                    else if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    {
                        model.Inlines.Add(new AozoraBold { Text = part.Substring(2, part.Length - 4) });
                    }
                    else
                    {
                        model.Inlines.Add(part); // String
                    }
                }
                
                blocks.Add(model);
            }

            return blocks;
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
                     blocks.Add(new AozoraBindingModel { Inlines = { "" }, Margin = new Thickness(0, 0, 0, _textFontSize) });
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

        private void PrepareAozoraElement(RichTextBlock rtb, int index, double availableHeight = 0)
        {
            if (index < 0 || index >= _aozoraBlocks.Count) return;
            var block = _aozoraBlocks[index];

            // Setup Container Properties
            rtb.FontSize = _textFontSize * block.FontSizeScale;
            rtb.FontFamily = block.FontFamily != null ? new FontFamily(block.FontFamily) : new FontFamily(_textFontFamily);
            rtb.Foreground = GetThemeForeground();
            rtb.TextAlignment = block.Alignment;
            rtb.Margin = block.Margin;
            rtb.Padding = block.Padding;
            rtb.Margin = block.Margin;
            rtb.Padding = block.Padding;
            
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
                    p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, rtb.FontSize));
                }
                else if (item is AozoraImage img)
                {
                    var ui = CreateImageInline(img.Source, availableHeight);
                    if (ui != null) p.Inlines.Add(ui);
                }
            }
            
            rtb.Blocks.Add(p);
        }

        private InlineUIContainer CreateRubyInline(string baseText, string rubyText, double baseFontSize)
        {
            var grid = new Grid();
            
            // Auto 높이를 사용하여 내용물 크기에 딱 맞게 설정
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ruby (0행)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Base (1행)
            
            // 루비 텍스트 (윗첨자)
            var rt = new TextBlock
            {
                Text = rubyText,
                FontSize = baseFontSize * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                Opacity = 1,
                TextLineBounds = TextLineBounds.Tight, // 여백 없이 텍스트 영역만 차지
                IsHitTestVisible = false,
                Margin = new Thickness(0, 0, 0, 4)
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
                Padding = new Thickness(0)
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
            if (string.IsNullOrEmpty(_currentTextFilePath) && (_currentArchive == null || string.IsNullOrEmpty(_currentTextArchiveEntryKey))) return null;
            
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

            if (!string.IsNullOrEmpty(_currentTextFilePath))
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
            else if (_currentArchive != null && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
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
                var exists = _currentArchive.Entries.Any(e => e.Key != null && 
                             (e.Key.Replace('\\', '/') == targetKey || 
                              string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));

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

            if ((_isAozoraMode || _isVerticalMode) && _aozoraBlocks.Count > 0)
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
                                // Keep searching to find the *last* matching TOC entry (e.g. sub-chapters) 
                                // that starts at this file? No, usually TOC is ordered. 
                                // If "Chapter 1" (chap1.html) and "Section 1.1" (chap1.html#sec1), 
                                // we can't distinguish which one purely by file path without hash/line checking.
                                // For now, taking the first one is safer as a "Chapter" marker.
                                break; 
                            }
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
                    if (_verticalPageInfos != null && _currentVerticalPageIndex >= 0 && _currentVerticalPageIndex < _verticalPageInfos.Count)
                    {
                        currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
                    }
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
