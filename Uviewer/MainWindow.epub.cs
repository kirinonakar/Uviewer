using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
        private string? _epubTocPath;
        private object _epubLock = new object();
        private double _epubTextWidth = 0;
        private bool _isEpubMode = false;
        public int PendingEpubChapterIndex { get; set; } = -1;
        public int PendingEpubPageIndex { get; set; } = -1;
        private List<UIElement> _epubPages = new();
        private int _currentEpubPageIndex = 0;
        private UIElement? EpubSelectedItem => (_epubPages.Count > 0 && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubPages.Count) ? _epubPages[_currentEpubPageIndex] : null;

        private Dictionary<int, List<UIElement>> _epubPreloadCache = new();
        private CancellationTokenSource? _epubPreloadCts;

        // Optimized Static Regexes
        private static readonly Regex RxEpubFullPath = new Regex("full-path=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubItem = new Regex("<item\\s+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubId = new Regex("id=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubHref = new Regex("href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubNavProp = new Regex("properties=[\"'][^\"']*nav[^\"']*[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubItemRef = new Regex("<itemref[^>]*idref=\"([^\"]+)\"[^>]*/>", RegexOptions.Compiled);
        private static readonly Regex RxEpubSpineToc = new Regex("<spine[^>]*toc=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex RxEpubImgTag = new Regex("(<(?:img|image)\\b[^>]*>)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEpubIsImg = new Regex("^<(?:img|image)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubAnyTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex RxEpubSrc = new Regex("(?:src|xlink:href)=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubScript = new Regex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubStyle = new Regex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubBr = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubRuby = new Regex(@"<ruby[^>]*>(.*?)</ruby>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEpubRp = new Regex(@"<rp[^>]*>.*?</rp>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEpubRt = new Regex(@"<rt[^>]*>(.*?)</rt>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEpubRubySplit = new Regex(@"(\{\{RUBY\|.*?\}\})", RegexOptions.Compiled);
        private static readonly Regex RxEpubXmlns = new Regex("xmlns=\"[^\"]*\"", RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxNav = new Regex("<navPoint[^>]*>([\\s\\S]*?)</navPoint>", RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxText = new Regex("<text>([^<]+)</text>", RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxContent = new Regex("<content[^>]*src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubNavAnchor = new Regex("<a[^>]*href=[\"']([^\"']+)[\"'][^>]*>([^<]+)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);



        public class EpubTocItem
        {
            public string Title { get; set; } = "";
            public string Link { get; set; } = "";
            public int Level { get; set; } = 0;
        }

        private List<EpubTocItem> _epubToc = new();


        public class EpubPageInfoTag
        {
            public int StartLine { get; set; }
            public int LineCount { get; set; }
            public int TotalLinesInChapter { get; set; }
        }



        public int CurrentEpubChapterIndex => _currentEpubChapterIndex;
        public int CurrentEpubPageIndex => _currentEpubPageIndex;

        private DispatcherQueueTimer? _epubResizeTimer;

        public void TriggerEpubResize()
        {
            if (!_isEpubMode) return;

            if (_epubResizeTimer == null)
            {
                _epubResizeTimer = this.DispatcherQueue.CreateTimer();
                _epubResizeTimer.Interval = TimeSpan.FromMilliseconds(500);
                _epubResizeTimer.IsRepeating = false;
                _epubResizeTimer.Tick += (s, e) => 
                {
                     if (_isEpubMode)
                     {
                         _epubPreloadCache.Clear();
                         // Maintain current page ratio or just reload chapter (which defaults to page 0 usually, need to keep index?)
                         // LoadEpubChapterAsync resets index to 0 by default but we can try to restore?
                         // Ideally we want to stay at same *percentage* or *text position*.
                         // For now, simple reload as requested. 
                         int currentLine = 1;
                         if (EpubSelectedItem is Grid g && g.Tag is EpubPageInfoTag tag)
                         {
                             currentLine = tag.StartLine;
                         }
                         
                         _ = LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine);
                     }
                };
            }

            _epubResizeTimer.Stop();
            _epubResizeTimer.Start();
        }

        public async Task RestoreEpubStateAsync(int chapterIndex, int pageIndex)
        {
            if (chapterIndex >= 0 && chapterIndex < _epubSpine.Count)
            {
                 _currentEpubChapterIndex = chapterIndex;
                 await LoadEpubChapterAsync(_currentEpubChapterIndex);
                 
                     // Wait for rendering
                 await Task.Delay(100);
                 
                 if (pageIndex >= 0 && pageIndex < _epubPages.Count)
                 {
                     SetEpubPageIndex(pageIndex);
                 }
                 UpdateEpubStatus();
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
                FileNameText.Text = Strings.EpubLoadError(ex.Message);
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
            if (e.Handled) return;
            if (!_isEpubMode) return;

            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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
            else if (e.Key == Windows.System.VirtualKey.G)
            {
                _ = ShowEpubGoToLineDialog();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.F)
            {
                 ToggleFont();
                 e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Subtract || e.Key == (Windows.System.VirtualKey)189) // - key
            {
                DecreaseTextSize();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Add || e.Key == (Windows.System.VirtualKey)187) // + key
            {
                IncreaseTextSize();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.B)
            {
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
            else if (e.Key == Windows.System.VirtualKey.Home)
            {
                 // Previous Chapter
                 if (_currentEpubChapterIndex > 0)
                 {
                     _currentEpubChapterIndex--;
                     _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                 }
                 e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.End)
            {
                 // Next Chapter
                 if (_currentEpubChapterIndex < _epubSpine.Count - 1)
                 {
                     _currentEpubChapterIndex++;
                     _ = LoadEpubChapterAsync(_currentEpubChapterIndex);
                 }
                 e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Space || (e.Key == Windows.System.VirtualKey.S && !ctrlPressed))
            {
                // Disable Space and S (allow Ctrl+S)
                e.Handled = true;
            }
        }

        private async Task LoadEpubFileAsync(StorageFile file)
        {
             await AddToRecentAsync(true);
             InitializeEpub();
             StopAnimatedWebp();
             _currentEpubFilePath = file.Path;
             
              try
              {
                  _epubPreloadCache.Clear();
                  var stream = await file.OpenStreamForReadAsync();
                  _currentEpubArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                 
                 // 1. Parse Container
                 var rootPath = await ParseEpubContainerAsync();
                 if (string.IsNullOrEmpty(rootPath)) throw new Exception("Invalid container.xml");
                 
                 // 2. Parse OPF
                 await ParseEpubOpfAsync(rootPath);
                 
                 if (_epubSpine.Count == 0) throw new Exception("No content found in EPUB");
                 
                 SwitchToEpubMode();
                 LoadEpubSettings();
                 
                 // 3. Load Chapter (Updated to handle pending positions)
                 if (PendingEpubChapterIndex >= 0 && PendingEpubChapterIndex < _epubSpine.Count)
                 {
                     _currentEpubChapterIndex = PendingEpubChapterIndex;
                     await LoadEpubChapterAsync(_currentEpubChapterIndex);

                     // Page navigation (wait for items to be populated)
                     if (PendingEpubPageIndex > 0)
                     {
                         await Task.Delay(100);
                         if (PendingEpubPageIndex < _epubPages.Count)
                         {
                             SetEpubPageIndex(PendingEpubPageIndex);
                         }
                     }
                 }
                 else
                 {
                     _currentEpubChapterIndex = 0;
                     await LoadEpubChapterAsync(_currentEpubChapterIndex);
                 }
                 
                 // Reset pending values
                 PendingEpubChapterIndex = -1;
                 PendingEpubPageIndex = -1;
                 
                 // 4. Load TOC (Background)
                 _ = ParseEpubTocAsync();

                 FileNameText.Text = file.Name;
                 SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
             }
             catch (Exception ex)
             {
                 FileNameText.Text = Strings.EpubParseError(ex.Message);
             }
        }

        // ... [Rest of File, ensuring CreateTextPages wraps in ScrollViewer] ...



        private void SwitchToEpubMode()
        {
            _isEpubMode = true;
            _isTextMode = false;
            _isAozoraMode = false;
            _aozoraBlocks.Clear(); // Clear text/aozora cache
            _currentTextContent = ""; // Clear raw text
            
            ImageArea.Visibility = Visibility.Collapsed;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Visible; // Defined in MainWindow.xaml
            
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible; // Reuse text toolbar for now
            
            Title = "Uviewer - Image & Text Viewer";
        }

        private async Task<string> ParseEpubContainerAsync()
        {
            var entry = _currentEpubArchive?.GetEntry("META-INF/container.xml");
            if (entry == null) return "";

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            
            // Regex to find full-path
            var match = RxEpubFullPath.Match(content);
            if (match.Success) return match.Groups[1].Value;
            
            return "";
        }

        private async Task ParseEpubOpfAsync(string opfPath)
        {
            var entry = _currentEpubArchive?.GetEntry(opfPath);
            if (entry == null) return;
            
            _epubTocPath = null; // Reset

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            
            // Extract Manifest
            var manifest = new Dictionary<string, string>(); // id -> href
            string opfDir = Path.GetDirectoryName(opfPath)?.Replace("\\", "/") ?? "";

            var itemMatches = RxEpubItem.Matches(content);
            foreach (Match m in itemMatches)
            {
                string tagContent = m.Value;
                var idMatch = RxEpubId.Match(tagContent);
                var hrefMatch = RxEpubHref.Match(tagContent);

                if (idMatch.Success && hrefMatch.Success)
                {
                    string id = idMatch.Groups[1].Value;
                    string href = hrefMatch.Groups[1].Value;
                    manifest[id] = href;

                    // Check for EPUB 3 nav property
                    if (RxEpubNavProp.IsMatch(tagContent))
                    {
                        _epubTocPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    }
                }
            }
            
            // Extract Spine
            _epubSpine.Clear();
            var itemRefMatches = RxEpubItemRef.Matches(content);
            
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
            
            // Try to find TOC path if not already found (EPUB 2 fallback)
            if (string.IsNullOrEmpty(_epubTocPath))
            {
                var spineMatch = RxEpubSpineToc.Match(content);
                if (spineMatch.Success)
                {
                    string tocId = spineMatch.Groups[1].Value;
                    if (manifest.ContainsKey(tocId))
                    {
                         string href = manifest[tocId];
                         _epubTocPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    }
                }
            }
        }

        private async Task LoadEpubChapterAsync(int index, bool fromEnd = false, int targetLine = -1)
        {
            if (index < 0 || index >= _epubSpine.Count) return;

            try
            {
                // Show loading
                if (EpubFastNavOverlay != null) EpubFastNavOverlay.Visibility = Visibility.Visible;
                await Task.Delay(10); // UI yield to show overlay

                List<UIElement> pages;
                if (_epubPreloadCache.TryGetValue(index, out var cachedPages))
                {
                    pages = cachedPages;
                }
                else
                {
                    string path = _epubSpine[index];
                    var entry = _currentEpubArchive?.GetEntry(path);
                    if (entry == null) return;

                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    string html = await reader.ReadToEndAsync();

                    // Convert HTML to Blocks and Images
                    pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[index] = pages;
                }
                
                // Update pages list
                _epubPages = pages;
                _currentEpubPageIndex = -1;

                // Update UI Container
                EpubPageDisplay.Children.Clear();
                foreach (var page in _epubPages)
                {
                    EpubPageDisplay.Children.Add(page);
                }
                
                int targetPage = 0;
                if (targetLine > 0)
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        if (pages[i] is Grid pg && pg.Tag is EpubPageInfoTag tag)
                        {
                            if (targetLine >= tag.StartLine && targetLine < tag.StartLine + tag.LineCount)
                            {
                                targetPage = i;
                                break;
                            }
                            // Last page fallback
                            if (i == pages.Count - 1 && targetLine >= tag.StartLine)
                            {
                                targetPage = i;
                                break;
                            }
                        }
                    }
                }
                else if (fromEnd && pages.Count > 0)
                {
                    targetPage = pages.Count - 1;
                }
                
                SetEpubPageIndex(targetPage);

                // Trigger preloading for neighbors
                _ = PreloadEpubChaptersAsync(index);
            }
            finally
            {
                if (EpubFastNavOverlay != null) EpubFastNavOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PreloadEpubChaptersAsync(int currentIndex)
        {
            _epubPreloadCts?.Cancel();
            _epubPreloadCts = new CancellationTokenSource();
            var token = _epubPreloadCts.Token;

            try
            {
                // Preload next and previous
                var indicesToPreload = new List<int>();
                if (currentIndex + 1 < _epubSpine.Count) indicesToPreload.Add(currentIndex + 1);
                if (currentIndex - 1 >= 0) indicesToPreload.Add(currentIndex - 1);

                foreach (int idx in indicesToPreload)
                {
                    if (token.IsCancellationRequested) return;
                    if (_epubPreloadCache.ContainsKey(idx)) continue;

                    string path = _epubSpine[idx];
                    var entry = _currentEpubArchive?.GetEntry(path);
                    if (entry == null) continue;

                    string html;
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        html = await reader.ReadToEndAsync();
                    }

                    if (token.IsCancellationRequested) return;

                    var pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[idx] = pages;

                    // Small delay to let UI breathe
                    await Task.Delay(50, token);
                }
                
                // Keep cache size reasonable (e.g., current + 2 neighbors each side)
                if (_epubPreloadCache.Count > 5)
                {
                    var keysToRemove = _epubPreloadCache.Keys
                        .Where(k => Math.Abs(k - currentIndex) > 2)
                        .ToList();
                    foreach (var k in keysToRemove) _epubPreloadCache.Remove(k);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Epub preload error: {ex.Message}");
            }
        }

        private void UpdateEpubStatus()
        {
            if (!_isEpubMode) return;
            
            int currentPage = _currentEpubPageIndex + 1;
            int totalPages = _epubPages.Count;
            if (totalPages == 0) totalPages = 1;

            int currentLine = 1;
            int totalLines = 1;

            if (EpubSelectedItem is Grid g && g.Tag is EpubPageInfoTag tag)
            {
                currentLine = tag.StartLine;
                totalLines = tag.TotalLinesInChapter;
            }

            double totalProgress = 0;
            if (_epubSpine.Count > 0)
            {
                double chapterProgress = (double)_currentEpubChapterIndex / _epubSpine.Count;
                double pageProgressInChapter = (double)(currentPage - 1) / totalPages / _epubSpine.Count;
                totalProgress = (chapterProgress + pageProgressInChapter) * 100.0;
                if (totalProgress > 100) totalProgress = 100;
            }

            if (ImageInfoText != null)
            {
                ImageInfoText.Text = $"Line {currentLine} / {totalLines}";
            }

            if (TextProgressText != null)
            {
                TextProgressText.Text = $"{totalProgress:F1}%";
            }
            
            if (ImageIndexText != null)
            {
                ImageIndexText.Text = $"{currentPage} / {totalPages} (Ch.{_currentEpubChapterIndex + 1})";
            }

            _ = AddToRecentAsync(true);
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
            
            _epubTextWidth = 42 * _textFontSize; 
            
            // Regex to split by img/image tags
            var segments = RxEpubImgTag.Split(html);

            bool hasImages = html.Contains("<img", StringComparison.OrdinalIgnoreCase) || html.Contains("<image", StringComparison.OrdinalIgnoreCase);
            bool isFirstContent = true;

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;

                // Check if segment is an image tag
                if (RxEpubIsImg.IsMatch(segment))
                {
                    var imgPage = await CreateImagePageAsync(segment, currentPath);
                    if (imgPage != null) 
                    {
                        pages.Add(imgPage);
                        isFirstContent = false;
                    }
                }
                else
                {
                    var textPages = await CreateTextPagesAsync(segment);
                    if (textPages.Count == 0) continue;

                    // [User Request] If this is a short title page at the start of a chapter with images, skip it.
                    if (isFirstContent && hasImages && textPages.Count == 1)
                    {
                        string plainText = RxEpubAnyTag.Replace(segment, "");
                        plainText = System.Net.WebUtility.HtmlDecode(plainText).Trim();
                        if (plainText.Length > 0 && plainText.Length < 100)
                        {
                            isFirstContent = false; // Mark that we've handled the "first" part
                            continue;
                        }
                    }

                    pages.AddRange(textPages);
                    isFirstContent = false;
                }
            }

            return pages;
        }



        private void EpubTouchOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;

             var ptr = e.GetCurrentPoint(EpubArea);
             if (ptr.Properties.IsLeftButtonPressed)
             {
                 // Check for fullscreen edge zones first
                 if (_isFullscreen)
                 {
                     var rootPt = e.GetCurrentPoint(RootGrid);
                     // Top Edge -> Show Toolbar
                     if (rootPt.Position.Y < FullscreenTopHoverZone)
                     {
                         if (ToolbarGrid.Visibility != Visibility.Visible)
                         {
                             ToolbarGrid.Visibility = Visibility.Visible;
                         }
                         StartOrRestartFullscreenToolbarHideTimer();
                         e.Handled = true;
                         return;
                     }
                     
                     // Left Edge -> Show Sidebar
                     if (rootPt.Position.X < FullscreenLeftHoverZone)
                     {
                         if (SidebarGrid.Visibility != Visibility.Visible)
                         {
                             SidebarColumn.Width = new GridLength(_SidebarWidth);
                             SidebarGrid.Visibility = Visibility.Visible;
                         }
                         StartOrRestartFullscreenSidebarHideTimer();
                         e.Handled = true;
                         return;
                     }
                 }
                 
                 // Navigation: Left half = prev page, Right half = next page
                 double x = ptr.Position.X;
                 double areaWidth = EpubArea.ActualWidth;
                 
                 if (x < areaWidth / 2)
                 {
                     _ = NavigateEpubAsync(-1);
                 }
                 else
                 {
                     _ = NavigateEpubAsync(1);
                 }
                 
                 e.Handled = true;
             }
        }

        private void EpubTouchOverlay_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEpubMode) return;

            var ptr = e.GetCurrentPoint(EpubArea);
            var delta = ptr.Properties.MouseWheelDelta;

            if (delta > 0)
            {
                // Scroll up -> Previous page
                _ = NavigateEpubAsync(-1);
            }
            else if (delta < 0)
            {
                // Scroll down -> Next page
                _ = NavigateEpubAsync(1);
            }

            e.Handled = true;
        }

        private void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;
             // Handled by FlipView parent
        }

        private async Task<UIElement?> CreateImagePageAsync(string imgTag, string currentPath)
        {
            // Extract src or xlink:href (for svg image tags)
            var match = RxEpubSrc.Match(imgTag);
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
                    Stretch = Stretch.Uniform, // Keep Stretch on the image
                };
                
                // Use a Viewbox or just Grid to center
                var grid = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { img },
                    Background = GetEpubThemeBackground(), // Use theme background
                    Opacity = 0,
                    IsHitTestVisible = false
                };
                
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

        private async Task<List<UIElement>> CreateTextPagesAsync(string htmlContent)
        {
            var blocks = ParseHtmlToBlocks(htmlContent);
            if (blocks.Count == 0) return new List<UIElement>();

            List<UIElement> textPages = new List<UIElement>();
            
            // Get available dimensions
            double availableWidth = EpubArea?.ActualWidth ?? 800;
            if (availableWidth < 100) availableWidth = RootGrid.ActualWidth - (SidebarColumn?.ActualWidth ?? 320);
            
            double availableHeight = EpubArea?.ActualHeight ?? 800;
            if (availableHeight < 200) 
            {
               availableHeight = RootGrid.ActualHeight;
               if (!_isFullscreen) availableHeight -= 120;
            }
            
            // Text Area Width (subtracting 20 padding each side)
            double textPadding = 20;
            double targetWidth = Math.Min(availableWidth - (textPadding * 2), 45 * _textFontSize);
            if (targetWidth < 200) targetWidth = 600;

            // Reserved height (subtracting padding + ruby safety buffer)
            double targetHeight = availableHeight - 45; 
            if (targetHeight < 200) targetHeight = 800; 

            // 1. Create Master RichTextBlock
            var rtb = new RichTextBlock 
            { 
                IsTextSelectionEnabled = false,
                FontFamily = new FontFamily(_textFontFamily),
                FontSize = _textFontSize,
                Foreground = GetEpubThemeForeground(),
                Width = targetWidth,
                Height = targetHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = _textFontSize * 2, // Increased multiplier for better ruby spacing
                TextWrapping = TextWrapping.Wrap
            };

            foreach (var b in blocks) rtb.Blocks.Add(b);

            var page1Grid = new Grid 
            { 
                Background = GetEpubThemeBackground(), 
                Padding = new Thickness(20, 25, 20, 20), // Added top padding for ruby safety
                Opacity = 0,
                IsHitTestVisible = false
            };
            page1Grid.Children.Add(rtb);
            page1Grid.PointerPressed += EpubPage_PointerPressed;
            textPages.Add(page1Grid);
            
            // Force measure
            rtb.Measure(new Windows.Foundation.Size((float)targetWidth, (float)targetHeight));

            int linesPerPage = (int)(targetHeight / (_textFontSize * 2));
            if (linesPerPage < 1) linesPerPage = 1;
            
            int cumulativeLines = linesPerPage;
            page1Grid.Tag = new EpubPageInfoTag { StartLine = 1, LineCount = linesPerPage };

            // Chain Overflow
            FrameworkElement lastLinked = rtb;
            int maxPages = 2000;
            int pageCount = 1;

            while (pageCount < maxPages)
            {
                // [Optimization] Yield UI control periodically to keep app responsive
                if (pageCount % 50 == 0) await Task.Delay(1);

                bool hasOverflow = false;
                if (lastLinked is RichTextBlock m && m.HasOverflowContent) hasOverflow = true;
                else if (lastLinked is RichTextBlockOverflow o && o.HasOverflowContent) hasOverflow = true;

                if (!hasOverflow) break;

                var overflow = new RichTextBlockOverflow
                {
                    Width = targetWidth,
                    Height = targetHeight,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0),
                    Padding = new Thickness(0)
                };

                if (lastLinked is RichTextBlock master) master.OverflowContentTarget = overflow;
                else if (lastLinked is RichTextBlockOverflow ov) ov.OverflowContentTarget = overflow;

                var pageGrid = new Grid 
                { 
                    Background = GetEpubThemeBackground(), 
                    Padding = new Thickness(20, 25, 20, 20), // Added top padding for ruby safety
                    Opacity = 0,
                    IsHitTestVisible = false
                };
                pageGrid.Children.Add(overflow);
                pageGrid.PointerPressed += EpubPage_PointerPressed;
                textPages.Add(pageGrid);

                overflow.Measure(new Windows.Foundation.Size((float)targetWidth, (float)targetHeight));
                
                pageGrid.Tag = new EpubPageInfoTag { StartLine = cumulativeLines + 1, LineCount = linesPerPage };
                cumulativeLines += linesPerPage;

                lastLinked = overflow;
                pageCount++;
            }
            
            // Set total lines in chapter for each page tag
            foreach (var page in textPages)
            {
                if (page is Grid g && g.Tag is EpubPageInfoTag tag)
                {
                    tag.TotalLinesInChapter = cumulativeLines;
                }
            }
            
            return textPages;
        }

        private void AddTextPage(List<UIElement> pages, List<Block> pageBlocks, double width)
        {
            // This method is now obsolete as pagination is handled inline in CreateTextPages
            // to ensure accurate container measurement.
        }

        private async Task ShowEpubGoToLineDialog()
        {
             int totalLines = 1;
             int currentLine = 1;
             if (EpubSelectedItem is Grid cg && cg.Tag is EpubPageInfoTag ctag)
             {
                 currentLine = ctag.StartLine;
                 totalLines = ctag.TotalLinesInChapter;
             }
             else if (_epubPages.Count > 0)
             {
                 totalLines = _epubPages.Count; // Page fallback if no tag
                 currentLine = _currentEpubPageIndex + 1;
             }

             var dialog = new ContentDialog
             {
                 Title = Strings.DialogTitle,
                 PrimaryButtonText = Strings.DialogPrimary,
                 CloseButtonText = Strings.DialogClose,
                 DefaultButton = ContentDialogButton.Primary,
                 XamlRoot = RootGrid.XamlRoot
             };

             var input = new TextBox 
             { 
                 PlaceholderText = $"1 - {totalLines}",
                 Text = currentLine.ToString(),
                 InputScope = new InputScope { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } }
             };
             
             input.SelectAll();
             dialog.Content = input;

             void PerformGoToLine()
             {
                 if (int.TryParse(input.Text, out int targetLine))
                 {
                     // Find page that contains this line
                     for (int i = 0; i < _epubPages.Count; i++)
                     {
                         if (_epubPages[i] is Grid pg && pg.Tag is EpubPageInfoTag ptag)
                         {
                             if (targetLine >= ptag.StartLine && targetLine < ptag.StartLine + ptag.LineCount)
                             {
                                 SetEpubPageIndex(i);
                                 return;
                             }
                             // Last page safety or exact chapter end
                             if (i == _epubPages.Count - 1 && targetLine >= ptag.StartLine)
                             {
                                 SetEpubPageIndex(i);
                                 return;
                             }
                         }
                     }
                     
                     // Fallback to page-based indexing if tags missing or out of bounds
                     int pageIndex = targetLine - 1;
                     if (pageIndex >= 0 && pageIndex < _epubPages.Count)
                     {
                         SetEpubPageIndex(pageIndex);
                     }
                 }
             }

             input.KeyDown += (s, e) => 
             {
                 if (e.Key == Windows.System.VirtualKey.Enter)
                 {
                     dialog.Hide();
                     PerformGoToLine();
                 }
             };

             var result = await dialog.ShowAsync();
             if (result == ContentDialogResult.Primary)
             {
                 PerformGoToLine();
             }
        }

        private List<Block> ParseHtmlToBlocks(string html)
        {
            var blocks = new List<Block>();
            
            // Cleanup
            html = RxEpubScript.Replace(html, "");
            html = RxEpubStyle.Replace(html, "");
            
            // Pre-process special tags
            html = RxEpubBr.Replace(html, "\n");

            // --- Ruby Processing ---
            html = RxEpubRuby.Replace(html, m => 
            {
                string rubyContent = m.Groups[1].Value;
                // Strip <rp>
                rubyContent = RxEpubRp.Replace(rubyContent, "");
                
                StringBuilder sb = new StringBuilder();
                var rtMatches = RxEpubRt.Matches(rubyContent);
                
                int lastIndex = 0;
                foreach (Match rtMatch in rtMatches)
                {
                    string basePart = rubyContent.Substring(lastIndex, rtMatch.Index - lastIndex);
                    string rtPart = rtMatch.Groups[1].Value;
                    
                    // Cleanup tags like <rb> from basePart and rtPart
                    string baseText = RxEpubAnyTag.Replace(basePart, "").Trim();
                    string rtText = RxEpubAnyTag.Replace(rtPart, "").Trim();
                    
                    if (!string.IsNullOrEmpty(baseText) || !string.IsNullOrEmpty(rtText))
                    {
                        sb.Append($"{{{{RUBY|{baseText}|~|{rtText}}}}}");
                    }
                    lastIndex = rtMatch.Index + rtMatch.Length;
                }
                
                // Append any remaining text after the last <rt>
                if (lastIndex < rubyContent.Length)
                {
                    string tail = rubyContent.Substring(lastIndex);
                    string tailText = RxEpubAnyTag.Replace(tail, "").Trim();
                    if (!string.IsNullOrEmpty(tailText)) sb.Append(tailText);
                }
                
                return sb.ToString();
            });
            
            // Strip remaining tags
            html = RxEpubAnyTag.Replace(html, ""); 
            html = System.Net.WebUtility.HtmlDecode(html);
            
            // Normalize newlines and whitespace
            html = html.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Split
            var lines = html.Split('\n');
            
            foreach (var rawLine in lines)
            {
                // aggressive trim to find "empty" lines even if they contain nbsp or fullwidth space
                var line = rawLine; 
                var trimmed = line.Replace('\u3000', ' ').Replace('\u00A0', ' ').Trim();
                
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                
                var p = new Paragraph();
                p.TextAlignment = TextAlignment.Left;
                p.Margin = new Thickness(0, 0, 0, _textFontSize * 0.5); 
                p.LineHeight = _textFontSize * 2; // Increased multiplier for better ruby spacing
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; // Force rigid line height
                
                // Tokenize by custom Ruby marker
                var tokens = RxEpubRubySplit.Split(line);
                
                foreach (var token in tokens)
                {
                    if (token.StartsWith("{{RUBY|"))
                    {
                        var content = token.Substring(7, token.Length - 9); // Strip {{RUBY| and }}
                        var parts = content.Split(new[] { "|~|" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            p.Inlines.Add(CreateRuby(parts[0], parts[1]));
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
            
            // Use Auto height
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ruby
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Base
            
            var rt = new TextBlock
            {
                Text = rubyText,
                FontSize = _textFontSize * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                FontFamily = new FontFamily(_textFontFamily),
                Opacity = 1,
                TextLineBounds = TextLineBounds.Tight,
                IsHitTestVisible = false,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // [추가] 루비가 너무 길어지는 경우 장평(ScaleX)을 75%로 설정
            bool shouldScale = (baseText.Length == 1 && rubyText.Length >= 3) || 
                               (baseText.Length == 2 && rubyText.Length >= 5) ||
                               (baseText.Length == 3 && rubyText.Length >= 7);
            if (shouldScale)
            {
                rt.RenderTransform = new ScaleTransform { ScaleX = 0.75 };
                rt.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }
            
            var rb = new TextBlock
            {
                Text = baseText,
                FontSize = _textFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeForeground(),
                FontFamily = new FontFamily(_textFontFamily),
                TextLineBounds = TextLineBounds.Tight,
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };
            
            Grid.SetRow(rt, 0);
            Grid.SetRow(rb, 1);
            
            grid.Children.Add(rt);
            grid.Children.Add(rb);
            
            // [수정] 중요: Grid 자체의 수직 정렬을 Bottom으로 설정하여
            // 본문 텍스트(rb)의 하단이 주변 텍스트의 기준선(Baseline)에 맞도록 유도
            grid.VerticalAlignment = VerticalAlignment.Bottom;

            // [수정] 루비가 3자인 경우 자간이 넓어지는 것을 방지하기 위해 왼쪽/오른쪽 마진을 음수로 설정
            double sideMargin = 0;
            if (rubyText.Length == 3 && baseText.Length == 1)
            {
                sideMargin = -(_textFontSize * 0.25);
            }
            grid.Margin = new Thickness(sideMargin, 0, sideMargin, 0); 

            return new InlineUIContainer { Child = grid };
        }

        // FlipView SelectionChanged logic is now handled in SetEpubPageIndex
        
        // Navigation Handlers (Hooked from MainWindow.xaml.cs logic ideally, or replicated keys)
        // Since we are in Partial Class, we can handle keys if we route them.
        
        public async Task NavigateEpubAsync(int direction)
        {
            if (!_isEpubMode) return;
            
            int newIndex = _currentEpubPageIndex + direction;
            
            if (newIndex >= 0 && newIndex < _epubPages.Count)
            {
                SetEpubPageIndex(newIndex);
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
            LoadTextSettings();
            UpdateEpubToolbarUI();
        }

        private void SaveEpubSettings()
        {
            SaveTextSettings();
        }

        private void UpdateEpubToolbarUI()
        {
            if (TextSizeLevelText != null)
            {
                TextSizeLevelText.Text = _textFontSize.ToString();
            }
        }



        private void UpdateEpubVisuals()
        {
            var bg = GetEpubThemeBackground();
            var fg = GetEpubThemeForeground();
            
            if (EpubArea != null) EpubArea.Background = bg;
            // if (StatusBarGrid != null) StatusBarGrid.Background = bg; // Keep status bar default color

            if (_epubPages != null)
            {
                foreach (var item in _epubPages)
                {
                    if (item is Grid pageGrid)
                    {
                        pageGrid.Background = bg;
                        
                        // Find child... deeper
                        if (pageGrid.Children.Count > 0 && pageGrid.Children[0] is RichTextBlock rtb)
                        {
                            rtb.Foreground = fg;
                            rtb.FontFamily = new FontFamily(_textFontFamily);
                            
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
                                                     rubytb.FontFamily = new FontFamily(_textFontFamily);
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
            if (_themeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            return new SolidColorBrush(Colors.Black);
        }
        
        private Brush GetEpubThemeBackground()
        {
             if (_themeIndex == 0) return new SolidColorBrush(Colors.White);
             if (_themeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
             return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
        }

        private async Task ParseEpubTocAsync()
        {
             _epubToc.Clear();
             
             // Try parsing explicit TOC
             if (!string.IsNullOrEmpty(_epubTocPath))
             {
                 try
                 {
                     var entry = _currentEpubArchive?.GetEntry(_epubTocPath);
                     if (entry != null)
                     {
                         using var stream = entry.Open();
                         using var reader = new StreamReader(stream);
                         string content = await reader.ReadToEndAsync();
                         
                         string ext = Path.GetExtension(_epubTocPath).ToLower();
                         if (ext == ".ncx")
                         {
                             ParseNcxToc(content);
                         }
                         else if (ext == ".html" || ext == ".xhtml" || ext == ".htm")
                         {
                             ParseNavToc(content);
                         }
                     }
                 }
                 catch (Exception ex)
                 {
                     System.Diagnostics.Debug.WriteLine($"TOC Parse Error: {ex.Message}");
                 }
             }

             // Fallback if empty
             if (_epubToc.Count == 0 && _epubSpine.Count > 0)
             {
                 for (int i = 0; i < _epubSpine.Count; i++)
                 {
                     _epubToc.Add(new EpubTocItem 
                     { 
                         Title = $"Chapter {i + 1}", 
                         Link = _epubSpine[i],
                         Level = 1
                     });
                 }
             }
        }


        private void ParseNcxToc(string xml)
        {
            // Simple Regex parsing for NCX
            xml = RxEpubXmlns.Replace(xml, "");
            
            var matches = RxEpubNcxNav.Matches(xml);
            foreach (Match m in matches)
            {
                string inner = m.Groups[1].Value;
                
                string title = "";
                var tm = RxEpubNcxText.Match(inner);
                if (tm.Success) title = tm.Groups[1].Value;
                
                string src = "";
                var cm = RxEpubNcxContent.Match(inner);
                if (cm.Success) src = cm.Groups[1].Value;
                
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(src))
                {
                    string fullSrc = ResolveRelativePath(_epubTocPath!, src);
                    _epubToc.Add(new EpubTocItem { Title = title, Link = fullSrc });
                }
            }
        }
        
        private void ParseNavToc(string html)
        {
            // Extract <a> tags with href
            var matches = RxEpubNavAnchor.Matches(html);
            foreach (Match m in matches)
            {
                string src = m.Groups[1].Value;
                string title = m.Groups[2].Value.Trim();
                
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(src))
                {
                    string fullSrc = ResolveRelativePath(_epubTocPath!, src);
                    _epubToc.Add(new EpubTocItem { Title = title, Link = fullSrc });
                }
            }
        }

        public async void JumpToEpubTocItem(EpubTocItem item)
        {
             // item.Link might contain hash: chapter.html#id
             string path = item.Link;
             string hash = "";
             int hashIdx = path.IndexOf('#');
             if (hashIdx >= 0)
             {
                 hash = path.Substring(hashIdx + 1);
                 path = path.Substring(0, hashIdx);
             }
             
             // Find in spine
             // Spine stores full paths from container (OPS/chapter1.html)
             // item.Link should already be resolved to full path
             
             int index = -1;
             for (int i = 0; i < _epubSpine.Count; i++)
             {
                 if (_epubSpine[i].Equals(path, StringComparison.OrdinalIgnoreCase))
                 {
                     index = i;
                     break;
                 }
             }
             
             if (index >= 0)
             {
                 _currentEpubChapterIndex = index;
                 await LoadEpubChapterAsync(index);
                 // If hash exists, we could theoretically scroll to it, but our rendering is page based
                 // so mapping hash to page is hard without DOM analysis.
                 // For now, just jump to chapter.
             }
        }
        private void SetEpubPageIndex(int index)
        {
            if (index >= 0 && index < _epubPages.Count)
            {
                // Hide current page
                if (_currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubPages.Count)
                {
                    _epubPages[_currentEpubPageIndex].Opacity = 0;
                    _epubPages[_currentEpubPageIndex].IsHitTestVisible = false;
                }

                _currentEpubPageIndex = index;
                
                // Show new page
                _epubPages[_currentEpubPageIndex].Opacity = 1;
                _epubPages[_currentEpubPageIndex].IsHitTestVisible = true;
                
                UpdateEpubStatus();
            }
        }

        public void ClearEpubCache()
        {
            _epubPreloadCache.Clear();
        }
    }
}
