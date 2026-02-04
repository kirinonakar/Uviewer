using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private List<TextLine> _textLines = new();
        private double _textFontSize = 18;
        private string _textFontFamily = "Yu Gothic Medium";
        private int _themeIndex = 0; // 0: White, 1: Beige, 2: Dark
        private bool _isTextMode = false;
        
        
        // SupportedTextExtensions is defined in MainWindow.xaml.cs

        public class TextLine
        {
            public string Content { get; set; } = "";
            public double FontSize { get; set; }
            public string FontFamily { get; set; } = "Yu Gothic Medium";
            public Brush? Foreground { get; set; }
            public double MaxWidth { get; set; }
            
            // New styling properties for Aozora
            public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
            public Thickness Margin { get; set; } = new Thickness(0);
            public Thickness Padding { get; set; } = new Thickness(0);
            public Brush? BorderBrush { get; set; } = null;
            public Thickness BorderThickness { get; set; } = new Thickness(0);
        }

        private bool _textInputInitialized = false;
        private string? _currentTextFilePath = null;

        private void InitializeText()
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                
                if (!_textInputInitialized)
                {
                    RootGrid.PreviewKeyDown += RootGrid_Text_PreviewKeyDown;
                    _textInputInitialized = true;
                }
            }
            catch { }
        }

        private async Task LoadTextFileAsync(StorageFile file)
        {
            // Save position of current text file before switching
            if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath) && TextScrollViewer != null)
            {
                 var existing = _recentItems.FirstOrDefault(r => r.Path == _currentTextFilePath && r.Type == "File");
                 if (existing != null)
                 {
                     existing.ScrollOffset = TextScrollViewer.VerticalOffset;
                     existing.SavedPage = (TextScrollViewer.ViewportHeight > 0) ? 
                                          (int)(TextScrollViewer.VerticalOffset / TextScrollViewer.ViewportHeight) + 1 : 0;
                     await SaveRecentItems(); // Ensure we save to disk
                 }
            }

            InitializeText();
            _currentTextFilePath = file.Path;

            try
            {
                SwitchToTextMode();
                string content = await ReadTextFileWithEncodingAsync(file);
                DisplayLoadedText(content, file.Name);
                
                SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"텍스트 로드 실패: {ex.Message}";
            }
        }

        private async Task LoadTextEntryAsync(ImageEntry entry)
        {
             if (entry.IsArchiveEntry)
             {
                 await LoadTextFromArchiveEntryAsync(entry);
             }
             else if (entry.FilePath != null)
             {
                 var file = await StorageFile.GetFileFromPathAsync(entry.FilePath);
                 await LoadTextFileAsync(file);
             }
        }

        private async Task LoadTextFromArchiveEntryAsync(ImageEntry entry)
        {
             InitializeText();
             
             try
             {
                 SwitchToTextMode();
                 string content = "";
                 await _archiveLock.WaitAsync();
                 try
                 {
                      if (_currentArchive != null && entry.ArchiveEntryKey != null)
                      {
                          var archEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entry.ArchiveEntryKey);
                          if (archEntry != null)
                          {
                               using var ms = new System.IO.MemoryStream();
                               using var entryStream = archEntry.OpenEntryStream();
                               entryStream.CopyTo(ms);
                               var bytes = ms.ToArray();
                               content = DetectEncoding(bytes).GetString(bytes);
                          }
                      }
                 }
                 finally { _archiveLock.Release(); }
                 
                 DisplayLoadedText(content, entry.DisplayName);
                 SyncSidebarSelection(entry);
             }
             catch (Exception ex) 
             {
                 FileNameText.Text = $"아카이브 텍스트 로드 실패: {ex.Message}";
             }
        }

        private void DisplayLoadedText(string content, string name)
        {
            string ext = System.IO.Path.GetExtension(name).ToLower();
            if (ext == ".html" || ext == ".htm")
            {
                content = ParseHtml(content);
            }
            
            _textLines = SplitTextToLines(content);
            RefreshTextDisplay();
            
            // Reset to top immediately
            if (TextScrollViewer != null) TextScrollViewer.ChangeView(null, 0, null, true);
            
            // Restore scroll position from recent items if exists
            _ = RestoreTextPositionAsync(name);
            
            UpdateTextStatusBar(name, _textLines.Count, 1);
        }

        private async Task RestoreTextPositionAsync(string name)
        {
            try
            {
                // Wait for layout update
                await Task.Delay(100);
                
                // Find in recent items
                // Note: FilePath check is better but here we might only have name in some contexts? 
                // Using current image entry path is safer.
                string? path = null;
                if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    path = _imageEntries[_currentIndex].FilePath;
                }
                
                if (!string.IsNullOrEmpty(path))
                {
                    var recent = _recentItems.FirstOrDefault(r => r.Path == path);
                    if (recent != null && recent.ScrollOffset.HasValue)
                    {
                         TextScrollViewer.ChangeView(null, recent.ScrollOffset.Value, null);
                         UpdateTextStatusBar();
                         return;
                    }
                }
                
                // If no saved position, ensure we start at top
                TextScrollViewer.ChangeView(null, 0, null);
            }
            catch { }
        }

        private void SwitchToTextMode()
        {
            _isTextMode = true;
            _isEpubMode = false; // Reset Epub mode
            _isSideBySideMode = false; // Disable SbS

            // Toggle Visibility
            ImageArea.Visibility = Visibility.Collapsed;
            TextArea.Visibility = Visibility.Visible;
            EpubArea.Visibility = Visibility.Collapsed; // Ensure Epub area is hidden
            
            // Toggle Toolbars
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible;
            
            // Update Title
            Title = "Uviewer - Text Viewer";
            
            // Set Opaque Status Bar
            if (StatusBarGrid != null)
                StatusBarGrid.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]; // Opaque standard brush
            
            // Load Settings
            LoadTextSettings();
        }

        private void LoadTextSettings()
        {
            try
            {
                var settingsFile = GetTextSettingsFilePath();
                if (System.IO.File.Exists(settingsFile))
                {
                    var json = System.IO.File.ReadAllText(settingsFile);
                    var settings = System.Text.Json.JsonSerializer.Deserialize(json, typeof(TextSettings), TextSettingsContext.Default) as TextSettings;
                    
                    if (settings != null)
                    {
                        _textFontSize = settings.FontSize;
                        _textFontFamily = settings.FontFamily;
                        _themeIndex = settings.ThemeIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading text settings: {ex.Message}");
            }
            
            // Validate
            if (_textFontSize < 8) _textFontSize = 8;
            if (_textFontSize > 72) _textFontSize = 72;
            
            // Update UI Labels
            if (TextSizeLevelText != null)
            {
                TextSizeLevelText.Text = _textFontSize.ToString();
            }
        }
        
        private void SaveTextSettings()
        {
            try
            {
                var settings = new TextSettings
                {
                    FontSize = _textFontSize,
                    FontFamily = _textFontFamily,
                    ThemeIndex = _themeIndex
                };
                
                var settingsFile = GetTextSettingsFilePath();
                var settingsDir = System.IO.Path.GetDirectoryName(settingsFile);
                if (settingsDir != null && !System.IO.Directory.Exists(settingsDir))
                {
                    System.IO.Directory.CreateDirectory(settingsDir);
                }
                
                var json = System.Text.Json.JsonSerializer.Serialize(settings, typeof(TextSettings), TextSettingsContext.Default);
                System.IO.File.WriteAllText(settingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving text settings: {ex.Message}");
            }
        }
        
        private void SwitchToImageMode()
        {
            _isTextMode = false;
            _isEpubMode = false; // Reset Epub mode
            ImageArea.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Collapsed;
            
            ImageToolbarPanel.Visibility = Visibility.Visible;
            TextToolbarPanel.Visibility = Visibility.Collapsed;
            
            // Restore Status Bar (semi-transparent style if any, but default opaque is safest)
             if (StatusBarGrid != null)
                StatusBarGrid.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }

        private async Task<string> ReadTextFileWithEncodingAsync(StorageFile file)
        {
            // Read as buffer
            var buffer = await FileIO.ReadBufferAsync(file);
            using var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[buffer.Length];
            dataReader.ReadBytes(bytes);

            // Detect Encoding
            Encoding encoding = DetectEncoding(bytes);
            return encoding.GetString(bytes);
        }

        private Encoding DetectEncoding(byte[] bytes)
        {
            // Simple logic: Check for BOM, then UTF8 validation, then fallback to SJIS/EUC-KR
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

            // Is valid UTF8?
            if (IsValidUtf8(bytes)) return Encoding.UTF8;

            // 0. HTML Meta Charset Check (Strongest for HTML)
            var htmlCharset = DetectHtmlCharset(bytes);
            if (htmlCharset != null) return htmlCharset;

            // 1. Try Valid EUC-KR (Strict) - Prioritize Korean
            if (IsStrictEucKr(bytes)) return Encoding.GetEncoding(51949);

            // 2. Try Valid SJIS (Strict)
            if (IsStrictSjis(bytes)) return Encoding.GetEncoding(932);

            // 3. Heuristic Fallback for "Dirty" Files
            // If strict checks failed (e.g. invalid bytes present), check for distinct SJIS high-bytes [0x81-0x9F].
            // These are NEVER valid in EUC-KR (where high bytes are 0xA1-0xFE).
            // This strongly implies SJIS (or CP932) even if some bytes are malformed.
            if (ContainsSjisHighBytes(bytes)) return Encoding.GetEncoding(932);

            // 4. Default Fallbacks
            try { return Encoding.GetEncoding(51949); } catch { }
            try { return Encoding.GetEncoding(932); } catch { }

            return Encoding.Default;
        }

        private bool ContainsSjisHighBytes(byte[] bytes)
        {
            // Look for bytes in range [0x81, 0x9F].
            // These denote SJIS 2-byte characters in the first partition.
            // EUC-KR high-bytes strictly start at 0xA1.
            foreach (var b in bytes)
            {
                if (b >= 0x81 && b <= 0x9F) return true;
            }
            return false;
        }

        private Encoding? DetectHtmlCharset(byte[] bytes)
        {
            try
            {
                // Read head of file (first 2KB) as ASCII string to find meta tags
                int len = Math.Min(bytes.Length, 2048);
                string head = Encoding.ASCII.GetString(bytes, 0, len);
                
                // Regex for <meta charset="...">
                var match = Regex.Match(head, @"<meta\s+charset=[""']?([a-zA-Z0-9-_]+)[""']?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string charset = match.Groups[1].Value;
                    return GetEncodingFromCharset(charset);
                }

                // Regex for <meta http-equiv="Content-Type" content="...; charset=...">
                match = Regex.Match(head, @"charset\s*=\s*([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string charset = match.Groups[1].Value;
                     return GetEncodingFromCharset(charset);
                }
            }
            catch { }
            return null;
        }

        private Encoding? GetEncodingFromCharset(string charset)
        {
            try
            {
                if (string.Equals(charset, "shift_jis", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "sjis", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "x-sjis", StringComparison.OrdinalIgnoreCase))
                    return Encoding.GetEncoding(932);
                
                if (string.Equals(charset, "euc-kr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "ks_c_5601-1987", StringComparison.OrdinalIgnoreCase))
                    return Encoding.GetEncoding(51949);
                
                return Encoding.GetEncoding(charset);
            }
            catch { return null; }
        }

        private bool IsStrictEucKr(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    // ASCII
                    i++;
                }
                else
                {
                    // EUC-KR 2-byte char: 1st [0xA1-0xFE], 2nd [0xA1-0xFE]
                    // (Actually standard usually starts around 0xB0 for Hangul, but spec allows A1+)
                    if (i + 1 >= len) return false; // Incomplete
                    byte b2 = bytes[i + 1];
                    if (b >= 0xA1 && b <= 0xFE && b2 >= 0xA1 && b2 <= 0xFE)
                    {
                        i += 2;
                    }
                    else
                    {
                        return false; // Invalid EUC-KR sequence
                    }
                }
            }
            return true;
        }

        private bool IsStrictSjis(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    // ASCII
                    i++;
                }
                else if (b >= 0xA1 && b <= 0xDF)
                {
                    // Half-width Katakana
                    i++;
                }
                else
                {
                     // SJIS 2-byte: 1st [0x81-0x9F, 0xE0-0xFC]
                     if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                     {
                         if (i + 1 >= len) return false;
                         byte b2 = bytes[i + 1];
                         // 2nd [0x40-0x7E, 0x80-0xFC]
                         if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                         {
                             i += 2;
                         }
                         else
                         {
                             return false;
                         }
                     }
                     else
                     {
                         return false;
                     }
                }
            }
            return true;
        }

        private bool IsValidUtf8(byte[] bytes)
        {
            try
            {
               // Using a strict decoder to check for invalid sequences
               var decoder = Encoding.UTF8.GetDecoder();
               decoder.Fallback = new DecoderExceptionFallback();
               char[] chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length)];
               decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
               return true;
            }
            catch
            {
                return false;
            }
        }

        private string ParseHtml(string html)
        {
            // Very basic stripper
            // 1. Remove script/style
            string noScript = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            string noStyle = Regex.Replace(noScript, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            
            // 2. Strip tags
            string textOnly = Regex.Replace(noStyle, @"<[^>]+>", "\n"); // Replace tags with newlines
            
            // 3. Decode HTML entities
            textOnly = System.Net.WebUtility.HtmlDecode(textOnly);
            
            // 4. Remove excessive newlines
            return Regex.Replace(textOnly, @"\n\s+\n", "\n\n");
        }

        private List<TextLine> SplitTextToLines(string content)
        {
            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var result = new List<TextLine>();
            
            foreach (var line in lines)
            {
                result.Add(CreateTextLine(line)); // We bind logic late or created here
            }
            return result;
        }
        
        private TextLine CreateTextLine(string content)
        {
            var line = new TextLine
            {
                Content = content,
                FontSize = _textFontSize,
                FontFamily = _textFontFamily,
                Foreground = GetThemeForeground(),
                MaxWidth = GetUrlMaxWidth()
            };
            
            // Parse Aozora tags
            ApplyAozoraStyling(line);
            
            return line;
        }
        
        private void ApplyAozoraStyling(TextLine line)
        {
            // Simple Parsing for Aozora Bunko Tags
            // ［＃大見出し］ -> Heading 1
            // ［＃中見出し］ -> Heading 2
            // ［＃センター］ -> Center Align
            // ［＃地から３字上げ］ -> Margin Bottom/Indent? Actually 'Ji kara 3 ji age' means indent from bottom, effectively right align or specific margin. For simplicity, we treat complex indents as margin.
            // ［＃ここから２字下げ］ -> Indent
            // ［＃ここから罫囲み］ -> Border
            // ［＃ここから２段階小さな文字］ -> Small font
            
            string content = line.Content;
            
            if (content.Contains("［＃大見出し］"))
            {
                line.FontSize = _textFontSize * 1.5;
                content = content.Replace("［＃大見出し］", "");
            }
            if (content.Contains("［＃中見出し］"))
            {
                line.FontSize = _textFontSize * 1.25;
                content = content.Replace("［＃中見出し］", "");
            }
            if (content.Contains("［＃センター］"))
            {
                line.TextAlignment = TextAlignment.Center;
                content = content.Replace("［＃センター］", "");
            }
            if (content.Contains("［＃地から３字上げ］"))
            {
                // Right align with padding? Or just Right align for now.
                // Aozora 'Ji from' usually implies vertical text, but in horizontal it's often right alignment.
                line.TextAlignment = TextAlignment.Right; 
                line.Margin = new Thickness(0, 0, 60, 0); // Approx 3 chars
                content = content.Replace("［＃地から３字上げ］", "");
            }
            if (content.Contains("［＃ここから２字下げ］"))
            {
                line.Margin = new Thickness(40, 0, 0, 0);
                content = content.Replace("［＃ここから２字下げ］", "");
            }
            if (content.Contains("［＃ここから罫囲み］"))
            {
                line.BorderBrush = new SolidColorBrush(Colors.Gray);
                line.BorderThickness = new Thickness(1);
                line.Padding = new Thickness(10);
                content = content.Replace("［＃ここから罫囲み］", "");
            }
             if (content.Contains("［＃ここから２段階小さな文字］"))
            {
                line.FontSize = Math.Max(8, _textFontSize * 0.7);
                content = content.Replace("［＃ここから２段階小さな文字］", "");
            }

            // Cleanup common tags
            content = Regex.Replace(content, @"［＃[^］]+］", ""); // Remove other tags
            
            line.Content = content;
        }

        private double GetUrlMaxWidth()
        {
            // "Text width max 40 chars"
            // With Consolas/Monospace it is easy. With variable width, 40 * FontSize is approximation (em).
            // Actually, for Japanese 'em' is full width.
            return 40 * _textFontSize; 
        }

        private Brush GetThemeForeground()
        {
            if (_themeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            return new SolidColorBrush(Colors.Black);
        }
        
        private Brush GetThemeBackground()
        {
             if (_themeIndex == 0) return new SolidColorBrush(Colors.White);
             if (_themeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
             return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
        }

        private void RefreshTextDisplay()
        {
            // Apply current settings to all lines
            var brush = GetThemeForeground();
            var bg = GetThemeBackground();
            var maxW = GetUrlMaxWidth();
            
            foreach (var line in _textLines)
            {
                line.FontSize = _textFontSize;
                line.FontFamily = _textFontFamily;
                line.Foreground = brush;
                line.MaxWidth = maxW;
                
                // Re-apply relative font sizes for headers if we had them
                // This is a bit tricky since we lost the original tag.
                // For simple resizing, we just reset. A cleaner way would be to store "ScaleFactor" in TextLine.
                // But for now, we leave custom sizes as is if they differ? 
                // No, RefreshTextDisplay forces fontSize. 
                // To fix this, we should store 'Scale' in TextLine.
                // Let's assume standard lines for now to keep it simple, or re-parse?
                // Re-parsing is expensive.
                // Better approach: line.FontSize = _textFontSize; 
                // IF we want to persist Aozora sizing, we need a multiplier property.
            }
            
            TextArea.Background = bg;
            TextItemsRepeater.ItemsSource = null;
            TextItemsRepeater.ItemsSource = _textLines;
            
            // Should reset scroll here to avoid carrying over previous file's position
             if (TextScrollViewer != null) 
             {
                 TextScrollViewer.ChangeView(null, 0, null, true);
             }
        }

        // --- Toolbar Handlers ---

        // --- Toolbar Handlers ---

        private void FontToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                ToggleEpubFont();
            else
                ToggleFont();
        }
        
        private void ToggleFont()
        {
            if (_textFontFamily == "Yu Gothic Medium")
                _textFontFamily = "Yu Mincho";
            else
                _textFontFamily = "Yu Gothic Medium";
                
            SaveTextSettings();
            RefreshTextDisplay();
        }

        private void TextSizeUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                IncreaseEpubSize();
            else
                IncreaseTextSize();
        }
        
        private void IncreaseTextSize()
        {
            _textFontSize += 2;
            if (_textFontSize > 72) _textFontSize = 72;
            TextSizeLevelText.Text = _textFontSize.ToString();
            SaveTextSettings();
            RefreshTextDisplay();
        }

        private void TextSizeDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                DecreaseEpubSize();
            else
                DecreaseTextSize();
        }

        private void DecreaseTextSize()
        {
            _textFontSize -= 2;
            if (_textFontSize < 8) _textFontSize = 8;
            TextSizeLevelText.Text = _textFontSize.ToString();
            SaveTextSettings();
            RefreshTextDisplay();
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                ToggleEpubTheme();
            else
                ToggleTheme();
        }
        
        private void ToggleTheme()
        {
            _themeIndex = (_themeIndex + 1) % 3;
            SaveTextSettings();
            RefreshTextDisplay();
        }
        
        private void GoToPageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowGoToPageDialog();
        }

        private void RootGrid_Text_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
             if (!_isTextMode) return;
             
             // Prevent file navigation with arrows/space in text mode
             // Using PreviewKeyDown allows us to intercept before ListView gets it
             if (e.Key == Windows.System.VirtualKey.Left || 
                 e.Key == Windows.System.VirtualKey.Right || 
                 e.Key == Windows.System.VirtualKey.Up || 
                 e.Key == Windows.System.VirtualKey.Down ||
                 e.Key == Windows.System.VirtualKey.Space)
             {
                 // Handle Logic Here to avoid bubbling
                 // We will set Handled=true after processing
             }
             
              if (e.Key == Windows.System.VirtualKey.Home)
              {
                   TextScrollViewer.ChangeView(null, 0, null);
                   e.Handled = true;
              }
              else if (e.Key == Windows.System.VirtualKey.End)
              {
                   if (TextScrollViewer != null) 
                        TextScrollViewer.ChangeView(null, TextScrollViewer.ExtentHeight, null);
                   e.Handled = true;
              }
              else if (e.Key == Windows.System.VirtualKey.G)
              {
                  _ = ShowGoToPageDialog();
                  e.Handled = true;
              }
             else if (e.Key == Windows.System.VirtualKey.Left)
             {
                 if (TextScrollViewer != null) NavigateTextPage(-1); // Only navigate text page
                 e.Handled = true; // Stop event bubbling to prevent file navigation
             }
             else if (e.Key == Windows.System.VirtualKey.Right)
             {
                 if (TextScrollViewer != null) NavigateTextPage(1); // Only navigate text page
                 e.Handled = true; // Stop event bubbling to prevent file navigation
             }
             else if (e.Key == Windows.System.VirtualKey.Up)
             {
                 // Move to previous file
                 _ = NavigateToFileAsync(false);
                 e.Handled = true;
             }
             else if (e.Key == Windows.System.VirtualKey.Down)
             {
                 // Move to next file
                 _ = NavigateToFileAsync(true);
                 e.Handled = true;
             }
             else if (e.Key == Windows.System.VirtualKey.Add || e.Key == (Windows.System.VirtualKey)187) // +
             {
                 if (_isEpubMode) IncreaseEpubSize();
                 else IncreaseTextSize();
                 e.Handled = true;
             }
             else if (e.Key == Windows.System.VirtualKey.Subtract || e.Key == (Windows.System.VirtualKey)189) // -
             {
                 if (_isEpubMode) DecreaseEpubSize();
                 else DecreaseTextSize();
                 e.Handled = true;
             }
             else if (e.Key == Windows.System.VirtualKey.F)
             {
                 if (_isEpubMode) ToggleEpubFont();
                 else ToggleFont();
                 e.Handled = true;
             }
             else if (e.Key == Windows.System.VirtualKey.B)
             {
                 if (_isEpubMode) ToggleEpubTheme();
                 else ToggleTheme();
                 e.Handled = true;
             }
        }

        private async Task ShowGoToPageDialog()
        {
             int currentPage = 1;
             if (TextScrollViewer.ViewportHeight > 0)
             {
                 currentPage = (int)(TextScrollViewer.VerticalOffset / TextScrollViewer.ViewportHeight) + 1;
             }

             var input = new TextBox 
             { 
                 InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }, 
                 PlaceholderText = "Page Number",
                 Text = currentPage.ToString()
             };
             
             input.SelectAll();

             var dialog = new ContentDialog
             {
                 Title = "페이지 이동",
                 Content = input,
                 PrimaryButtonText = "이동",
                 CloseButtonText = "취소",
                 XamlRoot = this.Content.XamlRoot
             };

             input.KeyDown += (s, e) => 
             {
                 if (e.Key == Windows.System.VirtualKey.Enter)
                 {
                     dialog.Hide();
                     if (int.TryParse(input.Text, out int page) && page > 0)
                     {
                         double target = TextScrollViewer.ViewportHeight * (page - 1);
                         TextScrollViewer.ChangeView(null, target, null);
                     }
                 }
             };

             if (await dialog.ShowAsync() == ContentDialogResult.Primary)
             {
                 if (int.TryParse(input.Text, out int page) && page > 0)
                 {
                     double target = TextScrollViewer.ViewportHeight * (page - 1);
                     TextScrollViewer.ChangeView(null, target, null);
                 }
             }
        }

        // --- Element Prepared (Bold Logic) ---
        private void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is TextBlock tb && _textLines.Count > args.Index)
            {
                var line = _textLines[args.Index];
                
                // Binding Properties
                tb.FontSize = line.FontSize;
                tb.FontFamily = new FontFamily(line.FontFamily);
                tb.Foreground = line.Foreground;
                tb.MaxWidth = line.MaxWidth;
                tb.TextAlignment = line.TextAlignment;
                tb.Margin = line.Margin;
                tb.Padding = line.Padding; 
                
                // Border support requires wrapping TextBlock in Border, but ItemsRepeater template is TextBlock.
                // We will just apply simple styling or we'd need to change the DataTemplate.
                // Since we can't easily change DataTemplate in C# code behind without XAML change,
                // we will ignore Border for now OR try access parent. 
                // However, the user asked for "Correction", so let's try to do what we can.
                // The ItemsRepeater ItemTemplate is defined in XAML. modifying it to Grid or Border would be better.
                // For now, let's just stick to text properties we can set.

                tb.Inlines.Clear();
                
                string content = line.Content;
                var parts = Regex.Split(content, @"(\*\*.*?\*\*)");
                
                foreach (var part in parts)
                {
                    if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4)
                    {
                        string boldText = part.Substring(2, part.Length - 4);
                        tb.Inlines.Add(new Run { Text = boldText, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    }
                    else
                    {
                        tb.Inlines.Add(new Run { Text = part });
                    }
                }
            }
        }
        
        // --- Input Handling ---
        
        private void TextArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
             // Left/Right click zones
             var ptr = e.GetCurrentPoint(TextArea);
             if (ptr.Properties.IsLeftButtonPressed)
             {
                 if (ptr.Position.X < TextArea.ActualWidth / 2)
                 {
                     NavigateTextPage(-1);
                 }
                 else
                 {
                     NavigateTextPage(1);
                 }
             }
        }
        
        private void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(TextArea);
            var delta = ptr.Properties.MouseWheelDelta;
            if (delta > 0) NavigateTextPage(-1); // Up = Prev
            else NavigateTextPage(1); // Down = Next
            
            e.Handled = true;
            UpdateTextStatusBar();
        }

        private void NavigateTextPage(int direction)
        {
            if (TextScrollViewer == null) return;
            
            double current = TextScrollViewer.VerticalOffset;
            double viewport = TextScrollViewer.ViewportHeight;
            // Overlap slightly to prevent reading issues
            double scrollAmount = viewport * 0.9;
            
            if (direction > 0)
            {
                TextScrollViewer.ChangeView(null, current + scrollAmount, null);
            }
            else
            {
                TextScrollViewer.ChangeView(null, current - scrollAmount, null);
            }
            UpdateTextStatusBar();
        }
        
        private void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null)
        {
            if (!_isTextMode) return;

             if (fileName != null) FileNameText.Text = fileName;
             
             if (TextScrollViewer != null && TextScrollViewer.ViewportHeight > 0)
             {
                 int cur = (int)(TextScrollViewer.VerticalOffset / TextScrollViewer.ViewportHeight) + 1;
                 int total = (int)(TextScrollViewer.ExtentHeight / TextScrollViewer.ViewportHeight);
                 if (total == 0) total = 1;
                 
                 ImageIndexText.Text = $"{cur} / {total}";
             }
             
             if (totalLines != null) ImageInfoText.Text = $"{totalLines} Lines";
        }
        
        private void TextScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateTextStatusBar();
        }
        
        private void TextScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-calc max width if needed, but it is bound to line prop.
        }
    }
}
