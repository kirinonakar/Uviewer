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
using Windows.Foundation;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private List<TextLine> _textLines = new();
        private string _currentTextContent = ""; // Stores raw text for mode switching
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
                     existing.SavedLine = GetTopVisibleLineIndex();
                     await SaveRecentItems(); // Ensure we save to disk
                 }
            }

            InitializeText();
            _currentTextFilePath = file.Path;

            try
            {
                SwitchToTextMode();
                string content = await ReadTextFileWithEncodingAsync(file);
                await DisplayLoadedText(content, file.Name);
                
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
                 
                 await DisplayLoadedText(content, entry.DisplayName);
                 SyncSidebarSelection(entry);
             }
             catch (Exception ex) 
             {
                 FileNameText.Text = $"아카이브 텍스트 로드 실패: {ex.Message}";
             }
        }

        private async Task DisplayLoadedText(string content, string name)
        {
            _currentTextContent = content; // Save for reload

            string ext = System.IO.Path.GetExtension(name).ToLower();
            if (ext == ".html" || ext == ".htm")
            {
                content = ParseHtml(content);
                _currentTextContent = content;
            }
            
            // Unified Target Line Logic
            int targetLine = 1;
            if (_aozoraPendingTargetLine != 1)
            {
                targetLine = _aozoraPendingTargetLine;
                _aozoraPendingTargetLine = 1; // Reset
            }
            else
            {
                // Fallback to automatic restoration from recent items
                targetLine = GetSavedStartLine(name);
            }

            if (_isAozoraMode)
            {
                // Use page-based container display with target line restoration
                await PrepareAozoraDisplayAsync(content, targetLine);
                
                // Show Container, hide ScrollViewer
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Visible;
                
                FileNameText.Text = name;
            }
            else
            {
                // Show ScrollViewer, hide Container
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                if (AozoraPageContainer != null) AozoraPageContainer.Visibility = Visibility.Collapsed;
                
                // Ensure default template
                if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                {
                     TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                }
                
                _textLines = SplitTextToLines(content);
                await RefreshTextDisplay(true); // Reset scroll for new file
                
                // Reset to top immediately
                if (TextScrollViewer != null) TextScrollViewer.ChangeView(null, 0, null, true);
                
                // Update Text Status
                UpdateTextStatusBar(name, _textLines.Count, 1);
                
                // Unified Scroll Restoration for General Text
                if (targetLine > 1)
                {
                    // Wait slightly for layout
                    await Task.Delay(50);
                    ScrollToLine(targetLine);
                    UpdateTextStatusBar();
                }
                else 
                {
                     // Restore scroll position from recent items if exists (Legacy Offset)
                    _ = RestoreTextPositionAsync(name);
                }
            }
        }

        private int GetSavedStartLine(string name)
        {
            try
            {
                // Find in recent items by name (simple fallback) or path
                var recent = _recentItems.OrderByDescending(r => r.AccessedAt)
                                         .FirstOrDefault(r => r.Name == name || r.Path == _currentTextFilePath);
                
                if (recent != null)
                {
                    if (recent.SavedLine > 1) return recent.SavedLine;
                    if (recent.SavedPage > 0) return -recent.SavedPage; // Legacy support
                    return 1;
                }

                // If not in recent, check favorites
                var favorite = _favorites.FirstOrDefault(f => f.Name == name || f.Path == _currentTextFilePath);
                if (favorite != null)
                {
                    if (favorite.SavedLine > 1) return favorite.SavedLine;
                    if (favorite.SavedPage > 0) return -favorite.SavedPage;
                }
            }
            catch { }
            return 1; // Default to start
        }

        private async Task RestoreTextPositionAsync(string name)
        {
            // For Aozora mode, position is now restored progressively via targetLine in PrepareAozoraDisplayAsync
            if (_isAozoraMode) return;

            try
            {
                // Wait for layout update for normal mode
                await Task.Delay(100);
                
                if (TextScrollViewer != null)
                {
                    var recent = _recentItems.OrderByDescending(r => r.AccessedAt).FirstOrDefault(r => r.Name == name);
                    if (recent != null)
                    {
                        if (recent.SavedLine > 1)
                        {
                            ScrollToLine(recent.SavedLine);
                        }
                        else if (recent.ScrollOffset.HasValue)
                        {
                            TextScrollViewer.ChangeView(null, recent.ScrollOffset.Value, null);
                        }
                        else
                        {
                            TextScrollViewer.ChangeView(null, 0, null);
                        }
                    }
                }
                UpdateTextStatusBar();
            }
            catch { }
        }

        private void SwitchToTextMode()
        {
            _isTextMode = true;
            _isEpubMode = false; // Reset Epub mode
            _isSideBySideMode = false; // Disable SbS

            // Toggle Visibility
            if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
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
            
            LoadAozoraSettings(); // Load Aozora settings

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

            // 1. Heuristic Scoring Comparison
            // Compare scores for EUC-KR, SJIS, and Johab.
            // Johab needs to be in the competition because it overlaps with both (CP949 Ext & SJIS).
            int eucKrScore = GetEucKrScore(bytes);
            int sjisScore = GetSjisScore(bytes);
            int johabScore = GetJohabScore(bytes);
            
            // Winner takes all
            if (sjisScore > eucKrScore && sjisScore > johabScore && sjisScore > 0) return Encoding.GetEncoding(932);
            if (eucKrScore > sjisScore && eucKrScore > johabScore && eucKrScore > 0) return Encoding.GetEncoding(949);
            if (johabScore > sjisScore && johabScore > eucKrScore && johabScore > 0) return Encoding.GetEncoding(1361);
            
            // Default preference if scores match
            // Johab is rarest, so lowest priority in tie-break
            if (eucKrScore > 0 && eucKrScore >= sjisScore) return Encoding.GetEncoding(949);
            if (sjisScore > 0) return Encoding.GetEncoding(932);
            if (johabScore > 0) return Encoding.GetEncoding(1361);

            // 5. Try Johab (Korean Combination, CP1361) - heuristic fallback
            // (Redundant with scoring, but serves as final check)
            if (ContainsJohabPattern(bytes)) return Encoding.GetEncoding(1361);

            // 5. Default Fallbacks
            try { return Encoding.GetEncoding(51949); } catch { }
            try { return Encoding.GetEncoding(932); } catch { }

            return Encoding.Default;
        }

        private int GetSjisScore(byte[] bytes)
        {
            // Calculate a score for likelihood of SJIS.
            // +2 for Kana (strong signal)
            // +1 for Kanji
            
            int score = 0;
            int i = 0;
            int len = bytes.Length;
            
            while (i < len)
            {
                byte b = bytes[i];
                
                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                
                // Half-width Katakana (0xA1-0xDF)
                // This overlaps with EUC-KR first byte.
                // But if followed by ASCII, it's a strong SJIS signal.
                if (b >= 0xA1 && b <= 0xDF)
                {
                     if (i + 1 < len && bytes[i + 1] < 0x80) score += 1;
                     i++;
                     continue;
                }
                
                // Need 2 bytes
                if (i + 1 >= len) break;
                
                byte b2 = bytes[i + 1];
                
                // SJIS First Byte: 0x81-0x9F, 0xE0-0xFC
                // Includes Level 1 Kanji (0x81-0x9F) and Level 2 (0xE0-0xFC)
                if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                {
                    // Valid SJIS second byte: 0x40-0x7E or 0x80-0xFC
                    bool validSecond = (b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC);
                    if (validSecond)
                    {
                        // 0x82, 0x83 are Hiragana and Katakana - VERY strong signal for Japanese
                        if (b == 0x82 || b == 0x83) score += 5;
                        else score += 1;
                        
                        i += 2;
                        continue;
                    }
                }
                
                i++;
            }
            
            return score;
        }

        private int GetEucKrScore(byte[] bytes)
        {
            // Calculate a score for likelihood of EUC-KR.
            // +2 for Standard Hangul (0xB0-0xC8) - strong signal
            // +1 for Symbols or CP949 Extended
            
            int score = 0;
            int i = 0;
            int len = bytes.Length;
            
            while (i < len)
            {
                byte b1 = bytes[i];
                
                // ASCII - skip
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }
                
                // Need 2 bytes
                if (i + 1 >= len) break;
                
                byte b2 = bytes[i + 1];
                
                // Standard EUC-KR Hangul: 0xB0-0xC8 first, 0xA1-0xFE second
                // This is the strongest signal for Korean text.
                if (b1 >= 0xB0 && b1 <= 0xC8 && b2 >= 0xA1 && b2 <= 0xFE)
                {
                    score += 2;
                    i += 2;
                    continue;
                }
                
                // NOTE: We specifically DO NOT count CP949 Extended Range (0x81-0xA0) here.
                // NOTE: We ALSO removed 0xA1-0xAF (Symbols) because it overlaps with SJIS Half-width Katakana.
                // This makes EUC-KR detection purely based on Standard Hangul (0xB0+), which is safest.
                
                // NOTE: We specifically DO NOT count CP949 Extended Range (0x81-0xA0) here.
                // Reason: This range completely overlaps with SJIS (Lev 1 Kanji & Kana) and Johab.
                
                i++;
            }
            
            return score;
        }

        private int GetJohabScore(byte[] bytes)
        {
            // Calculate a score for likelihood of Johab.
            // +5 for Johab-ONLY second bytes (smoking gun)
            // +1 for valid Johab sequences
            
            int score = 0;
            int i = 0;
            int len = bytes.Length;
            
            while (i < len)
            {
                byte b = bytes[i];
                
                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                
                // Need 2 bytes
                if (i + 1 >= len) break;
                
                byte b2 = bytes[i + 1];
                
                // Johab First Byte: 0x84-0xD3
                if (b >= 0x84 && b <= 0xD3)
                {
                    // Check for Johab-ONLY second byte ranges: 0x5B-0x60, 0x7B-0x7E
                    // These are NOT used in CP949 or standard SJIS
                    if ((b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E))
                    {
                        score += 3; // Reduced from 5 to avoid false positives with SJIS Kanji
                        i += 2;
                        continue;
                    }
                    
                    // Normal Johab second byte: 0x41-0x7E or 0x81-0xFE
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        score += 1;
                        i += 2;
                        continue;
                    }
                }
                
                i++;
            }
            return score;
        }

        private bool ContainsJohabPattern(byte[] bytes)
        {
            // Check for Korean Johab (조합형) encoding patterns.
            // 
            // IMPORTANT: CP949 (Korean Windows codepage) also uses first bytes 0x81-0xA0
            // for extended Hangul characters! This overlaps with Johab.
            // 
            // Johab (CP1361):
            // - First byte: 0x84-0xD3
            // - Second byte: 0x41-0x7E, 0x81-0xFE
            // 
            // CP949 extended characters:
            // - First byte: 0x81-0xA0  
            // - Second byte: 0x41-0x5A (A-Z), 0x61-0x7A (a-z), 0x81-0xFE
            // 
            // KEY DIFFERENCE: Johab uses 0x5B-0x60 and 0x7B-0x7E as second bytes,
            // but CP949 does NOT use these ranges.
            // 
            // Strategy: Only detect Johab if we find second bytes in 0x5B-0x60 or 0x7B-0x7E
            // (ranges used by Johab but not by CP949)
            
            int johabOnlyPairCount = 0;
            int i = 0;
            int len = bytes.Length;
            
            while (i < len)
            {
                byte b = bytes[i];
                
                // ASCII byte - just skip and continue
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                
                // Need at least 2 bytes for multibyte character
                if (i + 1 >= len) break;
                
                byte b2 = bytes[i + 1];
                
                // Check for Johab-ONLY patterns:
                // First byte 0x84-0xD3, second byte in ranges CP949 doesn't use
                if (b >= 0x84 && b <= 0xD3)
                {
                    // Johab-only second byte ranges: 0x5B-0x60, 0x7B-0x7E
                    // These are NOT used by CP949 extended characters
                    bool johabOnlySecond = (b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E);
                    if (johabOnlySecond)
                    {
                        johabOnlyPairCount++;
                        i += 2;
                        
                        // If we found enough Johab-only pairs, it's definitely Johab
                        if (johabOnlyPairCount >= 2) return true;
                        continue;
                    }
                }
                
                // For any high byte, skip as 2-byte sequence to maintain alignment
                if (b >= 0x81)
                {
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        i += 2;
                        continue;
                    }
                }
                
                // Unknown byte pattern, advance by 1
                i++;
            }
            
            return false;
        }

        private bool IsStrictJohab(byte[] bytes)
        {
            // Detect Johab encoding by looking for Johab-specific first byte patterns.
            // 
            // KEY INSIGHT: The ONLY reliable way to distinguish Johab from EUC-KR/CP949 is:
            // - Johab uses first bytes 0x84-0xA0 (EUC-KR uses 0xA1+)
            // 
            // We DON'T count second byte patterns because they can cause false positives
            // due to byte alignment issues.
            
            int i = 0;
            int len = bytes.Length;
            int johabFirstByteCount = 0;  // Count of first bytes in 0x84-0xA0 range
            int totalMultibyte = 0;
            
            while (i < len)
            {
                byte b = bytes[i];
                
                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                
                // Single byte in 0x80-0x83 range - skip
                if (b >= 0x80 && b <= 0x83)
                {
                    i++;
                    continue;
                }
                
                // Need 2 bytes for multibyte
                if (i + 1 >= len)
                {
                    i++;
                    continue;
                }
                
                byte b2 = bytes[i + 1];
                totalMultibyte++;
                
                // First byte 0x84-0xA0: Johab-only range (EUC-KR doesn't use this for first byte)
                if (b >= 0x84 && b <= 0xA0)
                {
                    // Valid Johab second byte: 0x41-0x7E or 0x81-0xFE
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        johabFirstByteCount++;
                        i += 2;
                        continue;
                    }
                    // Invalid second byte - skip as single
                    i++;
                    continue;
                }
                
                // First byte 0xA1-0xFE: Could be EUC-KR or Johab - skip as 2-byte
                if (b >= 0xA1 && b <= 0xFE)
                {
                    if ((b2 >= 0x41 && b2 <= 0xFE) && b2 != 0x7F)
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                    continue;
                }
                
                // Other bytes - skip
                i++;
            }
            
            // VERY STRICT criteria: Only detect as Johab if we have MANY Johab-only first bytes
            // This is extremely conservative to avoid false positives with EUC-KR/CP949 files
            // Since IsStrictEucKr runs before this, we only get here if EUC-KR validation failed
            // Require at least 50 Johab-specific first bytes, or 15% of total multibyte chars
            if (johabFirstByteCount >= 50)
                return true;
            if (totalMultibyte >= 100 && johabFirstByteCount >= (totalMultibyte * 15 / 100)) // At least 15%
                return true;
                
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
            // "Text width max 42 chars"
            // With Consolas/Monospace it is easy. With variable width, 42 * FontSize is approximation (em).
            // Actually, for Japanese 'em' is full width.
            return 42 * _textFontSize; 
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

        private async Task RefreshTextDisplay(bool resetScroll = false)
        {
            if (_isAozoraMode && !string.IsNullOrEmpty(_currentTextContent))
            {
                // Capture current line to preserve position
                int currentLine = 1;
                if (!resetScroll && _aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                {
                    currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }
                
                // Re-calculate pages with new font size/settings
                await PrepareAozoraDisplayAsync(_currentTextContent, currentLine);
                
                // Content is already rendered progressively by PrepareAozoraDisplayAsync
                if (TextArea != null)
                    TextArea.Background = GetThemeBackground();
                      
                return;
            }

            // Apply current settings to all lines
            var brush = GetThemeForeground();
            var bg = GetThemeBackground();
            var maxW = GetUrlMaxWidth();
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < _textLines.Count; i++)
            {
                var line = _textLines[i];
                line.FontSize = _textFontSize;
                line.FontFamily = _textFontFamily;
                line.Foreground = brush;
                line.MaxWidth = maxW;

                if (i % 1000 == 0 && sw.ElapsedMilliseconds > 16)
                {
                    await Task.Yield();
                    sw.Restart();
                }
            }
            
            // Store current scroll ratio
            double scrollRatio = 0;
            if (TextScrollViewer != null && TextScrollViewer.ScrollableHeight > 0)
            {
                scrollRatio = TextScrollViewer.VerticalOffset / TextScrollViewer.ScrollableHeight;
            }

            TextArea.Background = bg;
            TextItemsRepeater.ItemsSource = null;
            TextItemsRepeater.ItemsSource = _textLines;
            
            // Restore scroll position based on ratio
            if (TextScrollViewer != null) 
            {
                 if (resetScroll)
                 {
                     TextScrollViewer.ChangeView(null, 0, null, true);
                 }
                 else
                 {
                     // We need to wait for layout update to get accurate ScrollableHeight
                     // Since we cannot await here easily without making method async (which is fine but might affect callers)
                     // Let's use a fire-and-forget task with delay
                     _ = Task.Run(async () => 
                     {
                         await Task.Delay(50); // Small delay for layout
                         RootGrid.DispatcherQueue.TryEnqueue(() => 
                         {
                             if (TextScrollViewer.ScrollableHeight > 0)
                             {
                                 double newOffset = scrollRatio * TextScrollViewer.ScrollableHeight;
                                 TextScrollViewer.ChangeView(null, newOffset, null, true);
                             }
                     });
                     });
                 }
            }

            // Trigger background page calculation
            StartPageCalculationAsync();
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
        
        private async void ToggleFont()
        {
            if (_textFontFamily == "Yu Gothic Medium")
                _textFontFamily = "Yu Mincho";
            else
                _textFontFamily = "Yu Gothic Medium";
                
            SaveTextSettings();
            await RefreshTextDisplay();
        }

        private void TextSizeUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                IncreaseEpubSize();
            else
                IncreaseTextSize();
        }
        
        private async void IncreaseTextSize()
        {
            _textFontSize += 2;
            if (_textFontSize > 72) _textFontSize = 72;
            TextSizeLevelText.Text = _textFontSize.ToString();
            SaveTextSettings();
            await RefreshTextDisplay();
        }

        private void TextSizeDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                DecreaseEpubSize();
            else
                DecreaseTextSize();
        }

        private async void DecreaseTextSize()
        {
            _textFontSize -= 2;
            if (_textFontSize < 8) _textFontSize = 8;
            TextSizeLevelText.Text = _textFontSize.ToString();
            SaveTextSettings();
            await RefreshTextDisplay();
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEpubMode)
                ToggleEpubTheme();
            else
                ToggleTheme();
        }
        
        private async void ToggleTheme()
        {
            _themeIndex = (_themeIndex + 1) % 3;
            SaveTextSettings();
            await RefreshTextDisplay();
        }
        
        private void GoToPageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowGoToLineDialog();
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
                   if (_isAozoraMode && _aozoraBlocks.Count > 0)
                   {
                       _aozoraNavHistory.Clear();
                       RenderAozoraDynamicPage(0);
                       UpdateAozoraStatusBar();
                   }
                   else if (TextScrollViewer != null)
                   {
                       TextScrollViewer.ChangeView(null, 0, null);
                   }
                   e.Handled = true;
              }
              else if (e.Key == Windows.System.VirtualKey.End)
              {
                   if (_isAozoraMode && _aozoraBlocks.Count > 0)
                   {
                       _aozoraNavHistory.Clear();
                       // Start rendering from slightly before the end to fill the last page
                       int lastIdx = Math.Max(0, _aozoraBlocks.Count - 5);
                       RenderAozoraDynamicPage(lastIdx);
                       UpdateAozoraStatusBar();
                   }
                   else if (TextScrollViewer != null)
                   {
                       TextScrollViewer.ChangeView(null, TextScrollViewer.ExtentHeight, null);
                   }
                   e.Handled = true;
              }
              else if (e.Key == Windows.System.VirtualKey.G)
              {
                  _ = ShowGoToLineDialog();
                  e.Handled = true;
              }
             else if (e.Key == Windows.System.VirtualKey.Left)
             {
                 if (_isAozoraMode)
                 {
                     NavigateAozoraPage(-1);
                 }
                 else if (TextScrollViewer != null)
                 {
                     NavigateTextPage(-1);
                 }
                 e.Handled = true; // Stop event bubbling to prevent file navigation
             }
             else if (e.Key == Windows.System.VirtualKey.Right)
             {
                 if (_isAozoraMode)
                 {
                     NavigateAozoraPage(1);
                 }
                 else if (TextScrollViewer != null)
                 {
                     NavigateTextPage(1);
                 }
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
             else if (e.Key == Windows.System.VirtualKey.A)
             {
                 ToggleAozoraMode();
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
                 var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                     Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                 
                 if (ctrlPressed)
                 {
                     ToggleSidebar();
                 }
                 else
                 {
                     if (_isEpubMode) ToggleEpubTheme();
                     else ToggleTheme();
                 }
                 e.Handled = true;
             }
        }

        private async Task ShowGoToLineDialog()
        {
             int currentLine = 1;
             int totalLines = 1;
             
             if (_isAozoraMode && _aozoraBlocks.Count > 0)
             {
                 totalLines = _aozoraTotalLineCount;
                 currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
             }
             else if (TextScrollViewer != null)
             {
                 totalLines = _textLines.Count;
                 currentLine = GetTopVisibleLineIndex();
             }
             
             if (currentLine < 1) currentLine = 1;

             var input = new TextBox 
             { 
                 InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }, 
                 PlaceholderText = $"1 - {totalLines}",
                 Text = currentLine.ToString()
             };
             
             input.SelectAll();

             var dialog = new ContentDialog
             {
                 Title = Strings.DialogTitle,
                 Content = input,
                 PrimaryButtonText = Strings.DialogPrimary,
                 CloseButtonText = Strings.DialogClose,
                 XamlRoot = this.Content.XamlRoot
             };
 
             input.KeyDown += (s, e) => 
             {
                 if (e.Key == Windows.System.VirtualKey.Enter)
                 {
                     dialog.Hide();
                     GoToLine(input.Text);
                 }
             };
 
             if (await dialog.ShowAsync() == ContentDialogResult.Primary)
             {
                 GoToLine(input.Text);
             }
        }
        
        private void GoToLine(string lineText)
        {
            if (!int.TryParse(lineText, out int line) || line < 1) return;
            
             if (_isAozoraMode && _aozoraBlocks.Count > 0)
             {
                 // Find block by line number
                 int targetIdx = 0;
                 for (int i = 0; i < _aozoraBlocks.Count; i++)
                 {
                     if (_aozoraBlocks[i].SourceLineNumber >= line)
                     {
                         targetIdx = i;
                         break;
                     }
                 }
                 
                 _aozoraNavHistory.Clear();
                 RenderAozoraDynamicPage(targetIdx);
                 UpdateAozoraStatusBar();
             }
            else if (TextScrollViewer != null)
            {
                ScrollToLine(line);
                UpdateTextStatusBar();
            }
        }

        // --- Element Prepared (Bold Logic) ---
        private void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (_isAozoraMode)
            {
                if (args.Element is RichTextBlock rtb)
                {
                     PrepareAozoraElement(rtb, args.Index);
                }
                return;
            }

            if (args.Element is TextBlock tb && _textLines.Count > args.Index)
            {
                var line = _textLines[args.Index];
                
                // Binding Properties
                tb.FontSize = line.FontSize;
                tb.FontFamily = new FontFamily(line.FontFamily);
                tb.Foreground = line.Foreground;
                tb.MaxWidth = line.MaxWidth;
                tb.TextAlignment = line.TextAlignment;
                tb.LineHeight = line.FontSize * 1.8;
                tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
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
             // Use unified touch handler (Next/Prev + Fullscreen Edge UI)
             var ptr = e.GetCurrentPoint(RootGrid);
             if (ptr.Properties.IsLeftButtonPressed)
             {
                 if (_isAozoraMode)
                 {
                     HandleSmartTouchNavigation(e, 
                        () => NavigateAozoraPage(-1), 
                        () => NavigateAozoraPage(1));
                 }
                 else
                 {
                     HandleSmartTouchNavigation(e, 
                        () => NavigateTextPage(-1), 
                        () => NavigateTextPage(1));
                 }
                 
                 e.Handled = true;
             }
        }
        
        private void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(TextArea);
            var delta = ptr.Properties.MouseWheelDelta;
            
            if (_isAozoraMode)
            {
                if (delta > 0) NavigateAozoraPage(-1); // Up = Prev
                else NavigateAozoraPage(1); // Down = Next
            }
            else
            {
                if (delta > 0) NavigateTextPage(-1); // Up = Prev
                else NavigateTextPage(1); // Down = Next
                UpdateTextStatusBar();
            }
            
            e.Handled = true;
        }

        private void NavigateTextPage(int direction)
        {
            if (TextScrollViewer == null) return;
            
            double current = TextScrollViewer.VerticalOffset;
            double viewport = TextScrollViewer.ViewportHeight;
            
            // Calculate scroll amount based on LineHeight (FontSize * 1.8)
            double lineH = _textFontSize * 1.8;
            double overlap = lineH;
            
            // Safety check for very small viewports
            if (overlap > viewport * 0.5) overlap = viewport * 0.2;
            
            double scrollAmount = viewport - overlap;
            
            if (direction > 0)
            {
                TextScrollViewer.ChangeView(null, current + scrollAmount, null, true);
            }
            else
            {
                TextScrollViewer.ChangeView(null, current - scrollAmount, null, true);
            }
            UpdateTextStatusBar();
        }
        
        private void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null)
        {
            if (!_isTextMode) return;
            if (_isAozoraMode) { UpdateAozoraStatusBar(); return; }

             if (fileName != null) FileNameText.Text = fileName;
             
             int total = totalLines ?? _textLines.Count;
             if (total == 0) total = 1;

             if (TextScrollViewer != null)
             {
                 int currentLine = GetTopVisibleLineIndex();
                 if (currentLine > total) currentLine = total;

                 double progress = (TextScrollViewer.ExtentHeight > 0) ? (TextScrollViewer.VerticalOffset + TextScrollViewer.ViewportHeight) * 100.0 / TextScrollViewer.ExtentHeight : 0;
                 if (progress > 100) progress = 100;

                 string status = $"{progress:F1}%";

                 // Append Page Info if calculated
                 if (_isPageCalculationCompleted && _calculatedTotalHeight > 0 && TextScrollViewer.ViewportHeight > 0)
                 {
                     int totalPages = (int)Math.Ceiling(_calculatedTotalHeight / TextScrollViewer.ViewportHeight);
                     int calcCurrentPage = (int)Math.Floor(TextScrollViewer.VerticalOffset / TextScrollViewer.ViewportHeight) + 1;
                     
                     if (totalPages < 1) totalPages = 1;
                     if (calcCurrentPage > totalPages) calcCurrentPage = totalPages;
                     if (calcCurrentPage < 1) calcCurrentPage = 1;

                     status += $" ({calcCurrentPage} / {totalPages})";
                 }

                 ImageIndexText.Text = status;
                 ImageInfoText.Text = $"Line {currentLine} / {total}";
             }
        }
        
        private void TextScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateTextStatusBar();
        }
        
        private void TextScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-calc max width if needed, but it is bound to line prop.
            if (_isTextMode && !_isAozoraMode)
            {
                StartPageCalculationAsync();
            }
        }

        // --- Page Calculation Logic ---
        private double _calculatedTotalHeight = 0;
        private bool _isPageCalculationCompleted = false;
        private CancellationTokenSource? _pageCalcCts;
        private FontFamily? _cachedFontFamily = null;

        private async void StartPageCalculationAsync()
        {
            _pageCalcCts?.Cancel();
            _pageCalcCts = new CancellationTokenSource();
            var token = _pageCalcCts.Token;

            _isPageCalculationCompleted = false;
            _calculatedTotalHeight = 0;
            _cachedFontFamily = null;
            UpdateTextStatusBar(); // Reset display to just %

            if (TextScrollViewer == null || _textLines.Count == 0 || TextScrollViewer.ViewportHeight <= 0) 
            {
                 // If viewport is not ready, wait a bit
                 if (TextScrollViewer != null) 
                 {
                     try { await Task.Delay(500, token); } catch { return; }
                     if (TextScrollViewer.ViewportHeight <= 0) return;
                 }
                 else return;
            }

            double viewportWidth = TextScrollViewer.ViewportWidth;
            
            try
            {
                // Dummy TextBlock for measurement
                var dummy = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };

                double totalH = 0;
                int batchSize = 50; 
                int count = 0;

                // Cache the font family if it's common
                if (!string.IsNullOrEmpty(_textFontFamily))
                {
                    _cachedFontFamily = new FontFamily(_textFontFamily);
                }

                foreach (var line in _textLines)
                {
                    if (token.IsCancellationRequested) return;

                    // Apply properties matching TextItemsRepeater_ElementPrepared logic
                    dummy.FontSize = line.FontSize;
                    if (_cachedFontFamily != null && line.FontFamily == _textFontFamily)
                        dummy.FontFamily = _cachedFontFamily;
                    else
                        dummy.FontFamily = new FontFamily(line.FontFamily);

                    dummy.Text = line.Content;
                    dummy.MaxWidth = line.MaxWidth;
                    dummy.Margin = line.Margin;
                    dummy.Padding = line.Padding;
                    dummy.LineHeight = line.FontSize * 1.8;
                    dummy.TextAlignment = line.TextAlignment;

                    // Measure
                    // Constraint width is ViewportWidth, height is Infinite
                    dummy.Measure(new Size(viewportWidth, double.PositiveInfinity));
                    
                    totalH += dummy.DesiredSize.Height;
                    
                    count++;
                    if (count % batchSize == 0)
                    {
                        // Yield to UI thread to keep app responsive
                        await Task.Delay(1, token);
                    }
                }

                _calculatedTotalHeight = totalH;
                _isPageCalculationCompleted = true;
                UpdateTextStatusBar();
            }
            catch (OperationCanceledException)
            {
                // Expected on new calculation start
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating text pages: {ex.Message}");
            }
        }

        private int GetTopVisibleLineIndex()
        {
            if (TextItemsRepeater == null || TextScrollViewer == null) return 1;
            if (_textLines == null || _textLines.Count == 0) return 1;

            try
            {
                // Use VisualTreeHelper to check realized children
                int childCount = VisualTreeHelper.GetChildrenCount(TextItemsRepeater);
                if (childCount == 0) return 1;

                UIElement? closest = null;
                double minDist = double.MaxValue;
                
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(TextItemsRepeater, i) as UIElement;
                    if (child == null) continue;

                    var transform = child.TransformToVisual(TextScrollViewer);
                    var point = transform.TransformPoint(new Point(0, 0));
                    
                    double top = point.Y;
                    double bottom = top + ((FrameworkElement)child).ActualHeight;
                    
                    // If the item covers the top edge (Top <= 0 and Bottom > 0) - this is THE reading line
                    if (top <= 0 && bottom > 0)
                    {
                        int idx = TextItemsRepeater.GetElementIndex(child);
                        if (idx >= 0) return idx + 1;
                    }
                    
                    // Otherwise, find the one closest to 0
                    if (Math.Abs(top) < minDist)
                    {
                        minDist = Math.Abs(top);
                        closest = child;
                    }
                }
                
                if (closest != null)
                {
                    int idx = TextItemsRepeater.GetElementIndex(closest);
                    if (idx >= 0) return idx + 1;
                }
            }
            catch { }

            // Fallback
             double lineH = _textFontSize * 1.8;
             if (lineH > 0) 
                return (int)(TextScrollViewer.VerticalOffset / lineH) + 1;
                
             return 1;
        }

        private void ScrollToLine(int line)
        {
            if (TextItemsRepeater == null) return;
            if (line < 1) line = 1;
            int index = line - 1;
             if (_textLines == null) return;
            if (index >= _textLines.Count) index = _textLines.Count - 1;
            if (index < 0) return;
            
            try
            {
                var element = TextItemsRepeater.GetOrCreateElement(index);
                if (element != null)
                {
                    element.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                }
            }
            catch { }
        }
    }
}
