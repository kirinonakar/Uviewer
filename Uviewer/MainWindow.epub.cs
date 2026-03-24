using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.UI;
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
        private SemaphoreSlim _epubArchiveLock = new SemaphoreSlim(1, 1);

        private bool _isEpubMode = false;
        public int PendingEpubChapterIndex { get; set; } = -1;
        public int PendingEpubPageIndex { get; set; } = -1;

        // Win2D 기반 EPUB 페이지 정보
        public class EpubWin2DPage
        {
            public List<AozoraBindingModel> Blocks { get; set; } = new();
            public int StartLine { get; set; }
            public int LineCount { get; set; }
            public int TotalLinesInChapter { get; set; }
            public bool IsImagePage { get; set; }
            public string ImagePath { get; set; } = "";
        }

        private List<EpubWin2DPage> _epubWin2DPages = new();
        private int _currentEpubPageIndex = 0;
        private EpubWin2DPage? CurrentEpubWin2DPage => (_epubWin2DPages.Count > 0 && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count) ? _epubWin2DPages[_currentEpubPageIndex] : null;
        private bool _isEpubShowingTwoPages = false;

        private Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache = new();
        private Dictionary<int, bool> _epubChapterHasText = new();
        private CancellationTokenSource? _epubPreloadCts;

        // 이미지 캐시 (Win2D CanvasBitmap)
        private Dictionary<string, CanvasBitmap?> _epubImageCache = new();

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
                         _epubImageCache.Clear();
                         int currentLine = CurrentEpubWin2DPage?.StartLine ?? 1;
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
                 await LoadEpubChapterAsync(_currentEpubChapterIndex, token: CancellationToken.None);
                 
                     // Wait for rendering
                 await Task.Delay(100, CancellationToken.None);
                 
                 if (pageIndex >= 0 && pageIndex < _epubWin2DPages.Count)
                 {
                     SetEpubPageIndex(pageIndex);
                 }
                 UpdateEpubStatus();
            }
        }

        private async Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token = default)
        {
            try
            {
                if (entry.FilePath != null)
                {
                    var file = await StorageFile.GetFileFromPathAsync(entry.FilePath);
                    await LoadEpubFileAsync(file, token);
                }
                else if (entry.IsWebDavEntry && _isWebDavMode)
                {
                    FileNameText.Text = $"EPUB 다운로드 중: {entry.DisplayName}...";
                    var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                    if (!string.IsNullOrEmpty(tempPath) && !token.IsCancellationRequested)
                    {
                        entry.FilePath = tempPath;
                        var file = await StorageFile.GetFileFromPathAsync(tempPath);
                        await LoadEpubFileAsync(file, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileNameText.Text = Strings.EpubLoadError(ex.Message);
            }
        }

        private void InitializeEpub()
        {
            // Now handled in MainWindow.keys.cs via RootGrid_PreviewKeyDown
        }

        private async Task LoadEpubFileAsync(StorageFile file, CancellationToken token = default)
        {
             await AddToRecentAsync(true);
             InitializeEpub();
             StopAnimatedWebp();

             // Close other formats first
             CloseCurrentArchive();
             await CloseCurrentPdfAsync();
             CloseCurrentEpub();

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
                 // Ensure the EPUB file we are loading is in the current image entries (album)
                 // to prevent sidebar sync logic from reverting to previous files.
                 if (_imageEntries == null || _imageEntries.Count == 0 || !_imageEntries.Any(e => e.FilePath != null && e.FilePath.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                 {
                     _imageEntries = new List<ImageEntry>
                     {
                         new ImageEntry { DisplayName = file.Name, FilePath = file.Path }
                     };
                     _currentIndex = 0;
                 }

                 LoadEpubSettings();
                // If the app is currently in vertical mode, ensure UI reflects that
                if (_isVerticalMode)
                {
                    if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = true;
                    if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                    if (!_verticalKeyAttached && RootGrid != null)
                    {
                        RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                        _verticalKeyAttached = true;
                    }
                }
                 
                 // 3. Load Chapter (Updated to handle pending positions)
                 if (PendingEpubChapterIndex >= 0 && PendingEpubChapterIndex < _epubSpine.Count)
                 {
                     _currentEpubChapterIndex = PendingEpubChapterIndex;
                     await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: _aozoraPendingTargetLine, targetPage: PendingEpubPageIndex, token: token);

                     // Page navigation (wait for items to be populated)
                     if (!_isVerticalMode && PendingEpubPageIndex > 0)
                     {
                         await Task.Delay(100, token);
                         if (PendingEpubPageIndex < _epubWin2DPages.Count)
                         {
                             SetEpubPageIndex(PendingEpubPageIndex);
                         }
                     }
                 }
                 else
                 {
                     _currentEpubChapterIndex = 0;
                     await LoadEpubChapterAsync(_currentEpubChapterIndex, token: token);
                 }
                 
                 // Reset pending values
                PendingEpubChapterIndex = -1;
                PendingEpubPageIndex = -1;
                _aozoraPendingTargetLine = 0;
                _epubChapterHasText.Clear();
                 
                 // 4. Load TOC (Background)
                 _ = ParseEpubTocAsync();

                 FileNameText.Text = GetFormattedDisplayName(file.Name, false);
                 SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
             }
             catch (Exception ex)
             {
                 FileNameText.Text = Strings.EpubParseError(ex.Message);
             }
        }

        // ... [Rest of File, ensuring CreateTextPages wraps in ScrollViewer] ...


        private void CloseCurrentEpub()
        {
            if (_currentEpubArchive == null && _currentEpubFilePath == null) return;

            if (_epubArchiveLock.Wait(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    _currentEpubArchive?.Dispose();
                    _currentEpubArchive = null;
                    _currentEpubFilePath = null;
                    _currentEpubChapterIndex = 0;
                    _currentEpubPageIndex = 0;
                    _epubSpine.Clear();
                    _epubWin2DPages.Clear();
                    _epubPreloadCache.Clear();
                    _epubImageCache.Clear();
                    _aozoraBlocks.Clear();
                    ClearVerticalDisplayState();
                }
                finally
                {
                    _epubArchiveLock.Release();
                }
            }
        }

        private void SwitchToEpubMode()
        {
            _isEpubMode = true;
            _isTextMode = false;
            _isAozoraMode = false;
            _aozoraBlocks.Clear(); // Clear text/aozora cache
            _currentTextContent = ""; // Clear raw text

            // [추가] EPUB 모드 진입 시 창 크기 검사 및 조정
            EnsureMinWindowSizeForText();
            
            ImageArea.Visibility = Visibility.Collapsed;
            if (_isVerticalMode)
            {
                EpubArea.Visibility = Visibility.Collapsed;
                TextArea.Visibility = Visibility.Visible;
            }
            else
            {
                EpubArea.Visibility = Visibility.Visible;
                TextArea.Visibility = Visibility.Collapsed;
            }
            
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible; // Reuse text toolbar for now
            SideBySideToolbarPanel.Visibility = Visibility.Visible;
            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();
            
            Title = "Uviewer - Image & Text Viewer";
        }

        private async Task<string> ParseEpubContainerAsync()
        {
            var entry = _currentEpubArchive?.GetEntry("META-INF/container.xml");
            if (entry == null) return "";

            string content;
            await _epubArchiveLock.WaitAsync();
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                content = await reader.ReadToEndAsync();
            }
            finally
            {
                _epubArchiveLock.Release();
            }
            
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

            string content;
            await _epubArchiveLock.WaitAsync();
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                content = await reader.ReadToEndAsync();
            }
            finally
            {
                _epubArchiveLock.Release();
            }
            
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



        private async Task PreloadEpubChaptersAsync(int currentIndex)
        {
            _epubPreloadCts?.Cancel();
            _epubPreloadCts = new CancellationTokenSource();
            var token = _epubPreloadCts.Token;

            try
            {
                // Preload next 3 and previous 1
                var indicesToPreload = new List<int>();
                for (int i = 1; i <= 3; i++)
                {
                    if (currentIndex + i < _epubSpine.Count) indicesToPreload.Add(currentIndex + i);
                }
                if (currentIndex - 1 >= 0) indicesToPreload.Add(currentIndex - 1);

                foreach (int idx in indicesToPreload)
                {
                    if (token.IsCancellationRequested) return;
                    if (_epubPreloadCache.ContainsKey(idx)) continue;

                    string path = _epubSpine[idx];
                    var entry = _currentEpubArchive?.GetEntry(path);
                    if (entry == null) continue;

                    string html;
                    await _epubArchiveLock.WaitAsync();
                    try
                    {
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream))
                        {
                            html = await reader.ReadToEndAsync();
                        }
                    }
                    finally
                    {
                        _epubArchiveLock.Release();
                    }

                    if (token.IsCancellationRequested) return;

                    var pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[idx] = pages;
                    _epubChapterHasText[idx] = pages.Any(p => !p.IsImagePage);

                    await Task.Delay(50, token);

                    // If this is the next chapter, refresh to check if we can now show side-by-side.
                    if (idx == _currentEpubChapterIndex + 1 && _isSideBySideMode)
                    {
                        var _ = DispatcherQueue.TryEnqueue(() => 
                        {
                             if (_isEpubMode) SetEpubPageIndex(_currentEpubPageIndex);
                        });
                    }
                }
                
                // Keep cache size reasonable (current + 3 ahead + 1 behind)
                if (_epubPreloadCache.Count > 8)
                {
                    var keysToRemove = _epubPreloadCache.Keys
                        .Where(k => (k < currentIndex - 1) || (k > currentIndex + 3))
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
            int totalPages = _epubWin2DPages.Count;
            if (totalPages == 0) totalPages = 1;

            var pg = CurrentEpubWin2DPage;
            int currentLine = pg?.StartLine ?? 1;
            int totalLines = pg?.TotalLinesInChapter ?? 1;

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
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);
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
                relativePath = Uri.UnescapeDataString(relativePath);

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

        private async Task<List<EpubWin2DPage>> RenderEpubPagesAsync(string html, string currentPath)
        {
            var pages = new List<EpubWin2DPage>();

            // EPUB 챕터의 블록을 AozoraBindingModel로 파싱 (세로 모드와 동일한 파이프라인 활용)
            var allBlocks = ParseEpubHtmlToAozoraBlocks(html, currentPath, _currentEpubChapterIndex);

            if (allBlocks.Count == 0) return pages;

            // 페이지 분할 파라미터 계산
            float availableWidth = (float)(EpubArea?.ActualWidth ?? 800);
            if (availableWidth < 100) availableWidth = (float)(RootGrid.ActualWidth - (SidebarColumn?.ActualWidth ?? 320));
            float availableHeight = (float)(EpubArea?.ActualHeight ?? 800);
            if (availableHeight < 200) availableHeight = (float)(RootGrid.ActualHeight - 120);

            float marginH = 80f; // Left 40 + Right 40
            float marginV = 40f; // Top 30 + Bottom 10
            float limitedWidth = (float)(_textFontSize * 42); 
            float maxWidth = availableWidth - marginH;
            if (maxWidth > limitedWidth) maxWidth = limitedWidth; // Limit to 42 characters
            if (maxWidth < 200) maxWidth = 600;

            float pageHeight = availableHeight - marginV;
            if (pageHeight < 200) pageHeight = 600;

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();

            // 이미지 블록은 단독 페이지로, 텍스트 블록은 Win2D 높이 측정 기반으로 분할
            int i = 0;
            int totalBlocks = allBlocks.Count;
            int maxSourceLine = allBlocks[allBlocks.Count - 1].SourceLineNumber;

            while (i < totalBlocks)
            {
                if (i % 100 == 0) await Task.Delay(1);

                var block = allBlocks[i];

                // 이미지 블록
                if (block.HasImage)
                {
                    var imgSrc = block.Inlines.OfType<AozoraImage>().FirstOrDefault()?.Source ?? "";
                    pages.Add(new EpubWin2DPage
                    {
                        Blocks = new List<AozoraBindingModel> { block },
                        IsImagePage = true,
                        ImagePath = imgSrc,
                        StartLine = block.SourceLineNumber,
                        LineCount = 1
                    });
                    i++;
                    continue;
                }

                // 페이지 분리 기호 건너뜀
                if (block.IsPageBreak)
                {
                    i++;
                    continue;
                }

                // 텍스트 블록 페이지 분할 (AozoraHorizontal 방식과 동일)
                int pageStart = i;
                var pageBlocks = new List<AozoraBindingModel>();

                int ref_i = i;
                pageBlocks = PaginateHorizontalAozoraPage(ref ref_i, allBlocks, maxWidth, pageHeight, device);
                i = ref_i;

                if (pageBlocks.Count == 0)
                {
                    i++;
                    continue;
                }

                pages.Add(new EpubWin2DPage
                {
                    Blocks = pageBlocks,
                    IsImagePage = false,
                    StartLine = pageBlocks[0].SourceLineNumber,
                    LineCount = pageBlocks.Count
                });
            }

            // TotalLinesInChapter 역산
            int total = Math.Max(1, maxSourceLine);
            foreach (var p in pages) p.TotalLinesInChapter = total;

            return pages;
        }

        private void EpubArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode || _isVerticalMode) return;
            TriggerEpubResize();
        }

        private void EpubTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { }

        private void EpubTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode || _isVerticalMode) return;
            if (_epubWin2DPages.Count > 0)
                EpubTextCanvas?.Invalidate();
        }

        private void EpubTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isEpubMode) return;
            var ds = args.DrawingSession;
            var size = sender.Size;

            Color bgColor = GetVerticalBackgroundColor();
            Color textColor = GetVerticalTextColor();
            ds.Clear(bgColor);

            var pg = CurrentEpubWin2DPage;
            if (pg == null || pg.Blocks == null || pg.Blocks.Count == 0) return;

            // 이미지 페이지는 EpubImageHost에서 처리
            if (pg.IsImagePage) return;

            float limitedWidth = (float)(_textFontSize * 42);
            float marginLeft = 40f; 
            float contentWidth = Math.Min(limitedWidth, (float)size.Width - 80f);
            float marginTop = 30f;
            float currentY = marginTop;

            // Aozora 수평 드로우 로직 재사용 (세로모드 예외)
            DrawHorizontalEpubBlocks(ds, size, pg.Blocks, textColor, marginLeft, marginTop, contentWidth);
        }

        private void DrawHorizontalEpubBlocks(CanvasDrawingSession ds, Windows.Foundation.Size size,
            List<AozoraBindingModel> blocks, Color textColor, float marginLeft, float marginTop, float maxWidth)
        {
            float currentY = marginTop;

            bool isBoxing = false;
            float boxLeft = float.MaxValue, boxRight = float.MinValue;
            float boxTop = 0f, boxBottom = float.MaxValue;
            Color boxColor = Colors.Gray;
            float boxPad = 20f;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                float fontSize = (float)(_textFontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;

                // 테이블
                if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
                {
                    var row = block.TableRows[0];
                    int colCount = row.Count;
                    int r = block.TableRowIndex;
                    bool isHeader = (r == 0);
                    bool isFirstOnPage = (i == 0) || !blocks[i - 1].IsTable;

                    float tableIndent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                    float tableMaxWidth = maxWidth - tableIndent;
                    float tableDrawX = marginLeft + tableIndent;
                    float colWidth = tableMaxWidth / colCount;

                    using var tableFormat = new CanvasTextFormat
                    {
                        FontSize = fontSize,
                        FontFamily = block.FontFamily ?? _textFontFamily,
                        WordWrapping = CanvasWordWrapping.Wrap,
                        FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : GetFontWeightForFamily(block.FontFamily ?? _textFontFamily)
                    };

                    if (isHeader || isFirstOnPage)
                        ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, 1.5f);

                    float maxCellHeight = 0;
                    var cellLayouts = new List<CanvasTextLayout>();
                    foreach (var cellText in row)
                    {
                        var parsed = ParseTableInline(cellText);
                        var cellLayout = new CanvasTextLayout(ds, parsed.text, tableFormat, Math.Max(10, colWidth - 20), 0.0f);
                        cellLayout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont;
                        foreach (var br in parsed.boldRanges)
                            cellLayout.SetFontWeight(br.start, br.length, Microsoft.UI.Text.FontWeights.Bold);
                        cellLayouts.Add(cellLayout);
                        float h = (float)cellLayout.LayoutBounds.Height;
                        if (h > maxCellHeight) maxCellHeight = h;
                    }
                    float rowHeight = maxCellHeight + 20f;
                    if (isHeader)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128));
                    else if (r % 2 == 1)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, Microsoft.UI.ColorHelper.FromArgb(10, 128, 128, 128));
                    for (int c = 0; c < colCount; c++)
                    {
                        float cellX = tableDrawX + (c * colWidth);
                        ds.DrawTextLayout(cellLayouts[c], cellX + 10, currentY + 10, textColor);
                        cellLayouts[c].Dispose();
                        ds.DrawLine(cellX, currentY, cellX, currentY + rowHeight, Colors.Gray, 1f);
                    }
                    ds.DrawLine(tableDrawX + tableMaxWidth, currentY, tableDrawX + tableMaxWidth, currentY + rowHeight, Colors.Gray, 1f);
                    currentY += rowHeight;
                    ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, isHeader ? 2f : 1f);
                    if (r == block.TableRowCount - 1) currentY += 20f;
                    continue;
                }

                float lineSpacing = block.IsTable ? fontSize * 1.3f : fontSize * 2.1f;

                var sb2 = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges2 = new List<(int start, int length)>();
                var italicRanges2 = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int st = sb2.Length;
                    if (inline is string s) sb2.Append(s);
                    else if (inline is AozoraRuby ruby) { sb2.Append(ruby.BaseText); rubyRanges.Add((st, ruby.BaseText.Length, ruby.RubyText)); if (ruby.IsBold) boldRanges2.Add((st, ruby.BaseText.Length)); }
                    else if (inline is AozoraBold bold) { sb2.Append(bold.Text); boldRanges2.Add((st, bold.Text.Length)); }
                    else if (inline is AozoraItalic italic) { sb2.Append(italic.Text); italicRanges2.Add((st, italic.Text.Length)); }
                    else if (inline is AozoraCode code) sb2.Append(code.Text);
                    else if (inline is AozoraTCY tcy) { sb2.Append(tcy.Text); if (tcy.IsBold) boldRanges2.Add((st, tcy.Text.Length)); }
                    else if (inline is AozoraLineBreak) sb2.Append("\n");
                }

                string blockText = sb2.ToString();
                float indent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                float actualMaxWidth = maxWidth - indent;

                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? _textFontFamily,
                    FontWeight = GetFontWeightForFamily(block.FontFamily ?? _textFontFamily),
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    WordWrapping = block.IsTable ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap,
                    LineSpacing = lineSpacing,
                    VerticalAlignment = CanvasVerticalAlignment.Top
                };

                using var textLayout = new CanvasTextLayout(ds, blockText, format, actualMaxWidth, 0.0f);
                textLayout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont;
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var rr in boldRanges2) textLayout.SetFontWeight(rr.start, rr.length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var ir in italicRanges2) textLayout.SetFontStyle(ir.start, ir.length, Windows.UI.Text.FontStyle.Italic);

                int lineCount2 = textLayout.LineCount;
                float currentBlockHeight = block.IsBlankLine ? lineSpacing * 0.3f : lineCount2 * lineSpacing;

                var bounds = textLayout.LayoutBounds;
                float drawX = marginLeft + indent;
                if (block.Alignment == TextAlignment.Center) drawX = (float)((size.Width - bounds.Width) / 2);
                else if (block.Alignment == TextAlignment.Right) drawX = (float)(size.Width - bounds.Width - 40);

                bool isKeigakomi = block.BorderThickness.Top > 0 && block.BorderThickness.Bottom > 0 && block.BorderThickness.Left > 0 && block.BorderThickness.Right > 0;
                float currentW = (float)bounds.Width;
                if (block.IsBlankLine && currentW < fontSize) currentW = fontSize;

                if (isKeigakomi)
                {
                    if (!isBoxing) { currentY += boxPad; isBoxing = true; boxTop = currentY; boxBottom = currentY + currentBlockHeight; boxLeft = drawX + (float)bounds.X; boxRight = drawX + (float)bounds.X + currentW; boxColor = block.BorderColor ?? Colors.Gray; }
                    else { boxTop = Math.Min(boxTop, currentY + (float)bounds.Y); boxBottom = Math.Max(boxBottom, currentY + (float)bounds.Y + currentBlockHeight); boxLeft = Math.Min(boxLeft, drawX + (float)bounds.X); boxRight = Math.Max(boxRight, drawX + (float)bounds.X + currentW); }
                }
                else if (isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false; currentY += boxPad + lineSpacing;
                }

                if (block.BackgroundColor != null)
                {
                    var db = textLayout.DrawBounds;
                    float dbTop = (float)db.Top; float dbH = (float)db.Height;
                    if (dbH < fontSize) dbH = fontSize;
                    ds.FillRectangle(drawX - 4, currentY + dbTop - 4f, currentW + 8, dbH + 8f, block.BackgroundColor.Value);
                }

                ds.DrawTextLayout(textLayout, drawX, currentY, textColor);

                if (!isKeigakomi && block.BorderColor != null)
                {
                    var db2 = textLayout.DrawBounds;
                    float actualTextBottom = (float)Math.Max(db2.Bottom, fontSize);
                    float borderBottomY = currentY + actualTextBottom - 20f;
                    if (block.BorderThickness.Bottom > 0) ds.DrawLine(drawX, borderBottomY, drawX + currentW, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Bottom);
                    if (block.BorderThickness.Left > 0) { float quoteLeft = drawX - 15; float actualTextTop = (float)Math.Min(db2.Top, 0); ds.DrawLine(quoteLeft, currentY + actualTextTop, quoteLeft, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Left); }
                }

                // 루비 그리기
                using var rubyFormat2 = new CanvasTextFormat { FontSize = rubyFontSize, FontFamily = _textFontFamily, FontWeight = GetFontWeightForFamily(_textFontFamily), Direction = CanvasTextDirection.LeftToRightThenTopToBottom, VerticalAlignment = CanvasVerticalAlignment.Top, WordWrapping = CanvasWordWrapping.NoWrap };
                var rubyRenderInfos2 = new List<HorizontalRubyRenderInfo>();
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;
                        float lineBoxTop = currentY + (float)charBounds.Top;
                        float rubyY = lineBoxTop - (rubyFontSize * 3f);
                        float charCenter = drawX + (float)charBounds.Left + (float)charBounds.Width / 2.0f;
                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat2, 0.0f, 0.0f);
                        rubyLayout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont;
                        float rubyWidth = (float)rubyLayout.LayoutBounds.Width;
                        float idealLeft = charCenter - (rubyWidth / 2.0f);
                        rubyRenderInfos2.Add(new HorizontalRubyRenderInfo { Layout = rubyLayout, IdealX = idealLeft, Width = rubyWidth, X = idealLeft, Y = rubyY });
                    }
                }
                ResolveHorizontalRubyOverlaps(rubyRenderInfos2);
                foreach (var info in rubyRenderInfos2) { ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor); info.Layout.Dispose(); }

                currentY += currentBlockHeight;

                if (i == blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }

        private void EpubTouchOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEpubMode) return;
            DispatcherQueue.TryEnqueue(() => EpubTextCanvas?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
            var pt = e.GetCurrentPoint(EpubTouchOverlay);
            double half = EpubTouchOverlay.ActualWidth / 2;
            if (pt.Position.X < half) 
            {
                if (_isVerticalMode) NavigateVerticalPage(-1); 
                else _ = NavigateEpubAsync(-1);
            }
            else 
            {
                if (_isVerticalMode) NavigateVerticalPage(1);
                else _ = NavigateEpubAsync(1);
            }
        }

        private void EpubTouchOverlay_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEpubMode) return;
            DispatcherQueue.TryEnqueue(() => EpubTextCanvas?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
            var delta = e.GetCurrentPoint(EpubTouchOverlay).Properties.MouseWheelDelta;
            if (delta > 0) 
            {
                if (_isVerticalMode) NavigateVerticalPage(-1);
                else _ = NavigateEpubAsync(-1);
            }
            else 
            {
                if (_isVerticalMode) NavigateVerticalPage(1);
                else _ = NavigateEpubAsync(1);
            }
        }

        private void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;
        }

        private async Task LoadEpubImageForWin2DAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return;
            if (_epubImageCache.ContainsKey(imagePath)) return;

            _epubImageCache[imagePath] = null;
            try
            {
                var entry = FindEntryLoose(imagePath);
                if (entry == null) return;

                byte[] bytes;
                await _epubArchiveLock.WaitAsync();
                try
                {
                    using var ms = new MemoryStream();
                    using var es = entry.Open();
                    await es.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                finally { _epubArchiveLock.Release(); }

                if (EpubTextCanvas == null) return;
                var winrtStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(winrtStream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                winrtStream.Seek(0);
                var bitmap = await CanvasBitmap.LoadAsync(EpubTextCanvas.Device, winrtStream);
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    _epubImageCache[imagePath] = bitmap;
                    if (_isEpubMode && CurrentEpubWin2DPage?.IsImagePage == true)
                        ShowEpubImagePage(CurrentEpubWin2DPage);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadEpubImageForWin2DAsync failed: {ex.Message}");
            }
        }

        private void ShowEpubImagePage(EpubWin2DPage page)
        {
            if (page == null || !page.IsImagePage) return;
            EpubTextCanvas.Visibility = Visibility.Collapsed;
            EpubImageHost.Visibility = Visibility.Visible;

            if (!_isSideBySideMode || !_isEpubShowingTwoPages)
            {
                // Single image mode
                EpubImageDisplay.Visibility = Visibility.Visible;
                EpubImageDisplayLeft.Visibility = Visibility.Collapsed;
                EpubImageDisplayRight.Visibility = Visibility.Collapsed;
                EpubImageLeftColumn.Width = new GridLength(1, GridUnitType.Star);
                EpubImageRightColumn.Width = new GridLength(0);

                _ = LoadBitmapToImageDisplayAsync(page.ImagePath, EpubImageDisplay);
            }
            else
            {
                // Side-by-side mode
                EpubImageDisplay.Visibility = Visibility.Collapsed;
                EpubImageDisplayLeft.Visibility = Visibility.Visible;
                EpubImageDisplayRight.Visibility = Visibility.Visible;
                EpubImageLeftColumn.Width = new GridLength(1, GridUnitType.Star);
                EpubImageRightColumn.Width = new GridLength(1, GridUnitType.Star);

                int nextChapIndex = _currentEpubChapterIndex;
                int nextPgIndex = _currentEpubPageIndex + 1;
                if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
                var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);

                bool actualNextImageOnRight = _nextImageOnRight;
                // Note: EPUB often follows LTR, but we can respect user setting

                Image targetLeft = actualNextImageOnRight ? EpubImageDisplayLeft : EpubImageDisplayRight;
                Image targetRight = actualNextImageOnRight ? EpubImageDisplayRight : EpubImageDisplayLeft;

                _ = LoadBitmapToImageDisplayAsync(page.ImagePath, targetLeft);
                if (pg2 != null && pg2.IsImagePage)
                {
                    _ = LoadBitmapToImageDisplayAsync(pg2.ImagePath, targetRight);
                }
            }
        }

        private async Task LoadBitmapToImageDisplayAsync(string imagePath, Image targetImage)
        {
            try
            {
                var entry = FindEntryLoose(imagePath);
                if (entry == null) return;
                byte[] bytes;
                await _epubArchiveLock.WaitAsync();
                try
                {
                    using var ms = new MemoryStream();
                    using var es = entry.Open();
                    await es.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                finally { _epubArchiveLock.Release(); }

                var ras = new InMemoryRandomAccessStream();
                using (var dw = new DataWriter(ras))
                {
                    dw.WriteBytes(bytes); await dw.StoreAsync(); await dw.FlushAsync(); dw.DetachStream();
                }
                ras.Seek(0);
                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(ras);
                targetImage.Source = bitmapImage;
            }
            catch { }
        }

        private ZipArchiveEntry? FindEntryLoose(string path)
        {
            var entry = _currentEpubArchive?.GetEntry(path);
            if (entry != null) return entry;
            string name = Path.GetFileName(path);
            return _currentEpubArchive?.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private object? EpubSelectedItem 
        {
            get
            {
                if (_currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count)
                {
                    var p = _epubWin2DPages[_currentEpubPageIndex];
                    var grid = new Grid();
                    if (p.IsImagePage) grid.Tag = new EpubImageTag { FullPath = p.ImagePath };
                    else grid.Tag = new EpubPageInfoTag { StartLine = p.StartLine, LineCount = p.LineCount, TotalLinesInChapter = p.TotalLinesInChapter };
                    return grid;
                }
                return null;
            }
        }

        private List<UIElement> _epubPages
        {
            get
            {
                var list = new List<UIElement>();
                foreach (var p in _epubWin2DPages)
                {
                    var grid = new Grid();
                    if (p.IsImagePage) grid.Tag = new EpubImageTag { FullPath = p.ImagePath };
                    else grid.Tag = new EpubPageInfoTag { StartLine = p.StartLine, LineCount = p.LineCount, TotalLinesInChapter = p.TotalLinesInChapter };
                    list.Add(grid);
                }
                return list;
            }
        }



        private async Task ShowEpubGoToLineDialog()
        {
             var pg = CurrentEpubWin2DPage;
             int totalLines = pg?.TotalLinesInChapter ?? _epubWin2DPages.Count;
             int currentLine = pg?.StartLine ?? (_currentEpubPageIndex + 1);

             var dialog = new ContentDialog
             {
                 Title = Strings.DialogTitle,
                 PrimaryButtonText = Strings.DialogPrimary,
                 CloseButtonText = Strings.DialogClose,
                 DefaultButton = ContentDialogButton.Primary,
                 XamlRoot = RootGrid.XamlRoot,
                 RequestedTheme = RootGrid.ActualTheme
             };

             var input = new TextBox 
             { 
                 PlaceholderText = $"1 - {totalLines}",
                 Text = currentLine.ToString()
             };
             
             input.SelectAll();
             dialog.Content = input;

             void PerformGoToLine()
             {
                 if (int.TryParse(input.Text, out int targetLine))
                 {
                     if (_isVerticalMode)
                     {
                         _ = PrepareVerticalTextAsync(targetLine);
                         return;
                     }

                     // Find page that contains this line
                    for (int i = 0; i < _epubWin2DPages.Count; i++)
                    {
                        var p = _epubWin2DPages[i];
                        if (p.Blocks != null && p.Blocks.Count > 0)
                        {
                            int start = p.Blocks.First().SourceLineNumber;
                            int end = p.Blocks.Last().SourceLineNumber;
                            
                            if (targetLine >= start && targetLine <= end)
                            {
                                SetEpubPageIndex(i);
                                return;
                            }
                            if (i == _epubWin2DPages.Count - 1 && targetLine >= start)
                            {
                                SetEpubPageIndex(i);
                                return;
                            }
                        }
                    }
                     
                     // Fallback
                     int pageIndex = targetLine - 1;
                     if (pageIndex >= 0 && pageIndex < _epubWin2DPages.Count)
                         SetEpubPageIndex(pageIndex);
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

        // Navigation Handlers
        
        public async Task NavigateEpubAsync(int direction)
        {
            if (!_isEpubMode) return;

            int step = direction;
            if (_isSideBySideMode) step = direction * 2;

            int targetChapter = _currentEpubChapterIndex;
            int targetPage = _currentEpubPageIndex + step;

            while (true)
            {
                int currentLimit = (targetChapter == _currentEpubChapterIndex)
                    ? _epubWin2DPages.Count
                    : (_epubPreloadCache.TryGetValue(targetChapter, out var cached) ? cached.Count : 0);

                if (targetPage >= currentLimit && targetChapter < _epubSpine.Count - 1)
                {
                    targetPage -= currentLimit;
                    targetChapter++;
                    await ForceLoadChapterPagesAsync(targetChapter);
                    continue;
                }

                if (targetPage < 0 && targetChapter > 0)
                {
                    targetChapter--;
                    await ForceLoadChapterPagesAsync(targetChapter);
                    int prevLimit = (targetChapter == _currentEpubChapterIndex)
                        ? _epubWin2DPages.Count
                        : (_epubPreloadCache.TryGetValue(targetChapter, out var cachedPrev) ? cachedPrev.Count : 0);
                    targetPage += prevLimit;
                    continue;
                }
                break;
            }

            if (targetChapter != _currentEpubChapterIndex)
            {
                _currentEpubChapterIndex = targetChapter;
                await LoadEpubChapterAsync(targetChapter, targetPage: targetPage);
            }
            else
            {
                int finalIndex = Math.Clamp(targetPage, 0, _epubWin2DPages.Count - 1);
                SetEpubPageIndex(finalIndex);
            }
        }

        private async Task ForceLoadChapterPagesAsync(int chapterIndex)
        {
            if (chapterIndex == _currentEpubChapterIndex) return;
            
            if (_epubPreloadCache.TryGetValue(chapterIndex, out var cached))
            {
                // Temporarily swap pages to check count if needed, 
                // but actually we just need the count.
                // If it's cached, we are good.
            }
            else
            {
                // Not cached, must load now
                string path = _epubSpine[chapterIndex];
                var entry = _currentEpubArchive?.GetEntry(path);
                if (entry != null)
                {
                    string html;
                    await _epubArchiveLock.WaitAsync();
                    try {
                        using var s = entry.Open();
                        using var r = new StreamReader(s);
                        html = await r.ReadToEndAsync();
                    } finally { _epubArchiveLock.Release(); }
                    
                    var pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[chapterIndex] = pages;
                }
            }
            
            // Note: We don't update _epubPages yet, 
            // the loop uses _epubPreloadCache or _epubPages based on targetChapter.
            // Wait, let's fix the loop to use the correct source.
        }

        private EpubWin2DPage? GetEpubWin2DPage(int chapterIndex, int pageIndex)
        {
            if (chapterIndex == _currentEpubChapterIndex)
            {
                if (pageIndex >= 0 && pageIndex < _epubWin2DPages.Count) return _epubWin2DPages[pageIndex];
            }
            else if (_epubPreloadCache.TryGetValue(chapterIndex, out var cachedPages))
            {
                if (pageIndex >= 0 && pageIndex < cachedPages.Count) return cachedPages[pageIndex];
            }
            return null;
        }

        private async Task LoadEpubChapterAsync(int index, bool fromEnd = false, int targetLine = -1, int targetPage = -1, double? progress = null, CancellationToken token = default)
        {
            if (index < 0 || index >= _epubSpine.Count) return;

            try
            {
                if (token.IsCancellationRequested) return;
                FileNameText.Text = (Path.GetFileName(_currentEpubFilePath) ?? "") + Strings.Loading;
                await Task.Delay(1, token);
                if (token.IsCancellationRequested) return;

                List<EpubWin2DPage> pages;
                if (_epubPreloadCache.TryGetValue(index, out var cachedPages))
                {
                    pages = cachedPages;
                }
                else
                {
                    string path = _epubSpine[index];
                    var entry = _currentEpubArchive?.GetEntry(path);
                    if (entry == null) return;

                    string html;
                    await _epubArchiveLock.WaitAsync();
                    try
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        html = await reader.ReadToEndAsync();
                    }
                    finally { _epubArchiveLock.Release(); }

                    pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[index] = pages;
                    _epubChapterHasText[index] = pages.Any(p => !p.IsImagePage);
                }

                _epubWin2DPages = pages;
                _currentEpubPageIndex = -1;

                if (_isVerticalMode)
                {
                    _currentEpubChapterIndex = index;
                    var blocks = await GetEpubChapterAsAozoraBlocksAsync(index);
                    if (_isSideBySideMode && blocks.Count > 0 && blocks.Any(b => b.HasImage))
                    {
                        // 이미지 챕터인 경우(혹은 이미지가 포함된 경우) 다음 챕터도 이미지면 가져옴
                        int nextIdx = index + 1;
                        if (nextIdx < _epubSpine.Count)
                        {
                            var nextBlocks = await GetEpubChapterAsAozoraBlocksAsync(nextIdx);
                            if (nextBlocks.Count > 0 && nextBlocks.Any(b => b.HasImage))
                                blocks.AddRange(nextBlocks);
                        }
                    }
                    _aozoraBlocks = blocks;
                    await PrepareVerticalTextAsync(fromEnd ? 999999 : (targetLine > 0 ? targetLine : 1), token);
                    return;
                }

                // [추가] 가로 모드에서도 SideBySide인 경우 다음 챕터가 연달아 이미지면 미리 렌더링해서 캐시에 넣음
                // 이렇게 해야 SetEpubPageIndex에서 다음 페이지(이미지)를 즉시 찾아 2페이지 모드를 유지할 수 있음
                if (_isSideBySideMode && pages.Count > 0 && pages.Any(p => p.IsImagePage))
                {
                    int nextIdx = index + 1;
                    if (nextIdx < _epubSpine.Count && !_epubPreloadCache.ContainsKey(nextIdx))
                    {
                        var entry = _currentEpubArchive?.GetEntry(_epubSpine[nextIdx]);
                        if (entry != null)
                        {
                            string nextHtml;
                            await _epubArchiveLock.WaitAsync();
                            try {
                                using var s = entry.Open();
                                using var r = new StreamReader(s);
                                nextHtml = await r.ReadToEndAsync();
                            } finally { _epubArchiveLock.Release(); }
                            
                            var nextPages = await RenderEpubPagesAsync(nextHtml, _epubSpine[nextIdx]);
                            _epubPreloadCache[nextIdx] = nextPages;
                        }
                    }
                }

                // 가로 모드: Win2D 캔버스 활성화
                EpubTextCanvas.Visibility = Visibility.Visible;
                EpubImageHost.Visibility = Visibility.Collapsed;

                int finalTargetPage = 0;

                if (targetLine > 1)
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        var p = pages[i];
                        if (!p.IsImagePage && p.Blocks != null && p.Blocks.Count > 0)
                        {
                            int pageStartLine = p.Blocks.First().SourceLineNumber;
                            int pageEndLine = p.Blocks.Last().SourceLineNumber;

                            // 목표 라인이 이 페이지의 시작과 끝 사이에 있다면 이 페이지가 정답
                            if (targetLine >= pageStartLine && targetLine <= pageEndLine)
                            { 
                                finalTargetPage = i; 
                                break; 
                            }
                            // 마지막 페이지 처리 방어 로직
                            if (i == pages.Count - 1 && targetLine >= pageStartLine)
                            { 
                                finalTargetPage = i; 
                                break; 
                            }
                        }
                        else if (p.IsImagePage && p.Blocks != null && p.Blocks.Count > 0)
                        {
                            if (targetLine == p.Blocks.First().SourceLineNumber)
                            {
                                finalTargetPage = i;
                                break;
                            }
                        }
                    }
                }
                else if (targetPage > 0)
                {
                    finalTargetPage = Math.Min(targetPage, pages.Count - 1);
                }
                else if (progress.HasValue)
                {
                    finalTargetPage = (int)(Math.Max(0, pages.Count - 1) * progress.Value);
                }
                else if (fromEnd && pages.Count > 0)
                {
                    finalTargetPage = pages.Count - 1;
                }

                SetEpubPageIndex(finalTargetPage);
                _ = PreloadEpubChaptersAsync(index);
            }
            finally
            {
                FileNameText.Text = Path.GetFileName(_currentEpubFilePath) ?? "";
            }
        }

        private void SetEpubPageIndex(int index)
        {
            if (index < 0 || index >= _epubWin2DPages.Count) return;

            _currentEpubPageIndex = index;
            var page = _epubWin2DPages[index];

            // 이미지 페이지 처리
            if (page.IsImagePage)
            {
                // [수정] 무조건 false로 리셋하지 않고, SBS 모드가 아니거나 다음 장이 이미지가 아닐 때만 결과적으로 false가 되도록 함
                bool nextIsImage = false;
                if (_isSideBySideMode)
                {
                    int nextChapIndex = _currentEpubChapterIndex;
                    int nextPgIndex = index + 1;
                    if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
                    var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);
                    if (pg2 != null && pg2.IsImagePage) nextIsImage = true;
                }

                _isEpubShowingTwoPages = nextIsImage;
                EpubTextCanvas.Visibility = Visibility.Collapsed;
                // 연속 이미지 SBS 처리
                if (_isSideBySideMode)
                {
                    // 위에서 이미 검사했으므로 _isEpubShowingTwoPages 값을 그대로 따름
                }
                ShowEpubImagePage(page);
            }
            else
            {
                // 텍스트 페이지: Win2D 캐단스
                EpubImageHost.Visibility = Visibility.Collapsed;
                EpubTextCanvas.Visibility = Visibility.Visible;
                EpubTextCanvas.Invalidate();

                _isEpubShowingTwoPages = false;
            }

            UpdateEpubStatus();
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
            if (EpubArea != null) EpubArea.Background = bg;
            if (_isEpubMode && !_isVerticalMode)
            {
                EpubTextCanvas?.Invalidate();
            }
        }

        private Brush GetEpubThemeForeground()
        {
            if (_themeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            if (_themeIndex == 3 && _customForegroundColor.HasValue) return new SolidColorBrush(_customForegroundColor.Value);
            return new SolidColorBrush(Colors.Black);
        }
        
        private Brush GetEpubThemeBackground()
        {
             if (_themeIndex == 0) return new SolidColorBrush(Colors.White);
             if (_themeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
             if (_themeIndex == 3 && _customBackgroundColor.HasValue) return new SolidColorBrush(_customBackgroundColor.Value);
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
                     string content;
                     await _epubArchiveLock.WaitAsync();
                     try
                     {
                         using var stream = entry.Open();
                         using var reader = new StreamReader(stream);
                         content = await reader.ReadToEndAsync();
                     }
                     finally
                     {
                         _epubArchiveLock.Release();
                     }
                         
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


        public void ClearEpubCache()
        {
            _epubPreloadCache.Clear();
        }

        private async Task<List<AozoraBindingModel>> GetEpubChapterAsAozoraBlocksAsync(int index)
        {
            if (index < 0 || index >= _epubSpine.Count) return new List<AozoraBindingModel>();

            string path = _epubSpine[index];
            var entry = _currentEpubArchive?.GetEntry(path);
            if (entry == null) return new List<AozoraBindingModel>();

            string html;
            await _epubArchiveLock.WaitAsync();
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                html = await reader.ReadToEndAsync();
            }
            finally
            {
                _epubArchiveLock.Release();
            }

            return ParseEpubHtmlToAozoraBlocks(html, path, index);
        }

        private List<AozoraBindingModel> ParseEpubHtmlToAozoraBlocks(string html, string currentPath, int chapterIndex)
        {
            var blocks = new List<AozoraBindingModel>();
            
            // ...
            
            
            // Cleanup
            html = RxEpubScript.Replace(html, "");
            html = RxEpubStyle.Replace(html, "");
            
            // Pre-process special tags
            html = RxEpubBr.Replace(html, "\n");

            // Split by Image tags
            var segments = RxEpubImgTag.Split(html);
            int lineNum = 1;
            bool hasImages = html.Contains("<img", StringComparison.OrdinalIgnoreCase) || html.Contains("<image", StringComparison.OrdinalIgnoreCase);
            bool isFirstContent = true;

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;

                if (RxEpubIsImg.IsMatch(segment))
                {
                    var match = RxEpubSrc.Match(segment);
                    if (match.Success)
                    {
                        string src = match.Groups[1].Value;
                        string fullPath = ResolveRelativePath(currentPath, src);

                        var block = new AozoraBindingModel { SourceLineNumber = lineNum++, EpubChapterIndex = chapterIndex };
                        block.Inlines.Add(new AozoraImage { Source = fullPath });
                        blocks.Add(block);
                        isFirstContent = false;
                    }
                }
                else
                {
                    // Text segment
                    var textBlocks = ParseHtmlToAozoraTextBlocks(segment, ref lineNum, chapterIndex);

                    // If this is the first content and chapter has images, skip short title-like text segments
                    if (isFirstContent && hasImages)
                    {
                        string plainText = RxEpubAnyTag.Replace(segment, "");
                        plainText = System.Net.WebUtility.HtmlDecode(plainText).Trim();
                        // If total text in this segment is short (< 150 chars), skip it as a likely title
                        if (plainText.Length > 0 && plainText.Length < 150)
                        {
                            isFirstContent = false;
                            continue;
                        }
                    }

                    if (textBlocks.Count > 0)
                    {
                        blocks.AddRange(textBlocks);
                        isFirstContent = false;
                    }
                }
            }

            _textTotalLineCountInSource = lineNum - 1;

            // ===== [추가] 문단이 긴 경우 문장 단위로 블록 분리 (Aozora와 일치) =====
            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(SplitBlockBySentences(block));
            }
            return splitBlocks;
        }

        private List<AozoraBindingModel> ParseHtmlToAozoraTextBlocks(string html, ref int lineNum, int chapterIndex)
        {
            var blocks = new List<AozoraBindingModel>();

            // --- Ruby Processing ---
            html = RxEpubRuby.Replace(html, m => 
            {
                string rubyContent = m.Groups[1].Value;
                rubyContent = RxEpubRp.Replace(rubyContent, "");
                
                StringBuilder sb = new StringBuilder();
                var rtMatches = RxEpubRt.Matches(rubyContent);
                
                int lastIndex = 0;
                foreach (Match rtMatch in rtMatches)
                {
                    string basePart = rubyContent.Substring(lastIndex, rtMatch.Index - lastIndex);
                    string rtPart = rtMatch.Groups[1].Value;
                    
                    string baseText = RxEpubAnyTag.Replace(basePart, "").Trim();
                    string rtText = RxEpubAnyTag.Replace(rtPart, "").Trim();
                    
                    if (!string.IsNullOrEmpty(baseText) || !string.IsNullOrEmpty(rtText))
                    {
                        sb.Append($"{{{{RUBY|{baseText}|~|{rtText}}}}}");
                    }
                    lastIndex = rtMatch.Index + rtMatch.Length;
                }
                
                if (lastIndex < rubyContent.Length)
                {
                    string tailText = RxEpubAnyTag.Replace(rubyContent.Substring(lastIndex), "").Trim();
                    if (!string.IsNullOrEmpty(tailText)) sb.Append(tailText);
                }
                
                return sb.ToString();
            });
            
            // Strip remaining tags
            html = RxEpubAnyTag.Replace(html, ""); 
            html = System.Net.WebUtility.HtmlDecode(html);
            
            html = html.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = html.Split('\n');
            
            foreach (var line in lines)
            {
                var trimmed = line.Replace('\u3000', ' ').Replace('\u00A0', ' ').Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                
                var block = new AozoraBindingModel { SourceLineNumber = lineNum++, EpubChapterIndex = chapterIndex };
                
                var tokens = RxEpubRubySplit.Split(line);
                foreach (var token in tokens)
                {
                    if (token.StartsWith("{{RUBY|"))
                    {
                        var content = token.Substring(7, token.Length - 9);
                        var parts = content.Split(new[] { "|~|" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            block.Inlines.Add(new AozoraRuby { BaseText = parts[0], RubyText = parts[1] });
                        }
                    }
                    else if (!string.IsNullOrEmpty(token))
                    {
                        block.Inlines.Add(token);
                    }
                }
                blocks.Add(block);
            }
            
            return blocks;
        }
    }
    public class EpubPageInfoTag
    {
        public int StartLine { get; set; }
        public int LineCount { get; set; }
        public int TotalLinesInChapter { get; set; }
    }

    public class EpubImageTag
    {
        public string FullPath { get; set; } = string.Empty;
    }
}
