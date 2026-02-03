using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Markdig;
using System.Text.RegularExpressions;
using Windows.Foundation;
using System.Text.Json;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private string _currentTextContent = "";
        private string _currentTextFilePath = "";
        private DispatcherQueueTimer? _pageInfoTimer;

        // Virtualized text viewer for very large plain .txt files
        private System.Collections.ObjectModel.ObservableCollection<string> _virtualTextLines = new System.Collections.ObjectModel.ObservableCollection<string>();
        private bool _useVirtualTextViewer = false;
        private ScrollViewer? _virtualScrollViewer = null;
        private bool _virtualEventsAttached = false;

        // --- 탐색 속도 개선을 위한 변수 추가 ---
        private DispatcherQueueTimer? _navDebounceTimer;
        private StorageFile? _pendingFile;
        // -------------------------------------														
        private bool _isCurrentTextContentPreprocessed = false;
        private string? _currentTextTempHtmlPath;
        private const int MaxNavigateToStringLength = 600_000;

        private static int CountReplacementChars(string s)
        {
            int count = 0;
            foreach (var ch in s)
            {
                if (ch == '\ufffd') count++;
            }
            return count;
        }

        private static int CountHangul(string s)
        {
            int count = 0;
            foreach (var ch in s)
            {
                if (ch >= 0xAC00 && ch <= 0xD7A3) count++;
            }
            return count;
        }

        private static int CountKana(string s)
        {
            int count = 0;
            foreach (var ch in s)
            {
                if ((ch >= 0x3040 && ch <= 0x309F) || (ch >= 0x30A0 && ch <= 0x30FF)) count++;
            }
            return count;
        }

        private static bool LooksLikeAozora(string s)
        {
            // More strict Aozora Bunko detection - require multiple indicators
            int aozoraIndicators = 0;
            
            if (s.Contains("青空文庫")) aozoraIndicators++;
            if (s.Contains("底本：")) aozoraIndicators++;
            if (s.Contains("［＃")) aozoraIndicators++;
            
            // Only consider it Aozora if we have multiple strong indicators
            // or a combination of indicators with specific patterns
            bool hasStrongIndicators = aozoraIndicators >= 2;
            bool hasSpecificPattern = s.Contains("［＃") && (s.Contains("地から") || s.Contains("改ページ") || s.Contains("「"));
            
            return hasStrongIndicators || hasSpecificPattern;
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static System.Text.Encoding ChooseTextEncoding(byte[] bytes)
        {
            var utf8 = System.Text.Encoding.UTF8;
            var euckr = System.Text.Encoding.GetEncoding(949);
            var sjis = System.Text.Encoding.GetEncoding(932);

            System.Diagnostics.Debug.WriteLine("--- ChooseTextEncoding Analysis ---");

            // 1. UTF-8 BOM이 있다면 무조건 UTF-8로 처리
            if (HasUtf8Bom(bytes))
            {
                System.Diagnostics.Debug.WriteLine("UTF-8 BOM detected, using UTF-8");
                return utf8;
            }

            string utf8Text = "";
            try { utf8Text = utf8.GetString(bytes); } catch { }
            string euckrText = "";
            try { euckrText = euckr.GetString(bytes); } catch { }
            string sjisText = "";
            try { sjisText = sjis.GetString(bytes); } catch { }

            int utf8Rep = CountReplacementChars(utf8Text);
            int euckrRep = CountReplacementChars(euckrText);
            int sjisRep = CountReplacementChars(sjisText);

            System.Diagnostics.Debug.WriteLine($"UTF-8: {utf8Rep} replacement chars");
            System.Diagnostics.Debug.WriteLine($"EUC-KR: {euckrRep} replacement chars");
            System.Diagnostics.Debug.WriteLine($"S-JIS: {sjisRep} replacement chars");

            bool utf8Aozora = LooksLikeAozora(utf8Text);
            bool euckrAozora = LooksLikeAozora(euckrText);
            bool sjisAozora = LooksLikeAozora(sjisText);

            System.Diagnostics.Debug.WriteLine($"UTF-8 Aozora: {utf8Aozora}");
            System.Diagnostics.Debug.WriteLine($"EUC-KR Aozora: {euckrAozora}");
            System.Diagnostics.Debug.WriteLine($"S-JIS Aozora: {sjisAozora}");

            if (sjisAozora && !euckrAozora) 
            {
                System.Diagnostics.Debug.WriteLine("S-JIS Aozora detected (not EUC-KR), choosing S-JIS");
                return sjis;
            }
            if (euckrAozora && !sjisAozora) 
            {
                System.Diagnostics.Debug.WriteLine("EUC-KR Aozora detected (not S-JIS), choosing EUC-KR");
                return euckr;
            }
            if (utf8Aozora && !sjisAozora && !euckrAozora) 
            {
                System.Diagnostics.Debug.WriteLine("UTF-8 Aozora detected (not S-JIS/EUC-KR), choosing UTF-8");
                return utf8;
            }

            int utf8Hangul = CountHangul(utf8Text);
            int euckrHangul = CountHangul(euckrText);
            int sjisHangul = CountHangul(sjisText);
            int utf8Kana = CountKana(utf8Text);
            int euckrKana = CountKana(euckrText);
            int sjisKana = CountKana(sjisText);

            System.Diagnostics.Debug.WriteLine($"UTF-8: {utf8Hangul} hangul, {utf8Kana} kana");
            System.Diagnostics.Debug.WriteLine($"EUC-KR: {euckrHangul} hangul, {euckrKana} kana");
            System.Diagnostics.Debug.WriteLine($"S-JIS: {sjisHangul} hangul, {sjisKana} kana");

            int bestScore = int.MinValue;
            System.Text.Encoding best = utf8;

            void consider(System.Text.Encoding enc, int rep, int hangul, int kana)
            {
                int score = 0;
                score -= rep * 1000;
                score += hangul * 3;
                score += kana * 3;
                
                // Give extra bonus to UTF-8 for Japanese content
                if (enc == utf8 && kana > 0)
                {
                    score += kana * 2; // Extra bonus for UTF-8 with kana
                }
                
                // Give bonus to Shift-JIS for Japanese content without replacement chars
                if (enc == sjis && kana > 0 && rep == 0)
                {
                    score += kana * 1; // Small bonus for valid Shift-JIS
                }
                
                string encName = enc == utf8 ? "UTF-8" : enc == euckr ? "EUC-KR" : "S-JIS";
                System.Diagnostics.Debug.WriteLine($"{encName} score: {score} (rep:{rep}, hangul:{hangul}, kana:{kana})");
                
                if (score > bestScore)
                {
                    bestScore = score;
                    best = enc;
                }
            }
            consider(utf8, utf8Rep, utf8Hangul, utf8Kana);
            consider(euckr, euckrRep, euckrHangul, euckrKana);
            consider(sjis, sjisRep, sjisHangul, sjisKana);

            System.Diagnostics.Debug.WriteLine($"Best score: {bestScore}, best encoding: {(best == utf8 ? "UTF-8" : best == euckr ? "EUC-KR" : "S-JIS")}");
            System.Diagnostics.Debug.WriteLine($"Final choice: {(best == utf8 ? "UTF-8" : best == euckr ? "EUC-KR" : "S-JIS")}");
            System.Diagnostics.Debug.WriteLine("--- ChooseTextEncoding Analysis Complete ---");
            return best;
        }

        private ScrollViewer? FindScrollViewer(DependencyObject? root)
        {
            if (root == null) return null;
            try
            {
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is ScrollViewer sv) return sv;
                    var nested = FindScrollViewer(child);
                    if (nested != null) return nested;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 파일 로딩을 디바운싱하여 처리하는 메서드
        /// </summary>
        // 이 메서드가 탐색기 성능의 핵심입니다.
        private void LoadTextFromFileWithDebounce(StorageFile file)
        {
            _pendingFile = file;

            if (_navDebounceTimer == null)
            {
                _navDebounceTimer = this.DispatcherQueue.CreateTimer();
                _navDebounceTimer.Interval = TimeSpan.FromMilliseconds(50); // 50ms 대기 (이 수치를 조절하여 감도 변경 가능)
                _navDebounceTimer.Tick += async (s, e) =>
                {
                    _navDebounceTimer.Stop();
                    if (_pendingFile != null)
                    {
                        // 여기서 실제 로드 함수를 호출합니다. 
                        // Task<void>가 아닌 Task를 반환하는 메서드여야 오류가 나지 않습니다.
                        await LoadTextFromFileAsync(_pendingFile);
                        _pendingFile = null;
                    }
                };
            }

            _navDebounceTimer.Stop();
            _navDebounceTimer.Start();
        }

        private async Task LoadTextFromFileAsync(StorageFile file)
        {
            _isTextMode = true;
            _currentTextFilePath = file.Path;
            
            try
            {
                FileNameText.Text = $"{file.Name} (읽는 중...)";

                // Check file size to determine loading strategy
                var basicProperties = await file.GetBasicPropertiesAsync();
                var fileSize = basicProperties.Size;
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                
                // Use streaming for files larger than 0.3MB
                if (fileSize > 0.3 * 1024 * 1024)
                {
                    System.Diagnostics.Debug.WriteLine($"Large file detected: {fileSize} bytes, using streaming load");
                    // For plain .txt files use the virtualized ListView for best responsiveness
                    bool useVirtual = ext == ".txt";
                    await LoadLargeTextFileAsync(file, useVirtual);
                    return;
                }

                // For smaller files, read whole file (we may still use virtualized viewer for .txt)
                byte[] bytes = await File.ReadAllBytesAsync(file.Path);
                
                string content = "";
                System.Text.Encoding? detectedEncoding = null;
                
                // 1. UTF-8 BOM 확인
                if (HasUtf8Bom(bytes))
                {
                    detectedEncoding = System.Text.Encoding.UTF8;
                    System.Diagnostics.Debug.WriteLine("UTF-8 BOM detected, using UTF-8");
                    // BOM 제거 후 디코딩
                    bytes = bytes.Skip(3).ToArray();
                    content = detectedEncoding!.GetString(bytes);
                }
                else
                {
                    // 2. BOM이 없다면 UTF-8로 먼저 시도하고 일본어 문자 확인
                    System.Diagnostics.Debug.WriteLine($"=== Starting encoding detection for file: {file.Name} ===");
                    System.Diagnostics.Debug.WriteLine($"File size: {bytes.Length} bytes");
                    
                    try
                    {
                        content = System.Text.Encoding.UTF8.GetString(bytes);
                        detectedEncoding = System.Text.Encoding.UTF8;
                        System.Diagnostics.Debug.WriteLine("Initial UTF-8 decoding successful");
                        
                        // If UTF-8 decoding produces valid Japanese characters without replacement chars, 
                        // and has significant Japanese content, prefer UTF-8
                        if (!content.Contains('\ufffd'))
                        {
                            int kanaCount = CountKana(content);
                            System.Diagnostics.Debug.WriteLine($"UTF-8 kana count: {kanaCount}");
                            if (kanaCount > 5 // If we have significant kana characters, likely UTF-8
                            )
                            {
                                System.Diagnostics.Debug.WriteLine($"UTF-8 with {kanaCount} kana characters detected, preferring UTF-8");
                                // Skip the encoding fallback logic below
                                detectedEncoding = System.Text.Encoding.UTF8;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("UTF-8 has few kana characters, will check HTML charset");
                            }
                        }
                        else
                        {
                            int replacementCount = CountReplacementChars(content);
                            System.Diagnostics.Debug.WriteLine($"UTF-8 has {replacementCount} replacement characters, will check HTML charset");
                        }
                    }
                    catch 
                    { 
                        System.Diagnostics.Debug.WriteLine("Initial UTF-8 decoding failed");
                    }
                }

                // HTML 파일인 경우 내부 charset 확인
                ext = Path.GetExtension(file.Name).ToLowerInvariant();
                bool isHtml = (ext == ".html" || ext == ".htm");
                System.Diagnostics.Debug.WriteLine($"Is HTML: {isHtml}");

                // 인코딩 개체 정의
                var euckr = System.Text.Encoding.GetEncoding(949); // Korean (EUC-KR)
                var sjis = System.Text.Encoding.GetEncoding(932);  // Japanese (Shift-JIS)

                if (isHtml)
                {
                    System.Diagnostics.Debug.WriteLine("--- Starting HTML charset detection ---");
                    
                    // HTML 내 메타 태그 확인 - 원본 바이트에서 각 인코딩으로 디코딩하여 확인
                    // HTML 파일은 charset 선언을 우선시함
                    System.Text.Encoding? htmlDetectedEncoding = null;
                    string htmlContent = "";
                    
                    // 먼저 S-JIS로 디코딩하여 charset 확인
                    try
                    {
                        string sjisContent = sjis.GetString(bytes);
                        System.Diagnostics.Debug.WriteLine("S-JIS decoding successful, checking for charset patterns");
                        
                        if (sjisContent.Contains("Shift_JIS", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("windows-31j", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=shift_jis", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=\"shift_jis\"", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset='shift_jis'", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=shift-jis", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=\"shift-jis\"", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset='shift-jis'", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=x-sjis", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=\"x-sjis\"", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset='x-sjis'", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=ms_kanji", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset=\"ms_kanji\"", StringComparison.OrdinalIgnoreCase) ||
                            sjisContent.Contains("charset='ms_kanji'", StringComparison.OrdinalIgnoreCase))
                        {
                            htmlDetectedEncoding = sjis;
                            htmlContent = sjisContent;
                            System.Diagnostics.Debug.WriteLine("S-JIS charset detected in HTML, using Shift-JIS encoding");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No S-JIS charset patterns found in decoded content");
                        }
                    }
                    catch 
                    { 
                        System.Diagnostics.Debug.WriteLine("S-JIS decoding failed");
                    }

                    // S-JIS가 아니면 EUC-KR로 확인
                    if (htmlDetectedEncoding == null)
                    {
                        try
                        {
                            string euckrContent = euckr.GetString(bytes);
                            System.Diagnostics.Debug.WriteLine("EUC-KR decoding successful, checking for charset patterns");
                            
                            if (euckrContent.Contains("euc-kr", StringComparison.OrdinalIgnoreCase) ||
                                euckrContent.Contains("ks_c_5601-1987", StringComparison.OrdinalIgnoreCase) ||
                                euckrContent.Contains("cp949", StringComparison.OrdinalIgnoreCase) ||
                                euckrContent.Contains("charset=euc-kr", StringComparison.OrdinalIgnoreCase) ||
                                euckrContent.Contains("charset=\"euc-kr\"", StringComparison.OrdinalIgnoreCase) ||
                                euckrContent.Contains("charset='euc-kr'", StringComparison.OrdinalIgnoreCase))
                            {
                                htmlDetectedEncoding = euckr;
                                htmlContent = euckrContent;
                                System.Diagnostics.Debug.WriteLine("EUC-KR charset detected in HTML, using EUC-KR encoding");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("No EUC-KR charset patterns found in decoded content");
                            }
                        }
                        catch 
                        { 
                            System.Diagnostics.Debug.WriteLine("EUC-KR decoding failed");
                        }
                    }

                    // 여전히 charset이 없으면 UTF-8인지 확인
                    if (htmlDetectedEncoding == null)
                    {
                        System.Diagnostics.Debug.WriteLine("No specific charset detected, checking UTF-8");
                        
                        // 기존에 UTF-8로 디코딩된 content가 있으면 그것을 사용
                        if (!string.IsNullOrEmpty(content))
                        {
                            System.Diagnostics.Debug.WriteLine("Using existing UTF-8 content for charset check");
                            if (content.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("charset=\"utf-8\"", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("charset='utf-8'", StringComparison.OrdinalIgnoreCase))
                            {
                                htmlDetectedEncoding = System.Text.Encoding.UTF8;
                                htmlContent = content;
                                System.Diagnostics.Debug.WriteLine("UTF-8 charset detected in existing content, keeping UTF-8 encoding");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("No UTF-8 charset found in existing content");
                            }
                        }
                        // UTF-8 content가 없으면 직접 UTF-8로 디코딩하여 확인
                        else
                        {
                            try
                            {
                                string utf8Content = System.Text.Encoding.UTF8.GetString(bytes);
                                System.Diagnostics.Debug.WriteLine("Decoded fresh UTF-8 content for charset check");
                                if (utf8Content.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase) ||
                                    utf8Content.Contains("charset=\"utf-8\"", StringComparison.OrdinalIgnoreCase) ||
                                    utf8Content.Contains("charset='utf-8'", StringComparison.OrdinalIgnoreCase))
                                {
                                    htmlDetectedEncoding = System.Text.Encoding.UTF8;
                                    htmlContent = utf8Content;
                                    System.Diagnostics.Debug.WriteLine("UTF-8 charset detected in fresh content, using UTF-8 encoding");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("No UTF-8 charset found in fresh content");
                                }
                            }
                            catch 
                            { 
                                System.Diagnostics.Debug.WriteLine("Fresh UTF-8 decoding failed");
                            }
                        }
                    }

                    // HTML에서 charset을 찾았으면 그것을 사용
                    if (htmlDetectedEncoding != null)
                    {
                        content = htmlContent;
                        detectedEncoding = htmlDetectedEncoding;
                        System.Diagnostics.Debug.WriteLine($"HTML charset detected, using encoding: {detectedEncoding.WebName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No HTML charset detected, will use fallback logic");
                    }
                    
                    System.Diagnostics.Debug.WriteLine("--- HTML charset detection complete ---");
                }

                // 3. 디코딩 실패 시(깨짐 문자 \ufffd 발견) 또는 빈 내용일 때 폴백
                // 단, 일본어 문자가 많고 깨진 문자가 적으면 UTF-8 유지
                if (string.IsNullOrEmpty(content) || content.Contains('\ufffd'))
                {
                    // Check if we have significant Japanese content before falling back
                    int currentKanaCount = CountKana(content);
                    int replacementCount = CountReplacementChars(content);
                    
                    System.Diagnostics.Debug.WriteLine($"Fallback analysis - kana: {currentKanaCount}, replacements: {replacementCount}");
                    
                    // If we have Japanese content but few replacement chars, stick with UTF-8
                    if (currentKanaCount > 10 && replacementCount < 5)
                    {
                        System.Diagnostics.Debug.WriteLine($"Keeping UTF-8: {currentKanaCount} kana, {replacementCount} replacements");
                        // Keep current content and encoding
                    }
                    else
                    {
                        // For HTML files, do special S-JIS analysis before falling back
                        if (isHtml)
                        {
                            System.Diagnostics.Debug.WriteLine("HTML file with issues, doing special S-JIS analysis");
                            
                            // Check S-JIS directly
                            try
                            {
                                string sjisContent = sjis.GetString(bytes);
                                int sjisReplacements = CountReplacementChars(sjisContent);
                                int sjisKana = CountKana(sjisContent);
                                
                                System.Diagnostics.Debug.WriteLine($"S-JIS analysis - replacements: {sjisReplacements}, kana: {sjisKana}");
                                
                                // If S-JIS has significantly fewer replacements and has Japanese content, prefer it
                                if (sjisReplacements < replacementCount && sjisKana > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine("S-JIS has fewer replacements and Japanese content, choosing S-JIS");
                                    content = sjisContent;
                                    detectedEncoding = sjis;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("S-JIS not better, using fallback logic");
                                    var chosen = ChooseTextEncoding(bytes);
                                    content = chosen.GetString(bytes);
                                    detectedEncoding = chosen;
                                    System.Diagnostics.Debug.WriteLine($"Chosen encoding: {detectedEncoding?.WebName ?? "unknown"}");
                                }
                            }
                            catch
                            {
                                System.Diagnostics.Debug.WriteLine("S-JIS analysis failed, using fallback logic");
                                var chosen = ChooseTextEncoding(bytes);
                                content = chosen.GetString(bytes);
                                detectedEncoding = chosen;
                                System.Diagnostics.Debug.WriteLine($"Chosen encoding: {detectedEncoding?.WebName ?? "unknown"}");
                            }
                        }
                        else
                        {
                            var chosen = ChooseTextEncoding(bytes);
                            content = chosen.GetString(bytes);
                            detectedEncoding = chosen;
                            System.Diagnostics.Debug.WriteLine($"Chosen encoding: {detectedEncoding?.WebName ?? "unknown"}");
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Final encoding used: {detectedEncoding?.WebName ?? "unknown"}");
                System.Diagnostics.Debug.WriteLine($"Content length: {content?.Length ?? 0} characters");
                System.Diagnostics.Debug.WriteLine($"=== Encoding detection complete ===");

                if (content != null)
                {
                    _currentTextContent = content;
                }
                _isCurrentTextContentPreprocessed = false;

                // If this is a plain text file, use the virtualized ListView for small and large files
                if (ext == ".txt")
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadTextFromFileAsync] Before virtualized setup: _textFontSize = {_textFontSize}pt");
                    _useVirtualTextViewer = true;
                    _virtualTextLines.Clear();
                    var lines = _currentTextContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var l in lines) _virtualTextLines.Add(l);

                    await InitializeTextViewerAsync(); // ensure webview ready for other usages
                    // Ensure virtualized viewer is immediately bound and visible
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[LoadTextFromFileAsync] Setting ItemsSource: _textFontSize = {_textFontSize}pt");
                            VirtualTextListView.ItemsSource = _virtualTextLines;
                            VirtualTextListView.Visibility = Visibility.Visible;
                            TextViewer.Visibility = Visibility.Collapsed;
                            FileNameText.Text = Path.GetFileName(_currentTextFilePath);
                        }
                        catch { }
                    });

                    // Finalize virtualized viewer setup
                    await UpdateVirtualizedTextViewer();
                    System.Diagnostics.Debug.WriteLine($"[LoadTextFromFileAsync] After UpdateVirtualizedTextViewer: _textFontSize = {_textFontSize}pt");
                    ShowTextUI();
                    System.Diagnostics.Debug.WriteLine($"[LoadTextFromFileAsync] After ShowTextUI: _textFontSize = {_textFontSize}pt");
                    UpdateStatusBarForText();
                    _ = AddToRecentAsync();
                    return;
                }

                await InitializeTextViewerAsync();
                await UpdateTextViewer();

                ShowTextUI();
                UpdateStatusBarForText();

                _ = AddToRecentAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading text file: {ex.Message}");
                FileNameText.Text = $"텍스트 파일 로드 오류: {ex.Message}";
            }
        }
        private async Task InitializeTextViewerAsync()
        {
            if (TextViewer.CoreWebView2 == null)
            {
                await TextViewer.EnsureCoreWebView2Async();
                if (TextViewer.CoreWebView2?.Settings != null)
                {
                    TextViewer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    // 브라우저 단축키를 비활성화하여 메인 창의 단축키가 더 잘 작동하도록 함
                    TextViewer.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    // WebView2가 포커스를 받지 않도록 설정
                    TextViewer.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    // 포커스 관련 설정 추가 (사용 가능한 속성만)
                    TextViewer.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    // WebView2가 포커스를 훔치지 않도록 설정
                    TextViewer.CoreWebView2.Settings.AreHostObjectsAllowed = true;
                }

                // WebView2가 포커스를 갖지 않도록 추가 설정
                if (TextViewer.CoreWebView2 != null)
                {
                    // WebView2 초기화 후 포커스 해제
                    await Task.Delay(100);
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        // 다른 컨트롤에 포커스를 설정하여 WebView2에서 포커스를 빼앗음
                        if (SideBySideButton != null)
                        {
                            SideBySideButton.Focus(FocusState.Programmatic);
                        }
                    });
                }

                // Add title change listener as fallback
                if (TextViewer.CoreWebView2 != null)
                {
                    TextViewer.CoreWebView2.DocumentTitleChanged += (s, args) =>
                    {
                        string? title = TextViewer.CoreWebView2?.DocumentTitle;
                        if (!string.IsNullOrEmpty(title) && title.StartsWith("Page: "))
                        {
                            System.Diagnostics.Debug.WriteLine($"Title changed: {title}");
                            // Parse page info from title
                            var parts = title.Split('/');
                            if (parts.Length == 2)
                            {
                                string currentPageStr = parts[0].Replace("Page: ", "").Trim();
                                string totalPagesStr = parts[1].Trim();
                                if (int.TryParse(currentPageStr, out int currentPage) && int.TryParse(totalPagesStr, out int totalPages))
                                {
                                    _ = DispatcherQueue.TryEnqueue(() =>
                                    {
                                        ImageIndexText.Text = $"Page: {currentPage} / {totalPages} (Font: {_textFontSize}pt)";
                                    });
                                }
                            }
                        }
                    };
                }

                TextViewer.WebMessageReceived += (s, args) =>
                {
                    try
                    {
                        var json = args.WebMessageAsJson;
                        // 간단한 파싱 (System.Text.Json 사용)
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.GetProperty("type").GetString() == "keydown")
                        {
                            string keyStr = root.GetProperty("key").GetString() ?? "";
                            bool ctrl = root.GetProperty("ctrl").GetBoolean();

                            Windows.System.VirtualKey key = Windows.System.VirtualKey.None;
                            if (keyStr == "0") keyStr = "Number0";

                            // Explicit mapping for JS names
                            if (keyStr == "Escape") key = Windows.System.VirtualKey.Escape;
                            else if (keyStr == "F11") key = Windows.System.VirtualKey.F11;
                            else Enum.TryParse<Windows.System.VirtualKey>(keyStr, true, out key);

                            if (key != Windows.System.VirtualKey.None)
                            {
                                _ = DispatcherQueue.TryEnqueue(() => ProcessKeyDown(key, ctrl));
                            }
                            else if (keyStr == " ") // Space handled specially
                            {
                                _ = DispatcherQueue.TryEnqueue(() => ProcessKeyDown(Windows.System.VirtualKey.Space, ctrl));
                            }
                        }
                        else if (root.GetProperty("type").GetString() == "pageinfo")
                        {
                            int currentPage = root.GetProperty("current").GetInt32();
                            int totalPages = root.GetProperty("total").GetInt32();
                            System.Diagnostics.Debug.WriteLine($"Received page info: {currentPage}/{totalPages}");
                            _ = DispatcherQueue.TryEnqueue(() =>
                            {
                                ImageIndexText.Text = $"Page: {currentPage} / {totalPages} (Font: {_textFontSize}pt)";
                            });
                        }
                        else if (root.GetProperty("type").GetString() == "mousenavigation")
                        {
                            string? direction = root.GetProperty("direction").GetString();
                            if (!string.IsNullOrEmpty(direction))
                            {
                                System.Diagnostics.Debug.WriteLine($"Mouse navigation: {direction}");
                                
                                _ = DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (direction == "next")
                                    {
                         
                                            _ = ScrollTextPage(true);
                                    
                                    }
                                    else if (direction == "previous")
                                    {
                                       
                              
                                            _ = ScrollTextPage(false);
                                       
                                    }
                                });
                            }
                        }
                    }
                    catch { }
                };
            }
        }

        private void ShowTextUI()
        {
            System.Diagnostics.Debug.WriteLine($"=== ShowTextUI: Text settings at display - Font: {_currentFontFamily}, Bg: {_textBgColor}, Size: {_textFontSize}");
            
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            MainCanvas.Visibility = Visibility.Collapsed;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            TextViewerArea.Visibility = Visibility.Visible;

            TextOptionsButton.Visibility = Visibility.Visible;
            TextSeparator.Visibility = Visibility.Visible;

            // Ensure all buttons remain visible in text mode as requested
            SharpenButton.Visibility = Visibility.Visible;
            SideBySideButton.Visibility = Visibility.Visible;
            NextImageSideButton.Visibility = Visibility.Visible;
            ZoomOutButton.Visibility = Visibility.Visible;
            ZoomInButton.Visibility = Visibility.Visible;
            ZoomFitButton.Visibility = Visibility.Visible;
            ZoomActualButton.Visibility = Visibility.Visible;
            ZoomLevelText.Visibility = Visibility.Visible;

            // Sync side by side button state
            UpdateSideBySideButtonState();

            // Update zoom text to show font size
            ZoomLevelText.Text = $"{_textFontSize}pt";

            // Display filename in title
            Title = $"Uviewer - {Path.GetFileName(_currentTextFilePath)}";
            
            // CRITICAL: For virtual ListView, only update currently visible containers
            // ContainerFromIndex() only works for rendered (visible) containers
            // Scrolled-out containers are handled automatically by ContainerContentChanging event
            if (_useVirtualTextViewer && VirtualTextListView.Items != null && VirtualTextListView.Items.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowTextUI] Updating currently visible containers (total items: {VirtualTextListView.Items.Count})");
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        // CRITICAL: Wait for ListView to be laid out (ActualHeight > 0)
                        // This is essential on first load when ListView hasn't been measured yet
                        int retries = 0;
                        while (VirtualTextListView.ActualHeight <= 0 && retries < 50)
                        {
                            await Task.Delay(10);
                            retries++;
                        }
                        
                        if (VirtualTextListView.ActualHeight <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ShowTextUI] ListView not laid out after waiting");
                            return;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[ShowTextUI] ListView laid out after {retries * 10}ms");
                        
                        // Calculate foreground color based on background luminance
                        string bg = NormalizeColorToHex(_textBgColor ?? "#FFFFFF");
                        int r = Convert.ToInt32(bg.Substring(1, 2), 16);
                        int g = Convert.ToInt32(bg.Substring(3, 2), 16);
                        int b = Convert.ToInt32(bg.Substring(5, 2), 16);
                        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        SolidColorBrush foregroundBrush = new SolidColorBrush(lum < 128 ? Colors.White : Colors.Black);
                        
                        int updatedCount = 0;
                        int containerCount = VirtualTextListView.Items.Count;
                        
                        // IMPORTANT: Only update currently visible containers
                        // ContainerFromIndex(i) returns null for virtualized (scrolled-out) items
                        // Those items are handled by ContainerContentChanging when they become visible
                        System.Diagnostics.Debug.WriteLine($"[ShowTextUI] Updating currently visible containers...");
                        for (int i = 0; i < containerCount; i++)
                        {
                            try
                            {
                                var container = VirtualTextListView.ContainerFromIndex(i) as ListViewItem;
                                if (container != null)  // Only non-null = currently visible
                                {
                                    var tb = FindVisualChild<TextBlock>(container);
                                    if (tb != null)
                                    {
                                        tb.FontSize = (double)_textFontSize;
                                        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_currentFontFamily);
                                        tb.Foreground = foregroundBrush;
                                        updatedCount++;
                                        
                                        if (updatedCount <= 3)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[ShowTextUI] Updated visible container {i}: {_currentFontFamily} {_textFontSize}pt");
                                        }
                                    }
                                }
                                else
                                {
                                    // This is expected - virtualized items return null
                                    // They will be updated by ContainerContentChanging when scrolled into view
                                    break;  // Stop when we hit first virtualized item (no more visible items after)
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ShowTextUI] Error at container {i}: {ex.Message}");
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[ShowTextUI] ✓ Applied settings to {updatedCount} currently visible containers (ContainerContentChanging handles scrolled items)");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ShowTextUI] Error: {ex.Message}");
                    }
                });
            }
        }

        private void HideTextUI()
        {
            _isTextMode = false;
            TextViewerArea.Visibility = Visibility.Collapsed;
            TextOptionsButton.Visibility = Visibility.Visible;
            TextSeparator.Visibility = Visibility.Visible;

            // Restore image specific buttons
            SharpenButton.Visibility = Visibility.Visible;
            SideBySideButton.Visibility = Visibility.Visible;
            NextImageSideButton.Visibility = Visibility.Visible;
            ZoomOutButton.Visibility = Visibility.Visible;
            ZoomInButton.Visibility = Visibility.Visible;
            ZoomFitButton.Visibility = Visibility.Visible;
            ZoomActualButton.Visibility = Visibility.Visible;
            ZoomLevelText.Visibility = Visibility.Visible;
            
            // Stop page info timer
            _pageInfoTimer?.Stop();
        }

        private void UpdateStatusBarForText()
        {
            FileNameText.Text = Path.GetFileName(_currentTextFilePath);
            
            // Start page info timer for direct polling
            StartPageInfoTimer();
        }
        
        private void StartPageInfoTimer()
        {
            _pageInfoTimer?.Stop();
            _pageInfoTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _pageInfoTimer.Interval = TimeSpan.FromMilliseconds(200); // Faster update
            _pageInfoTimer.IsRepeating = true;
            _pageInfoTimer.Tick += async (s, e) =>
            {
                if (_isTextMode && TextViewer.CoreWebView2 != null)
                {
                    await UpdatePageInfoDirectly();
                }
            };
            _pageInfoTimer.Start();
            
            // Also do an immediate update
            _ = Task.Run(async () =>
            {
                await Task.Delay(100); // Small delay for WebView to be ready
                await UpdatePageInfoDirectly();
            });
        }
        
        private async Task UpdatePageInfoDirectly()
        {
            try
            {
                // If virtualized viewer is active, compute page info from ScrollViewer
                if (_useVirtualTextViewer)
                {
                    if (_virtualScrollViewer == null)
                    {
                        _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                    }

                    if (_virtualScrollViewer != null)
                    {
                        // Use TryEnqueue to marshal to UI thread
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            double viewport = _virtualScrollViewer.ViewportHeight;
                            double extent = _virtualScrollViewer.ExtentHeight;
                            double offset = _virtualScrollViewer.VerticalOffset;

                            // Align pages to whole lines to avoid truncation of top/bottom lines
                            double lineHeight = _textFontSize * 1.8;
                            int linesPerPage = Math.Max(1, (int)Math.Floor(viewport / lineHeight));
                            double pageHeight = linesPerPage * lineHeight;

                            int totalPages = Math.Max(1, (int)Math.Ceiling(extent / Math.Max(1.0, pageHeight)));
                            int currentPage = Math.Max(1, (int)Math.Floor(offset / Math.Max(1.0, pageHeight)) + 1);
                            ImageIndexText.Text = $"Page: {currentPage} / {totalPages} (Font: {_textFontSize}pt)";
                        });
                        return;
                    }
                }

                else
                {
                    // For vertical scrolling mode, get vertical scroll info
                    string scrollTopScript = "window.scrollY";
                    string totalHeightScript = "document.documentElement.scrollHeight";
                    string viewportHeightScript = "document.documentElement.clientHeight";
                    
                    var scrollTopResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(scrollTopScript).AsTask();
                    var totalHeightResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(totalHeightScript).AsTask();
                    var viewportHeightResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(viewportHeightScript).AsTask();
                    
                    if (double.TryParse(scrollTopResult, out double scrollTop) &&
                        double.TryParse(totalHeightResult, out double totalHeight) &&
                        double.TryParse(viewportHeightResult, out double viewportHeight))
                    {
                        // Align pages to whole lines so partial lines aren't shown
                        double lineHeight = _textFontSize * 1.8;
                        int linesPerPage = Math.Max(1, (int)Math.Floor(viewportHeight / lineHeight));
                        double pageHeight = linesPerPage * lineHeight;

                        int totalPages = Math.Max(1, (int)Math.Ceiling(totalHeight / Math.Max(1.0, pageHeight)));
                        int currentPage = Math.Max(1, (int)Math.Floor(scrollTop / Math.Max(1.0, pageHeight)) + 1);

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ImageIndexText.Text = $"Page: {currentPage} / {totalPages} (Font: {_textFontSize}pt)";
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating page info: {ex.Message}");
            }
        }

        private async Task<double?> GetTextScrollPositionAsync()
        {
            try
            {
                if (_useVirtualTextViewer)
                {
                    if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                    if (_virtualScrollViewer != null)
                    {
                        return _virtualScrollViewer.VerticalOffset;
                    }
                    return null;
                }

                if (_isTextMode && TextViewer.CoreWebView2 != null)
                {

       
                        // For vertical scrolling mode, get vertical scroll position
                        string scrollTopScript = "window.scrollY";
                        var scrollTopResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(scrollTopScript).AsTask();
                        if (double.TryParse(scrollTopResult, out double scrollTop))
                        {
                            return scrollTop;
                        }
                   
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting text scroll position: {ex.Message}");
            }
            return null;
        }

        private async Task SetTextScrollPosition(double position)
        {
            try
            {
                if (_useVirtualTextViewer)
                {
                    if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                    if (_virtualScrollViewer != null)
                    {
                        _virtualScrollViewer.ChangeView(null, position, null, true);
                    }
                    return;
                }

                if (_isTextMode && TextViewer.CoreWebView2 != null)
                {
   
              
                        // For vertical scrolling mode, set vertical scroll position
                        string scrollScript = $"window.scrollTo(0, {position})";
                        await TextViewer.CoreWebView2.ExecuteScriptAsync(scrollScript).AsTask();
                   
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting text scroll position: {ex.Message}");
            }
        }

        private async Task LoadLargeTextFileAsync(StorageFile file, bool useVirtualViewer)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting large file load: {file.Name}");
                
                const int chunkSize = 1024 * 1024; // 1MB chunks
                System.Text.Encoding? detectedEncoding = null;
                long totalBytes = 0;
                long bytesRead = 0;
                
                // Read first chunk to determine encoding
                byte[] firstChunk;
                bool hasBom = false;
                using (var fileStream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    totalBytes = fileStream.Length;
                    System.Diagnostics.Debug.WriteLine($"File size: {totalBytes} bytes");
                    
                    // Check for UTF-8 BOM first
                    var bomBuffer = new byte[3];
                    var bomBytesRead = await fileStream.ReadAsync(bomBuffer, 0, 3);
                    hasBom = bomBytesRead >= 3 && HasUtf8Bom(bomBuffer);
                    
                    if (hasBom)
                    {
                        System.Diagnostics.Debug.WriteLine("UTF-8 BOM detected in large file");
                        // Read remaining chunk data
                        var firstBuffer = new byte[chunkSize];
                        var firstChunkBytesRead = await fileStream.ReadAsync(firstBuffer, 0, chunkSize);
                        
                        if (firstChunkBytesRead > 0)
                        {
                            firstChunk = firstBuffer.Take((int)firstChunkBytesRead).ToArray();
                        }
                        else
                        {
                            firstChunk = Array.Empty<byte>();
                        }
                    }
                    else
                    {
                        // No BOM, reset position and read normally
                        fileStream.Seek(0, SeekOrigin.Begin);
                        var firstBuffer = new byte[chunkSize];
                        var firstChunkBytesRead = await fileStream.ReadAsync(firstBuffer, 0, chunkSize);
                        
                        if (firstChunkBytesRead > 0)
                        {
                            firstChunk = firstBuffer.Take((int)firstChunkBytesRead).ToArray();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No data read from first chunk");
                            FileNameText.Text = $"{file.Name} (빈 파일)";
                            return;
                        }
                    }
                }

                // If BOM detected, force UTF-8 encoding
                if (hasBom)
                {
                    detectedEncoding = System.Text.Encoding.UTF8;
                    System.Diagnostics.Debug.WriteLine("Using UTF-8 encoding due to BOM in large file");
                }
                else
                {
                    detectedEncoding = ChooseTextEncoding(firstChunk);
                    System.Diagnostics.Debug.WriteLine($"Chosen encoding for large file: {detectedEncoding?.WebName ?? "null"}");
                }
                
                if (detectedEncoding == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to detect encoding, falling back to UTF-8");
                    detectedEncoding = System.Text.Encoding.UTF8;
                }
                
                // Set initial content with first chunk
                var firstChunkText = detectedEncoding!.GetString(firstChunk);
                _currentTextContent = firstChunkText;
                _isCurrentTextContentPreprocessed = false;
                
                System.Diagnostics.Debug.WriteLine($"Initial content length: {firstChunkText.Length}");
                
                // Initialize viewer with first chunk immediately
                await InitializeTextViewerAsync();
                if (useVirtualViewer)
                {
                    // For virtualized viewer, clear existing lines and add initial chunk lines
                    _useVirtualTextViewer = true;
                    _virtualTextLines.Clear();

                    var initialLines = firstChunkText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var l in initialLines)
                    {
                        _virtualTextLines.Add(l);
                    }

                    // Ensure ListView is visible and WebView hidden
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            VirtualTextListView.ItemsSource = _virtualTextLines;
                            VirtualTextListView.Visibility = Visibility.Visible;
                            TextViewer.Visibility = Visibility.Collapsed;
                            ShowTextUI();
                        }
                        catch { }
                    });
                }
                else
                {
                    await UpdateTextViewer();
                    ShowTextUI();
                }
                
                // Update status with loading indicator
                FileNameText.Text = $"{file.Name} (로딩 중...)";
                
                bytesRead = firstChunk.Length;
                
                // Continue loading remaining chunks on UI thread to avoid file access issues
                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("Starting background loading...");
                        int chunkCount = 0;
                        
                        while (bytesRead < totalBytes)
                        {
                            chunkCount++;
                            var bytesToRead = (int)Math.Min(chunkSize, totalBytes - bytesRead);
                            
                            // Read chunk on UI thread to avoid file access issues
                            byte[]? chunkData = null;
                            var chunkReadTask = Task.Run(async () =>
                            {
                                using (var chunkStream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                                {
                                    chunkStream.Seek(bytesRead, SeekOrigin.Begin);
                                    var buffer = new byte[bytesToRead];
                                    var actualBytesRead = await chunkStream.ReadAsync(buffer, 0, bytesToRead);
                                    if (actualBytesRead > 0)
                                    {
                                        chunkData = buffer.Take(actualBytesRead).ToArray();
                                    }
                                    else
                                    {
                                        chunkData = Array.Empty<byte>();
                                    }
                                }
                            });
                            
                            await chunkReadTask;
                            
                            if (chunkData == null || chunkData.Length == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"No more data at chunk {chunkCount}");
                                break;
                            }
                            
                            var chunkText = detectedEncoding!.GetString(chunkData);
                            
                            System.Diagnostics.Debug.WriteLine($"Chunk {chunkCount}: {chunkData.Length} bytes, text length: {chunkText.Length}");
                            
                            // Append to content on UI thread
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                try
                                {
                                    _currentTextContent += chunkText;
                                    _isCurrentTextContentPreprocessed = false;
                                    
                                    if (useVirtualViewer)
                                    {
                                        // For virtualized viewer, add lines incrementally
                                        var lines = chunkText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                        foreach (var line in lines)
                                        {
                                            _virtualTextLines.Add(line);
                                        }
                                        
                                        // Update viewer to show new lines
                                        _ = UpdateVirtualizedTextViewer();
                                    }
                                    else
                                    {
                                        // Update viewer without flickering
                                        _ = UpdateTextViewerIncremental(chunkText);
                                    }
                                    
                                    // Update loading progress
                                    var progress = (int)((bytesRead * 100) / totalBytes);
                                    FileNameText.Text = $"{file.Name} (로딩 중... {progress}%)";
                                }
                                catch (Exception uiEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"UI update error: {uiEx.Message}");
                                }
                            });
                            
                            bytesRead += chunkData.Length;
                            
                            // Small delay to prevent UI blocking
                            await Task.Delay(10);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Background loading completed. Total chunks: {chunkCount}, Total bytes: {bytesRead}");
                        
                        // Final update when loading is complete
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                UpdateStatusBarForText();
                                _ = AddToRecentAsync();
                                System.Diagnostics.Debug.WriteLine("Large file loading completed successfully");
                            }
                            catch (Exception finalEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Final update error: {finalEx.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in background text loading: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            FileNameText.Text = $"{file.Name} (로딩 오류: {ex.Message})";
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading large text file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                FileNameText.Text = $"{file.Name} (로딩 오류: {ex.Message})";
            }
        }

        private async Task UpdateTextViewerWithContent(string content)
        {
            try
            {
                _currentTextContent = content;

                await InitializeTextViewerAsync();
                await UpdateTextViewer();
                ShowTextUI();
                UpdateStatusBarForText();
                _ = AddToRecentAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating text viewer: {ex.Message}");
            }
        }

        private async Task NavigateTextHtmlAsync(string fullHtml)
        {
            await InitializeTextViewerAsync();
            if (TextViewer.CoreWebView2 == null)
                return;

            if (fullHtml.Length <= MaxNavigateToStringLength)
            {
                try
                {
                    TextViewer.NavigateToString(fullHtml);
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NavigateToString failed, falling back to temp file: {ex.Message}");
                }
            }

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Uviewer");
                Directory.CreateDirectory(tempDir);

                if (!string.IsNullOrEmpty(_currentTextTempHtmlPath) && File.Exists(_currentTextTempHtmlPath))
                {
                    try { File.Delete(_currentTextTempHtmlPath); } catch { }
                }

                _currentTextTempHtmlPath = Path.Combine(tempDir, $"text_{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(_currentTextTempHtmlPath, fullHtml, System.Text.Encoding.UTF8);

                var uri = new Uri(_currentTextTempHtmlPath).AbsoluteUri;
                TextViewer.CoreWebView2.Navigate(uri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating large HTML: {ex.Message}");
                TextViewer.NavigateToString("<html><body>텍스트 표시 오류</body></html>");
            }
        }

        private string ProcessAozoraBunkoFormat(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            System.Diagnostics.Debug.WriteLine($"Processing Aozora content, length: {content.Length}");
            System.Diagnostics.Debug.WriteLine($"Content preview: {content.Substring(0, Math.Min(100, content.Length))}");

            // Check if it's likely Aozora Bunko format
            if (!content.Contains("青空文庫") && !content.Contains("底本：") && !content.Contains("［＃") && !content.Contains("《"))
            {
                System.Diagnostics.Debug.WriteLine("Not Aozora Bunko format");
                return content;
            }

            // For very large files (>1MB), limit processing to avoid memory issues
            if (content.Length > 1024 * 1024)
            {
                System.Diagnostics.Debug.WriteLine("Large file detected, applying simplified processing");
                return ProcessLargeAozoraFile(content);
            }

            System.Diagnostics.Debug.WriteLine("Detected Aozora Bunko format");
            string processed = content;

            try
            {
                // Process common Aozora Bunko markup patterns
                
                // Remove header information (lines starting with ［＃で始まるもの)
                processed = Regex.Replace(processed, @"^\［＃.+?］$", "", RegexOptions.Multiline);
                
                // Process indentation markers (［＃地からＸ字上げ］)
                processed = Regex.Replace(processed, @"^\［＃地から([０-９\d]+)字上げ］(.+)$", m => 
                {
                    string numStr = m.Groups[1].Value;
                    // Convert full-width numbers to half-width
                    string halfWidthNum = "";
                    foreach (char c in numStr)
                    {
                        if (c >= '０' && c <= '９')
                            halfWidthNum += (char)(c - '０' + '0');
                        else
                            halfWidthNum += c;
                    }
                    
                    if (int.TryParse(halfWidthNum, out int indent))
                    {
                        string text = m.Groups[2].Value;
                        return new string(' ', indent * 2) + text; // Convert to spaces (2 spaces per full-width char)
                    }
                    return m.Value; // Return original if parsing fails
                }, RegexOptions.Multiline);

                processed = Regex.Replace(processed, @"｜([^《\n]+)《([^》]+)》", "$1（$2）");

                // Convert ruby notation (《》 for ruby text)
                processed = Regex.Replace(processed, @"《([^》\n]+)》", "（$1）");
                
                // Convert emphasis markers (■ for emphasis)
                processed = Regex.Replace(processed, @"■([^■\n]+)■", "[$1]");
                
                // Convert vertical text markers (｜ for vertical text)
                processed = Regex.Replace(processed, @"｜([^｜\n]+)｜", "$1");
                processed = Regex.Replace(processed, @"｜(?=\S)", "");
                
                // Convert line break markers (＼ for line breaks)
                processed = Regex.Replace(processed, @"＼", "\n");
                
                // Convert page break markers (［＃改ページ］)
                processed = Regex.Replace(processed, @"^\［＃改ページ］$", "", RegexOptions.Multiline);
                
                // Convert chapter markers (［＃「.+?」］)
                processed = Regex.Replace(processed, @"^\［＃「(.+?)」］$", "\n\n【$1】\n\n", RegexOptions.Multiline);
                
                // Convert section markers (［＃.+?］)
                processed = Regex.Replace(processed, @"^\［＃(.+?)］$", "\n\n◆$1◆\n\n", RegexOptions.Multiline);
                
                // Clean up multiple empty lines
                processed = Regex.Replace(processed, @"\n{3,}", "\n\n");
                
                // Trim leading/trailing whitespace
                processed = processed.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing Aozora format: {ex.Message}");
                // Return original content if processing fails
                return content;
            }

            System.Diagnostics.Debug.WriteLine($"Processed Aozora content, length: {processed.Length}");
            return processed;
        }

        private string ProcessLargeAozoraFile(string content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Processing large Aozora file, length: {content.Length}");
                System.Diagnostics.Debug.WriteLine($"Content preview: {content.Substring(0, Math.Min(200, content.Length))}");
                
                // For large files, do minimal processing to avoid memory issues
                string processed = content;

                var sb = new System.Text.StringBuilder(processed.Length);
                using var reader = new StringReader(processed);
                string? line;
                int emptyLineCount = 0;
                int lineCount = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineCount++;

                    if (line.StartsWith("［＃") && line.EndsWith("］"))
                    {
                        continue;
                    }

                    if (line == "［＃改ページ］")
                    {
                        line = "";
                    }
                    else
                    {
                        if (line.StartsWith("［＃地から") && line.Contains("字上げ］"))
                        {
                            int numStart = "［＃地から".Length;
                            int numEnd = line.IndexOf("字上げ］", StringComparison.Ordinal);
                            if (numEnd > numStart)
                            {
                                string numStr = line.Substring(numStart, numEnd - numStart);
                                string halfWidthNum = "";
                                foreach (char c in numStr)
                                {
                                    if (c >= '０' && c <= '９')
                                        halfWidthNum += (char)(c - '０' + '0');
                                    else
                                        halfWidthNum += c;
                                }
                                string text = line.Substring(numEnd + "字上げ］".Length);
                                if (int.TryParse(halfWidthNum, out int indent) && indent < 200)
                                {
                                    line = new string(' ', indent * 2) + text;
                                }
                            }
                        }

                        if (line.StartsWith("［＃「") && line.EndsWith("」］"))
                        {
                            string title = line.Substring("［＃「".Length, line.Length - "［＃「".Length - "」］".Length);
                            line = $"\n\n【{title}】\n\n";
                        }

                        line = Regex.Replace(line, @"｜([^《\n]+)《([^》]+)》", "$1（$2）");
                        line = Regex.Replace(line, @"《([^》\n]+)》", "（$1）");
                        line = Regex.Replace(line, @"■([^■\n]+)■", "[$1]");
                        line = Regex.Replace(line, @"｜([^｜\n]+)｜", "$1");
                        line = Regex.Replace(line, @"｜(?=\S)", "");

                        if (line.Contains('＼'))
                        {
                            var parts = line.Split('＼');
                            for (int i = 0; i < parts.Length; i++)
                            {
                                var part = parts[i];
                                if (string.IsNullOrWhiteSpace(part))
                                {
                                    emptyLineCount++;
                                    if (emptyLineCount < 3)
                                    {
                                        sb.AppendLine();
                                    }
                                }
                                else
                                {
                                    emptyLineCount = 0;
                                    sb.AppendLine(part);
                                }
                            }
                            continue;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        emptyLineCount++;
                        if (emptyLineCount < 3)
                        {
                            sb.AppendLine();
                        }
                    }
                    else
                    {
                        emptyLineCount = 0;
                        sb.AppendLine(line);
                    }
                }

                string finalResult = sb.ToString().Trim();
                System.Diagnostics.Debug.WriteLine($"Large Aozora processing completed: {content.Length} -> {finalResult.Length} chars, {lineCount} lines processed");

                return finalResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing large Aozora file: {ex.Message}");
                return content; // Return original if processing fails
            }
        }

        private async Task UpdateTextViewerIncremental(string newContent)
        {
            try
            {
                if (TextViewer == null || TextViewer.CoreWebView2 == null) 
                {
                    System.Diagnostics.Debug.WriteLine("TextViewer or CoreWebView2 is null");
                    return;
                }

                if (string.IsNullOrEmpty(newContent))
                {
                    System.Diagnostics.Debug.WriteLine("New content is null or empty");
                    return;
                }

                // Escape the new content for JavaScript
                var escapedContent = System.Web.HttpUtility.JavaScriptStringEncode(newContent);
                
                // Append content to the end without re-initializing the entire document
                var script = $@"
(function() {{
    try {{
        // Get current scroll position
        const scrollX = window.pageXOffset || document.documentElement.scrollLeft;
        const scrollY = window.pageYOffset || document.documentElement.scrollTop;
        const isAtBottom = (window.innerHeight + scrollY) >= document.body.scrollHeight - 10;
        
        // Create a temporary div for the new content
        const tempDiv = document.createElement('div');
        tempDiv.innerHTML = `{escapedContent}`;
        
        // Check if content was actually added
        if (tempDiv.innerHTML.trim() === '') {{
            console.log('Empty content, skipping update');
            return;
        }}
        
        // Append to body
        document.body.appendChild(tempDiv);
        
        // If user was at bottom, scroll to new bottom
        if (isAtBottom) {{
            window.scrollTo(0, document.body.scrollHeight);
        }} else {{
            // Restore previous scroll position
            window.scrollTo(scrollX, scrollY);
        }}
        
        return {{
            success: true,
            contentLength: document.body.innerText.length,
            scrollHeight: document.body.scrollHeight,
            addedContentLength: `{escapedContent}`.length
        }};
    }} catch (e) {{
        console.error('Error in incremental update:', e);
        return {{
            success: false,
            error: e.toString()
        }};
    }}
}})()";

                var result = await TextViewer.CoreWebView2.ExecuteScriptAsync(script).AsTask();
                System.Diagnostics.Debug.WriteLine($"Incremental update result: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating text viewer incrementally: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static string ProcessBoldMarkup(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            // Process **bold** markup patterns
            // Use regex to find **text** patterns and replace with <strong>text</strong>
            // This handles nested cases and avoids processing empty bold markers
            string processed = Regex.Replace(content, @"\*\*([^*\s]+(?:\s+[^*\s]+)*)\*\*", "<strong>$1</strong>");
            
            return processed;
        }

        private static string CleanHtmlContent(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent)) return htmlContent;

            try
            {
                // Remove excessive whitespace between HTML tags
                string cleaned = Regex.Replace(htmlContent, @">\s+<", "><", RegexOptions.Singleline);
                
                // Remove multiple consecutive spaces within text content (but preserve single spaces)
                cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
                
                // Remove multiple consecutive newlines (but preserve single newlines)
                cleaned = Regex.Replace(cleaned, @"[\r\n]{3,}", "\r\n\r\n");
                
                // Remove leading/trailing whitespace from each line
                string[] lines = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = lines[i].Trim();
                }
                cleaned = string.Join("\r\n", lines);
                
                // Remove empty lines at the beginning and end
                cleaned = cleaned.Trim();
                
                // Remove excessive paragraph spacing (multiple <br> tags)
                cleaned = Regex.Replace(cleaned, @"(<br\s*/?>[\s]*){3,}", "<br><br>", RegexOptions.IgnoreCase);
                
                // Remove excessive paragraph margins (multiple empty paragraphs)
                cleaned = Regex.Replace(cleaned, @"(<p[^>]*>\s*</p>[\s]*){2,}", "<p></p>", RegexOptions.IgnoreCase);
                
                // Clean up div spacing
                cleaned = Regex.Replace(cleaned, @"(<div[^>]*>\s*</div>[\s]*){2,}", "<div></div>", RegexOptions.IgnoreCase);
                
                return cleaned;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cleaning HTML content: {ex.Message}");
                return htmlContent; // Return original if cleaning fails
            }
        }

        private static string HtmlEncodePreservingTags(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            // First, protect <strong> tags by replacing them with placeholders
            string processed = content;
            processed = Regex.Replace(processed, @"<strong>", "%%STRONG_START%%", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"</strong>", "%%STRONG_END%%", RegexOptions.IgnoreCase);
            
            // HTML encode everything else
            processed = System.Net.WebUtility.HtmlEncode(processed);
            
            // Restore the <strong> tags
            processed = processed.Replace("%%STRONG_START%%", "<strong>");
            processed = processed.Replace("%%STRONG_END%%", "</strong>");
            
            return processed;
        }

        private async Task UpdateTextViewer()
        {
            string htmlContent = "";
            string ext = Path.GetExtension(_currentTextFilePath).ToLowerInvariant();

            if (ext == ".md" || ext == ".markdown")
            {
                var pipeline = new MarkdownPipelineBuilder()
                    .UseAdvancedExtensions()
                    .Build();
                htmlContent = Markdown.ToHtml(_currentTextContent, pipeline);
            }
            else if (ext == ".html" || ext == ".htm")
            {
                // Clean up HTML content by removing excessive whitespace
                htmlContent = CleanHtmlContent(_currentTextContent);
            }
            else
            {
                // Check if it's Aozora Bunko format
                System.Diagnostics.Debug.WriteLine($"Processing text file: {_currentTextFilePath}");
                string processedContent = _isCurrentTextContentPreprocessed ? _currentTextContent : ProcessAozoraBunkoFormat(_currentTextContent);
                
                // Process **bold** markup first
                processedContent = ProcessBoldMarkup(processedContent);
                
                System.Diagnostics.Debug.WriteLine($"Content after processing: {processedContent.Substring(0, Math.Min(100, processedContent.Length))}");
                // Escape HTML for plain text while preserving <strong> tags
                htmlContent = $"<div style='white-space: pre-wrap; word-wrap: break-word; overflow-wrap: break-word; font-family: inherit;'>{HtmlEncodePreservingTags(processedContent)}</div>";
            }

            string sideBySideStyle = ""; // Always empty for 1-column view
            string isDark = (_textBgColor == "#1E1E1E") ? "true" : "false";
            string textColor = isDark == "true" ? "#E0E0E0" : "#202020";

            // HTML 파일인 경우 UTF-8 메타 태그가 없을 수 있으므로 강제로 추가하거나 교체
            if (ext == ".html" || ext == ".htm")
            {
                // meta charset 제거 후 새로 삽입
                htmlContent = Regex.Replace(htmlContent, @"<meta[^>]*charset=[^>]*>", "", RegexOptions.IgnoreCase);
                htmlContent = $"<meta charset='UTF-8'>{htmlContent}";
            }

            string fullHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        /* 1. 모든 요소의 기존 스타일을 강제로 초기화 */
        * {{
            background-color: transparent !important;
            color: inherit !important;
            font-family: inherit !important;
            font-size: inherit !important;
            line-height: inherit !important;
            
            /* 좌우가 좁아지게 만드는 원인 제거 */
            width: auto !important; 
            max-width: none !important;
            float: none !important;
            position: static !important;
            margin-left: 10px !important;
            margin-right: 10px !important;
            box-sizing: border-box !important;
        }}

        /* 2. 기본 배경 및 글꼴 설정 */
        html, body {{
            background-color: {_textBgColor} !important;
            color: {textColor} !important;
            font-family: '{_currentFontFamily}', 'Yu Mincho', sans-serif !important;
            font-size: {_textFontSize}px !important;
            line-height: 1.8 !important;
            margin: 0 !important;
            padding: 0 !important;
            width: 100% !important;
        }}

        body {{
	   
            padding: 60px 20%;
            overflow-x: hidden;
        }}

        .content-wrapper {{
            {sideBySideStyle}
            max-width: calc({_textFontSize}px * 40) !important;
            width: auto !important;
            margin: 0 auto;
            word-wrap: break-word !important;
            overflow-wrap: break-word !important;
            white-space: pre-wrap !important;
            hyphens: auto !important;
        }}

        /* 3. 텍스트 가독성을 위한 최소한의 서식 유지 */
        .plain-text {{ white-space: pre-wrap; word-wrap: break-word; }}
        p, div, li {{ break-inside: avoid-column; margin-bottom: 1em; }}
        h1, h2, h3, h4, h5, h6 {{ 
            color: {(isDark == "true" ? "#569CD6" : "#005A9E")} !important; 
            margin-top: 1.5em; 
							
        }}
        
        /* Bold styling for **text** markup */
        strong {{ 
            font-weight: bold !important; 
            color: inherit !important;
            font-family: inherit !important;
            font-size: inherit !important;
        }}
        
        /* 이미지 등은 완전히 사라지지 않게 조정 */
        img {{ max-width: 100%; height: auto; border-radius: 4px; background-color: transparent !important; }}
        
        pre, code {{ 
            background-color: {(isDark == "true" ? "#2D2D2D" : "#F0F0F0")} !important; 
            font-family: monospace !important;
            padding: 5px;
        }}
										 
    </style>
    <script>
        const isSideBySide = false; // Always false for 1-column view
        
        window.addEventListener('keydown', (e) => {{
            // Forward important keys to host
            const keysToForward = ['F11', 'Escape', ' ', 'f', 'g', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', '0'];
            const isArrow = e.key.startsWith('Arrow');
            
            if (keysToForward.includes(e.key) || e.ctrlKey) {{
                let keyName = e.key;
                if (keyName === 'ArrowLeft') keyName = 'Left';
                else if (keyName === 'ArrowRight') keyName = 'Right';
                else if (keyName === 'ArrowUp') keyName = 'Up';
                else if (keyName === 'ArrowDown') keyName = 'Down';
                else if (keyName === 'f') keyName = 'F';
                else if (keyName === 'g') keyName = 'G';
                else if (keyName === ' ') keyName = 'Space';

                window.chrome.webview.postMessage({{
                    type: 'keydown',
                    key: keyName,
                    ctrl: e.ctrlKey
                }});
                
                // Prevent browser default for keys handled by the app
                const preventDefaultKeys = ['f', 'g', ' ', 'ArrowUp', 'ArrowDown', 'F11', 'Escape'];
                if (preventDefaultKeys.includes(e.key)) e.preventDefault();
            }}
        }});

        // Wheel event removed for 1-column view

        // Mouse click navigation for text mode
        document.addEventListener('click', (e) => {{
            // Only handle clicks on the body element (not on links, buttons, etc.)
            if (e.target.tagName === 'BODY' || e.target.classList.contains('content-wrapper') || e.target.classList.contains('plain-text')) {{
                const viewportWidth = window.innerWidth;
                const clickX = e.clientX;
                const halfWidth = viewportWidth / 2;
                
                // Determine which half was clicked
                const isRightHalf = clickX > halfWidth;
                
                console.log('Mouse click detected:', {{ clickX, halfWidth, isRightHalf }});
                
                // Send message to host for navigation
                window.chrome.webview.postMessage({{
                    type: 'mousenavigation',
                    direction: isRightHalf ? 'next' : 'previous'
                }});
            }}
        }});

        // Add visual feedback for clickable areas
        document.addEventListener('DOMContentLoaded', () => {{
            const style = document.createElement('style');
            style.textContent = `
                body {{
                    cursor: pointer;
                }}
                body::before {{
                    content: '';
                    position: fixed;
                    top: 0;
                    left: 0;
                    width: 50%;
                    height: 100%;
                    background: linear-gradient(90deg, rgba(0,123,255,0.02) 0%, rgba(0,123,255,0) 100%);
                    pointer-events: none;
                    z-index: 1;
                }}
                body::after {{
                    content: '';
                    position: fixed;
                    top: 0;
                    right: 0;
                    width: 50%;
                    height: 100%;
                    background: linear-gradient(90deg, rgba(255,123,0,0) 0%, rgba(255,123,0,0.02) 100%);
                    pointer-events: none;
                    z-index: 1;
                }}
            `;
            document.head.appendChild(style);
        }});
        
        function updatePageInfo() {{
            const totalWidth = document.documentElement.scrollWidth;
            const viewportWidth = document.documentElement.clientWidth;
            const scrollLeft = window.scrollX;
            
            const totalHeight = document.documentElement.scrollHeight;
            const viewportHeight = document.documentElement.clientHeight;
            const scrollTop = window.scrollY;

            let totalPages, currentPage;

            // Always use vertical scrolling for 1-column view
            totalPages = Math.max(1, Math.ceil(totalHeight / viewportHeight));
            currentPage = Math.max(1, Math.floor(scrollTop / viewportHeight) + 1);
            
            console.log('Page info:', {{ currentPage, totalPages, totalHeight, viewportHeight, scrollTop }});
            
            // Update the document title as a fallback method
            document.title = `Page: ${{currentPage}} / ${{totalPages}}`;
            
            // Try multiple methods to send the message
            try {{
                if (window.chrome && window.chrome.webview) {{
                    window.chrome.webview.postMessage({{
                        type: 'pageinfo',
                        current: currentPage,
                        total: totalPages
                    }});
                    console.log('Message sent via chrome.webview');
                }} else {{
                    console.log('chrome.webview not available');
                }}
            }} catch (e) {{
                console.log('Error sending message:', e);
            }}
        }}

        window.addEventListener('scroll', updatePageInfo);
        window.addEventListener('resize', updatePageInfo);
        
        // Multiple attempts to ensure page info is calculated
        setTimeout(updatePageInfo, 100);
        setTimeout(updatePageInfo, 500);
        setTimeout(updatePageInfo, 1000);
        
        // Also try when DOM is fully loaded
        document.addEventListener('DOMContentLoaded', updatePageInfo);
        
        console.log('Page info listeners set up');
    </script>
</head>
<body>
    <div class='content-wrapper'>
        {htmlContent}
    </div>
</body>
</html>";
            // If virtualized viewer is active for .txt files, skip WebView2 and show ListView
            if (_useVirtualTextViewer && ext == ".txt")
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    VirtualTextListView.ItemsSource = _virtualTextLines;
                    VirtualTextListView.Visibility = Visibility.Visible;
                    TextViewer.Visibility = Visibility.Collapsed;
                    FileNameText.Text = Path.GetFileName(_currentTextFilePath);
                });
            }
            else
            {
                // Ensure virtual mode is disabled when showing non-txt content
                if (_useVirtualTextViewer)
                {
                    _useVirtualTextViewer = false;
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        VirtualTextListView.Visibility = Visibility.Collapsed;
                        VirtualTextListView.ItemsSource = null;
                        TextViewer.Visibility = Visibility.Visible;
                    });
                }
                await NavigateTextHtmlAsync(fullHtml);
            }
            
            // Wait a bit and then try to manually trigger page info update
            await Task.Delay(1500);
            try
            {
                await TextViewer.CoreWebView2.ExecuteScriptAsync("updatePageInfo()").AsTask();
            }
            catch { }
        }

        private async Task UpdateVirtualizedTextViewer()
        {
            // Ensure ListView is visible and bound
            try
            {
                await Task.Yield();
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] Starting update");
                    System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] Total items to load: {_virtualTextLines.Count}");
                    TextViewer.Visibility = Visibility.Collapsed;
                    VirtualTextListView.Visibility = Visibility.Visible;
                    VirtualTextListView.ItemsSource = _virtualTextLines;
                    
                     System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ItemsSource set to {_virtualTextLines.Count} items");
                     System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ListView ActualHeight: {VirtualTextListView.ActualHeight}, ActualWidth: {VirtualTextListView.ActualWidth}");
                     System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] NOTE: Virtualization shows only ~18 items due to viewport, but all {_virtualTextLines.Count} are loaded in memory");
                     
                     // Attach scroll and click handlers once
                     if (!_virtualEventsAttached)
                     {
                         System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] Attaching event handlers");
                         
                         _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                         if (_virtualScrollViewer != null)
                         {
                             System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ScrollViewer ViewportHeight: {_virtualScrollViewer.ViewportHeight}");
                             _virtualScrollViewer.ViewChanged += (s, e) => { _ = UpdatePageInfoDirectly(); };
                             System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ScrollViewer found and ViewChanged handler attached");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ERROR: ScrollViewer not found!");
                        }
                        
                        // Item click for mouse navigation
                        VirtualTextListView.PointerReleased += VirtualTextListView_PointerReleased;
                        System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] PointerReleased handler attached");
                        
                        // Container content changing to apply font, wrap and width
                        VirtualTextListView.ContainerContentChanging += VirtualTextListView_ContainerContentChanging;
                        System.Diagnostics.Debug.WriteLine($"[UpdateVirtualizedTextViewer] ContainerContentChanging handler attached");
                        
                        _virtualEventsAttached = true;
                    }

                    // Apply background/foreground and other visual settings
                    try
                    {
                        // Map known background choices to named colors for compatibility
                        if (!string.IsNullOrEmpty(_textBgColor))
                        {
                            if (_textBgColor.Equals("White", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
                            {
                                VirtualTextListView.Background = new SolidColorBrush(Colors.White);
                            }
                            else if (_textBgColor.Equals("#F4ECD8", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("Beige", StringComparison.OrdinalIgnoreCase))
                            {
                                // Use a Beige-like color available in Colors
                                VirtualTextListView.Background = new SolidColorBrush(Colors.Beige);
                            }
                            else if (_textBgColor.Equals("#1E1E1E", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                            {
                                VirtualTextListView.Background = new SolidColorBrush(Colors.Black);
                            }
                            else
                            {
                                // Default fallback
                                VirtualTextListView.Background = new SolidColorBrush(Colors.White);
                            }
                        }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating virtualized viewer: {ex.Message}");
            }
        }

        private void VirtualTextListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            try
            {
                if (args.ItemContainer == null)
                {
                    return;
                }
                
                var container = args.ItemContainer;
                
                // Find TextBlock in the visual tree of the container
                var tb = FindVisualChild<TextBlock>(container);
                if (tb != null)
                {
                    // CRITICAL DEBUG: Log current _textFontSize value
                    System.Diagnostics.Debug.WriteLine($"[ContainerContentChanging] Index {args.ItemIndex}: _textFontSize = {_textFontSize}pt, _currentFontFamily = {_currentFontFamily}");
                    
                    tb.TextWrapping = TextWrapping.Wrap;
                    tb.FontSize = (double)_textFontSize;
                    tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_currentFontFamily);
                    
                    // Foreground based on bg color
                    try
                    {
                        string bg = NormalizeColorToHex(_textBgColor ?? "#FFFFFF");
                        int r = Convert.ToInt32(bg.Substring(1, 2), 16);
                        int g = Convert.ToInt32(bg.Substring(3, 2), 16);
                        int b = Convert.ToInt32(bg.Substring(5, 2), 16);
                        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        tb.Foreground = new SolidColorBrush(lum < 128 ? Colors.White : Colors.Black);
                    }
                    catch { }

                    // Max width: approx 80 chars in pixels, but responsive to control width
                    double approxCharWidth = _textFontSize * 0.6;
                    double maxWidthPx = Math.Max(200, approxCharWidth * 80);
                    double available = VirtualTextListView.ActualWidth;
                    if (available > 0)
                    {
                        maxWidthPx = Math.Min(maxWidthPx, available * 0.95);
                    }
                    tb.MaxWidth = maxWidthPx;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in VirtualTextListView_ContainerContentChanging: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return default;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return default;
        }

        private void VirtualTextListView_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                // Get pointer position relative to ListView
                var point = e.GetCurrentPoint(VirtualTextListView);
                var pos = point.Position;
                double half = VirtualTextListView.ActualWidth / 2.0;
                
                System.Diagnostics.Debug.WriteLine($"[VirtualTextListView_PointerReleased] Pointer at X={pos.X}, ListView width={VirtualTextListView.ActualWidth}, half={half}");
                
                // Handle mouse click on release (when button is released)
                if (pos.X > half)
                {
                    // Right half - Next page
                    System.Diagnostics.Debug.WriteLine("[VirtualTextListView_PointerReleased] Navigating to NEXT page");
                    _ = ScrollTextPage(true);
                }
                else
                {
                    // Left half - Previous page
                    System.Diagnostics.Debug.WriteLine("[VirtualTextListView_PointerReleased] Navigating to PREVIOUS page");
                    _ = ScrollTextPage(false);
                }
                
                // Mark event as handled to prevent default behavior
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VirtualTextListView_PointerReleased] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private string NormalizeColorToHex(string colorValue)
        {
            // Convert color names to hex values
            if (string.IsNullOrEmpty(colorValue)) return "#FFFFFF";
            
            colorValue = colorValue.Trim();
            
            // If already hex, return as-is
            if (colorValue.StartsWith("#") && colorValue.Length == 7)
            {
                return colorValue;
            }
            
            // Convert color names to hex
            return colorValue.ToLowerInvariant() switch
            {
                "white" or "#ffffff" => "#FFFFFF",
                "beige" or "#f4ecd8" => "#F4ECD8",
                "dark" or "#1e1e1e" => "#1E1E1E",
                _ => "#FFFFFF" // Default fallback
            };
        }



        private async void ZoomTextStyle(bool increase)
        {
            // Save current scroll position before changing font size
            double? scrollPosition = null;
            if (_isTextMode)
            {
                scrollPosition = await GetTextScrollPositionAsync();
            }
            
            if (increase) _textFontSize += 2;
            else _textFontSize = Math.Max(8, _textFontSize - 2);
            
            // Update UI to show new font size immediately
            ZoomLevelText.Text = $"{_textFontSize}pt";
            
            if (_useVirtualTextViewer)
            {
                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Applying font size change to virtual viewer: {_textFontSize}pt");
                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Current scroll position before reset: {scrollPosition}");
                
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Directly updating all visible TextBlock containers");
                        
                        // Calculate foreground color based on background luminance
                        string bg = NormalizeColorToHex(_textBgColor ?? "#FFFFFF");
                        int r = Convert.ToInt32(bg.Substring(1, 2), 16);
                        int g = Convert.ToInt32(bg.Substring(3, 2), 16);
                        int b = Convert.ToInt32(bg.Substring(5, 2), 16);
                        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        SolidColorBrush foregroundBrush = new SolidColorBrush(lum < 128 ? Colors.White : Colors.Black);
                        
                        // Directly update all TextBlock children in the ListView containers
                        int updatedCount = 0;
                        int containerCount = VirtualTextListView.Items.Count;
                        int notFoundCount = 0;
                        
                        for (int i = 0; i < containerCount; i++)
                        {
                            try
                            {
                                var container = VirtualTextListView.ContainerFromIndex(i) as ListViewItem;
                                if (container != null)
                                {
                                    var tb = FindVisualChild<TextBlock>(container);
                                    if (tb != null)
                                    {
                                        double oldSize = tb.FontSize;
                                        tb.FontSize = (double)_textFontSize;
                                        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_currentFontFamily);
                                        tb.Foreground = foregroundBrush;
                                        updatedCount++;
                                        
                                        if (updatedCount <= 5 || updatedCount % 200 == 0)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Updated container {i}, font size: {oldSize}pt -> {tb.FontSize}pt, font: {_currentFontFamily}");
                                        }
                                    }
                                    else
                                    {
                                        notFoundCount++;
                                        if (notFoundCount <= 3)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] TextBlock NOT FOUND in container {i}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Exception at index {i}: {ex.Message}");
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Direct update completed: {updatedCount} containers updated, {notFoundCount} TextBlocks not found");
                        
                        await Task.Delay(150);
                        
                        // Restore scroll position
                        if (scrollPosition.HasValue)
                        {
                            if (_virtualScrollViewer == null) 
                                _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                            
                            if (_virtualScrollViewer != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Restoring scroll position to {scrollPosition.Value}");
                                _virtualScrollViewer.ChangeView(null, scrollPosition.Value, null, false);
                                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Scroll position restored");
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Font size change completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Error applying font size: {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
            else
            {
                // For WebView2, update content
                System.Diagnostics.Debug.WriteLine($"[ZoomTextStyle] Using WebView2 mode, calling UpdateTextViewer");
                _ = UpdateTextViewer();
                
                // Restore scroll position after font size change
                if (scrollPosition.HasValue)
                {
                    // Give it a moment to render the new content
                    await Task.Delay(100);
                    await SetTextScrollPosition(scrollPosition.Value);
                }
            }
            
            // Save settings after font size change
            SaveWindowSettings();
        }

        private async Task ScrollTextPage(bool forward)
        {
            try
            {
                // If virtualized viewer is in use, perform scrolling via its ScrollViewer
                if (_useVirtualTextViewer)
                {
                    try
                    {
                        if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                        if (_virtualScrollViewer != null)
                        {
                            // Calculate page height aligned to whole lines to avoid truncation
                            double viewport = _virtualScrollViewer.ViewportHeight;
                            double lineHeight = _textFontSize * 1.8;
                            int linesPerPage = Math.Max(1, (int)Math.Floor(viewport / lineHeight));
                            double pageHeight = linesPerPage * lineHeight;

                            double offset = _virtualScrollViewer.VerticalOffset;
                            double extent = _virtualScrollViewer.ExtentHeight;
                            double maxOffset = Math.Max(0, extent - viewport);

                            // Compute target page-aligned offset
                            double currentPage = Math.Floor(offset / Math.Max(1.0, pageHeight));
                            double newOffset;
                            if (forward)
                            {
                                newOffset = Math.Min((currentPage + 1) * pageHeight, maxOffset);
                            }
                            else
                            {
                                newOffset = Math.Max((currentPage - 1) * pageHeight, 0);
                            }

                            _virtualScrollViewer.ChangeView(null, newOffset, null, true);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Virtual scroll error: {ex.Message}");
                    }
                }

                // Fallback to WebView2 JS scrolling
                if (TextViewer.CoreWebView2 != null)
                {
                    string direction = forward ? "1" : "-1";
                    // Compute page height as an integer number of lines so partial lines are not shown
                    string script = $@"(function() {{
    const viewportHeight = document.documentElement.clientHeight || window.innerHeight;
    const scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    const documentHeight = document.documentElement.scrollHeight;
    const maxScroll = documentHeight - viewportHeight;
    const fontSize = {_textFontSize};
    const lineHeight = fontSize * 1.8;
    const linesPerPage = Math.max(1, Math.floor(viewportHeight / lineHeight));
    const pageHeight = linesPerPage * lineHeight;
    let newScrollTop;
    const currentPage = Math.floor(scrollTop / Math.max(1, pageHeight));
    if ({direction} > 0) {{
        newScrollTop = Math.min((currentPage + 1) * pageHeight, maxScroll);
    }} else {{
        newScrollTop = Math.max((currentPage - 1) * pageHeight, 0);
    }}
    window.scrollTo({{ top: newScrollTop, behavior: 'smooth' }});
    return {{ currentScroll: scrollTop, newScroll: newScrollTop, pageHeight: pageHeight }};
}})();";
                    var result = await TextViewer.CoreWebView2.ExecuteScriptAsync(script).AsTask();
                    System.Diagnostics.Debug.WriteLine($"Page scroll result: {result}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ScrollTextPage: {ex.Message}");

                // Fallback to simple scrolling
                try
                {
                    if (_useVirtualTextViewer && _virtualScrollViewer != null)
                    {
                        double delta = (_virtualScrollViewer.ViewportHeight * 0.9) * (forward ? 1 : -1);
                        double newOffset = Math.Max(0, Math.Min(_virtualScrollViewer.ExtentHeight - _virtualScrollViewer.ViewportHeight, _virtualScrollViewer.VerticalOffset + delta));
                        _virtualScrollViewer.ChangeView(null, newOffset, null, true);
                    }
                    else if (TextViewer.CoreWebView2 != null)
                    {
                        string direction = forward ? "1" : "-1";
                        await TextViewer.CoreWebView2.ExecuteScriptAsync($"window.scrollBy({{ top: window.innerHeight * 0.9 * {direction}, behavior: 'smooth' }})").AsTask();
                    }
                }
                catch { }
            }
        }

        // --- Event Handlers ---

        private async void ChangeFont_Click(object sender, RoutedEventArgs e)
        {
            // Save current scroll position before changing font
            double? scrollPosition = null;
            if (_isTextMode)
            {
                scrollPosition = await GetTextScrollPositionAsync();
            }
            
            _currentFontFamily = _currentFontFamily == "Yu Mincho" ? "Yu Gothic Medium" : "Yu Mincho";
            System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Changed font to: {_currentFontFamily}");
            
            if (_isEpubMode)
            {
                // For EPUB, reprocess and update content with new font
                _ = UpdateEpubViewer();
            }
            else
            {
                if (_useVirtualTextViewer)
                {
                    // For virtual viewer, directly update all visible TextBlock containers
                    System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Refreshing virtual viewer with font: {_currentFontFamily}");
                    _ = DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Directly updating all visible TextBlock containers");
                            
                            // Calculate foreground color based on background luminance
                            string bg = NormalizeColorToHex(_textBgColor ?? "#FFFFFF");
                            int r = Convert.ToInt32(bg.Substring(1, 2), 16);
                            int g = Convert.ToInt32(bg.Substring(3, 2), 16);
                            int b = Convert.ToInt32(bg.Substring(5, 2), 16);
                            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            SolidColorBrush foregroundBrush = new SolidColorBrush(lum < 128 ? Colors.White : Colors.Black);
                            
                            // Directly update all TextBlock children in the ListView containers
                            int updatedCount = 0;
                            int containerCount = VirtualTextListView.Items.Count;
                            
                            for (int i = 0; i < containerCount; i++)
                            {
                                try
                                {
                                    var container = VirtualTextListView.ContainerFromIndex(i) as ListViewItem;
                                    if (container != null)
                                    {
                                        var tb = FindVisualChild<TextBlock>(container);
                                        if (tb != null)
                                        {
                                            tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_currentFontFamily);
                                            tb.Foreground = foregroundBrush;
                                            updatedCount++;
                                            
                                            if (updatedCount <= 3 || updatedCount % 100 == 0)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Updated container {i}, font family now: {_currentFontFamily}");
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Direct update completed: {updatedCount} containers updated");
                            
                            await Task.Delay(150);
                            
                            // Restore scroll position
                            if (scrollPosition.HasValue)
                            {
                                if (_virtualScrollViewer == null)
                                    _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                                
                                if (_virtualScrollViewer != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Restoring scroll position to {scrollPosition.Value}");
                                    _virtualScrollViewer.ChangeView(null, scrollPosition.Value, null, false);
                                    System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Scroll position restored");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ChangeFont_Click] Error refreshing virtual viewer: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _ = UpdateTextViewer();
                    
                    // Restore scroll position after font change
                    if (scrollPosition.HasValue)
                    {
                        await Task.Delay(100);
                        await SetTextScrollPosition(scrollPosition.Value);
                    }
                }
            }
            
            // Save settings after font change
            SaveWindowSettings();
        }

        private async void ChangeBg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string color)
            {
                // Save current scroll position before changing background
                double? scrollPosition = null;
                if (_isTextMode)
                {
                    scrollPosition = await GetTextScrollPositionAsync();
                }
                
                _textBgColor = color;
                
                if (_isEpubMode)
                {
                    // For EPUB, reprocess and update content with new background
                    _ = UpdateEpubViewer();
                }
                else
                {
                    if (_useVirtualTextViewer)
                    {
                        // Apply background AND foreground colors immediately for virtualized viewer
                        _ = DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                // Set background color
                                if (!string.IsNullOrEmpty(_textBgColor))
                                {
                                    if (_textBgColor.Equals("White", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
                                    {
                                        VirtualTextListView.Background = new SolidColorBrush(Colors.White);
                                    }
                                    else if (_textBgColor.Equals("#F4ECD8", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("Beige", StringComparison.OrdinalIgnoreCase))
                                    {
                                        VirtualTextListView.Background = new SolidColorBrush(Colors.Beige);
                                    }
                                    else if (_textBgColor.Equals("#1E1E1E", StringComparison.OrdinalIgnoreCase) || _textBgColor.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                                    {
                                        VirtualTextListView.Background = new SolidColorBrush(Colors.Black);
                                    }
                                    else
                                    {
                                        VirtualTextListView.Background = new SolidColorBrush(Colors.White);
                                    }
                                }
                                
                                // Calculate foreground color based on background luminance
                                string bg = NormalizeColorToHex(_textBgColor ?? "#FFFFFF");
                                int r = Convert.ToInt32(bg.Substring(1, 2), 16);
                                int g = Convert.ToInt32(bg.Substring(3, 2), 16);
                                int b = Convert.ToInt32(bg.Substring(5, 2), 16);
                                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                                SolidColorBrush foregroundBrush = new SolidColorBrush(lum < 128 ? Colors.White : Colors.Black);
                                
                                // Update currently visible containers with new foreground color
                                // (Virtualized items will be updated by ContainerContentChanging when scrolled into view)
                                int updatedCount = 0;
                                int containerCount = VirtualTextListView.Items.Count;
                                for (int i = 0; i < containerCount; i++)
                                {
                                    try
                                    {
                                        var container = VirtualTextListView.ContainerFromIndex(i) as ListViewItem;
                                        if (container != null)
                                        {
                                            var tb = FindVisualChild<TextBlock>(container);
                                            if (tb != null)
                                            {
                                                tb.Foreground = foregroundBrush;
                                                updatedCount++;
                                            }
                                        }
                                        else
                                        {
                                            break;  // Stop at first virtualized item
                                        }
                                    }
                                    catch { }
                                }
                                
                                System.Diagnostics.Debug.WriteLine($"[ChangeBg_Click] Updated {updatedCount} currently visible containers, {containerCount - updatedCount} virtualized items will be handled by ContainerContentChanging");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ChangeBg_Click] Error updating colors: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        _ = UpdateTextViewer();
                        
                        // Restore scroll position after background change
                        if (scrollPosition.HasValue)
                        {
                            // Give it a moment to render the new content
                            await Task.Delay(100);
                            await SetTextScrollPosition(scrollPosition.Value);
                        }
                    }
                }
                
                // Save settings after background change
                SaveWindowSettings();
            }
        }

        private void SearchMenu_Click(object sender, RoutedEventArgs e)
        {
            SearchOverlay.Visibility = Visibility.Visible;
            SearchBox.Focus(FocusState.Programmatic);
        }

        private void PageJumpMenu_Click(object sender, RoutedEventArgs e)
        {
            PageJumpOverlay.Visibility = Visibility.Visible;
            
            // Extract current page number from ImageIndexText and set it in PageJumpBox
            if (ImageIndexText.Text.Contains("Page:"))
            {
                var parts = ImageIndexText.Text.Split('/');
                if (parts.Length >= 1)
                {
                    string currentPageStr = parts[0].Replace("Page: ", "").Trim();
                    if (int.TryParse(currentPageStr, out int currentPage))
                    {
                        PageJumpBox.Text = currentPage.ToString();
                        // Select all text so user can easily type over it or use backspace
                        PageJumpBox.SelectAll();
                    }
                    else
                    {
                        PageJumpBox.Text = "";
                    }
                }
                else
                {
                    PageJumpBox.Text = "";
                }
            }
            else
            {
                PageJumpBox.Text = "";
            }
            
            PageJumpBox.Focus(FocusState.Programmatic);
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await SearchNext();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                SearchOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private async Task SearchNext()
        {
            string query = SearchBox.Text;
            if (string.IsNullOrEmpty(query)) return;
            if (_useVirtualTextViewer)
            {
                try
                {
                    // Simple forward search in virtualized lines
                    int index = -1;
                    for (int i = 0; i < _virtualTextLines.Count; i++)
                    {
                        if (_virtualTextLines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index >= 0)
                    {
                        // Scroll to the item
                        if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                        if (_virtualScrollViewer != null)
                        {
                            // Estimate offset by item index * avg line height
                            double lineHeight = _textFontSize * 1.8;
                            double target = Math.Max(0, index * lineHeight - (_virtualScrollViewer.ViewportHeight / 2));
                            _virtualScrollViewer.ChangeView(null, target, null, true);
                        }
                    }
                }
                catch { }
            }
            else
            {
                await TextViewer.CoreWebView2.ExecuteScriptAsync($"window.find('{query}', false, false, true)").AsTask();
            }
        }

        private async Task SearchPrev()
        {
            string query = SearchBox.Text;
            if (string.IsNullOrEmpty(query)) return;
            if (_useVirtualTextViewer)
            {
                try
                {
                    int index = -1;
                    for (int i = _virtualTextLines.Count - 1; i >= 0; i--)
                    {
                        if (_virtualTextLines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index >= 0)
                    {
                        if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                        if (_virtualScrollViewer != null)
                        {
                            double lineHeight = _textFontSize * 1.8;
                            double target = Math.Max(0, index * lineHeight - (_virtualScrollViewer.ViewportHeight / 2));
                            _virtualScrollViewer.ChangeView(null, target, null, true);
                        }
                    }
                }
                catch { }
            }
            else
            {
                await TextViewer.CoreWebView2.ExecuteScriptAsync($"window.find('{query}', false, true, true)").AsTask();
            }
        }

        private async void SearchNext_Click(object sender, RoutedEventArgs e) => await SearchNext();
        private async void SearchPrev_Click(object sender, RoutedEventArgs e) => await SearchPrev();
        private void CloseSearch_Click(object sender, RoutedEventArgs e) => SearchOverlay.Visibility = Visibility.Collapsed;

        private async void PageJump_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PageJumpBox.Text, out int page))
            {
                // Jump to approximate position
                if (_useVirtualTextViewer)
                {
                    try
                    {
                        if (_virtualScrollViewer == null) _virtualScrollViewer = FindScrollViewer(VirtualTextListView);
                        if (_virtualScrollViewer != null)
                        {
                            double viewport = _virtualScrollViewer.ViewportHeight;
                            double lineHeight = _textFontSize * 1.8;
                            int linesPerPage = Math.Max(1, (int)Math.Floor(viewport / lineHeight));
                            double pageHeight = linesPerPage * lineHeight;
                            double target = Math.Max(0, (page - 1) * pageHeight);
                            _virtualScrollViewer.ChangeView(null, target, null, true);
                        }
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        // Compute page-aligned scroll position inside WebView to avoid partial lines
                        string script = $@"(function() {{
    const viewportHeight = document.documentElement.clientHeight || window.innerHeight;
    const fontSize = {_textFontSize};
    const lineHeight = fontSize * 1.8;
    const linesPerPage = Math.max(1, Math.floor(viewportHeight / lineHeight));
    const pageHeight = linesPerPage * lineHeight;
    const target = Math.max(0, ({page} - 1) * pageHeight);
    window.scrollTo(0, target);
    return target;
}})();";
                        await TextViewer.CoreWebView2.ExecuteScriptAsync(script).AsTask();
                    }
                    catch { }
                }
            }
            PageJumpOverlay.Visibility = Visibility.Collapsed;
        }

        private void PageJumpBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                PageJump_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                PageJumpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Back)
            {
                // Allow backspace to work normally for text deletion
                // Don't set e.Handled = true so the TextBox can process it
                return;
            }
        }
        private async Task LoadTextFromArchiveEntryAsync(string entryKey)
        {
            _isTextMode = true;
            _currentTextFilePath = $"{_currentArchivePath}!/{entryKey}";
            try
            {
                await _archiveLock.WaitAsync();
                try
                {
                    if (_currentArchive == null) return;

                    var archiveEntry = _currentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                    if (archiveEntry == null)
                    {
                        FileNameText.Text = "압축 파일 내에서 텍스트를 찾을 수 없습니다";
                        return;
                    }

                    // Check entry size
                    if (archiveEntry.Size > 10 * 1024 * 1024) // 10MB limit
                    {
                        System.Diagnostics.Debug.WriteLine($"Large archive entry detected: {archiveEntry.Size} bytes");
                    }

                    // Read entry content
                    using var entryStream = archiveEntry.OpenEntryStream();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream);
                    byte[] bytes = memoryStream.ToArray();

                    string content = "";
                    // 1. UTF-8 시도 (BOM이 있다면 자동으로 처리됨)
                    try
                    {
                        content = System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch { }

                    // HTML 파일인 경우 내부 charset 확인
                    string ext = Path.GetExtension(entryKey).ToLowerInvariant();
                    bool isHtml = (ext == ".html" || ext == ".htm");

                    // 인코딩 개체 정의
                    var euckr = System.Text.Encoding.GetEncoding(949); // Korean (EUC-KR)
                    var sjis = System.Text.Encoding.GetEncoding(932);  // Japanese (Shift-JIS)

                    if (isHtml)
                    {
                        // HTML 내 메타 태그 확인 (EUC-KR, CP949, MS949 대응)
                        if (content.Contains("euc-kr", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("ks_c_5601-1987", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("cp949", StringComparison.OrdinalIgnoreCase))
                        {
                            content = euckr.GetString(bytes);
                        }
                        else if (content.Contains("Shift_JIS", StringComparison.OrdinalIgnoreCase) ||
                                 content.Contains("windows-31j", StringComparison.OrdinalIgnoreCase))
                        {
                            content = sjis.GetString(bytes);
                        }
                    }

                    // 2. 디코딩 실패 시(깨짐 문자 \ufffd 발견) 또는 빈 내용일 때 폴백
                    if (string.IsNullOrEmpty(content) || content.Contains('\ufffd'))
                    {
                        var chosen = ChooseTextEncoding(bytes);
                        content = chosen.GetString(bytes);
                        System.Diagnostics.Debug.WriteLine($"Chosen encoding for archive text: {chosen.WebName}");
                    }

                    _currentTextContent = content;
                    _isCurrentTextContentPreprocessed = false;

                    await InitializeTextViewerAsync();
                    await UpdateTextViewer();

                    ShowTextUI();
                    UpdateStatusBarForText();

                    _ = AddToRecentAsync();
                }
                finally
                {
                    _archiveLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading text from archive: {ex.Message}");
                FileNameText.Text = $"압축 파일 텍스트 로드 오류: {ex.Message}";
            }
        }

        private void TextViewer_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ProcessKeyDown(e.Key, ctrlPressed))
            {
                e.Handled = true;
            }
        }
    }
}
