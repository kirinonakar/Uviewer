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
        private bool _isAozoraMode = false;
        private bool _isMarkdownRenderMode = false;
        private List<AozoraBindingModel> _aozoraBlocks = new();

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

            if (TextItemsRepeater != null)
            {
                // Update ItemTemplate
                if (RootGrid.Resources.TryGetValue("AozoraItemTemplate", out var template))
                {
                    TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                }
                TextItemsRepeater.ItemsSource = _aozoraBlocks;
            }
            
            if (TextArea != null)
            {
                TextArea.Background = GetThemeBackground();
            }
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
