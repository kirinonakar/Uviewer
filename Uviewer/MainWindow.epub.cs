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
using Uviewer.Models;
using Uviewer.Services;
using Uviewer.Renderers;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private ZipArchive? _currentEpubArchive;
        private List<string> _epubSpine = new();
        private int _currentEpubChapterIndex = 0;
        private string? _currentEpubFilePath;
        private string? _currentEpubDisplayName;
        private string? _epubTocPath;
        private object _epubLock = new object();
        private SemaphoreSlim _epubArchiveLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _epubNavigationLock = new SemaphoreSlim(1, 1);

        private bool _isEpubMode = false;
        public int PendingEpubChapterIndex { get; set; } = -1;
        public int PendingEpubPageIndex { get; set; } = -1;
        private int _pendingEpubStartBlockIndex = -1;

        // Win2D 기반 EPUB 페이지 정보
        public class EpubWin2DPage
        {
            public List<AozoraBindingModel> Blocks { get; set; } = new();
            public int StartBlockIndex { get; set; }
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
        private static readonly Regex RxEpubItemRef = new Regex("<itemref[^>]*idref=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubSpineToc = new Regex("<spine[^>]*toc=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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


        public int CurrentEpubChapterIndex => _currentEpubChapterIndex;
        public int CurrentEpubPageIndex => _currentEpubPageIndex;

        private DispatcherQueueTimer? _epubResizeTimer;

        public void TriggerEpubResize()
        {
            if (!_isEpubMode) return;

            if (_epubResizeTimer == null)
            {
                _epubResizeTimer = this.DispatcherQueue.CreateTimer();
                _epubResizeTimer.Interval = TimeSpan.FromMilliseconds(300);
                _epubResizeTimer.IsRepeating = false;
                _epubResizeTimer.Tick += (s, e) =>
                {
                     if (_isEpubMode)
                     {
                         // [버그 수정] 로딩 중에 SizeChanged가 발생하면 _epubWin2DPages가 없어서 리사이즈가 취소되는 문제 해결.
                         // 로딩 중이라면 타이머를 연장하여 로딩이 끝난 뒤에 반영되게 유도합니다.
                         if (CurrentEpubWin2DPage == null || _epubWin2DPages == null || _epubWin2DPages.Count == 0) 
                         {
                             _epubResizeTimer?.Start();
                             return;
                         }

                         _epubPreloadCache.Clear();
                         _epubImageCache.Clear();
                         // [핵심 해결] 글자 크기나 창 크기가 바뀌면 공용 측정 캐시(MainWindow.aozora.cs 정의)를 비워야 정확한 재계산이 가능합니다.
                         ClearBackwardCache(); 
                         
                         int currentLine = CurrentEpubWin2DPage?.StartLine ?? 1;
                         int currentBlockIdx = CurrentEpubWin2DPage?.StartBlockIndex ?? -1;
                         _ = LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, targetBlockIndex: currentBlockIdx);
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
                CloseCurrentEpub();
                if (entry.FilePath != null)
                {
                    var file = await StorageFile.GetFileFromPathAsync(entry.FilePath);
                    await LoadEpubFileAsync(file, entry, token);
                }
                else if (entry.IsWebDavEntry && _isWebDavMode)
                {
                    FileNameText.Text = entry.DisplayName + Strings.Loading;
                    var tempPath = await _webDavService.DownloadToTempFileAsync(entry.WebDavPath!, token);
                    if (!string.IsNullOrEmpty(tempPath) && !token.IsCancellationRequested)
                    {
                        entry.FilePath = tempPath;
                        var file = await StorageFile.GetFileFromPathAsync(tempPath);
                        await LoadEpubFileAsync(file, entry, token);
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

        private async Task LoadEpubFileAsync(StorageFile file, ImageEntry? entry = null, CancellationToken token = default)
        {
             await AddToRecentAsync(true);

             _isNavigatingRecent = true; // 로드 및 위치 복원 완료 전까지 자동 저장 차단
             try
             {
                 InitializeEpub();
                 _animatedWebpService.Stop();

                 // Ensure navigation token is fresh for vertical mode
                 CancelAndResetGlobalTextCts();
                 
                 // Close other formats first
                 CloseCurrentArchive();
                 await CloseCurrentPdfAsync();
                 CloseCurrentEpub();

                 _currentEpubFilePath = file.Path;
                 _currentEpubDisplayName = entry?.DisplayName ?? file.Name;
                 
                 _epubPreloadCache.Clear();
                 var stream = await file.OpenStreamForReadAsync();
                 _currentEpubArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                
                 // 1. Parse Container
                 var rootPath = await ParseEpubContainerAsync();
                 if (string.IsNullOrEmpty(rootPath)) throw new Exception("Invalid container.xml");
                 
                 // 2. Parse OPF
                 await ParseEpubOpfAsync(rootPath);
                 
                 if (_epubSpine.Count == 0) throw new Exception("No content found in EPUB");
                 
                 LoadEpubSettings();
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

                 // EPUB 모드에서는 가로/세로 관계 없이 항상 EpubArea를 사용하며 TextArea는 닫음
                 if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
                 if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                 if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
                 if (TextArea != null) TextArea.Visibility = Visibility.Collapsed;
                 
                 if (EpubArea != null) 
                 {
                     EpubArea.Visibility = Visibility.Visible;
                     // [버그 수정] Visibility 변경 즉시 레이아웃을 갱신하여 
                     // 옛날에 닫혀있을 때의 작았던 ActualWidth 값을 쓰지 않도록 강제합니다.
                     EpubArea.UpdateLayout();
                 }
                 RootGrid?.UpdateLayout(); // 전체 레이아웃도 동기화

                 if (_isVerticalMode)
                 {
                     if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = true;
                     if (!_verticalKeyAttached && RootGrid != null)
                     {
                         RootGrid.PreviewKeyDown += RootGrid_Vertical_PreviewKeyDown;
                         _verticalKeyAttached = true;
                     }
                 }

                 // 3. Load Chapter (Updated to handle pending positions)
                 int targetCh = (PendingEpubChapterIndex >= 0) ? PendingEpubChapterIndex : 0;
                 _currentEpubChapterIndex = targetCh;
                 await LoadEpubChapterAsync(targetCh, targetLine: _aozoraPendingTargetLine, targetBlockIndex: _pendingEpubStartBlockIndex, targetPage: PendingEpubPageIndex, token: token);

                 // LoadEpubChapterAsync 내부에서 targetBlockIndex 기반으로 이미 최적의 페이지를 설정하므로,
                 // 화면 크기에 종속적인 PendingEpubPageIndex를 여기서 다시 강제로 설정하지 않습니다.
                 
                 // Reset pending values
                 PendingEpubChapterIndex = -1;
                 PendingEpubPageIndex = -1;
                 _aozoraPendingTargetLine = 0;
                 _pendingEpubStartBlockIndex = -1;
                 _epubChapterHasText.Clear();
                 
                 // 4. Load TOC (Background)
                _ = Task.Run(async () => {
                    if (_currentEpubArchive != null && !string.IsNullOrEmpty(_epubTocPath))
                    {
                        _tocService.SetProvider(new EpubTocProvider(_currentEpubArchive, _epubTocPath, _epubSpine));
                        await _tocService.LoadTocAsync();
                    }
                });

                 FileNameText.Text = FileExplorerService.GetFormattedDisplayName(entry?.DisplayName ?? file.Name, false);
                 SyncSidebarSelection(entry ?? new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
             }
             catch (Exception ex)
             {
                 FileNameText.Text = Strings.EpubParseError(ex.Message);
             }
             finally
             {
                 _isNavigatingRecent = false;
             }
        }


        private void CloseCurrentEpub()
        {
            if (_epubArchiveLock.Wait(TimeSpan.FromSeconds(2)))
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
            else
            {
                // 타임아웃 발생 시 강제 정리 시도
                _currentEpubArchive = null;
                _currentEpubFilePath = null;
                _epubWin2DPages.Clear();
                _aozoraBlocks.Clear();
                ClearVerticalDisplayState();
            }
        }

        private void SwitchToEpubMode()
        {
            _isEpubMode = true;
            _isTextMode = false;
            _isAozoraMode = false;
            _isMarkdownRenderMode = false; // EPUB 모드에서는 마크다운 하이드 로직 해제
            _aozoraBlocks.Clear(); // Clear text/aozora cache
            _currentTextContent = ""; // Clear raw text

            // EPUB 모드 진입 시 창 크기 검사 및 조정
            EnsureMinWindowSizeForText();
            
            ImageArea.Visibility = Visibility.Collapsed;
            
            // EPUB 가로/세로 모두 통합된 컨테이너(EpubArea) 사용
            EpubArea.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;

            // [버그 수정] 모드 스위칭 시에도 레이아웃을 즉시 갱신하여 
            // EpubArea.ActualWidth/ActualHeight를 올바른 화면 크기로 재설정합니다.
            EpubArea.UpdateLayout();
            RootGrid?.UpdateLayout();
            
            ImageToolbarPanel.Visibility = Visibility.Collapsed;
            TextToolbarPanel.Visibility = Visibility.Visible; // Reuse text toolbar for now
            if (VerticalToggleButton != null) VerticalToggleButton.IsEnabled = true; // 버튼 활성화 확인
            
            SideBySideToolbarPanel.Visibility = Visibility.Visible;
            SharpenButton.Visibility = Visibility.Visible;
            SharpenSeparator.Visibility = Visibility.Visible;
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

        private async Task<List<EpubWin2DPage>> RenderEpubPagesAsync(string html, string currentPath, int pinBlockIndex = -1)
        {
            var pages = new List<EpubWin2DPage>();

            // EPUB 챕터의 블록을 AozoraBindingModel로 파싱 (세로 모드와 동일한 파이프라인 활용)
            var allBlocks = ParseEpubHtmlToAozoraBlocks(html, currentPath, _currentEpubChapterIndex);

            if (allBlocks.Count == 0) return pages;

            // 레이아웃 동기화로 정확해진 ActualWidth 사용
            float availableWidth = (float)(EpubArea?.ActualWidth ?? 0);
            if (availableWidth < 50) 
            {
                availableWidth = (float)(RootGrid?.ActualWidth ?? AppWindow.Size.Width / (RootGrid?.XamlRoot?.RasterizationScale ?? 1.0));
                if (availableWidth < 50) availableWidth = 800; // Final safe fallback if hidden
            }

            float availableHeight = (float)(EpubArea?.ActualHeight ?? 0);
            if (availableHeight < 50) 
            {
                availableHeight = (float)(RootGrid?.ActualHeight ?? AppWindow.Size.Height / (RootGrid?.XamlRoot?.RasterizationScale ?? 1.0));
                if (availableHeight < 50) availableHeight = 800;
            }

            // 렌더링 시와 동일한 마진을 사용하여 페이지 분할
            float marginTop = 30f, marginBottom = 10f;
            float marginRight = 40f, marginLeft = 40f; 
            
            if (_isVerticalMode)
            {
                marginTop = 20f; marginBottom = 20f;
                marginRight = 30f; marginLeft = 25f; 
            }
            
            float maxWidth = availableWidth - (marginRight + marginLeft);
            float pageHeight = availableHeight - (marginTop + marginBottom);

            // 세로 모드일 때는 42자 너비 제한을 풀어서 화면 전체를 줄(Column)로 채울 수 있게 함
            if (!_isVerticalMode)
            {
                float limitedWidth = (float)(_settingsManager.FontSize * 42); 
                if (maxWidth > limitedWidth) maxWidth = limitedWidth; 
            }

            if (maxWidth < 100) maxWidth = 100;
            if (pageHeight < 100) pageHeight = 100;

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();
            int totalBlocks = allBlocks.Count;
            int maxSourceLine = allBlocks[allBlocks.Count - 1].SourceLineNumber;

            // 특정 블록을 페이지 시작점으로 고정(Pin)하는 방식 도입
            if (pinBlockIndex >= 0 && pinBlockIndex < totalBlocks)
            {
                // 1. Pin 지점부터 끝까지 정방향 계산
                int forwardIdx = pinBlockIndex;
                while (forwardIdx < totalBlocks)
                {
                    var page = PaginateNextEpubPage(ref forwardIdx, allBlocks, maxWidth, pageHeight, device);
                    if (page != null) pages.Add(page);
                    else forwardIdx++;

                    if (forwardIdx % 100 == 0) await Task.Delay(1);
                }

                // 2. Pin 지점 이전은 역방향으로 거슬러 올라가며 계산
                int backwardIdx = pinBlockIndex;
                while (backwardIdx > 0)
                {
                    int prevStart = FindPreviousEpubPageStart(backwardIdx, allBlocks, maxWidth, pageHeight, device);
                    if (prevStart >= backwardIdx) break; // 더 이상 거슬러 올라갈 수 없음

                    int tempIdx = prevStart;
                    var page = PaginateNextEpubPage(ref tempIdx, allBlocks, maxWidth, pageHeight, device);
                    if (page != null) pages.Insert(0, page);
                    
                    backwardIdx = prevStart;
                    if (backwardIdx % 100 == 0) await Task.Delay(1);
                }
            }
            else
            {
                // 3. 표준 정방향 계산 (0번 블록부터)
                int i = 0;
                while (i < totalBlocks)
                {
                    var page = PaginateNextEpubPage(ref i, allBlocks, maxWidth, pageHeight, device);
                    if (page != null) pages.Add(page);
                    else i++;

                    if (i % 100 == 0) await Task.Delay(1);
                }
            }

            // TotalLinesInChapter 역산
            int total = Math.Max(1, maxSourceLine);
            foreach (var p in pages) p.TotalLinesInChapter = total;

            return pages;
        }

        private EpubWin2DPage? PaginateNextEpubPage(ref int index, List<AozoraBindingModel> allBlocks, float maxWidth, float pageHeight, CanvasDevice? device)
        {
            if (index >= allBlocks.Count) return null;

            var block = allBlocks[index];

            // 이미지 블록
            if (block.HasImage)
            {
                var imgSrc = block.Inlines.OfType<AozoraImage>().FirstOrDefault()?.Source ?? "";
                var p = new EpubWin2DPage
                {
                    Blocks = new List<AozoraBindingModel> { block },
                    IsImagePage = true,
                    ImagePath = imgSrc,
                    StartBlockIndex = index,
                    StartLine = block.SourceLineNumber,
                    LineCount = 1
                };
                index++;
                return p;
            }

            // 페이지 분리 기호 건너뜀
            if (block.IsPageBreak)
            {
                index++;
                return PaginateNextEpubPage(ref index, allBlocks, maxWidth, pageHeight, device);
            }

            // 텍스트 블록 페이지 분할
            int pageStart = index;
            List<AozoraBindingModel> pageBlocks;

            if (_isVerticalMode)
                pageBlocks = PaginateAozoraPage(ref index, allBlocks, maxWidth, pageHeight, device);
            else
                pageBlocks = PaginateHorizontalAozoraPage(ref index, allBlocks, maxWidth, pageHeight, device);

            if (pageBlocks.Count == 0)
            {
                index++;
                return null;
            }

            return new EpubWin2DPage
            {
                Blocks = pageBlocks,
                IsImagePage = false,
                StartBlockIndex = pageStart,
                StartLine = pageBlocks[0].SourceLineNumber,
                LineCount = pageBlocks.Count
            };
        }

        private int FindPreviousEpubPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, CanvasDevice? device)
        {
            if (targetIdx <= 0) return 0;

            int bestStart = Math.Max(0, targetIdx - 1);
            int scanStart = Math.Max(0, targetIdx - 1);
            int safetyLimit = 300; 

            for (int i = scanStart; i >= 0 && safetyLimit > 0; i--, safetyLimit--)
            {
                int tempIdx = i;
                var page = PaginateNextEpubPage(ref tempIdx, blocks, maxWidth, availHeight, device);
                if (page == null) continue;

                if (tempIdx >= targetIdx)
                {
                    bestStart = i;
                }
                else
                {
                    break;
                }
            }
            return bestStart;
        }

        private void EpubArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode) return;
            // 세로모드에서도 창 크기가 바뀌면 페이지를 다시 계산하도록 함 (글자 잘림 방지)
            TriggerEpubResize();
        }

        private void EpubTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { }

        private void EpubTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode) return;
            // 세로모드에서도 캔버스 크기 변경 시 내용 갱신
            if (_epubWin2DPages.Count > 0)
                EpubTextCanvas?.Invalidate();
        }

        private void EpubCanvasDisplay_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            DrawEpubCanvasInternal(sender, args, CurrentEpubWin2DPage?.ImagePath);
        }

        private void EpubCanvasDisplayLeft_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var nextChapIndex = _currentEpubChapterIndex;
            var nextPgIndex = _currentEpubPageIndex + 1;
            if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
            var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);

            bool actualNextImageOnRight = _nextImageOnRight;
            string? targetPath = actualNextImageOnRight ? CurrentEpubWin2DPage?.ImagePath : pg2?.ImagePath;
            // Always align towards center (Right edge for left column)
            DrawEpubCanvasInternal(sender, args, targetPath, HorizontalAlignment.Right);
        }

        private void EpubCanvasDisplayRight_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var nextChapIndex = _currentEpubChapterIndex;
            var nextPgIndex = _currentEpubPageIndex + 1;
            if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
            var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);

            bool actualNextImageOnRight = _nextImageOnRight;
            string? targetPath = actualNextImageOnRight ? pg2?.ImagePath : CurrentEpubWin2DPage?.ImagePath;
            // Always align towards center (Left edge for right column)
            DrawEpubCanvasInternal(sender, args, targetPath, HorizontalAlignment.Left);
        }

        private void DrawEpubCanvasInternal(CanvasControl sender, CanvasDrawEventArgs args, string? imagePath, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            if (string.IsNullOrEmpty(imagePath)) return;
            CanvasBitmap? bitmap = null;
            lock (_epubLock)
            {
                if (!_epubImageCache.TryGetValue(imagePath, out bitmap) || bitmap == null)
                {
                    // 로딩 중이면 표시하지 않음 (LoadEpubImageForWin2DAsync가 완료 시 Invalidate 호출)
                    return;
                }
            }

            var ds = args.DrawingSession;
            var canvasSize = sender.Size;
            var imageSize = bitmap.Size;

            var fitRatio = Math.Min(canvasSize.Width / imageSize.Width, canvasSize.Height / imageSize.Height);
            var scaledSize = new Windows.Foundation.Size(imageSize.Width * fitRatio, imageSize.Height * fitRatio);

            float posX = 0;
            if (align == HorizontalAlignment.Center) posX = (float)(canvasSize.Width - scaledSize.Width) / 2;
            else if (align == HorizontalAlignment.Right) posX = (float)(canvasSize.Width - scaledSize.Width);
            else posX = 0;

            float posY = (float)(canvasSize.Height - scaledSize.Height) / 2;

            ds.DrawImage(bitmap, new Rect(posX, posY, (float)scaledSize.Width, (float)scaledSize.Height), bitmap.Bounds, 1.0f, CanvasImageInterpolation.HighQualityCubic);
        }


        private void EpubTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isEpubMode) return;
            var ds = args.DrawingSession;
            var size = sender.Size;

            Color bgColor = _isVerticalMode ? GetVerticalBackgroundColor() : ((SolidColorBrush)GetEpubThemeBackground()).Color;
            Color textColor = _isVerticalMode ? GetVerticalTextColor() : ((SolidColorBrush)GetEpubThemeForeground()).Color;
            ds.Clear(bgColor);

            var pg = CurrentEpubWin2DPage;
            if (pg == null || pg.Blocks == null || pg.Blocks.Count == 0) return;

            // 이미지 페이지는 EpubImageHost에서 처리하므로 넘김
            if (pg.IsImagePage) return;

            if (_isVerticalMode)
            {
                float marginTop = 20f;
                float marginBottom = 20f;
                float marginRight = 30f;
                float marginLeft = 25f; // 페이지 분할 시와 동일하게 25 사용
                
                VerticalRenderer.RenderBlocks(
                    ds: ds,
                    blocks: pg.Blocks,
                    textColor: textColor,
                    canvasSize: size,
                    marginTop: marginTop,
                    marginBottom: marginBottom,
                    marginRight: marginRight,
                    marginLeft: marginLeft,
                    baseFontSize: _settingsManager.FontSize,
                    defaultFontFamily: _settingsManager.FontFamily,
                    getFontWeight: GetFontWeightForFamily
                );
            }
            else
            {
                float limitedWidth = (float)(_settingsManager.FontSize * 42);
                float marginLeft = 40f; 
                float contentWidth = Math.Min(limitedWidth, (float)size.Width - 80f);
                float marginTop = 30f;

                HorizontalRenderer.RenderBlocks(
                    ds: ds,
                    blocks: pg.Blocks,
                    textColor: textColor,
                    marginLeft: marginLeft,
                    marginTop: marginTop,
                    maxWidth: contentWidth,
                    baseFontSize: _settingsManager.FontSize,
                    defaultFontFamily: _settingsManager.FontFamily,
                    getFontWeight: GetFontWeightForFamily
                );
            }
        }


        private void EpubTouchOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEpubMode) return;
            DispatcherQueue.TryEnqueue(() => EpubTextCanvas?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
            var pt = e.GetCurrentPoint(EpubTouchOverlay);
            double half = EpubTouchOverlay.ActualWidth / 2;
            
            if (_isVerticalMode)
            {
                // Vertical (RTL): Left=Next, Right=Prev
                if (pt.Position.X < half) _ = NavigateEpubAsync(1);
                else _ = NavigateEpubAsync(-1);
            }
            else
            {
                // Horizontal (LTR): Left=Prev, Right=Next
                if (pt.Position.X < half) _ = NavigateEpubAsync(-1);
                else _ = NavigateEpubAsync(1);
            }
        }

        private void EpubTouchOverlay_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isEpubMode) return;
            // 세로 모드와 동일하게 RootGrid에 포커스 (EpubTextCanvas 포커스 시 잔상/깜박임 방지)
            RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            var delta = e.GetCurrentPoint(EpubTouchOverlay).Properties.MouseWheelDelta;
            if (delta > 0) 
            {
                _ = NavigateEpubAsync(-1);
            }
            else 
            {
                _ = NavigateEpubAsync(1);
            }
        }

        private void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;
        }

        private async Task LoadEpubImageForWin2DAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return;
            if (_epubImageCache.TryGetValue(imagePath, out var cached) && cached != null) return;

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

                var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();
                if (device == null) return;

                var ras = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(ras))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                ras.Seek(0);
                
                var originalBitmap = await CanvasBitmap.LoadAsync(device, ras, 96.0f);
                CanvasBitmap finalBitmap = originalBitmap;

                if (_sharpenEnabled)
                {
                    var sharpened = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, (float)ImageOptions.UpscaleFactor, (float)ImageOptions.SharpenAmount, (float)ImageOptions.SharpenThreshold, (float)ImageOptions.UnsharpAmount, (float)ImageOptions.UnsharpRadius, skipUpscale: false);
                    if (sharpened != null && sharpened != originalBitmap)
                    {
                        finalBitmap = sharpened;
                        originalBitmap.Dispose();
                    }
                }

                lock (_epubLock)
                {
                    _epubImageCache[imagePath] = finalBitmap;
                }

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isEpubMode && CurrentEpubWin2DPage?.IsImagePage == true)
                    {
                         EpubCanvasDisplay?.Invalidate();
                         EpubCanvasDisplayLeft?.Invalidate();
                         EpubCanvasDisplayRight?.Invalidate();
                    }
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
            
            // 가시성 상태가 이미 동일하면 변경하지 않음 (불필요한 레이아웃 갱신 방지)
            if (EpubTextCanvas.Visibility != Visibility.Collapsed) EpubTextCanvas.Visibility = Visibility.Collapsed;
            if (EpubImageHost.Visibility != Visibility.Visible) EpubImageHost.Visibility = Visibility.Visible;

            if (!_isEpubShowingTwoPages)
            {
                // Single image mode
                if (EpubCanvasDisplay.Visibility != Visibility.Visible) EpubCanvasDisplay.Visibility = Visibility.Visible;
                if (EpubCanvasDisplayLeft.Visibility != Visibility.Collapsed) EpubCanvasDisplayLeft.Visibility = Visibility.Collapsed;
                if (EpubCanvasDisplayRight.Visibility != Visibility.Collapsed) EpubCanvasDisplayRight.Visibility = Visibility.Collapsed;
                
                EpubImageLeftColumn.Width = new GridLength(1, GridUnitType.Star);
                EpubImageRightColumn.Width = new GridLength(0);

                _ = LoadEpubImageForWin2DAsync(page.ImagePath);
                EpubCanvasDisplay.Invalidate();
            }
            else
            {
                // Side-by-side mode
                if (EpubCanvasDisplay.Visibility != Visibility.Collapsed) EpubCanvasDisplay.Visibility = Visibility.Collapsed;
                if (EpubCanvasDisplayLeft.Visibility != Visibility.Visible) EpubCanvasDisplayLeft.Visibility = Visibility.Visible;
                if (EpubCanvasDisplayRight.Visibility != Visibility.Visible) EpubCanvasDisplayRight.Visibility = Visibility.Visible;
                
                EpubImageLeftColumn.Width = new GridLength(1, GridUnitType.Star);
                EpubImageRightColumn.Width = new GridLength(1, GridUnitType.Star);

                _ = LoadEpubImageForWin2DAsync(page.ImagePath);

                int nextChapIndex = _currentEpubChapterIndex;
                int nextPgIndex = _currentEpubPageIndex + 1;
                if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
                var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);

                if (pg2 != null && pg2.IsImagePage)
                {
                    _ = LoadEpubImageForWin2DAsync(pg2.ImagePath);
                }

                EpubCanvasDisplayLeft.Invalidate();
                EpubCanvasDisplayRight.Invalidate();
            }
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
            if (!await _epubNavigationLock.WaitAsync(0)) return;

            try
            {
                int step = _isEpubShowingTwoPages ? direction * 2 : direction;

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

                // 이미지 미리 로드 - 세로 모드 로직 참고 (깜박임 방지)
                var targetPgObj = GetEpubWin2DPage(targetChapter, targetPage);
                if (targetPgObj != null && targetPgObj.IsImagePage)
                {
                    await LoadEpubImageForWin2DAsync(targetPgObj.ImagePath);
                    if (_isSideBySideMode || _autoDoublePageForArchive)
                    {
                        var pg2 = GetEpubWin2DPage(targetChapter, targetPage + 1);
                        if (pg2 == null && targetChapter < _epubSpine.Count - 1 && targetPage + 1 >= (targetChapter == _currentEpubChapterIndex ? _epubWin2DPages.Count : _epubPreloadCache[targetChapter].Count))
                        {
                            // 다음 챕터의 첫 페이지 확인
                            pg2 = GetEpubWin2DPage(targetChapter + 1, 0);
                        }
                        if (pg2 != null && pg2.IsImagePage)
                        {
                            await LoadEpubImageForWin2DAsync(pg2.ImagePath);
                        }
                    }
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
            finally
            {
                _epubNavigationLock.Release();
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

        private async Task LoadEpubChapterAsync(int index, bool fromEnd = false, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1, double? progress = null, CancellationToken token = default)
        {
            if (index < 0 || index >= _epubSpine.Count) return;

            try
            {
                if (token.IsCancellationRequested) return;
                FileNameText.Text = (_currentEpubDisplayName ?? Path.GetFileName(_currentEpubFilePath) ?? "") + Strings.Loading;
                await Task.Delay(1, token);
                if (token.IsCancellationRequested) return;

                List<EpubWin2DPage> pages;
                // targetBlockIndex가 지정된 경우(북마크/리사이즈 등) 캐시를 무시하고 해당 블록을 기준으로 항상 다시 계산하여 위치 일관성 보장
                if (targetBlockIndex < 0 && _epubPreloadCache.TryGetValue(index, out var cachedPages))
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

                    pages = await RenderEpubPagesAsync(html, path, pinBlockIndex: targetBlockIndex);
                    _epubPreloadCache[index] = pages;
                    _epubChapterHasText[index] = pages.Any(p => !p.IsImagePage);
                }

                _epubWin2DPages = pages;
                _currentEpubPageIndex = -1;
                // 가로, 세로 모드 모두에서 SideBySide인 경우 다음 챕터가 연달아 이미지면 미리 렌더링해서 캐시에 넣음
                // 이렇게 해야 SetEpubPageIndex에서 다음 페이지(이미지)를 즉시 찾아 2페이지 모드를 유지할 수 있음
                if ((_isSideBySideMode || _autoDoublePageForArchive) && pages.Count > 0 && pages.Any(p => p.IsImagePage))
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

                int finalTargetPage = 0;

                if (targetBlockIndex >= 0)
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        var p = pages[i];
                        if (p.Blocks != null && p.Blocks.Count > 0)
                        {
                            // 병합된 블록으로 인해 Count가 축소되는 문제를 해결하기 위해 다음 페이지의 시작 인덱스와 비교
                            int nextStart = (i + 1 < pages.Count) ? pages[i + 1].StartBlockIndex : int.MaxValue;
                            
                            if (targetBlockIndex >= p.StartBlockIndex && targetBlockIndex < nextStart)
                            {
                                finalTargetPage = i;
                                break;
                            }
                        }
                    }
                }
                else if (targetLine > 1)
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        var p = pages[i];
                        if (p.Blocks != null && p.Blocks.Count > 0)
                        {
                            // 라인 번호도 동일하게 다음 페이지의 시작 라인과 비교하여 누락 방지
                            int pageStartLine = p.Blocks.First().SourceLineNumber;
                            int nextStartLine = (i + 1 < pages.Count && pages[i + 1].Blocks != null && pages[i + 1].Blocks.Count > 0) 
                                ? pages[i + 1].Blocks.First().SourceLineNumber 
                                : int.MaxValue;

                            if (targetLine >= pageStartLine && targetLine < nextStartLine)
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
                
                // SetEpubPageIndex 호출 전, 타겟 페이지가 이미지인 경우 세로 모드처럼 미리 로드 대기 (첫 로딩 시 캔버스 갱신 누락 방지)
                if (pages.Count > 0 && finalTargetPage >= 0 && finalTargetPage < pages.Count)
                {
                    var targetPg = pages[finalTargetPage];
                    if (targetPg.IsImagePage)
                    {
                        // 현재 페이지 이미지 캐시에 확실히 로드
                        await LoadEpubImageForWin2DAsync(targetPg.ImagePath);

                        // SideBySide 모드일 경우 우측(다음) 페이지 이미지도 미리 로드
                        if (_isSideBySideMode || _autoDoublePageForArchive)
                        {
                            int nextChapIndex = index;
                            int nextPgIndex = finalTargetPage + 1;
                            if (nextPgIndex >= pages.Count) { nextChapIndex++; nextPgIndex = 0; }
                            
                            var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);
                            if (pg2 != null && pg2.IsImagePage)
                            {
                                await LoadEpubImageForWin2DAsync(pg2.ImagePath);
                            }
                        }
                    }
                }
                
                SetEpubPageIndex(finalTargetPage);
                _ = PreloadEpubChaptersAsync(index);
            }
            finally
            {
                FileNameText.Text = FileExplorerService.GetFormattedDisplayName(_currentEpubDisplayName ?? Path.GetFileName(_currentEpubFilePath) ?? "", false);
            }
        }

        private void SetEpubPageIndex(int index)
        {
            if (index < 0 || index >= _epubWin2DPages.Count) return;

            _currentEpubPageIndex = index;
            var page = _epubWin2DPages[index];

            // 텍스트 페이지와 이미지 페이지 간 배경색 전환 처리 (이미지 페이지는 투명하게 하여 기본 앱 배경 사용)
            if (EpubArea != null)
            {
                if (page.IsImagePage) EpubArea.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                else EpubArea.Background = GetEpubThemeBackground();
            }

            // 이미지 페이지 처리
            if (page.IsImagePage)
            {
                bool nextIsImage = false;
                bool canSideBySide = _isSideBySideMode;
                // 자동 2장보기 옵션이 켜져 있는 경우 비율에 따른 자동 판단
                if (_autoDoublePageForArchive)
                {
                    lock (_epubLock)
                    {
                        if (_epubImageCache.TryGetValue(page.ImagePath, out var bmp) && bmp != null)
                        {
                            float imgW = (float)bmp.Size.Width;
                            float imgH = (float)bmp.Size.Height;

                            if (imgH >= imgW * 1.2f)
                            {
                                // 세로가 가로의 1.2배 이상 (세로형 이미지) -> 무조건 2장 보기 강제
                                canSideBySide = true;
                            }
                            else if (imgW >= imgH * 1.2f)
                            {
                                // 가로가 세로의 1.2배 이상 (가로형 이미지) -> 무조건 1장 보기 강제
                                canSideBySide = false;
                            }
                            else
                            {
                                // 그 외의 경우 (정사각형 등) 설정된 기본 모드(SideBySide)를 따름
                                canSideBySide = _isSideBySideMode;
                            }
                        }
                    }
                }

                if (canSideBySide)
                {
                    int nextChapIndex = _currentEpubChapterIndex;
                    int nextPgIndex = index + 1;
                    if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
                    var pg2 = GetEpubWin2DPage(nextChapIndex, nextPgIndex);
                    
                    if (pg2 != null && pg2.IsImagePage)
                    {
                        bool nextShouldForceSingle = false;
                        if (_autoDoublePageForArchive)
                        {
                            lock (_epubLock)
                            {
                                if (_epubImageCache.TryGetValue(pg2.ImagePath, out var bmp2) && bmp2 != null)
                                {
                                    if (bmp2.Size.Width >= bmp2.Size.Height * 1.2f) nextShouldForceSingle = true;
                                }
                            }
                        }
                        if (!nextShouldForceSingle) nextIsImage = true;
                    }
                }

                _isEpubShowingTwoPages = nextIsImage;
                
                if (EpubTextCanvas.Visibility != Visibility.Collapsed) EpubTextCanvas.Visibility = Visibility.Collapsed;
                ShowEpubImagePage(page);
            }
            else
            {
                // 텍스트 페이지: Win2D 캔버스
                if (EpubImageHost.Visibility != Visibility.Collapsed) EpubImageHost.Visibility = Visibility.Collapsed;
                if (EpubTextCanvas.Visibility != Visibility.Visible) EpubTextCanvas.Visibility = Visibility.Visible;
                
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
                TextSizeLevelText.Text = _settingsManager.FontSize.ToString();
            }
        }



        private void UpdateEpubVisuals()
        {
            if (EpubArea != null)
            {
                var currentPg = CurrentEpubWin2DPage;
                if (currentPg != null && currentPg.IsImagePage)
                {
                    EpubArea.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                else
                {
                    EpubArea.Background = GetEpubThemeBackground();
                }
            }
            if (_isEpubMode && !_isVerticalMode)
            {
                EpubTextCanvas?.Invalidate();
            }
        }

        private Brush GetEpubThemeForeground()
        {
            if (_settingsManager.ThemeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            if (_settingsManager.ThemeIndex == 3 && _settingsManager.CustomForegroundColor.HasValue) return new SolidColorBrush(_settingsManager.CustomForegroundColor.Value);
            return new SolidColorBrush(Colors.Black);
        }
        
        private Brush GetEpubThemeBackground()
        {
             if (_settingsManager.ThemeIndex == 0) return new SolidColorBrush(Colors.White);
             if (_settingsManager.ThemeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
             if (_settingsManager.ThemeIndex == 3 && _settingsManager.CustomBackgroundColor.HasValue) return new SolidColorBrush(_settingsManager.CustomBackgroundColor.Value);
             return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
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
             string normPath = path.Replace("\\", "/");
             for (int i = 0; i < _epubSpine.Count; i++)
             {
                 if (_epubSpine[i].Replace("\\", "/").Equals(normPath, StringComparison.OrdinalIgnoreCase))
                 {
                     index = i;
                     break;
                 }
             }
             
             if (index >= 0)
             {
                 _currentEpubChapterIndex = index;
                 await LoadEpubChapterAsync(index);
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

            // 문단이 긴 경우 문장 단위로 블록 분리 (Aozora와 일치)
            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(AozoraParserService.SplitBlockBySentences(block));
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
            
            // 블록 태그(p, div, h1~h6 등)를 이중 개행(\n\n)으로 치환하여 문단을 확실히 분리합니다.
            html = Regex.Replace(html, @"</?(?:p|div|h[1-6]|li|blockquote|tr|table|ul|ol)[^>]*>", "\n\n", RegexOptions.IgnoreCase);

            // Strip remaining tags
            html = RxEpubAnyTag.Replace(html, ""); 
            html = System.Net.WebUtility.HtmlDecode(html);
            
            html = html.Replace("\r\n", "\n").Replace("\r", "\n");
            // 여러 개의 연속된 빈 줄을 2개(새 문단 기준)로 줄임
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            
            var lines = html.Split('\n');
            bool isNewParagraph = true;
            

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) 
                {
                    isNewParagraph = true;
                    continue;
                }
                
                // 불필요한 공백 치환 (단일 띄어쓰기는 보존하여 Aozora 파서와 호환 유지)
                string cleanLine = line.Replace('\u00A0', ' ').TrimEnd('\r', '\n', ' ');
                if (isNewParagraph) cleanLine = cleanLine.TrimStart();
                if (string.IsNullOrWhiteSpace(cleanLine)) continue;
                
                var tokens = RxEpubRubySplit.Split(cleanLine);
                
                AozoraBindingModel currentBlock = new AozoraBindingModel 
                { 
                    SourceLineNumber = lineNum++, 
                    EpubChapterIndex = chapterIndex,
                    IsParagraphContinuation = !isNewParagraph
                };

                foreach (var token in tokens)
                {
                    // 루비 텍스트인 경우 내부를 건드리지 않고 보호합니다.
                    if (token.StartsWith("{{RUBY|"))
                    {
                        var content = token.Substring(7, token.Length - 9);
                        var parts = content.Split(new[] { "|~|" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            currentBlock.Inlines.Add(new AozoraRuby { BaseText = parts[0], RubyText = parts[1] });
                        }
                    }
                    else if (!string.IsNullOrEmpty(token))
                    {
                        currentBlock.Inlines.Add(token);
                    }
                }
                
                // 루프 종료 후 남은 인라인 처리
                if (currentBlock.Inlines.Count > 0)
                {
                    blocks.Add(currentBlock);
                }
                
                // 다음 줄은 빈 줄(개행)이 나타나지 않는 한 현재 문단과 이어집니다.
                isNewParagraph = false;
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