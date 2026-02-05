using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isAozoraMode = true;
        private bool _isMarkdownRenderMode = false;
        private List<AozoraBindingModel> _aozoraBlocks = new();
        private List<AozoraPageModel> _aozoraPages = new();
        private int _aozoraTotalLineCount = 0;

        // Page model for Aozora mode - each item = 1 page
        public class AozoraPageModel
        {
            public List<AozoraBindingModel> Blocks { get; set; } = new();
            public int StartLine { get; set; } = 0; // First line number on this page
            public int EndLine { get; set; } = 0;   // Last line number on this page
        }

        public class AozoraBindingModel
        {
            public List<object> Inlines { get; set; } = new(); // String (for text), AozoraBold (for bold), AozoraRuby (for ruby)
            public double FontSizeScale { get; set; } = 1.0;
            public TextAlignment Alignment { get; set; } = TextAlignment.Left;
            public Thickness Margin { get; set; } = new Thickness(0);
            public Thickness Padding { get; set; } = new Thickness(0);
            public Brush? BorderBrush { get; set; } = null;
            public Thickness BorderThickness { get; set; } = new Thickness(0);
            public Brush? Background { get; set; } = null;
            public string? FontFamily { get; set; } = null; // Override font family (e.g. for code)
            public bool IsTable { get; set; } = false;
            public List<List<string>> TableRows { get; set; } = new();
            public int SourceLineNumber { get; set; } = 0; // Original line number in source text
        }

        public class AozoraBold { public string Text { get; set; } = ""; }
        public class AozoraItalic { public string Text { get; set; } = ""; }
        public class AozoraCode { public string Text { get; set; } = ""; }
        public class AozoraLineBreak { }
        public class AozoraRuby { public string BaseText { get; set; } = ""; public string RubyText { get; set; } = ""; }

        // Settings
        public class AozoraSettings
        {
            public bool IsAozoraModeEnabled { get; set; } = false;
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
            // If called from Click, IsChecked is already toggled by UI.
            // If called from Shortcut, IsChecked needs update.
            // We use the current state of _isAozoraMode to decide.
            
            // However, since Click toggles IsChecked before this handler,
            // If we came from click: IsChecked(True) -> _isAozoraMode(False) -> we want True.
            // If we came from shortcut: IsChecked(False) -> _isAozoraMode(False) -> we want True.
            
            // Simplest way: Logic Toggle, then Force UI Sync.
            _isAozoraMode = !_isAozoraMode;
            
            if (AozoraToggleButton != null)
            {
                AozoraToggleButton.IsChecked = _isAozoraMode;
            }
            
            SaveAozoraSettings();
            
            // Reload current content
            if (_currentTextFilePath != null)
            {
                var entry = _imageEntries.FirstOrDefault(x => x.FilePath == _currentTextFilePath);
                if (entry != null)
                {
                    _ = LoadTextEntryAsync(entry);
                }
            }
        }

        private void PrepareAozoraDisplay(string rawContent)
        {
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
                _aozoraBlocks = ParseMarkdownContent(rawContent);
            }
            else
            {
                _isMarkdownRenderMode = false;
                _aozoraBlocks = ParseAozoraContent(rawContent);
            }

            // Calculate pages based on screen size
            _aozoraTotalLineCount = _aozoraBlocks.Count;
            _aozoraPages = CalculateAozoraPages(_aozoraBlocks);
            _currentAozoraPageIndex = 0;
            
            // Use simple container for virtualized rendering
            // Only the current page is rendered at any time
            if (AozoraPageContainer != null)
            {
                AozoraPageContainer.Background = GetThemeBackground();
                AozoraPageContainer.Visibility = Visibility.Visible;
                
                // Render first page
                RenderCurrentAozoraPage();
            }
            
            // Hide ScrollViewer-based TextArea
            if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
            
            if (TextArea != null)
            {
                TextArea.Background = GetThemeBackground();
            }
            
            // Update status bar
            UpdateAozoraStatusBar();
        }
        
        // Current page index for virtualized navigation
        private int _currentAozoraPageIndex = 0;
        private double _lastAozoraContainerHeight = 0;
        private double _lastAozoraContainerWidth = 0;
        private System.Threading.CancellationTokenSource? _aozoraResizeCts;
        
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
                
                // Remember current reading position (by line number)
                int currentStartLine = 0;
                if (_currentAozoraPageIndex >= 0 && _currentAozoraPageIndex < _aozoraPages.Count)
                {
                    currentStartLine = _aozoraPages[_currentAozoraPageIndex].StartLine;
                }
                
                // Recalculate pages
                _aozoraPages = CalculateAozoraPages(_aozoraBlocks);
                
                // Find page containing the previous start line
                _currentAozoraPageIndex = 0;
                for (int i = 0; i < _aozoraPages.Count; i++)
                {
                    if (_aozoraPages[i].StartLine <= currentStartLine && _aozoraPages[i].EndLine >= currentStartLine)
                    {
                        _currentAozoraPageIndex = i;
                        break;
                    }
                }
                _currentAozoraPageIndex = Math.Min(_currentAozoraPageIndex, _aozoraPages.Count - 1);
                
                // Re-render current page
                RenderCurrentAozoraPage();
                UpdateAozoraStatusBar();
            }
            catch (TaskCanceledException) { }
        }
        
        private void RenderCurrentAozoraPage()
        {
            if (_currentAozoraPageIndex < 0 || _currentAozoraPageIndex >= _aozoraPages.Count) return;
            if (AozoraPageContent == null) return;
            
            var page = _aozoraPages[_currentAozoraPageIndex];
            
            // Clear and rebuild content
            AozoraPageContent.Blocks.Clear();
            AozoraPageContent.FontFamily = new FontFamily(_textFontFamily);
            AozoraPageContent.FontSize = _textFontSize;
            AozoraPageContent.Foreground = GetThemeForeground();
            AozoraPageContent.MaxWidth = _isMarkdownRenderMode ? double.PositiveInfinity : GetUrlMaxWidth();
            
            foreach (var block in page.Blocks)
            {
                if (block.IsTable)
                {
                    var tablePara = new Paragraph();
                    tablePara.Inlines.Add(CreateTableInline(block.TableRows));
                    AozoraPageContent.Blocks.Add(tablePara);
                    continue;
                }
                
                var p = new Paragraph();
                p.LineHeight = _textFontSize * block.FontSizeScale * 1.8;
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                p.Margin = new Thickness(0, block.Margin.Top, 0, block.Margin.Bottom);
                p.TextAlignment = block.Alignment;
                
                foreach (var item in block.Inlines)
                {
                    if (item is string text)
                    {
                        p.Inlines.Add(new Run 
                        { 
                            Text = text,
                            FontSize = _textFontSize * block.FontSizeScale
                        });
                    }
                    else if (item is AozoraBold bold)
                    {
                        p.Inlines.Add(new Run 
                        { 
                            Text = bold.Text, 
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            FontSize = _textFontSize * block.FontSizeScale
                        });
                    }
                    else if (item is AozoraItalic italic)
                    {
                        p.Inlines.Add(new Run 
                        { 
                            Text = italic.Text, 
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            FontSize = _textFontSize * block.FontSizeScale
                        });
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
                            Foreground = new SolidColorBrush(Colors.DarkSlateGray),
                            FontSize = _textFontSize * block.FontSizeScale
                        });
                    }
                    else if (item is AozoraRuby ruby)
                    {
                        p.Inlines.Add(CreateRubyInline(ruby.BaseText, ruby.RubyText, _textFontSize * block.FontSizeScale));
                    }
                }
                
                AozoraPageContent.Blocks.Add(p);
            }
            
            // Scroll to top of page
            if (AozoraPageScroll != null)
            {
                AozoraPageScroll.ChangeView(null, 0, null, true);
            }
        }
        
        private List<AozoraPageModel> CalculateAozoraPages(List<AozoraBindingModel> blocks)
        {
            var pages = new List<AozoraPageModel>();
            if (blocks.Count == 0) return pages;
            
            // Get available height from the actual container
            double availableHeight = AozoraPageContainer?.ActualHeight ?? TextArea?.ActualHeight ?? RootGrid.ActualHeight;
            
            // If container not yet measured, use RootGrid with UI element offset
            if (availableHeight < 100)
            {
                availableHeight = RootGrid.ActualHeight;
                if (!_isFullscreen)
                {
                    availableHeight -= 60; // Toolbar + StatusBar
                }
            }
            
            // Container padding (matches XAML Padding="20") + Safety Buffer
            availableHeight -= 60;
            
            if (availableHeight < 200) availableHeight = 600; // Fallback
            
            // Consistent line height: FontSize * 1.8 (to accommodate ruby text)
            double lineHeight = _textFontSize * 1.8;
            double maxWidth = GetUrlMaxWidth();
            
            double currentHeight = 0;
            var currentPage = new AozoraPageModel();
            int lineNum = 1;
            
            foreach (var block in blocks)
            {
                block.SourceLineNumber = lineNum;
                
                double blockHeight = EstimateBlockHeight(block, lineHeight, maxWidth);
                
                // Check if we need new page
                if (currentHeight + blockHeight > availableHeight && currentPage.Blocks.Count > 0)
                {
                    currentPage.EndLine = lineNum - 1;
                    pages.Add(currentPage);
                    currentPage = new AozoraPageModel { StartLine = lineNum };
                    currentHeight = 0;
                }
                
                if (currentPage.Blocks.Count == 0)
                {
                    currentPage.StartLine = lineNum;
                }
                
                currentPage.Blocks.Add(block);
                currentHeight += blockHeight;
                lineNum++;
            }
            
            // Add last page
            if (currentPage.Blocks.Count > 0)
            {
                currentPage.EndLine = lineNum - 1;
                pages.Add(currentPage);
            }
            
            return pages;
        }
        
        private double EstimateBlockHeight(AozoraBindingModel block, double lineHeight, double maxWidth)
        {
            if (block.IsTable)
            {
                // Tables: estimate based on rows
                int rows = block.TableRows.Count;
                return rows * lineHeight + 20; // +20 for padding
            }
            
            // Calculate character count
            int charCount = 0;
            foreach (var item in block.Inlines)
            {
                if (item is string str) charCount += str.Length;
                else if (item is AozoraBold bold) charCount += bold.Text.Length;
                else if (item is AozoraItalic italic) charCount += italic.Text.Length;
                else if (item is AozoraCode code) charCount += code.Text.Length;
                else if (item is AozoraRuby ruby) charCount += ruby.BaseText.Length;
                else if (item is AozoraLineBreak) charCount += 50; // Force new line estimate
            }
            
            // Estimate lines based on character count and font size
            double effectiveFontSize = _textFontSize * block.FontSizeScale;
            double charsPerLine = (maxWidth / effectiveFontSize) * 0.95; // Safety factor for wrapping
            if (charsPerLine < 1) charsPerLine = 40;
            
            double estimatedLines = Math.Ceiling(charCount / charsPerLine);
            if (estimatedLines < 1) estimatedLines = 1;
            
            // Block vertical margins
            double margins = block.Margin.Top + block.Margin.Bottom + block.Padding.Top + block.Padding.Bottom;
            
            return (estimatedLines * lineHeight * block.FontSizeScale) + margins;
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
            int newIndex = _currentAozoraPageIndex + direction;
            
            if (newIndex >= 0 && newIndex < _aozoraPages.Count)
            {
                _currentAozoraPageIndex = newIndex;
                RenderCurrentAozoraPage();
                UpdateAozoraStatusBar();
            }
        }
        
        private void UpdateAozoraStatusBar()
        {
            if (!_isTextMode || !_isAozoraMode) return;
            
            int currentPage = _currentAozoraPageIndex + 1;
            int totalPages = _aozoraPages.Count;
            
            // Get current page's line range
            int startLine = 1;
            int endLine = _aozoraTotalLineCount;
            
            if (_currentAozoraPageIndex >= 0 && _currentAozoraPageIndex < _aozoraPages.Count)
            {
                var page = _aozoraPages[_currentAozoraPageIndex];
                startLine = page.StartLine;
                endLine = page.EndLine;
            }
            
            // Update status bar
            ImageIndexText.Text = $"{currentPage} / {totalPages}";
            ImageInfoText.Text = $"Line {startLine}-{endLine} / {_aozoraTotalLineCount}";
        }

        private List<AozoraBindingModel> ParseAozoraContent(string text)
        {
            var blocks = new List<AozoraBindingModel>();
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            bool lastWasEmpty = false;

            foreach (var line in lines)
            {
                // Basic clean
                var content = line.Replace('\u3000', ' ').Trim(); 
                // Note: Aozora often uses fullwidth space for indentation. If trim, we lose it.
                // Maybe preserve indentation?
                // Let's preserve leading space if it's not a command line.
                content = line.TrimEnd(); 
                
                if (string.IsNullOrEmpty(content)) 
                {
                     if (lastWasEmpty) continue; // Collapse consecutive empty lines
                     
                     // Empty line -> Spacer
                     blocks.Add(new AozoraBindingModel { Inlines = { "" }, Margin = new Thickness(0, 0, 0, _textFontSize) });
                     lastWasEmpty = true;
                     continue;
                }
                lastWasEmpty = false;

                var model = new AozoraBindingModel();
                model.Margin = new Thickness(0);

                // --- Aozora Tag Parsing ---
                // Headers
                if (content.Contains("［＃大見出し］") || content.StartsWith("# "))
                {
                    model.FontSizeScale = 1.5;
                    content = content.Replace("［＃大見出し］", "").TrimStart('#', ' ');
                }
                else if (content.Contains("［＃中見出し］") || content.StartsWith("## "))
                {
                    model.FontSizeScale = 1.25;
                    content = content.Replace("［＃中見出し］", "").TrimStart('#', ' ');
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
                     model.BorderBrush = new SolidColorBrush(Colors.Gray);
                     model.BorderThickness = new Thickness(1);
                     model.Padding = new Thickness(10);
                     content = content.Replace("［＃ここから罫囲み］", "");
                }

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
                // Heuristic: Take all adjacent Kanji before 《
                // Regex: ([一-龠々]+)《(.+?)》
                content = Regex.Replace(content, @"([一-龠々]+)《(.+?)》", "{{RUBY|$1|$2}}");
                
                // 3. Fallback for mixed/other scripts? Aozora spec usually strictly defines this. 
                // Some files use 《》 for other things? Assuming Ruby for now.
                
                // Tokenize
                // Split by {{RUBY|...}} AND **...**
                // Regex split captures delimiters if in parenthesis.
                
                string pattern = @"(\{\{RUBY\|.*?\|.*?\}\}|\*\*.*?\*\*)";
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
                    model.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128));
                    model.Padding = new Thickness(4, 0, 4, 0);
                    model.Margin = new Thickness(20, 0, 20, 0);
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
                         if (level == 1 || level == 2) 
                         {
                              blockModel.BorderBrush = new SolidColorBrush(Colors.LightGray);
                              blockModel.BorderThickness = new Thickness(0, 0, 0, 1); // Bottom border for H1/H2
                         }
                    }
                }
                // Quote
                else if (content.StartsWith(">"))
                {
                    content = content.TrimStart('>', ' ');
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                    blockModel.BorderBrush = new SolidColorBrush(Colors.Gray);
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
                     blockModel.BorderBrush = new SolidColorBrush(Colors.Gray);
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

                // 2. Bold **...** or __...__ (Support spaces inside: ** text **)
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "{{BOLD|$2}}");
                
                // 3. Italic *...* or _..._
                content = Regex.Replace(content, @"(\*|_)(.*?)\1", "{{ITALIC|$2}}");
                
                // Tokenize
                string pattern = @"(\{\{CODE\|.*?\}\}|\{\{BOLD\|.*?\}\}|\{\{ITALIC\|.*?\}\}|\{\{BR\}\})";
                var parts = Regex.Split(content, pattern);
                
                foreach (var part in parts)
                {
                     if (string.IsNullOrEmpty(part)) continue;
                     
                     if (part.StartsWith("{{CODE|"))
                     {
                         var inner = part.Substring(7, part.Length - 9);
                         blockModel.Inlines.Add(new AozoraCode { Text = inner });
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

        private void PrepareAozoraElement(RichTextBlock rtb, int index)
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
            p.LineHeight = rtb.FontSize * 1.8;
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
            }
            
            rtb.Blocks.Add(p);
        }

        private InlineUIContainer CreateRubyInline(string baseText, string rubyText, double baseFontSize)
        {
            // Reuse logic from epub.cs (CreateRuby) but adapted for InlineUIContainer
            
            bool isMincho = _textFontFamily.Contains("Mincho") || _textFontFamily.Contains("Myungjo");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ruby
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Base
            
            var rt = new TextBlock
            {
                Text = rubyText,
                FontSize = baseFontSize * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(), 
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, isMincho ? -7 : -5) 
            };
            Grid.SetRow(rt, 0);
            
            var rb = new TextBlock
            {
                Text = baseText,
                FontSize = baseFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                Margin = new Thickness(0, isMincho ? 2 : 4, 0, 0) 
            };
            Grid.SetRow(rb, 1);
            
            grid.Children.Add(rt);
            grid.Children.Add(rb);
            
            // Translate down to align base text with surrounding text
            // Gothic needs less offset (0.3), Mincho needs more (0.6)
            double offsetFactor = isMincho ? 0.6 : 0.3;
            grid.RenderTransform = new TranslateTransform { Y = baseFontSize * offsetFactor };
            
            return new InlineUIContainer { Child = grid }; // Align baseline? Default is Bottom.
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
    }
}
