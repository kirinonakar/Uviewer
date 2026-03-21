using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
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
using Windows.UI;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private List<TextLine> _textLines = new();
        private string _currentTextContent = ""; // Stores raw text for mode switching
        private string _userSelectedEncodingName = "Auto"; // User selected encoding
        private double _textFontSize = 18;
        private string _textFontFamily = "Yu Gothic";
        private string _uiFontFamily = ""; // Default empty (system default)
        private int _themeIndex = 0; // 0: White, 1: Beige, 2: Dark, 3: Custom
        private string _languageSetting = "Auto"; // Store "Auto", "ko-KR", etc.
        private Color? _customBackgroundColor = null;
        private Color? _customForegroundColor = null;
        private bool _isTextMode = false;
        private int _textTotalLineCountInSource = 0; // Total lines in source file for simple text mode
#pragma warning disable CS0414 // Field is assigned but never used - reserved for future loading state UI
        private bool _isTextLinesFullyLoaded = false; // Track if all lines are loaded
#pragma warning restore CS0414
        private CancellationTokenSource? _globalTextCts;



        // SupportedTextExtensions is defined in MainWindow.xaml.cs

        public class TextLine
        {
            public string Content { get; set; } = "";
            public double FontSize { get; set; }
            public string FontFamily { get; set; } = "Yu Gothic";
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
        private string? _currentTextArchiveEntryKey = null; // Track entry key if viewing from archive
        private int _lastRecentSaveLine = -1;


        // New Helper: Select encoding based on user choice or auto-detect
        private Encoding GetTextEncoding(byte[] bytes)
        {
            switch (_userSelectedEncodingName)
            {
                case "UTF-8": return Encoding.UTF8;
                case "EUC-KR": return Encoding.GetEncoding(949);
                case "Shift-JIS": return Encoding.GetEncoding(932);
                case "Johab": return Encoding.GetEncoding(1361);
                case "Auto":
                default:
                    return DetectEncoding(bytes);
            }
        }

        private async void EncodingItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string tag)
            {
                _userSelectedEncodingName = tag;

                // Update UI Check States
                if (EncAutoItem != null) EncAutoItem.IsChecked = (tag == "Auto Detect");
                if (EncUtf8Item != null) EncUtf8Item.IsChecked = (tag == "UTF-8");
                if (EncEucKrItem != null) EncEucKrItem.IsChecked = (tag == "EUC-KR");
                if (EncSjisItem != null) EncSjisItem.IsChecked = (tag == "Shift-JIS");
                if (EncJohabItem != null) EncJohabItem.IsChecked = (tag == "Johab");

                // Reload current text
                if (!string.IsNullOrEmpty(_currentTextFilePath))
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(_currentTextFilePath);
                        await LoadTextFileAsync(file);
                    }
                    catch { }
                }
                else if (_currentTextArchiveEntryKey != null && (_currentArchive != null || _current7zArchive != null))
                {
                    try
                    {
                        var entry = new ImageEntry { ArchiveEntryKey = _currentTextArchiveEntryKey, DisplayName = FileNameText.Text };
                        await LoadTextFromArchiveEntryAsync(entry);
                    }
                    catch { }
                }
            }
        }

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

        private void CancelAndResetGlobalTextCts()
        {
            try
            {
                _globalTextCts?.Cancel();
                _pageCalcCts?.Cancel(); // [추가] 모드 전환 시 뒤에서 돌고 있는 페이지 계산 작업 강제 종료
                // do not dispose here because it might be in use
            }
            catch { }
            _globalTextCts = new CancellationTokenSource();
        }

        private async Task LoadTextFileAsync(StorageFile file)
        {
            // Save position of current file before switching
            await AddToRecentAsync(true);

            InitializeText();
            _currentTextFilePath = file.Path;
            _currentTextArchiveEntryKey = null; // Clear archive context when loading local file
            // No reset here, DisplayLoadedText will handle it after using the value

            try
            {
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                SwitchToTextMode();
                string content = await ReadTextFileWithEncodingAsync(file);
                if (token.IsCancellationRequested) return;

                await DisplayLoadedText(content, file.Name, file.Path, token);

                if (token.IsCancellationRequested) return;
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
            _currentTextFilePath = null; // Clear to prevent state leakage from previous file
            _currentTextArchiveEntryKey = entry.ArchiveEntryKey; // Store entry key for relative path resolution
                                                                 // No reset here, DisplayLoadedText will handle it

            try
            {
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                SwitchToTextMode();
                string content = "";
                await _archiveLock.WaitAsync(token);
                try
                {
                    if (entry.ArchiveEntryKey != null)
                    {
                        if (_currentArchive != null)
                        {
                            var archEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entry.ArchiveEntryKey);
                            if (archEntry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                using var entryStream = archEntry.OpenEntryStream();
                                entryStream.CopyTo(ms);
                                var bytes = ms.ToArray();
                                content = GetTextEncoding(bytes).GetString(bytes);
                            }
                        }
                        else if (_current7zArchive != null)
                        {
                            var archEntry = _current7zArchive.Entries.FirstOrDefault(e => e.FileName == entry.ArchiveEntryKey);
                            if (archEntry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                archEntry.Extract(ms);
                                var bytes = ms.ToArray();
                                content = GetTextEncoding(bytes).GetString(bytes);
                            }
                        }
                    }
                }
                finally { _archiveLock.Release(); }

                if (token.IsCancellationRequested) return;
                await DisplayLoadedText(content, entry.DisplayName, null, token);

                if (token.IsCancellationRequested) return;
                SyncSidebarSelection(entry);
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"아카이브 텍스트 로드 실패: {ex.Message}";
            }
        }

        private async Task DisplayLoadedText(string content, string name, string? uniquePath = null, CancellationToken token = default)
        {
            _currentTextContent = content; // Save for reload
            _aozoraBlocks.Clear(); // [핵심 수정] 새 파일을 로드할 때 이전 파일의 블록 캐시를 제거합니다.

            // [추가] 이전 파일의 스크롤 추적 기록을 초기화하여 엉뚱한 위치가 자동 저장되는 것을 방지합니다.
            _lastRecentSaveLine = -1;

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
                // 일반 텍스트 모드와 아오조라 모드 모두 저장된 위치에서 열리도록 통합
                targetLine = GetSavedStartLine(name, uniquePath);
            }

            // [핵심 추가] UI가 숨겨지면서 가상화가 풀려 수만 줄을 동시 렌더링(프리징)하는 것을 막기 위해 데이터 연결 끊기
            if (_isAozoraMode || _isVerticalMode)
            {
                if (TextItemsRepeater != null) TextItemsRepeater.ItemsSource = null;
                _textLines.Clear();
            }

            // Ensure visibility is mutually exclusive
            if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
            if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
            if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;

            if (_isVerticalMode)
            {
                if (TextArea != null) TextArea.Background = GetThemeBackground();
                await PrepareVerticalTextAsync(targetLine, token);
                if (token.IsCancellationRequested) return;
                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;

                FileNameText.Text = GetFormattedDisplayName(name, _currentTextArchiveEntryKey != null);
                UpdateTextStatusBar();
                return;
            }

            if (_isAozoraMode)
            {
                // Use page-based container display with target line restoration
                await PrepareAozoraDisplayAsync(content, targetLine, token);
                if (token.IsCancellationRequested) return;
                if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;

                FileNameText.Text = GetFormattedDisplayName(name, _currentTextArchiveEntryKey != null);
            }
            else
            {
                // Ensure default template
                if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                {
                    TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                }

                // Progressive loading for large files
                await LoadTextLinesProgressivelyAsync(content, targetLine, token);
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;

                // Reset to top immediately if not restoring position
                if (targetLine <= 1 && TextScrollViewer != null)
                {
                    TextScrollViewer.ChangeView(null, 0, null, true);
                }

                // Update Text Status
                UpdateTextStatusBar(name, _textTotalLineCountInSource, 1);

                // Position is now handled inside LoadTextLinesProgressivelyAsync 
                // to avoid double-scrolling Jitter.
                // ScrollToLine(targetLine) moved there.
                if (targetLine <= 1 && _isAozoraMode)
                {
                    // Only auto-restore for Aozora mode if not explicitly set to 1
                    _ = RestoreTextPositionAsync(name);
                }
            }
        }

        private int GetSavedStartLine(string name, string? path)
        {
            try
            {
                // Identify target solely by path if available, else by name AND ensure no path collision
                // Use Case-Insensitive comparison for Windows paths

                var recent = _recentItems.OrderByDescending(r => r.AccessedAt)
                                         .FirstOrDefault(r => (path != null && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                                                              (path == null && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));

                if (recent != null)
                {
                    if (recent.SavedLine > 1) return recent.SavedLine;
                    if (recent.SavedPage > 0) return -recent.SavedPage; // Legacy support
                    return 1;
                }

                // If not in recent, check favorites
                var favorite = _favorites.FirstOrDefault(f => (path != null && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                                                              (path == null && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)));
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

            // [추가] 텍스트 모드 진입 시 창 크기 검사 및 조정
            EnsureMinWindowSizeForText();

            // Toggle Visibility
            if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
            ImageArea.Visibility = Visibility.Collapsed;
            TextArea.Visibility = Visibility.Visible;
            EpubArea.Visibility = Visibility.Collapsed; // Ensure Epub area is hidden

            // Toggle Toolbars
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible;
            SideBySideToolbarPanel.Visibility = Visibility.Collapsed;

            // Load Settings (Must happen before visibility check)
            LoadTextSettings();

            if (_isVerticalMode)
            {
                VerticalTextCanvas.Visibility = Visibility.Visible;
                TextScrollViewer.Visibility = Visibility.Collapsed;
                AozoraTextCanvas.Visibility = Visibility.Collapsed;
            }
            else if (_isAozoraMode)
            {
                VerticalTextCanvas.Visibility = Visibility.Collapsed;
                TextScrollViewer.Visibility = Visibility.Collapsed;
                AozoraTextCanvas.Visibility = Visibility.Visible;
            }
            else
            {
                VerticalTextCanvas.Visibility = Visibility.Collapsed;
                TextScrollViewer.Visibility = Visibility.Visible;
                AozoraTextCanvas.Visibility = Visibility.Collapsed;
            }

            // Update Title
            Title = "Uviewer - Image & Text Viewer";
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
                        _isVerticalMode = settings.IsVerticalMode;
                        if (settings.CustomBackgroundColor != null) _customBackgroundColor = ParseHexColor(settings.CustomBackgroundColor);
                        if (settings.CustomForegroundColor != null) _customForegroundColor = ParseHexColor(settings.CustomForegroundColor);
                        if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = _isVerticalMode;

                        if (!string.IsNullOrEmpty(settings.UIFontFamily))
                        {
                            _uiFontFamily = settings.UIFontFamily;
                            this.DispatcherQueue.TryEnqueue(() => SetUiFont(_uiFontFamily));
                        }

                        try
                        {
                            _languageSetting = settings.Language ?? "Auto";
                            ApplyLanguage(_languageSetting);
                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                await Task.Delay(100);
                                Strings.Reload();
                                ApplyLocalization();
                                UpdateLanguageMenuCheckmark();
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Language load error: {ex.Message}");
                        }
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
                    ThemeIndex = _themeIndex,
                    IsVerticalMode = _isVerticalMode,
                    CustomBackgroundColor = _customBackgroundColor?.ToString(),
                    CustomForegroundColor = _customForegroundColor?.ToString(),
                    Language = _languageSetting,
                    UIFontFamily = _uiFontFamily
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
            _isVerticalMode = false; // [Fix] Images should always open in normal mode, not vertical
            if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = false;

            ImageArea.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Collapsed;
            VerticalTextCanvas.Visibility = Visibility.Collapsed;

            ImageToolbarPanel.Visibility = Visibility.Visible;
            TextToolbarPanel.Visibility = Visibility.Collapsed;
            SideBySideToolbarPanel.Visibility = (_currentPdfDocument != null) ? Visibility.Collapsed : Visibility.Visible;
            SharpenButton.Visibility = (_currentPdfDocument != null) ? Visibility.Collapsed : Visibility.Visible;

            // PDF의 경우 핀치 줌과 스와이프 스크롤을 위해 조작 모드 활성화
            ImageArea.ManipulationMode = (_currentPdfDocument != null) ? Microsoft.UI.Xaml.Input.ManipulationModes.All : Microsoft.UI.Xaml.Input.ManipulationModes.None;

            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();
        }

        /// <summary>
        /// Progressive loading for simple text mode - loads initial chunk first, then rest in background
        /// </summary>
        private async Task LoadTextLinesProgressivelyAsync(string content, int targetLine = 1, CancellationToken token = default)
        {
            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            _textTotalLineCountInSource = lines.Length;
            _isTextLinesFullyLoaded = false;

            // Cache common values to avoid repeated calls
            var brush = GetThemeForeground();
            var maxW = GetUrlMaxWidth();

            int initialLimit = 2000;
            // Ensure we load enough to reach target line
            if (targetLine > initialLimit - 500) initialLimit = targetLine + 500;

            if (lines.Length > initialLimit)
            {
                // 1. Parse initial chunk in background thread
                var initialLines = lines.Take(initialLimit).ToArray();
                _textLines = await Task.Run(() => ParseTextLinesChunk(initialLines, brush, maxW), token);

                // 2. Update UI with initial chunk
                if (TextItemsRepeater != null)
                {
                    TextItemsRepeater.ItemsSource = null;

                    // [핵심 수정] 새 데이터를 바인딩하기 전에 스크롤을 맨 위로 강제 초기화합니다.
                    // 이렇게 해야 이전 파일의 스크롤 위치를 기억하고 중간부터 렌더링하는 현상을 막을 수 있습니다.
                    if (targetLine <= 1 && TextScrollViewer != null)
                    {
                        TextScrollViewer.ChangeView(null, 0, null, true);
                    }

                    TextItemsRepeater.ItemsSource = _textLines;
                    if (targetLine > 1)
                    {
                        // Wait for first layout pass
                        await Task.Delay(50);
                        ScrollToLine(targetLine);
                    }
                }
                if (TextArea != null) TextArea.Background = GetThemeBackground();

                // 3. Load rest in background
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        var restLines = lines.Skip(initialLimit).ToArray();
                        var restTextLines = ParseTextLinesChunk(restLines, brush, maxW);

                        if (token.IsCancellationRequested) return;
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            if (_isAozoraMode) return; // Mode switched, abort

                            _textLines.AddRange(restTextLines);
                            _isTextLinesFullyLoaded = true;

                            // Refresh ItemsSource to show all lines
                            if (TextItemsRepeater != null)
                            {
                                // Use a more efficient approach - just update the source
                                var currentSource = _textLines;
                                TextItemsRepeater.ItemsSource = null;
                                TextItemsRepeater.ItemsSource = currentSource;
                            }

                            // Recalculate pages
                            StartPageCalculationAsync();
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background text parse error: {ex.Message}");
                    }
                }, token);
            }
            else
            {
                // Small file - load all at once in background
                _textLines = await Task.Run(() => ParseTextLinesChunk(lines, brush, maxW), token);
                _isTextLinesFullyLoaded = true;

                if (TextItemsRepeater != null)
                {
                    TextItemsRepeater.ItemsSource = null;

                    // [핵심 수정] 스몰 파일 처리 시에도 데이터 바인딩 전 스크롤 0으로 초기화
                    if (targetLine <= 1 && TextScrollViewer != null)
                    {
                        TextScrollViewer.ChangeView(null, 0, null, true);
                    }

                    TextItemsRepeater.ItemsSource = _textLines;
                    if (targetLine > 1)
                    {
                        await Task.Yield();
                        ScrollToLine(targetLine);
                    }
                }
                if (TextArea != null) TextArea.Background = GetThemeBackground();
            }

            // Trigger background page calculation
            StartPageCalculationAsync();

            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Parse a chunk of lines into TextLine objects (runs on background thread)
        /// </summary>
        private List<TextLine> ParseTextLinesChunk(string[] lines, Brush brush, double maxW)
        {
            var result = new List<TextLine>(lines.Length);

            foreach (var line in lines)
            {
                var textLine = new TextLine
                {
                    Content = line,
                    FontSize = _textFontSize,
                    FontFamily = _textFontFamily,
                    Foreground = brush,
                    MaxWidth = maxW
                };
                // Note: ApplyAozoraStyling is skipped for simple text mode for performance
                result.Add(textLine);
            }
            return result;
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
            if (_themeIndex == 3 && _customForegroundColor.HasValue) return new SolidColorBrush(_customForegroundColor.Value);
            return new SolidColorBrush(Colors.Black);
        }
        
        private Windows.UI.Text.FontWeight GetFontWeightForFamily(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily)) return Microsoft.UI.Text.FontWeights.Normal;
            if (fontFamily.Contains("Yu Gothic", StringComparison.OrdinalIgnoreCase) || 
                fontFamily.Contains("游ゴシック", StringComparison.OrdinalIgnoreCase))
            {
                return Microsoft.UI.Text.FontWeights.Medium;
            }
            return Microsoft.UI.Text.FontWeights.Normal;
        }

        private Brush GetThemeBackground()
        {
            if (_themeIndex == 0) return new SolidColorBrush(Colors.White);
            if (_themeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
            if (_themeIndex == 3 && _customBackgroundColor.HasValue) return new SolidColorBrush(_customBackgroundColor.Value);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
        }

        private Color ParseHexColor(string hex)
        {
            try
            {
                if (hex.StartsWith("#")) hex = hex.Substring(1);
                if (hex.Length == 8) // ARGB
                {
                    byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
                else if (hex.Length == 6) // RGB
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch { }
            return Colors.White;
        }

        private async Task RefreshTextDisplay(bool resetScroll = false)
        {
            if (_isEpubMode)
            {
                int currentLine = 1;
                if (!resetScroll && EpubSelectedItem is Grid g && g.Tag is EpubPageInfoTag tag)
                {
                    currentLine = tag.StartLine;
                }

                UpdateEpubVisuals();
                ClearEpubCache();
                _ = LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine);
                return;
            }
            if (_isVerticalMode)
            {
                if (TextArea != null) TextArea.Background = GetThemeBackground();
                VerticalTextCanvas.Invalidate();
                UpdateTextStatusBar();
                return;
            }

            if (_isAozoraMode && !string.IsNullOrEmpty(_currentTextContent))
            {
                // Capture current line to preserve position
                int currentLine = 1;
                if (!resetScroll && _aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                {
                    currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }

                // Re-calculate pages with new font size/settings
                await PrepareAozoraDisplayAsync(_currentTextContent, currentLine, _globalTextCts?.Token ?? default);

                // Content is already rendered progressively by PrepareAozoraDisplayAsync
                if (TextArea != null)
                    TextArea.Background = GetThemeBackground();

                return;
            }

            // Store current scroll ratio before updating
            double scrollRatio = 0;
            if (TextScrollViewer != null && TextScrollViewer.ScrollableHeight > 0)
            {
                scrollRatio = TextScrollViewer.VerticalOffset / TextScrollViewer.ScrollableHeight;
            }

            // Apply current settings to all lines - process in background for large files
            var brush = GetThemeForeground();
            var bg = GetThemeBackground();
            var maxW = GetUrlMaxWidth();
            var fontSize = _textFontSize;
            var fontFamily = _textFontFamily;

            if (_textLines.Count > 1000)
            {
                // Large file: update in background
                var linesToUpdate = _textLines;
                await Task.Run(() =>
                {
                    foreach (var line in linesToUpdate)
                    {
                        line.FontSize = fontSize;
                        line.FontFamily = fontFamily;
                        line.Foreground = brush;
                        line.MaxWidth = maxW;
                    }
                });
            }
            else
            {
                // Small file: update synchronously
                foreach (var line in _textLines)
                {
                    line.FontSize = fontSize;
                    line.FontFamily = fontFamily;
                    line.Foreground = brush;
                    line.MaxWidth = maxW;
                }
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

        private void ColorsMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowColorPickerDialog();
        }

        private async void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string lang)
            {
                ApplyLanguage(lang);
                SaveTextSettings();

                // Give a moment for the system to process the language change
                await Task.Delay(100);

                // Reload strings
                Strings.Reload();

                // Refresh UI
                ApplyLocalization();
                UpdateLanguageMenuCheckmark();

                // Refresh status bar immediately
                if (_isTextMode || _isEpubMode || _isAozoraMode || _isVerticalMode)
                {
                    UpdateTextStatusBar();
                }
                else if (_imageEntries != null && _currentIndex >= 0 && _currentIndex < _imageEntries.Count && _currentBitmap != null)
                {
                    UpdateStatusBar(_imageEntries[_currentIndex], _currentBitmap);
                }
            }
        }

        private void ApplyLanguage(string lang)
        {
            _languageSetting = lang;
            try
            {
                if (lang == "Auto" || string.IsNullOrEmpty(lang))
                {
                    // For Auto, we explicitly fetch the first available language from system preferences
                    // and set it as the override to force the app's resource manager to switch.
                    var systemLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
                    if (systemLanguages.Count > 0)
                    {
                        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = systemLanguages[0];
                    }
                    else
                    {
                        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
                    }
                }
                else
                {
                    Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language apply error: {ex.Message}");
            }
        }

        private void UpdateLanguageMenuCheckmark()
        {
            string current = _languageSetting;
            if (string.IsNullOrEmpty(current)) current = "Auto";

            if (LangAutoItem != null) LangAutoItem.IsChecked = current == "Auto";
            if (LangKoItem != null) LangKoItem.IsChecked = current == "ko-KR";
            if (LangEnItem != null) LangEnItem.IsChecked = current == "en-US";
            if (LangJaItem != null) LangJaItem.IsChecked = current == "ja-JP";
        }

        private bool _isColorPickerOpen = false;

        private async Task ShowColorPickerDialog()
        {
            _isColorPickerOpen = true;
            try
            {
                var bgHsl = ToHsl(_customBackgroundColor ?? ((SolidColorBrush)GetThemeBackground()).Color);
                var fgHsl = ToHsl(_customForegroundColor ?? ((SolidColorBrush)GetThemeForeground()).Color);

                var previewBorder = new Border
                {
                    Height = 60,
                    Margin = new Thickness(0, 0, 0, 10),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(FromHsl(bgHsl.h, bgHsl.s, bgHsl.l)),
                    Child = new TextBlock
                    {
                        Text = Strings.Preview + " - Abc 가나다 123",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 18,
                        Foreground = new SolidColorBrush(FromHsl(fgHsl.h, fgHsl.s, fgHsl.l))
                    }
                };

                var previewText = (TextBlock)previewBorder.Child;

                Slider CreateSlider(string label, double min, double max, double val, Action<double> onChange)
                {
                    var slider = new Slider { Minimum = min, Maximum = max, Value = val, Margin = new Thickness(0, 0, 0, 8) };
                    slider.ValueChanged += (s, e) => onChange(e.NewValue);
                    return slider;
                }

                var stackPanel = new StackPanel { Width = 300 };
                stackPanel.Children.Add(previewBorder);

                stackPanel.Children.Add(new TextBlock { Text = Strings.BackgroundColor, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                stackPanel.Children.Add(new TextBlock { Text = Strings.Hue, FontSize = 12 });
                var bgHSlider = CreateSlider(Strings.Hue, 0, 360, bgHsl.h, v => { bgHsl.h = v; previewBorder.Background = new SolidColorBrush(FromHsl(bgHsl.h, bgHsl.s, bgHsl.l)); });
                stackPanel.Children.Add(bgHSlider);
                stackPanel.Children.Add(new TextBlock { Text = Strings.Saturation, FontSize = 12 });
                var bgSSlider = CreateSlider(Strings.Saturation, 0, 100, bgHsl.s, v => { bgHsl.s = v; previewBorder.Background = new SolidColorBrush(FromHsl(bgHsl.h, bgHsl.s, bgHsl.l)); });
                stackPanel.Children.Add(bgSSlider);
                stackPanel.Children.Add(new TextBlock { Text = Strings.Lightness, FontSize = 12 });
                var bgLSlider = CreateSlider(Strings.Lightness, 0, 100, bgHsl.l, v => { bgHsl.l = v; previewBorder.Background = new SolidColorBrush(FromHsl(bgHsl.h, bgHsl.s, bgHsl.l)); });
                stackPanel.Children.Add(bgLSlider);

                stackPanel.Children.Add(new TextBlock { Text = Strings.TextColor, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
                stackPanel.Children.Add(new TextBlock { Text = Strings.Hue, FontSize = 12 });
                var fgHSlider = CreateSlider(Strings.Hue, 0, 360, fgHsl.h, v => { fgHsl.h = v; previewText.Foreground = new SolidColorBrush(FromHsl(fgHsl.h, fgHsl.s, fgHsl.l)); });
                stackPanel.Children.Add(fgHSlider);
                stackPanel.Children.Add(new TextBlock { Text = Strings.Saturation, FontSize = 12 });
                var fgSSlider = CreateSlider(Strings.Saturation, 0, 100, fgHsl.s, v => { fgHsl.s = v; previewText.Foreground = new SolidColorBrush(FromHsl(fgHsl.h, fgHsl.s, fgHsl.l)); });
                stackPanel.Children.Add(fgSSlider);
                stackPanel.Children.Add(new TextBlock { Text = Strings.Lightness, FontSize = 12 });
                var fgLSlider = CreateSlider(Strings.Lightness, 0, 100, fgHsl.l, v => { fgHsl.l = v; previewText.Foreground = new SolidColorBrush(FromHsl(fgHsl.h, fgHsl.s, fgHsl.l)); });
                stackPanel.Children.Add(fgLSlider);

                var dialog = new ContentDialog
                {
                    Title = Strings.ChangeColors,
                    Content = stackPanel,
                    PrimaryButtonText = Strings.DialogPrimary,
                    CloseButtonText = Strings.DialogClose,
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary,
                    RequestedTheme = RootGrid.ActualTheme
                };

                stackPanel.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        dialog.Hide(); // 강제 닫기
                        e.Handled = true; // 이벤트 전파 중단
                    }
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _customBackgroundColor = FromHsl(bgHsl.h, bgHsl.s, bgHsl.l);
                    _customForegroundColor = FromHsl(fgHsl.h, fgHsl.s, fgHsl.l);
                    _themeIndex = 3; // Custom
                    SaveTextSettings();
                    await RefreshTextDisplay();
                }
            }
            finally
            {
                _isColorPickerOpen = false;
            }
        }

        private (double h, double s, double l) ToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h, s, l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0; // achromatic
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6.0;
            }
            return (h * 360, s * 100, l * 100);
        }

        private Color FromHsl(double h, double s, double l)
        {
            h /= 360.0;
            s /= 100.0;
            l /= 100.0;
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }
            return Color.FromArgb(255, (byte)Math.Clamp(r * 255, 0, 255), (byte)Math.Clamp(g * 255, 0, 255), (byte)Math.Clamp(b * 255, 0, 255));
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private void FontToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFont();
        }

        private void FontMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialog();
        }

        private async Task ShowFontPickerDialog()
        {
            try
            {
                var fonts = CanvasTextFormat.GetSystemFontFamilies()
                    .OrderBy(f => f)
                    .ToList();

                var searchBox = new AutoSuggestBox
                {
                    PlaceholderText = Strings.FontSearchPlaceholder,
                    QueryIcon = new SymbolIcon(Symbol.Find),
                    Margin = new Thickness(0, 0, 0, 10),
                    Width = 300
                };

                var fontList = new ListView
                {
                    ItemsSource = fonts,
                    SelectionMode = ListViewSelectionMode.Single,
                    MaxHeight = 400,
                    Width = 300,
                    ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                        @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='{Binding}' FontFamily='{Binding}' FontSize='16' VerticalAlignment='Center' Padding='4'/>
                        </DataTemplate>")
                };

                // Pre-select current font
                string currentFont = _textFontFamily;
                fontList.SelectedItem = fonts.FirstOrDefault(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase));
                if (fontList.SelectedItem != null) fontList.ScrollIntoView(fontList.SelectedItem);

                searchBox.TextChanged += (s, e) =>
                {
                    if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                    {
                        var filtered = fonts.Where(f => f.Contains(s.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                        fontList.ItemsSource = filtered;
                    }
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(searchBox);
                stackPanel.Children.Add(fontList);

                var dialog = new ContentDialog
                {
                    Title = Strings.FontSelectionTitle,
                    Content = stackPanel,
                    PrimaryButtonText = Strings.DialogPrimary,
                    CloseButtonText = Strings.DialogClose,
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary,
                    RequestedTheme = RootGrid.ActualTheme
                };

                // ★ 핵심: PreviewKeyDown을 사용하여 입력 컨트롤보다 먼저 ESC를 감지합니다.
                stackPanel.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        dialog.Hide(); // 강제 닫기
                        e.Handled = true; // 이벤트 전파 중단
                    }
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary && fontList.SelectedItem is string selectedFont)
                {
                    SetTextFont(selectedFont);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing font picker: {ex.Message}");
            }
        }

        private async void SetTextFont(string fontFamily)
        {
            _textFontFamily = fontFamily;
            SaveTextSettings();
            await RefreshTextDisplay();
        }

        private void UiFontMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowUiFontPickerDialog();
        }

        private async Task ShowUiFontPickerDialog()
        {
            try
            {
                var fonts = CanvasTextFormat.GetSystemFontFamilies()
                    .OrderBy(f => f)
                    .ToList();

                var searchBox = new AutoSuggestBox
                {
                    PlaceholderText = Strings.FontSearchPlaceholder,
                    QueryIcon = new SymbolIcon(Symbol.Find),
                    Margin = new Thickness(0, 0, 0, 10),
                    Width = 300
                };

                var fontList = new ListView
                {
                    ItemsSource = fonts,
                    SelectionMode = ListViewSelectionMode.Single,
                    MaxHeight = 400,
                    Width = 300,
                    ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                        @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='{Binding}' FontFamily='{Binding}' FontSize='16' VerticalAlignment='Center' Padding='4'/>
                        </DataTemplate>")
                };

                // Pre-select current UI font
                string currentFont = _uiFontFamily;
                if (string.IsNullOrEmpty(currentFont))
                {
                    // Try to get current default font if possible, or just don't select
                }
                else
                {
                    fontList.SelectedItem = fonts.FirstOrDefault(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase));
                }

                if (fontList.SelectedItem != null) fontList.ScrollIntoView(fontList.SelectedItem);

                searchBox.TextChanged += (s, e) =>
                {
                    if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                    {
                        var filtered = fonts.Where(f => f.Contains(s.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                        fontList.ItemsSource = filtered;
                    }
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(searchBox);
                stackPanel.Children.Add(fontList);

                var dialog = new ContentDialog
                {
                    Title = Strings.UIFontSelectionTitle,
                    Content = stackPanel,
                    PrimaryButtonText = Strings.DialogPrimary,
                    CloseButtonText = Strings.DialogClose,
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary,
                    RequestedTheme = RootGrid.ActualTheme
                };

                // PreviewKeyDown for ESC
                stackPanel.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        dialog.Hide();
                        e.Handled = true;
                    }
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary && fontList.SelectedItem is string selectedFont)
                {
                    SetUiFont(selectedFont);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing UI font picker: {ex.Message}");
            }
        }

        private void SetUiFont(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily) || fontFamily == "Unknown")
            {
                _uiFontFamily = "";
                return; // Or reset to system default
            }

            _uiFontFamily = fontFamily;
            FontFamily ff;
            try { ff = new FontFamily(fontFamily); }
            catch { return; }

            if (RootFontControl != null)
            {
                RootFontControl.FontFamily = ff;
            }

            // Explicitly set on sidebar containers to ensure inheritance in virtualized templates
            if (FileListView != null) FileListView.FontFamily = ff;
            if (FileGridView != null) FileGridView.FontFamily = ff;

            // Cast to FrameworkElement/Control/TextBlock to avoid build ambiguity
            if (CurrentPathText is TextBlock cpt) cpt.FontFamily = ff;
            if (NotificationText is TextBlock nt) nt.FontFamily = ff;
            if (FileNameText is TextBlock fnt) fnt.FontFamily = ff;
            if (ZoomLevelText is TextBlock zlt) zlt.FontFamily = ff;
            if (TextSizeLevelText is TextBlock tslt) tslt.FontFamily = ff;

            // Favorites & Recent Containers (Pivots are Controls, so they have FontFamily)
            if (FavoritesPivot != null) FavoritesPivot.FontFamily = ff;
            if (SidebarFavoritesPivot != null) SidebarFavoritesPivot.FontFamily = ff;

            // Refresh dynamic items to apply font to already created elements
            UpdateFavoritesMenu();
            UpdateRecentMenu();
            UpdateWebDavServerList();

            // Update app resources to affect popups/dialogs and theme-bound items
            try
            {
                var resources = Application.Current.Resources;
                resources["ContentControlThemeFontFamily"] = ff;
                resources["ControlContentThemeFontFamily"] = ff;
                resources["TextControlFontFamily"] = ff;
                resources["ComboBoxPlaceholderTextThemeFontFamily"] = ff;
                resources["ContentPresenterFontFamily"] = ff;
                resources["ListViewItemFontFamily"] = ff;
                resources["GridViewItemFontFamily"] = ff;
                resources["MenuFlyoutItemFontFamily"] = ff;
                resources["PickerPlaceholderTextFontFamily"] = ff;

                // Force WinUI to re-evaluate all {ThemeResource} bindings by toggling the theme
                if (RootGrid != null)
                {
                    var currentTheme = RootGrid.RequestedTheme;
                    // Switch to an explicit theme then back to force reload
                    RootGrid.RequestedTheme = (currentTheme == ElementTheme.Dark) ? ElementTheme.Light : ElementTheme.Dark;
                    RootGrid.RequestedTheme = currentTheme;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Font resource update error: {ex.Message}");
            }

            SaveTextSettings();
        }



        private async void ToggleFont()
        {
            if (_textFontFamily.Contains("Yu Gothic"))
                _textFontFamily = "Yu Mincho";
            else
                _textFontFamily = "Yu Gothic";

            SaveTextSettings();
            await RefreshTextDisplay();
        }

        private void TextSizeUpButton_Click(object sender, RoutedEventArgs e)
        {
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

        private async void RootGrid_Text_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled) return;
            if (!_isTextMode) return;

            // Prevent file navigation with arrows/space in text mode
            // Using PreviewKeyDown allows us to intercept before ListView gets it
 
            if (e.Key == Windows.System.VirtualKey.Home)
            {
                if (_isVerticalMode)
                {
                    _verticalNavHistory.Clear();
                    await RenderVerticalDynamicPageAsync(0);
                    UpdateTextStatusBar();
                }
                else if (_isAozoraMode && _aozoraBlocks.Count > 0)
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
                if (_isVerticalMode)
                {
                    _verticalNavHistory.Clear();
                    await RenderVerticalDynamicPageAsync(999999);
                    UpdateTextStatusBar();
                }
                else if (_isAozoraMode && _aozoraBlocks.Count > 0)
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
                if (_isVerticalMode)
                {
                    NavigateVerticalPage(1);
                }
                else
                {
                    int dir = ShouldInvertControls ? 1 : -1;
                    if (_isAozoraMode)
                    {
                        NavigateAozoraPage(dir);
                    }
                    else if (TextScrollViewer != null)
                    {
                        NavigateTextPage(dir);
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                if (_isVerticalMode)
                {
                    NavigateVerticalPage(-1);
                }
                else
                {
                    int dir = ShouldInvertControls ? -1 : 1;
                    if (_isAozoraMode)
                    {
                        NavigateAozoraPage(dir);
                    }
                    else if (TextScrollViewer != null)
                    {
                        NavigateTextPage(dir);
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Add || e.Key == (Windows.System.VirtualKey)187) // +
            {
                IncreaseTextSize();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Subtract || e.Key == (Windows.System.VirtualKey)189) // -
            {
                DecreaseTextSize();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.A)
            {
                ToggleAozoraMode();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.V)
            {
                _ = AddToRecentAsync(true);
                _isVerticalMode = !_isVerticalMode;
                if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = _isVerticalMode;
                SaveTextSettings();
                ToggleVerticalMode();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.F)
            {
                ToggleFont();
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
                    ToggleTheme();
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.D)
            {
                var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                    Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                if (!ctrlPressed)
                {
                    GlobalThemeToggleButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        private async Task ShowGoToLineDialog()
        {
            int currentLine = 1;
            int totalLines = 1;
            string title = Strings.DialogTitle;

            if (_currentPdfDocument != null)
            {
                totalLines = (int)_currentPdfDocument.PageCount;
                currentLine = _currentIndex + 1;
                title = Strings.GoToPageTitle;
            }
            else if (_isAozoraMode && _aozoraBlocks.Count > 0)
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
                Title = title,
                Content = input,
                PrimaryButtonText = Strings.DialogPrimary,
                CloseButtonText = Strings.DialogClose,
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
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

            if (_currentPdfDocument != null)
            {
                int pageIndex = line - 1;
                if (pageIndex >= 0 && pageIndex < (int)_currentPdfDocument.PageCount)
                {
                    _currentIndex = pageIndex;
                    _ = DisplayCurrentImageAsync();
                }
                return;
            }

            if (_isVerticalMode)
            {
                _ = PrepareVerticalTextAsync(line);
                return;
            }

            if (_isAozoraMode && _aozoraBlocks.Count > 0)
            {
                // Find block by line number
                int targetIdx = 0;
                for (int i = 0; i < _aozoraBlocks.Count; i++)
                {
                    if (_aozoraBlocks[i].SourceLineNumber >= line)
                    {
                        if (_aozoraBlocks[i].SourceLineNumber == line)
                        {
                            targetIdx = i;
                        }
                        else
                        {
                            targetIdx = i > 0 ? i - 1 : 0;
                        }
                        break;
                    }
                    targetIdx = i;
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
                // Aozora mode now uses Win2D CanvasControl (AozoraTextCanvas) for rendering.
                // No XAML element preparation needed here.
                return;
            }

            if (args.Element is TextBlock tb && _textLines.Count > args.Index)
            {
                var line = _textLines[args.Index];

                // Binding Properties
                tb.FontSize = line.FontSize;
                tb.FontFamily = new FontFamily(line.FontFamily);
                tb.FontWeight = GetFontWeightForFamily(line.FontFamily);
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
                RootGrid.Focus(FocusState.Programmatic);
            }
        }

        private void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_isVerticalMode) return;
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
            if (!_isTextMode && !_isEpubMode) return;
            if (_isVerticalMode) { UpdateVerticalStatusBar(); return; }
            if (_isAozoraMode) { UpdateAozoraStatusBar(); return; }
            if (_isEpubMode) { UpdateEpubStatus(); return; }

            if (fileName != null) FileNameText.Text = GetFormattedDisplayName(fileName, _currentTextArchiveEntryKey != null);

            int total = totalLines ?? _textLines.Count;
            if (total == 0) total = 1;

            if (TextScrollViewer != null)
            {
                int currentLine = GetTopVisibleLineIndex();
                if (currentLine > total) currentLine = total;

                double progress = (TextScrollViewer.ExtentHeight > 0) ? (TextScrollViewer.VerticalOffset + TextScrollViewer.ViewportHeight) * 100.0 / TextScrollViewer.ExtentHeight : 0;
                if (progress > 100) progress = 100;

                ImageInfoText.Text = Strings.LineInfo(currentLine, total);
                TextProgressText.Text = $"{progress:F1}%";

                // Update Page Info if calculated
                if (_isPageCalculationCompleted && _calculatedTotalHeight > 0 && TextScrollViewer.ViewportHeight > 0)
                {
                    int totalPages = (int)Math.Ceiling(_calculatedTotalHeight / TextScrollViewer.ViewportHeight);
                    int calcCurrentPage = (int)Math.Floor(TextScrollViewer.VerticalOffset / TextScrollViewer.ViewportHeight) + 1;

                    if (totalPages < 1) totalPages = 1;
                    if (calcCurrentPage > totalPages) calcCurrentPage = totalPages;
                    if (calcCurrentPage < 1) calcCurrentPage = 1;

                    ImageIndexText.Text = $"{calcCurrentPage} / {totalPages}";
                }
                else if (!_isPageCalculationCompleted)
                {
                    ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
                }
                else
                {
                    ImageIndexText.Text = "";
                }

                // Throttle Recent update: only if line changed
                if (currentLine != _lastRecentSaveLine)
                {
                    _lastRecentSaveLine = currentLine;
                    _ = AddToRecentAsync(true);
                }
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

        private void TextArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Handle TextArea resize for window drag resizing in Aozora mode.
            // AozoraTextCanvas_SizeChanged handles the canvas itself;
            // this covers the outer Grid container resize event wired in XAML.
            if (!_isAozoraMode) return;
            if (AozoraTextCanvas != null)
            {
                RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                StartAozoraPageCalculationAsync();
            }
        }

        // --- Page Calculation Logic ---
        private double _calculatedTotalHeight = 0;
        private bool _isPageCalculationCompleted = false;
        private CancellationTokenSource? _pageCalcCts;
        private FontFamily? _cachedFontFamily = null;

        private async void StartPageCalculationAsync()
        {
            // Early exit if in Aozora mode (use Aozora's own page calculation)
            if (_isAozoraMode) return;

            _pageCalcCts?.Cancel();
            _pageCalcCts = new CancellationTokenSource();
            var token = _pageCalcCts.Token;

            _isPageCalculationCompleted = false;
            _calculatedTotalHeight = 0;
            _cachedFontFamily = null;
            UpdateTextStatusBar(); // Reset display to just %

            // Robust check for Viewport readiness
            // Wait up to 5 seconds (50 * 100ms) for layout to settle
            int retryCount = 0;
            while (TextScrollViewer == null || TextScrollViewer.ViewportHeight <= 0 || TextScrollViewer.ViewportWidth <= 0)
            {
                if (retryCount++ > 50)
                {
                    // Timeout: Fallback to ExtentHeight
                    if (TextScrollViewer != null)
                    {
                        _calculatedTotalHeight = TextScrollViewer.ExtentHeight;
                        _isPageCalculationCompleted = true;
                        UpdateTextStatusBar();
                    }
                    return;
                }
                try { await Task.Delay(100, token); } catch { return; }
            }

            if (_textLines.Count == 0) return;

            double viewportWidth = TextScrollViewer.ViewportWidth;
            if (viewportWidth <= 0) viewportWidth = TextScrollViewer.ActualWidth; // Fallback

            try
            {
                // Dummy TextBlock for measurement
                var dummy = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };

                double totalH = 0;

                // Use Stopwatch for time slicing
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Snapshot the list reference to iterate safely
                var linesToCalc = _textLines;

                // Cache the font family if it's common
                if (!string.IsNullOrEmpty(_textFontFamily))
                {
                    _cachedFontFamily = new FontFamily(_textFontFamily);
                }

                foreach (var line in linesToCalc)
                {
                    if (token.IsCancellationRequested) return;

                    // Apply properties matching TextItemsRepeater_ElementPrepared logic
                    dummy.FontSize = line.FontSize;
                    if (_cachedFontFamily != null && line.FontFamily == _textFontFamily)
                        dummy.FontFamily = _cachedFontFamily;
                    else
                        dummy.FontFamily = new FontFamily(line.FontFamily);
                    
                    dummy.FontWeight = GetFontWeightForFamily(line.FontFamily);

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

                    // Yield if we've used up our time slice (e.g., 15ms to allow ~60fps)
                    if (sw.ElapsedMilliseconds > 15)
                    {
                        await Task.Delay(1, token);
                        sw.Restart();
                    }
                }

                if (totalH > 0)
                {
                    _calculatedTotalHeight = totalH;
                }
                else if (TextScrollViewer != null)
                {
                    // Fallback if calculation resulted in 0 (weird)
                    _calculatedTotalHeight = TextScrollViewer.ExtentHeight;
                }

                _isPageCalculationCompleted = true;

                // Final update
                UpdateTextStatusBar();
            }
            catch (OperationCanceledException)
            {
                // Expected on new calculation start
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating text pages: {ex.Message}");
                // Fallback on error
                if (TextScrollViewer != null)
                {
                    _calculatedTotalHeight = TextScrollViewer.ExtentHeight;
                    _isPageCalculationCompleted = true;
                    UpdateTextStatusBar();
                }
            }
        }

        private int GetTopVisibleLineIndex()
        {
            if (TextItemsRepeater == null || TextScrollViewer == null) return 1;
            if (_textLines == null || _textLines.Count == 0) return 1;

            try
            {
                // ScrollViewer의 Content 시작 지점(Padding.Top)을 기준으로 계산
                double viewportTop = TextScrollViewer.Padding.Top;

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

                    // 해당 라인이 뷰포트의 상단 경계(Padding 포함)를 걸치고 있는지 확인
                    if (top <= viewportTop && bottom > viewportTop)
                    {
                        int idx = TextItemsRepeater.GetElementIndex(child);
                        if (idx >= 0) return idx + 1;
                    }

                    // 정확히 걸치는 것을 못 찾을 경우 보정 지점에 가장 가까운 것을 찾음
                    double dist = Math.Abs(top - viewportTop);
                    if (dist < minDist)
                    {
                        minDist = dist;
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

        private async void ScrollToLine(int line)
        {
            if (TextItemsRepeater == null || TextScrollViewer == null) return;
            if (line < 1) line = 1;
            int index = line - 1;
            if (_textLines == null || _textLines.Count == 0) return;
            if (index >= _textLines.Count) index = _textLines.Count - 1;
            if (index < 0) return;

            double lineH = _textFontSize * 1.8;
            double targetOffset = index * lineH;

            // 1. ItemsRepeater가 데이터 바인딩 후 UI 레이아웃을 계산할 수 있도록 대기
            await Task.Delay(100);

            // 2. 가상화(Virtualization) 환경에서는 ExtentHeight가 즉시 전체를 반영하지 못하므로,
            // 목표 오프셋까지 도달할 수 있도록 점진적으로 ChangeView를 시도하여 Extent를 늘립니다.
            for (int i = 0; i < 5; i++)
            {
                TextScrollViewer.ChangeView(null, targetOffset, null, true);
                await Task.Delay(50);

                // 스크롤 오프셋이 목표치 근처에 도달했거나 화면 전체 Extent가 목표를 수용할 만큼 충분히 커졌다면 중단
                if (Math.Abs(TextScrollViewer.VerticalOffset - targetOffset) <= lineH * 2 ||
                    TextScrollViewer.ScrollableHeight >= targetOffset)
                {
                    TextScrollViewer.ChangeView(null, targetOffset, null, true);
                    break;
                }
            }

            // 3. 정밀 위치 보정 (실제 UI Element 생성 후 화면 상단에 맞춤)
            try
            {
                var element = TextItemsRepeater.GetOrCreateElement(index);
                if (element != null)
                {
                    element.StartBringIntoView(new BringIntoViewOptions
                    {
                        VerticalAlignmentRatio = 0,
                        AnimationDesired = false
                    });
                }
            }
            catch
            {
                // 무시하고 넘어갑니다 (이미 ChangeView로 대부분 맞춰진 상태)
                TextScrollViewer.ChangeView(null, targetOffset, null, true);
            }

            UpdateTextStatusBar();
        }
    }
}
