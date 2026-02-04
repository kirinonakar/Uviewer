using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private ZipArchive? _currentEpubArchive;
        private List<string> _epubSpine = new();
        private int _currentEpubChapterIndex = 0;
        private string? _currentEpubFilePath;
        private object _epubLock = new object();
        private double _epubTextWidth = 0;
        private bool _isEpubMode = false;

        // EPUB Settings
        private double _epubFontSize = 18;
        private string _epubFontFamily = "Yu Gothic Medium";
        private int _epubThemeIndex = 0; // 0: White, 1: Beige, 2: Dark

        public class EpubSettings
        {
             public double FontSize { get; set; } = 18;
             public string FontFamily { get; set; } = "Yu Gothic Medium";
             public int ThemeIndex { get; set; } = 0;
        }

        private const string EpubSettingsFilePath = "epub_settings.json";
        private string GetEpubSettingsFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", EpubSettingsFilePath);

        [System.Text.Json.Serialization.JsonSerializable(typeof(EpubSettings))]
        public partial class EpubSettingsContext : System.Text.Json.Serialization.JsonSerializerContext;

        public int CurrentEpubChapterIndex => _currentEpubChapterIndex;
        public int CurrentEpubPageIndex => EpubFlipView != null ? EpubFlipView.SelectedIndex : 0;

        public async Task RestoreEpubStateAsync(int chapterIndex, int pageIndex)
        {
            if (chapterIndex >= 0 && chapterIndex < _epubSpine.Count)
            {
                 _currentEpubChapterIndex = chapterIndex;
                 await LoadEpubChapterAsync(_currentEpubChapterIndex);
                 
                 // Wait for rendering
                 await Task.Delay(100);
                 
                 if (pageIndex >= 0 && pageIndex < EpubFlipView.Items.Count)
                 {
                     EpubFlipView.SelectedIndex = pageIndex;
                 }
            }
        }

        private async Task LoadEpubEntryAsync(ImageEntry entry)
        {
            if (entry.FilePath == null) return;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(entry.FilePath);
                await LoadEpubFileAsync(file);
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"EPUB 로드 실패: {ex.Message}";
            }
        }

        private bool _epubInputInitialized = false;

        private void InitializeEpub()
        {
            if (!_epubInputInitialized)
            {
                 RootGrid.PreviewKeyDown += RootGrid_Epub_PreviewKeyDown;
                 _epubInputInitialized = true;
            }
        }

        private void RootGrid_Epub_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (!_isEpubMode) return;

            if (e.Key == Windows.System.VirtualKey.Left)
            {
                _ = NavigateEpubAsync(-1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                _ = NavigateEpubAsync(1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Up)
            {
                // Previous File
                MoveExplorerSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                // Next File
                MoveExplorerSelection(1);
                e.Handled = true;
            }
        }

        private async Task LoadEpubFileAsync(StorageFile file)
        {
             InitializeEpub();
             StopAnimatedWebp();
             _currentEpubFilePath = file.Path;
             
             try
             {
                 var stream = await file.OpenStreamForReadAsync();
                 _currentEpubArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                 
                 // 1. Parse Container
                 var rootPath = await ParseEpubContainerAsync();
                 if (string.IsNullOrEmpty(rootPath)) throw new Exception("Invalid container.xml");
                 
                 // 2. Parse OPF
                 await ParseEpubOpfAsync(rootPath);
                 
                 if (_epubSpine.Count == 0) throw new Exception("No content found in EPUB");
                 
                 SwitchToEpubMode();
                 
                 // 3. Load First Chapter
                 _currentEpubChapterIndex = 0;

                 // Load Settings
                 LoadEpubSettings();

                 await LoadEpubChapterAsync(_currentEpubChapterIndex);
                 
                 FileNameText.Text = file.Name;
                 SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
             }
             catch (Exception ex)
             {
                 FileNameText.Text = $"EPUB 파싱 오류: {ex.Message}";
             }
        }

        // ... [Rest of File, ensuring CreateTextPages wraps in ScrollViewer] ...



        private void SwitchToEpubMode()
        {
            _isEpubMode = true;
            _isTextMode = false;
            
            ImageArea.Visibility = Visibility.Collapsed;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Visible; // Defined in MainWindow.xaml
            
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible; // Reuse text toolbar for now
            
            Title = "Uviewer - Epub Reader";
            
             if (StatusBarGrid != null)
                StatusBarGrid.Background = GetEpubThemeBackground();
        }

        private async Task<string> ParseEpubContainerAsync()
        {
            var entry = _currentEpubArchive?.GetEntry("META-INF/container.xml");
            if (entry == null) return "";

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            
            // Regex to find full-path
            var match = Regex.Match(content, "full-path=\"([^\"]+)\"");
            if (match.Success) return match.Groups[1].Value;
            
            return "";
        }

        private async Task ParseEpubOpfAsync(string opfPath)
        {
            var entry = _currentEpubArchive?.GetEntry(opfPath);
            if (entry == null) return;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            
            // Extract Manifest
            var manifest = new Dictionary<string, string>(); // id -> href
            var manifestMatches = Regex.Matches(content, "<item[^>]*id=\"([^\"]+)\"[^>]*href=\"([^\"]+)\"[^>]*>");
            foreach (Match m in manifestMatches)
            {
                manifest[m.Groups[1].Value] = m.Groups[2].Value;
            }
            
            // Extract Spine
            _epubSpine.Clear();
            var itemRefMatches = Regex.Matches(content, "<itemref[^>]*idref=\"([^\"]+)\"[^>]*/>");
            
            string opfDir = Path.GetDirectoryName(opfPath)?.Replace("\\", "/") ?? "";
            
            foreach (Match m in itemRefMatches)
            {
                string id = m.Groups[1].Value;
                if (manifest.ContainsKey(id))
                {
                    string href = manifest[id];
                    // Resolve relative path
                    string fullPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    _epubSpine.Add(fullPath);
                }
            }
        }

        private async Task LoadEpubChapterAsync(int index, bool fromEnd = false)
        {
            if (index < 0 || index >= _epubSpine.Count) return;

            try
            {
                // Show loading
                if (EpubFastNavOverlay != null) EpubFastNavOverlay.Visibility = Visibility.Visible;
                await Task.Delay(10); // UI yield

                string path = _epubSpine[index];
                var entry = _currentEpubArchive?.GetEntry(path);
                if (entry == null) return;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string html = await reader.ReadToEndAsync();

                // Convert HTML to Blocks and Images
                var pages = await RenderEpubPagesAsync(html, path);
                
                // Update FlipView
                EpubFlipView.ItemsSource = pages;
                
                if (fromEnd && pages.Count > 0)
                {
                    EpubFlipView.SelectedIndex = pages.Count - 1;
                }
                else
                {
                    EpubFlipView.SelectedIndex = 0;
                }
                
                UpdateEpubStatus(index + 1, _epubSpine.Count);
            }
            finally
            {
                if (EpubFastNavOverlay != null) EpubFastNavOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateEpubStatus(int current, int total)
        {
            if (ImageInfoText != null)
            {
                ImageInfoText.Text = $"Chapter {current} / {total}";
            }
        }

        // --- Core Rendering Logic ---

        // Helper for path resolution
        private string ResolveRelativePath(string baseXhtmlPath, string relativePath)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath)) return "";

                // Decoded URL chars (e.g. %20)
                relativePath = System.Net.WebUtility.UrlDecode(relativePath);

                // Handle absolute paths (starting with /) relative to epub root?? 
                // Usually in EPUB, / implies root of zip.
                if (relativePath.StartsWith("/"))
                {
                    return relativePath.TrimStart('/');
                }

                string baseDir = Path.GetDirectoryName(baseXhtmlPath)?.Replace("\\", "/") ?? "";
                
                // Combine
                string combined = string.IsNullOrEmpty(baseDir) 
                    ? relativePath 
                    : baseDir + "/" + relativePath;
                
                // Canonicalize (resolve .. and .)
                var parts = combined.Replace("\\", "/").Split('/');
                var stack = new Stack<string>();
                
                foreach (var part in parts)
                {
                    if (part == "." || string.IsNullOrEmpty(part)) continue;
                    if (part == "..")
                    {
                        if (stack.Count > 0) stack.Pop();
                    }
                    else
                    {
                        stack.Push(part);
                    }
                }
                
                var result = string.Join("/", stack.Reverse());
                return result;
            }
            catch
            {
                return relativePath;
            }
        }

        private async Task<List<UIElement>> RenderEpubPagesAsync(string html, string currentPath)
        {
            var pages = new List<UIElement>();
            
            _epubTextWidth = 40 * _epubFontSize; 
            
            // Regex to split by img/image tags
            // Use a capture group to keep the tag in the result array
            string pattern = @"(<(?:img|image)\b[^>]*>)"; 
            var segments = Regex.Split(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;

                // Check if segment is an image tag
                if (Regex.IsMatch(segment, @"^<(?:img|image)\b", RegexOptions.IgnoreCase))
                {
                    var imgPage = await CreateImagePageAsync(segment, currentPath);
                    if (imgPage != null) 
                    {
                        pages.Add(imgPage);
                    }
                    else
                    {
                        // Failed image load, create a placeholder text page? 
                        // Or just ignore.
                        System.Diagnostics.Debug.WriteLine($"Failed to load image from tag: {segment}");
                    }
                }
                else
                {
                    var textPages = CreateTextPages(segment);
                    pages.AddRange(textPages);
                }
            }

            return pages;
        }



        // Mouse/Touch Navigation Handler
        private void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;

             var ptr = e.GetCurrentPoint(RootGrid);
             var width = RootGrid.ActualWidth;
             
             if (ptr.Position.X < width / 2)
             {
                 _ = NavigateEpubAsync(-1);
             }
             else
             {
                 _ = NavigateEpubAsync(1);
             }
             
             e.Handled = true;
        }

        private async Task<UIElement?> CreateImagePageAsync(string imgTag, string currentPath)
        {
            // Extract src or xlink:href (for svg image tags)
            var match = Regex.Match(imgTag, "(?:src|xlink:href)=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            
            string src = match.Groups[1].Value;
            string fullPath = ResolveRelativePath(currentPath, src);
            
            var entry = FindEntryLoose(fullPath);
            if (entry == null) 
            {
                System.Diagnostics.Debug.WriteLine($"Image not found: {fullPath} (orig: {src})");
                return null;
            }

            try
            {
                using var stream = entry.Open();
                var mem = new MemoryStream();
                await stream.CopyToAsync(mem);
                mem.Position = 0;
                
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(mem.AsRandomAccessStream());
                
                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                // Use a Viewbox or just Grid to center
                var grid = new Grid { Background = new SolidColorBrush(Colors.Black) };
                grid.Children.Add(img);
                
                // Attach Navigation
                grid.PointerPressed += EpubPage_PointerPressed;
                
                return grid;
            }
            catch
            {
                return null;
            }
        }

        private ZipArchiveEntry? FindEntryLoose(string path)
        {
            // Try exact match
            var entry = _currentEpubArchive?.GetEntry(path);
            if (entry != null) return entry;
            
            // Try matching just filename (fallback) - dangerous but useful for flat structures
            string name = Path.GetFileName(path);
            return _currentEpubArchive?.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private List<UIElement> CreateTextPages(string htmlContent)
        {
            var blocks = ParseHtmlToBlocks(htmlContent);
            if (blocks.Count == 0) return new List<UIElement>();

            List<UIElement> textPages = new List<UIElement>();
            
            // Dynamic Chunking based on estimated length
            int maxCharsPerPage = 800;
            List<Block> currentBlocks = new List<Block>();
            int currentLength = 0;

            foreach (var block in blocks)
            {
                int blockLen = 100; // default for unknown block
                if (block is Paragraph p && p.Inlines.Count > 0)
                {
                     // Estimate length from inlines
                     blockLen = p.Inlines.Sum(i => i is Run r ? r.Text.Length : 10);
                }
                
                // If adding this block exceeds limit and we have content, flush
                if (currentLength + blockLen > maxCharsPerPage && currentBlocks.Count > 0)
                {
                     AddTextPage(textPages, currentBlocks);
                     currentBlocks.Clear();
                     currentLength = 0;
                }
                
                currentBlocks.Add(block);
                currentLength += blockLen;
            }
            
            // Flush remaining
            if (currentBlocks.Count > 0)
            {
                AddTextPage(textPages, currentBlocks);
            }
            
            return textPages;
        }

        private void AddTextPage(List<UIElement> pages, List<Block> pageBlocks)
        {
             var rtb = new RichTextBlock 
             { 
                 IsTextSelectionEnabled = false,
                 FontFamily = new FontFamily(_epubFontFamily),
                 FontSize = _epubFontSize,
                 Foreground = GetEpubThemeForeground(),
                 MaxWidth = Math.Min(40 * _epubFontSize, 1800), 
                 HorizontalAlignment = HorizontalAlignment.Center,
                 TextAlignment = TextAlignment.Left,
                 Padding = new Thickness(0, 0, 0, 100)
             };
             
             foreach (var b in pageBlocks) rtb.Blocks.Add(b);
             
             var scroll = new ScrollViewer 
             { 
                 VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                 HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                 Content = rtb
             };

             var grid = new Grid 
             { 
                  Background = GetEpubThemeBackground(), 
                 Padding = new Thickness(20, 20, 20, 20)
             };
             grid.Children.Add(scroll);
             
             // Attach Navigation (Use PointerReleased to allow potential drag/scroll detection if we wanted)
             // But for now sticking to PointerPressed for responsiveness as per request "left/right click" which implies press.
             // If scrolling is needed, this might block it. But with shorter pages, scrolling should be rare.
             grid.PointerPressed += EpubPage_PointerPressed;
             
             pages.Add(grid);
        }

        private List<Block> ParseHtmlToBlocks(string html)
        {
            var blocks = new List<Block>();
            
            // Cleanup
            html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            
            // Pre-process special tags
            html = html.Replace("<br/>", "\n").Replace("<br>", "\n");

            // --- Ruby Processing ---
            // Convert to {{RUBY|Base|Top}} using a more unique delimiter
            html = Regex.Replace(html, @"<ruby[^>]*>(.*?)<rt[^>]*>(.*?)</rt>.*?</ruby>", 
                m => 
                {
                    string baseText = m.Groups[1].Value; // Content inside ruby before rt
                    string rubyText = m.Groups[2].Value; // RT content
                    
                    // Handle <rb> tag if present
                    if (Regex.IsMatch(baseText, @"<rb[^>]*>", RegexOptions.IgnoreCase))
                    {
                        baseText = Regex.Replace(baseText, @"<rb[^>]*>(.*?)</rb>", "$1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    }

                    // Strip <rp>
                    baseText = Regex.Replace(baseText, @"<rp[^>]*>.*?</rp>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    
                    // Strip all other tags from base/ruby
                    baseText = Regex.Replace(baseText, @"<[^>]+>", ""); 
                    rubyText = Regex.Replace(rubyText, @"<[^>]+>", "");

                    return $"{{{{RUBY|{baseText.Trim()}|{rubyText.Trim()}}}}}";
                }, 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            // Strip remaining tags
            html = Regex.Replace(html, @"<[^>]+>", ""); 
            html = System.Net.WebUtility.HtmlDecode(html);
            
            var lines = html.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var p = new Paragraph();
                p.Margin = new Thickness(0, 0, 0, _epubFontSize);
                p.LineHeight = _epubFontSize * 1.6; // Better readability
                
                // Tokenize by custom Ruby marker
                // Regex must strictly match the output from the replacement above
                // We escape the pip for regex: \| and braces \{ \}
                var tokens = Regex.Split(line, @"(\{\{RUBY\|.*?\|.*?\}\})");
                
                foreach (var token in tokens)
                {
                    if (token.StartsWith("{{RUBY|"))
                    {
                        var content = token.Trim('{', '}'); // RUBY|Base|Text
                        var parts = content.Split('|');
                        if (parts.Length >= 3)
                        {
                            var baseText = parts[1];
                            var rubyText = parts[2];
                            p.Inlines.Add(CreateRuby(baseText, rubyText));
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(token))
                        {
                            p.Inlines.Add(new Run { Text = token });
                        }
                    }
                }
                
                blocks.Add(p);
            }
            
            return blocks;
        }

        private InlineUIContainer CreateRuby(string baseText, string rubyText)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ruby
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Base
            
            var rt = new TextBlock
            {
                Text = rubyText,
                FontSize = _epubFontSize * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, -5) // Tighten gap even more
            };
            
            var rb = new TextBlock
            {
                Text = baseText,
                FontSize = _epubFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                Margin = new Thickness(0, 4, 0, 0) 
            };
            
            Grid.SetRow(rt, 0);
            Grid.SetRow(rb, 1);
            
            grid.Children.Add(rt);
            grid.Children.Add(rb);
            
            // Use TranslateTransform to push the entire Ruby block down relative to the baseline
            // Push down slightly more to match baseline
            if (_epubFontFamily == "Yu Mincho")
            {
                grid.RenderTransform = new TranslateTransform { Y = _epubFontSize * 0.60 }; 
            }
            else
            {
                grid.RenderTransform = new TranslateTransform { Y = _epubFontSize * 0.30 }; 
            }

            return new InlineUIContainer { Child = grid };
        }

        private void EpubFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (!_isEpubMode) return;
             
             // Check bounds
             if (EpubFlipView.SelectedIndex == EpubFlipView.Items.Count - 1)
             {
                 // Last page, prepare to load next chapter?
                 // No, we wait for user to try to go NEXT.
                 // SelectionChanged happens AFTER move.
             }
        }
        
        // Navigation Handlers (Hooked from MainWindow.xaml.cs logic ideally, or replicated keys)
        // Since we are in Partial Class, we can handle keys if we route them.
        
        public async Task NavigateEpubAsync(int direction)
        {
            if (!_isEpubMode) return;
            
            int newIndex = EpubFlipView.SelectedIndex + direction;
            
            if (newIndex >= 0 && newIndex < EpubFlipView.Items.Count)
            {
                EpubFlipView.SelectedIndex = newIndex;
            }
            else
            {
                // Chapter Transition
                int nextChapter = _currentEpubChapterIndex + direction;
                if (nextChapter >= 0 && nextChapter < _epubSpine.Count)
                {
                    _currentEpubChapterIndex = nextChapter;
                    await LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: direction < 0);
                }
            }
        }

        // --- Epub Settings Logic ---

        private void LoadEpubSettings()
        {
            try
            {
                var settingsFile = GetEpubSettingsFilePath();
                if (System.IO.File.Exists(settingsFile))
                {
                    var json = System.IO.File.ReadAllText(settingsFile);
                    var settings = System.Text.Json.JsonSerializer.Deserialize(json, typeof(EpubSettings), EpubSettingsContext.Default) as EpubSettings;
                    
                    if (settings != null)
                    {
                        _epubFontSize = settings.FontSize;
                        _epubFontFamily = settings.FontFamily;
                        _epubThemeIndex = settings.ThemeIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading epub settings: {ex.Message}");
            }

            if (_epubFontSize < 8) _epubFontSize = 8;
            if (_epubFontSize > 72) _epubFontSize = 72;
            
            UpdateEpubToolbarUI();
        }

        private void SaveEpubSettings()
        {
            try
            {
                var settings = new EpubSettings
                {
                    FontSize = _epubFontSize,
                    FontFamily = _epubFontFamily,
                    ThemeIndex = _epubThemeIndex
                };
                
                var settingsFile = GetEpubSettingsFilePath();
                var settingsDir = System.IO.Path.GetDirectoryName(settingsFile);
                if (settingsDir != null && !System.IO.Directory.Exists(settingsDir))
                {
                    System.IO.Directory.CreateDirectory(settingsDir);
                }
                
                var json = System.Text.Json.JsonSerializer.Serialize(settings, typeof(EpubSettings), EpubSettingsContext.Default);
                System.IO.File.WriteAllText(settingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving epub settings: {ex.Message}");
            }
        }

        private void UpdateEpubToolbarUI()
        {
            if (TextSizeLevelText != null)
            {
                TextSizeLevelText.Text = _epubFontSize.ToString();
            }
        }

        public void ToggleEpubFont()
        {
            if (_epubFontFamily == "Yu Gothic Medium")
                _epubFontFamily = "Yu Mincho";
            else
                _epubFontFamily = "Yu Gothic Medium";
            
            SaveEpubSettings();
            UpdateEpubVisuals();
        }

        public void ToggleEpubTheme()
        {
            _epubThemeIndex = (_epubThemeIndex + 1) % 3;
            SaveEpubSettings();
            UpdateEpubVisuals();
        }

        public void IncreaseEpubSize()
        {
            _epubFontSize += 2;
            if (_epubFontSize > 72) _epubFontSize = 72;
            SaveEpubSettings();
            UpdateEpubToolbarUI();
            
            // For font size change, we usually need to re-pagination because it affects how much text fits per page.
            // Just updating visuals isn't enough as content flows differently.
            // So we reload the chapter.
             _ = LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: false); // Maintain page? Hard with reflow.
        }

        public void DecreaseEpubSize()
        {
            _epubFontSize -= 2;
            if (_epubFontSize < 8) _epubFontSize = 8;
            SaveEpubSettings();
            UpdateEpubToolbarUI();
            _ = LoadEpubChapterAsync(_currentEpubChapterIndex, fromEnd: false);
        }

        private void UpdateEpubVisuals()
        {
            var bg = GetEpubThemeBackground();
            var fg = GetEpubThemeForeground();
            
            if (EpubArea != null) EpubArea.Background = bg;
            if (StatusBarGrid != null) StatusBarGrid.Background = bg;

            if (EpubFlipView != null)
            {
                foreach (var item in EpubFlipView.Items)
                {
                    if (item is Grid pageGrid)
                    {
                        pageGrid.Background = bg;
                        
                        // Find child... deeper
                        if (pageGrid.Children.Count > 0 && pageGrid.Children[0] is ScrollViewer scroll)
                        {
                            if (scroll.Content is RichTextBlock rtb)
                            {
                                rtb.Foreground = fg;
                                rtb.FontFamily = new FontFamily(_epubFontFamily);
                                
                                // Also update Rubies inside blocks if possible? 
                                // Traversing Blocks is hard.
                                // Actually, since we generated rubies as InlineUIContainers containing Grids and TextBlocks,
                                // we might need to drill down or just rely on Inheritance?
                                // TextBlock Foreground inherits if not set? 
                                // In CreateRuby, we EXPLICITLY set Foreground. So we need to update it.
                                
                                foreach (var block in rtb.Blocks)
                                {
                                    if (block is Paragraph p)
                                    {
                                        foreach (var inline in p.Inlines)
                                        {
                                            if (inline is InlineUIContainer container && container.Child is Grid rGrid)
                                            {
                                                 foreach (var child in rGrid.Children)
                                                 {
                                                     if (child is TextBlock rubytb)
                                                     {
                                                         rubytb.Foreground = fg;
                                                     }
                                                 }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private Brush GetEpubThemeForeground()
        {
            if (_epubThemeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            return new SolidColorBrush(Colors.Black);
        }
        
        private Brush GetEpubThemeBackground()
        {
             if (_epubThemeIndex == 0) return new SolidColorBrush(Colors.White);
             if (_epubThemeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
             return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
        }
    }
}
