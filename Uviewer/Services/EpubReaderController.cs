using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;
using Uviewer.Renderers;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer.Services
{
    internal sealed class EpubReaderController
    {
        private readonly IEpubReaderHost _host;

        internal EpubReaderController(IEpubReaderHost host)
        {
            _host = host;
        }

        internal EpubSession Session => _epubSession;
        internal EpubDocumentService DocumentService => _epubDocumentService;
        internal EpubPageFlowService PageFlowService => _epubPageFlowService;
        internal bool IsEpubMode { get => _isEpubMode; set => _isEpubMode = value; }
        internal int CurrentChapterIndex { get => _currentEpubChapterIndex; set => _currentEpubChapterIndex = value; }
        internal int CurrentPageIndex { get => _currentEpubPageIndex; set => _currentEpubPageIndex = value; }
        internal string? CurrentFilePath => _currentEpubFilePath;
        internal string? CurrentDisplayName => _currentEpubDisplayName;
        internal IReadOnlyList<string> Spine => _epubSpine;
        internal int SpineCount => _epubSpine.Count;
        internal int PendingChapterIndex { get => PendingEpubChapterIndex; set => PendingEpubChapterIndex = value; }
        internal int PendingPageIndex { get => PendingEpubPageIndex; set => PendingEpubPageIndex = value; }
        internal int PendingStartBlockIndex { get => _pendingEpubStartBlockIndex; set => _pendingEpubStartBlockIndex = value; }
        internal EpubWin2DPage? CurrentPage => CurrentEpubWin2DPage;
        internal List<EpubWin2DPage> Pages { get => _epubWin2DPages; set => _epubWin2DPages = value; }
        internal Dictionary<int, List<EpubWin2DPage>> PreloadCache => _epubPreloadCache;
        internal Dictionary<int, bool> ChapterHasText => _epubChapterHasText;
        internal object? SelectedItem => EpubSelectedItem;
        internal List<UIElement> PageElements => _epubPages;

        private bool _isWindowClosing => _host.IsWindowClosing;
        private bool _isWebDavMode => _host.IsWebDavMode;
        private bool _isTextMode { get => _host.IsTextMode; set => _host.IsTextMode = value; }
        private bool _isAozoraMode { get => _host.IsAozoraMode; set => _host.IsAozoraMode = value; }
        private bool _isMarkdownRenderMode { get => _host.IsMarkdownRenderMode; set => _host.IsMarkdownRenderMode = value; }
        private bool _isVerticalMode { get => _host.IsVerticalMode; set => _host.IsVerticalMode = value; }
        private bool _isSideBySideMode => _host.IsSideBySideMode;
        private bool _autoDoublePageForArchive => _host.AutoDoublePageForArchive;
        private bool _nextImageOnRight => _host.NextImageOnRight;
        private bool _isNavigatingRecent { get => _host.IsNavigatingRecent; set => _host.IsNavigatingRecent = value; }
        private int _currentIndex { get => _host.CurrentIndex; set => _host.CurrentIndex = value; }
        private int _aozoraPendingTargetLine { get => _host.AozoraPendingTargetLine; set => _host.AozoraPendingTargetLine = value; }
        private int _textTotalLineCountInSource { get => _host.TextTotalLineCountInSource; set => _host.TextTotalLineCountInSource = value; }
        private string _currentTextContent { get => _host.CurrentTextContent; set => _host.CurrentTextContent = value; }
        private string Title { get => _host.WindowTitle; set => _host.WindowTitle = value; }
        private List<ImageEntry> _imageEntries { get => _host.ImageEntries; set => _host.ImageEntries = value; }
        private List<AozoraBindingModel> _aozoraBlocks => _host.AozoraBlocks;
        private string? _activeSearchQuery => _host.ActiveSearchQuery;

        private DispatcherQueue DispatcherQueue => _host.DispatcherQueue;
        private AppWindow AppWindow => _host.AppWindow;
        private Grid RootGrid => _host.RootGrid;
        private Grid ImageArea => _host.ImageArea;
        private Grid TextArea => _host.TextArea;
        private Grid EpubArea => _host.EpubArea;
        private Grid EpubImageHost => _host.EpubImageHost;
        private Grid EpubTouchOverlay => _host.EpubTouchOverlay;
        private ScrollViewer TextScrollViewer => _host.TextScrollViewer;
        private CanvasControl VerticalTextCanvas => _host.VerticalTextCanvas;
        private CanvasControl AozoraTextCanvas => _host.AozoraTextCanvas;
        private CanvasControl EpubTextCanvas => _host.EpubTextCanvas;
        private CanvasControl EpubCanvasDisplay => _host.EpubCanvasDisplay;
        private CanvasControl EpubCanvasDisplayLeft => _host.EpubCanvasDisplayLeft;
        private CanvasControl EpubCanvasDisplayRight => _host.EpubCanvasDisplayRight;
        private ColumnDefinition EpubImageLeftColumn => _host.EpubImageLeftColumn;
        private ColumnDefinition EpubImageRightColumn => _host.EpubImageRightColumn;
        private TextBlock FileNameText => _host.FileNameText;
        private TextBlock ImageInfoText => _host.ImageInfoText;
        private TextBlock TextProgressText => _host.TextProgressText;
        private TextBlock ImageIndexText => _host.ImageIndexText;
        private MainToolbarControl MainToolbar => _host.MainToolbar;

        private TextSettingsManager _settingsManager => _host.SettingsManager;
        private ReaderLayoutService _readerLayoutService => _host.ReaderLayoutService;
        private TextBlockDocumentService _textBlockDocumentService => _host.TextBlockDocumentService;
        private TextStatusBarService _textStatusBarService => _host.TextStatusBarService;
        private TextDialogService _textDialogService => _host.TextDialogService;
        private ImageResourceService _imageResourceService => _host.ImageResourceService;
        private IAnimatedWebpService _animatedWebpService => _host.AnimatedWebpService;
        private WebDavService _webDavService => _host.WebDavService;
        private TocService _tocService => _host.TocService;

        private Task AddToRecentAsync(bool immediate) => _host.AddToRecentAsync(immediate);
        private Task<bool> CloseCurrentArchiveAsync() => _host.CloseCurrentArchiveAsync();
        private Task<bool> CloseCurrentPdfAsync() => _host.CloseCurrentPdfAsync();
        private void CancelAndResetGlobalTextCts() => _host.CancelAndResetGlobalTextCts();
        private void LoadTextSettings() => _host.LoadTextSettings();
        private void SaveTextSettings() => _host.SaveTextSettings();
        private void EnsureMinWindowSizeForText() => _host.EnsureMinWindowSizeForText();
        private void UpdateSideBySideButtonState() => _host.UpdateSideBySideButtonState();
        private void UpdateNextImageSideButtonState() => _host.UpdateNextImageSideButtonState();
        private void SyncSidebarSelection(ImageEntry entry) => _host.SyncSidebarSelection(entry);
        private void ClearBackwardCache() => _host.ClearBackwardCache();
        private void ClearVerticalDisplayState() => _host.ClearVerticalDisplayState();
        private void AttachVerticalPreviewKeyIfNeeded() => _host.AttachVerticalPreviewKeyIfNeeded();
        private Task PrepareVerticalTextAsync(int line) => _host.PrepareVerticalTextAsync(line);
        private void ShowNotification(string message, string icon = "\uE735", string color = "Gold") => _host.ShowNotification(message, icon, color);
        private FontWeight GetFontWeightForFamily(string fontFamily) => _host.GetFontWeightForFamily(fontFamily);
        private Color GetVerticalBackgroundColor() => _host.GetVerticalBackgroundColor();
        private Color GetVerticalTextColor() => _host.GetVerticalTextColor();
        private DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind) => _host.GetActiveSearchMatchFor(kind);
        private List<AozoraBindingModel> PaginateVerticalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null) =>
            _host.PaginateVerticalAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        private List<AozoraBindingModel> PaginateHorizontalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null) =>
            _host.PaginateHorizontalAozoraPage(ref index, blocks, availableWidth, availableHeight, device);
        private int FindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, ICanvasResourceCreator device, bool isVertical) =>
            _host.FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical);
        private Task LoadImageResourceAndInvalidateAsync(string resourcePath, string cacheKey, CanvasDevice device, Action invalidate, Action? onMissing = null, Func<bool>? shouldKeepLoadedBitmap = null) =>
            _host.LoadImageResourceAndInvalidateAsync(resourcePath, cacheKey, device, invalidate, onMissing, shouldKeepLoadedBitmap);

        private readonly EpubSession _epubSession = new();
        private List<string> _epubSpine
        {
            get => _epubSession.Spine;
            set => _epubSession.ReplaceSpine(value);
        }

        private int _currentEpubChapterIndex = 0;
        private string? _currentEpubFilePath;
        private string? _currentEpubDisplayName;
        private object _epubLock = new object();
        private SemaphoreSlim _epubNavigationLock = new SemaphoreSlim(1, 1);
        private readonly EpubDocumentService _epubDocumentService = new();
        private readonly EpubPaginationService _epubPaginationService = new();
        private readonly EpubPageFlowService _epubPageFlowService = new();

        private bool _isEpubMode = false;
        public int PendingEpubChapterIndex { get; set; } = -1;
        public int PendingEpubPageIndex { get; set; } = -1;
        private int _pendingEpubStartBlockIndex = -1;
        private int _epubChapterLoadGeneration;
        private bool _isCurrentEpubChapterPartial;
        private Task? _epubFullPaginationTask;

        private readonly EpubReaderState _epubReaderState = new();
        private List<EpubWin2DPage> _epubWin2DPages
        {
            get => _epubReaderState.Pages;
            set => _epubReaderState.Pages = value ?? new List<EpubWin2DPage>();
        }

        private int _currentEpubPageIndex
        {
            get => _epubReaderState.CurrentPageIndex;
            set => _epubReaderState.CurrentPageIndex = value;
        }

        private EpubWin2DPage? CurrentEpubWin2DPage => _epubReaderState.CurrentPage;
        private bool _isEpubShowingTwoPages
        {
            get => _epubReaderState.IsShowingTwoPages;
            set => _epubReaderState.IsShowingTwoPages = value;
        }

        private Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache => _epubReaderState.PreloadCache;
        private Dictionary<int, bool> _epubChapterHasText => _epubReaderState.ChapterHasText;

        // 이미지 캐시는 _imageResourceService로 통합됨 (접두어 "epub:")

        internal int CurrentEpubChapterIndex => _currentEpubChapterIndex;
        internal int CurrentEpubPageIndex => _currentEpubPageIndex;

        private DispatcherQueueTimer? _epubResizeTimer;

        internal void TriggerEpubResize()
        {
            if (_isWindowClosing || !_isEpubMode) return;

            if (_epubResizeTimer == null)
            {
                _epubResizeTimer = this.DispatcherQueue.CreateTimer();
                _epubResizeTimer.Interval = TimeSpan.FromMilliseconds(300);
                _epubResizeTimer.IsRepeating = false;
                _epubResizeTimer.Tick += async (s, e) =>
                {
                    try
                    {
                        if (_isWindowClosing || !_isEpubMode)
                        {
                            _epubResizeTimer?.Stop();
                            return;
                        }

                        if (_isEpubMode)
                        {
                            // [버그 수정] 로딩 중에 SizeChanged가 발생하면 _epubWin2DPages가 없어서 리사이즈가 취소되는 문제 해결.
                            // 로딩 중이라면 타이머를 연장하여 로딩이 끝난 뒤에 반영되게 유도합니다.
                            if (CurrentEpubWin2DPage == null || _epubWin2DPages == null || _epubWin2DPages.Count == 0) 
                            {
                                if (!_isWindowClosing)
                                {
                                    _epubResizeTimer?.Start();
                                }
                                return;
                            }

                            _epubReaderState.ClearPreload();
                            _imageResourceService.ClearEpubEntries();
                            // [핵심 해결] 글자 크기나 창 크기가 바뀌면 공용 측정 캐시(MainWindow.aozora.cs 정의)를 비워야 정확한 재계산이 가능합니다.
                            ClearBackwardCache(); 
                            
                            int currentLine = CurrentEpubWin2DPage?.StartLine ?? 1;
                            int currentBlockIdx = CurrentEpubWin2DPage?.StartBlockIndex ?? -1;
                            await LoadEpubChapterAsync(_currentEpubChapterIndex, targetLine: currentLine, targetBlockIndex: currentBlockIdx);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in EpubResize timer: {ex.Message}");
                    }
                };
            }

            _epubResizeTimer.Stop();
            _epubResizeTimer.Start();
        }

        internal async Task RestoreEpubStateAsync(int chapterIndex, int pageIndex)
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

        internal async Task LoadEpubEntryAsync(ImageEntry entry, CancellationToken token = default)
        {
            try
            {
                if (!await CloseCurrentEpubAsync()) return;
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

        internal async Task LoadEpubFileAsync(StorageFile file, ImageEntry? entry = null, CancellationToken token = default)
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
                 if (!await CloseCurrentArchiveAsync()) return;
                 if (!await CloseCurrentPdfAsync()) return;
                 if (!await CloseCurrentEpubAsync()) return;

                 _currentEpubFilePath = file.Path;
                 _currentEpubDisplayName = entry?.DisplayName ?? file.Name;

                 // Keep the EPUB host in layout while the bookmarked first page is built,
                 // but do not expose the previous CanvasControl back buffer meanwhile.
                 // Collapsing it here would make pagination use a zero-sized viewport.
                 if (EpubArea != null) EpubArea.Opacity = 0;
                 
                 _epubReaderState.ClearPreload();
                 var stream = await file.OpenStreamForReadAsync();
                 var packageInfo = await _epubSession.OpenAsync(stream, _epubDocumentService);
                 if (string.IsNullOrEmpty(packageInfo.RootPath)) throw new Exception("Invalid container.xml");
                 
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
                     MainToolbar.SetVerticalToggleState(isChecked: true);
                     AttachVerticalPreviewKeyIfNeeded();
                 }

                 // 3. Load Chapter (Updated to handle pending positions)
                 int targetCh = (PendingEpubChapterIndex >= 0) ? PendingEpubChapterIndex : 0;
                 _currentEpubChapterIndex = targetCh;
                 await LoadEpubChapterAsync(targetCh, targetLine: _aozoraPendingTargetLine, targetBlockIndex: _pendingEpubStartBlockIndex, targetPage: PendingEpubPageIndex, token: token);

                 if (token.IsCancellationRequested) return;
                 // Give CanvasControl one composition frame to paint the selected page
                 // before revealing it; otherwise its previous back buffer can flash.
                 await Task.Delay(16, token);
                 if (EpubArea != null) EpubArea.Opacity = 1;

                 // LoadEpubChapterAsync 내부에서 targetBlockIndex 기반으로 이미 최적의 페이지를 설정하므로,
                 // 화면 크기에 종속적인 PendingEpubPageIndex를 여기서 다시 강제로 설정하지 않습니다.
                 
                 // Reset pending values
                 PendingEpubChapterIndex = -1;
                 PendingEpubPageIndex = -1;
                 _aozoraPendingTargetLine = 0;
                 _pendingEpubStartBlockIndex = -1;
                 _epubChapterHasText.Clear();
                 
                 // 4. Load TOC (Background)
                int tocSessionVersion = _epubSession.Version;
                var tocArchive = _epubSession.Archive;
                var tocPath = _epubSession.TocPath;
                var tocSpine = _epubSpine.ToList();
                _ = Task.Run(async () => {
                    if (_isWindowClosing || tocSessionVersion != _epubSession.Version) return;
                    if (tocArchive != null && !string.IsNullOrEmpty(tocPath))
                    {
                        _tocService.SetProvider(new EpubTocProvider(tocArchive, tocPath, tocSpine));
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
                 if (EpubArea != null) EpubArea.Opacity = 1;
                 _isNavigatingRecent = false;
             }
        }


        // [안정성 수정] 동기 Wait → 비동기 WaitAsync로 전환하여 UI 프리징 방지
        internal async Task<bool> CloseCurrentEpubAsync()
        {
            _epubChapterLoadGeneration++;
            _isCurrentEpubChapterPartial = false;
            _epubFullPaginationTask = null;
            StopEpubResizeTimer();
            _epubReaderState.ClearPreload();
            _tocService.Clear();

            if (!await _epubSession.CloseAsync(TimeSpan.FromSeconds(10)))
            {
                return false;
            }

            _isEpubMode = false;
            _currentEpubFilePath = null;
            _currentEpubDisplayName = null;
            _currentEpubChapterIndex = 0;
            _epubReaderState.ClearAll();
            _imageResourceService.ClearEpubEntries();
            _aozoraBlocks.Clear();
            ClearVerticalDisplayState();
            return true;
        }

        internal void StopEpubResizeTimer()
        {
            try
            {
                _epubResizeTimer?.Stop();
            }
            catch { }
        }

        internal void ShutdownEpubResources()
        {
            _epubChapterLoadGeneration++;
            _isCurrentEpubChapterPartial = false;
            _epubFullPaginationTask = null;
            StopEpubResizeTimer();
            _tocService.Clear();
            _epubReaderState.ClearAll();
            _imageResourceService.ClearEpubEntries();
            _aozoraBlocks.Clear();
            _isEpubMode = false;
            _currentEpubFilePath = null;
            _currentEpubDisplayName = null;
            _currentEpubChapterIndex = 0;

            try
            {
                _epubSession.Close(TimeSpan.FromMilliseconds(500));
            }
            catch { }
        }
        
        // [하위 호환] 동기 호출이 필요한 곳(Window.Closed 등)을 위한 래퍼
        internal void CloseCurrentEpub()
        {
            _ = CloseCurrentEpubAsync();
        }

        internal void SwitchToEpubMode()
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
            
            MainToolbar.SetImageToolbarVisible(false);
            MainToolbar.SetTextToolbarVisible(true); // Reuse text toolbar for now
            MainToolbar.SetVerticalToggleState(isEnabled: true); // 버튼 활성화 확인
            
            MainToolbar.SetSideBySideToolbarVisible(true);
            MainToolbar.SetSharpenControlsVisible(true);
            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();
            
            Title = "Uviewer - Image & Text Viewer";
        }

        private async Task PreloadEpubChaptersAsync(int currentIndex)
        {
            if (_isWindowClosing || !_isEpubMode) return;

            var token = _epubReaderState.RestartPreload();

            try
            {
                var indicesToPreload = _epubPageFlowService.GetPreloadChapterIndices(currentIndex, _epubSpine.Count);

                foreach (int idx in indicesToPreload)
                {
                    if (token.IsCancellationRequested) return;
                    if (_epubPreloadCache.ContainsKey(idx)) continue;
                    string path = _epubSpine[idx];
                    string? html = await _epubSession.ReadEntryTextAsync(path, _epubDocumentService);
                    if (html == null) continue;

                    if (token.IsCancellationRequested) return;

                    var pages = await RenderEpubPagesAsync(html, path);
                    if (_isWindowClosing || token.IsCancellationRequested || !_isEpubMode) return;

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
                    var keysToRemove = _epubPageFlowService.GetPreloadKeysToRemove(_epubPreloadCache.Keys, currentIndex);
                    foreach (var k in keysToRemove) _epubPreloadCache.Remove(k);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Epub preload error: {ex.Message}");
            }
        }

        internal void UpdateEpubStatus()
        {
            if (!_isEpubMode) return;

            var content = _textStatusBarService.CreateEpub(
                _currentEpubChapterIndex,
                _epubSpine.Count,
                _currentEpubPageIndex,
                _epubWin2DPages.Count,
                CurrentEpubWin2DPage);

            if (ImageInfoText != null)
            {
                ImageInfoText.Text = content.LineInfo;
            }

            if (TextProgressText != null)
            {
                TextProgressText.Text = content.ProgressText;
            }
            
            if (ImageIndexText != null)
            {
                ImageIndexText.Text = content.PageInfo;
            }

            _ = AddToRecentAsync(true);
        }

        // --- Core Rendering Logic ---

        internal async Task<List<EpubWin2DPage>> RenderEpubPagesAsync(string html, string currentPath, int pinBlockIndex = -1)
        {
            var viewport = _readerLayoutService.CreateEpubViewport(
                EpubArea?.ActualWidth ?? 0,
                EpubArea?.ActualHeight ?? 0,
                RootGrid?.ActualWidth ?? 0,
                RootGrid?.ActualHeight ?? 0,
                AppWindow.Size.Width,
                AppWindow.Size.Height,
                RootGrid?.XamlRoot?.RasterizationScale ?? 1.0);

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();

            var request = new EpubPaginationRequest(
                html,
                currentPath,
                _currentEpubChapterIndex,
                viewport.Width,
                viewport.Height,
                _settingsManager.FontSize,
                _isVerticalMode,
                pinBlockIndex,
                device);
            var result = await Task.Run(() => _epubPaginationService.CreatePagesAsync(
                request,
                _epubDocumentService,
                PaginateVerticalAozoraPage,
                PaginateHorizontalAozoraPage,
                FindPreviousPageStart));

            _textTotalLineCountInSource = result.TotalLineCount;
            return result.Pages;
        }

        private async Task<EpubPaginationResult> RenderEpubPreviewPagesAsync(
            string html,
            string currentPath,
            int chapterIndex,
            int targetLine,
            int targetBlockIndex)
        {
            var viewport = _readerLayoutService.CreateEpubViewport(
                EpubArea?.ActualWidth ?? 0,
                EpubArea?.ActualHeight ?? 0,
                RootGrid?.ActualWidth ?? 0,
                RootGrid?.ActualHeight ?? 0,
                AppWindow.Size.Width,
                AppWindow.Size.Height,
                RootGrid?.XamlRoot?.RasterizationScale ?? 1.0);

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();
            var request = new EpubPaginationRequest(
                    html,
                    currentPath,
                    chapterIndex,
                    viewport.Width,
                    viewport.Height,
                    _settingsManager.FontSize,
                    _isVerticalMode,
                    targetBlockIndex,
                    device,
                    isPreview: true,
                    targetLine: targetLine);
            var result = await Task.Run(() => _epubPaginationService.CreatePagesAsync(
                request,
                _epubDocumentService,
                PaginateVerticalAozoraPage,
                PaginateHorizontalAozoraPage,
                FindPreviousPageStart));

            _textTotalLineCountInSource = result.TotalLineCount;
            return result;
        }

        // FindPreviousEpubPageStart 제거됨 — aozora.cs의 FindPreviousPageStart(isVertical 파라미터)로 통합

        internal void EpubArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode) return;
            // 세로모드에서도 창 크기가 바뀌면 페이지를 다시 계산하도록 함 (글자 잘림 방지)
            TriggerEpubResize();
        }

        internal void EpubTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { }

        internal void EpubTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isEpubMode) return;
            // 세로모드에서도 캔버스 크기 변경 시 내용 갱신
            if (_epubWin2DPages.Count > 0)
                EpubTextCanvas?.Invalidate();
        }

        internal void EpubCanvasDisplay_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            DrawEpubCanvasInternal(sender, args, CurrentEpubWin2DPage?.ImagePath);
        }

        internal void EpubCanvasDisplayLeft_Draw(CanvasControl sender, CanvasDrawEventArgs args)
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

        internal void EpubCanvasDisplayRight_Draw(CanvasControl sender, CanvasDrawEventArgs args)
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

            string cacheKey = ImageResourceService.GetEpubCacheKey(imagePath);
            var bitmap = _imageResourceService.TryGetCached(cacheKey);
            if (bitmap == null) return;  // 로딩 중이면 표시하지 않음

            try
            {
                var ds = args.DrawingSession;
                var canvasSize = sender.Size;
                ImageCanvasRenderer.DrawBitmapFit(ds, bitmap, new Rect(0, 0, canvasSize.Width, canvasSize.Height), align);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EPUB image draw skipped: {ex.Message}");
            }
        }



        internal void EpubTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
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
                var margins = ReaderPageMargins.EpubVerticalText;
                
                VerticalRenderer.RenderBlocks(
                    ds: ds,
                    blocks: pg.Blocks,
                    textColor: textColor,
                    canvasSize: size,
                    marginTop: margins.Top,
                    marginBottom: margins.Bottom,
                    marginRight: margins.Right,
                    marginLeft: margins.Left,
                    baseFontSize: _settingsManager.FontSize,
                    defaultFontFamily: _settingsManager.FontFamily,
                    getFontWeight: GetFontWeightForFamily,
                    searchQuery: _activeSearchQuery,
                    currentSearchMatch: GetActiveSearchMatchFor(DocumentSearchKind.Epub),
                    renderedSearchKind: DocumentSearchKind.Epub,
                    firstBlockIndex: pg.StartBlockIndex
                );
            }
            else
            {
                var margins = ReaderPageMargins.HorizontalText;
                float limitedWidth = (float)(_settingsManager.FontSize * 42);
                float contentWidth = Math.Min(limitedWidth, (float)size.Width - margins.Horizontal);

                HorizontalRenderer.RenderBlocks(
                    ds: ds,
                    blocks: pg.Blocks,
                    textColor: textColor,
                    marginLeft: margins.Left,
                    marginTop: margins.Top,
                    maxWidth: contentWidth,
                    baseFontSize: _settingsManager.FontSize,
                    defaultFontFamily: _settingsManager.FontFamily,
                    getFontWeight: GetFontWeightForFamily,
                    searchQuery: _activeSearchQuery,
                    currentSearchMatch: GetActiveSearchMatchFor(DocumentSearchKind.Epub),
                    renderedSearchKind: DocumentSearchKind.Epub,
                    firstBlockIndex: pg.StartBlockIndex
                );
            }
        }


        internal async void EpubTouchOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                if (!_isEpubMode) return;
                DispatcherQueue.TryEnqueue(() => EpubTextCanvas?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
                var pt = e.GetCurrentPoint(EpubTouchOverlay);
                double half = EpubTouchOverlay.ActualWidth / 2;
                
                if (_isVerticalMode)
                {
                    // Vertical (RTL): Next is on the Left, Prev is on the Right
                    if (pt.Position.X < half) await NavigateEpubAsync(1);
                    else await NavigateEpubAsync(-1);
                }
                else
                {
                    // Horizontal (LTR): Prev is on the Left, Next is on the Right
                    if (pt.Position.X < half) await NavigateEpubAsync(-1);
                    else await NavigateEpubAsync(1);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in EpubTouchOverlay_PointerPressed: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal async void EpubTouchOverlay_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                if (!_isEpubMode) return;
                // 세로 모드와 동일하게 RootGrid에 포커스 (EpubTextCanvas 포커스 시 잔상/깜박임 방지)
                RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                var delta = e.GetCurrentPoint(EpubTouchOverlay).Properties.MouseWheelDelta;
                if (delta > 0) 
                {
                    await NavigateEpubAsync(-1);
                }
                else 
                {
                    await NavigateEpubAsync(1);
                }
             e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in EpubTouchOverlay_PointerWheelChanged: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;
        }

        private async Task LoadEpubImageForWin2DAsync(string imagePath)
        {
            if (_isWindowClosing || !_isEpubMode) return;
            if (string.IsNullOrEmpty(imagePath)) return;

            string cacheKey = ImageResourceService.GetEpubCacheKey(imagePath);
            if (_imageResourceService.TryGetCached(cacheKey) != null) return;
            int sessionVersionAtStart = _epubSession.Version;
            var filePathAtStart = _currentEpubFilePath;
            if (!_epubSession.HasDocument) return;

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();
            bool IsStillCurrentEpub() =>
                !_isWindowClosing &&
                _isEpubMode &&
                _epubSession.Version == sessionVersionAtStart &&
                string.Equals(_currentEpubFilePath, filePathAtStart, StringComparison.OrdinalIgnoreCase);

            await LoadImageResourceAndInvalidateAsync(
                imagePath,
                cacheKey,
                device,
                () =>
                {
                    if (!_isEpubMode || CurrentEpubWin2DPage?.IsImagePage != true) return;

                    EpubCanvasDisplay?.Invalidate();
                    EpubCanvasDisplayLeft?.Invalidate();
                    EpubCanvasDisplayRight?.Invalidate();
                },
                shouldKeepLoadedBitmap: IsStillCurrentEpub);
        }


        internal void ShowEpubImagePage(EpubWin2DPage page)
        {
            if (_isWindowClosing || !_isEpubMode) return;
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



        internal async Task ShowEpubGoToLineDialog()
        {
            var pg = CurrentEpubWin2DPage;
            int totalLines = pg?.TotalLinesInChapter ?? _epubWin2DPages.Count;
            int currentLine = pg?.StartLine ?? (_currentEpubPageIndex + 1);

            var result = await _textDialogService.ShowGoToLineAsync(currentLine, totalLines, Strings.DialogTitle);
            if (result.HasValue)
            {
                await GoToEpubLineAsync(result.Value);
            }
        }

        internal async Task GoToEpubLineAsync(int targetLine)
        {
            if (targetLine < 1) return;

            if (_isCurrentEpubChapterPartial && _epubFullPaginationTask != null)
            {
                await _epubFullPaginationTask;
                if (_isCurrentEpubChapterPartial) return;
            }

            if (_isVerticalMode)
            {
                await PrepareVerticalTextAsync(targetLine);
                return;
            }

            int pageIndex = _epubPageFlowService.FindPageByLine(_epubWin2DPages, targetLine);
            if (pageIndex >= 0 && pageIndex < _epubWin2DPages.Count)
            {
                SetEpubPageIndex(pageIndex);
            }
        }

        // Navigation Handlers
        
        internal async Task NavigateEpubAsync(int direction)
        {
            if (!_isEpubMode) return;
            if (!await _epubNavigationLock.WaitAsync(0)) return;

            try
            {
                int step = _isEpubShowingTwoPages ? direction * 2 : direction;

                if (direction > 0 &&
                    _isCurrentEpubChapterPartial &&
                    _currentEpubPageIndex + step >= _epubWin2DPages.Count &&
                    _epubFullPaginationTask != null)
                {
                    await _epubFullPaginationTask;
                    if (_isCurrentEpubChapterPartial) return;
                    step = _isEpubShowingTwoPages ? direction * 2 : direction;
                }

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
                string? html = await _epubSession.ReadEntryTextAsync(path, _epubDocumentService);
                if (html != null)
                {
                    var pages = await RenderEpubPagesAsync(html, path);
                    _epubPreloadCache[chapterIndex] = pages;
                }
            }
        }

        internal EpubWin2DPage? GetEpubWin2DPage(int chapterIndex, int pageIndex)
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

        internal async Task LoadEpubChapterAsync(int index, bool fromEnd = false, int targetLine = -1, int targetBlockIndex = -1, int targetPage = -1, double? progress = null, CancellationToken token = default)
        {
            if (_isWindowClosing || !_isEpubMode) return;
            if (index < 0 || index >= _epubSpine.Count) return;

            try
            {
                int loadGeneration = ++_epubChapterLoadGeneration;
                int sessionVersion = _epubSession.Version;
                if (token.IsCancellationRequested) return;
                FileNameText.Text = (_currentEpubDisplayName ?? Path.GetFileName(_currentEpubFilePath) ?? "") + Strings.Loading;
                await Task.Delay(1, token);
                if (token.IsCancellationRequested) return;

                List<EpubWin2DPage> pages;
                string? htmlForBackgroundPagination = null;
                bool pagesArePartial = false;
                // targetBlockIndex가 지정된 경우(북마크/리사이즈 등) 캐시를 무시하고 해당 블록을 기준으로 항상 다시 계산하여 위치 일관성 보장
                if (targetBlockIndex < 0 && _epubPreloadCache.TryGetValue(index, out var cachedPages))
                {
                    pages = cachedPages;
                }
                else
                {
                    string path = _epubSpine[index];
                    string? html = await _epubSession.ReadEntryTextAsync(path, _epubDocumentService);
                    if (html == null) return;

                    bool hasStableContentAnchor = targetBlockIndex >= 0 || targetLine > 1;
                    bool canUsePreview = !fromEnd &&
                        !progress.HasValue &&
                        (targetPage <= 0 || hasStableContentAnchor);
                    if (canUsePreview)
                    {
                        var preview = await RenderEpubPreviewPagesAsync(
                            html,
                            path,
                            index,
                            targetLine,
                            targetBlockIndex);
                        pages = preview.Pages;
                        pagesArePartial = preview.IsPartial;
                        if (pagesArePartial) htmlForBackgroundPagination = html;
                    }
                    else
                    {
                        pages = await RenderEpubPagesAsync(html, path, pinBlockIndex: targetBlockIndex);
                    }

                    if (!pagesArePartial)
                    {
                        _epubPreloadCache[index] = pages;
                        _epubChapterHasText[index] = pages.Any(p => !p.IsImagePage);
                    }
                }

                _epubWin2DPages = pages;
                _isCurrentEpubChapterPartial = pagesArePartial;
                _currentEpubPageIndex = -1;
                int finalTargetPage = _epubPageFlowService.FindTargetPage(
                    pages,
                    targetBlockIndex,
                    targetLine,
                    targetPage,
                    progress,
                    fromEnd);
                
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
                
                if (_isWindowClosing || token.IsCancellationRequested || !_isEpubMode) return;

                SetEpubPageIndex(finalTargetPage);
                if (pagesArePartial && htmlForBackgroundPagination != null)
                {
                    int anchorBlockIndex = CurrentEpubWin2DPage?.StartBlockIndex ?? targetBlockIndex;
                    _epubFullPaginationTask = CompleteEpubChapterPaginationAsync(
                        htmlForBackgroundPagination,
                        _epubSpine[index],
                        index,
                        anchorBlockIndex,
                        loadGeneration,
                        sessionVersion,
                        token);
                }
                else
                {
                    _epubFullPaginationTask = null;
                    _ = PreloadEpubChaptersAsync(index);
                }
            }
            finally
            {
                if (!_isWindowClosing)
                {
                    FileNameText.Text = FileExplorerService.GetFormattedDisplayName(_currentEpubDisplayName ?? Path.GetFileName(_currentEpubFilePath) ?? "", false);
                }
            }
        }

        private async Task CompleteEpubChapterPaginationAsync(
            string html,
            string path,
            int chapterIndex,
            int anchorBlockIndex,
            int loadGeneration,
            int sessionVersion,
            CancellationToken token)
        {
            try
            {
                // Keep the preview's first visible block as a hard page boundary. If the
                // full pass starts from the chapter beginning, that block can land in the
                // middle of a page and the first line visibly jumps when pages are swapped.
                var fullPages = await RenderEpubPagesAsync(html, path, anchorBlockIndex);
                if (token.IsCancellationRequested ||
                    loadGeneration != _epubChapterLoadGeneration ||
                    sessionVersion != _epubSession.Version ||
                    !_isEpubMode ||
                    chapterIndex != _currentEpubChapterIndex)
                {
                    return;
                }

                int currentLine = CurrentEpubWin2DPage?.StartLine ?? 1;
                int currentBlockIndex = CurrentEpubWin2DPage?.StartBlockIndex ?? -1;
                _epubPreloadCache[chapterIndex] = fullPages;
                _epubChapterHasText[chapterIndex] = fullPages.Any(p => !p.IsImagePage);
                _epubWin2DPages = fullPages;
                _isCurrentEpubChapterPartial = false;

                if (fullPages.Count > 0)
                {
                    int pageIndex = currentBlockIndex >= 0
                        ? _epubPageFlowService.FindPageByBlockIndex(fullPages, currentBlockIndex)
                        : _epubPageFlowService.FindPageByLine(fullPages, currentLine);
                    SetEpubPageIndex(pageIndex);
                }

                _ = PreloadEpubChaptersAsync(chapterIndex);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EPUB background pagination error: {ex.Message}");
            }
        }

        internal void SetEpubPageIndex(int index)
        {
            if (_isWindowClosing || !_isEpubMode) return;
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
                int nextChapIndex = _currentEpubChapterIndex;
                int nextPgIndex = index + 1;
                if (nextPgIndex >= _epubWin2DPages.Count) { nextChapIndex++; nextPgIndex = 0; }
                var nextPage = GetEpubWin2DPage(nextChapIndex, nextPgIndex);

                _isEpubShowingTwoPages = _epubPageFlowService.ShouldShowImageSideBySide(
                    page,
                    nextPage,
                    _isSideBySideMode,
                    _autoDoublePageForArchive,
                    GetCachedEpubImageSize,
                    ImageDoublePageDecisionService.IsTallCandidate);
                
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

        internal EpubImageSize? GetCachedEpubImageSize(string imagePath)
        {
            var bitmap = _imageResourceService.TryGetCached(ImageResourceService.GetEpubCacheKey(imagePath));
            return bitmap == null ? null : new EpubImageSize(bitmap.Size.Width, bitmap.Size.Height);
        }

        // --- Epub Settings Logic ---

        internal void LoadEpubSettings()
        {
            LoadTextSettings();
            UpdateEpubToolbarUI();
        }

        internal void SaveEpubSettings()
        {
            SaveTextSettings();
        }

        private void UpdateEpubToolbarUI()
        {
            MainToolbar.SetTextSizeLevel(_settingsManager.FontSize);
        }



        internal void UpdateEpubVisuals()
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
            return new SolidColorBrush(_settingsManager.GetThemeForegroundColor());
        }
        
        private Brush GetEpubThemeBackground()
        {
             return new SolidColorBrush(_settingsManager.GetThemeBackgroundColor());
        }
        

        internal async void JumpToEpubTocItem(EpubTocItem item)
        {
            try
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in JumpToEpubTocItem: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }


        internal void ClearEpubCache()
        {
            _epubReaderState.ClearPreload();
        }

        internal async Task<List<AozoraBindingModel>> GetEpubChapterAsAozoraBlocksAsync(int index)
        {
            if (index < 0 || index >= _epubSpine.Count) return new List<AozoraBindingModel>();
            string path = _epubSpine[index];
            string? html = await _epubSession.ReadEntryTextAsync(path, _epubDocumentService);
            if (html == null) return new List<AozoraBindingModel>();

            var parseResult = _epubDocumentService.ParseHtmlToAozoraBlocks(html, path, index);
            _textTotalLineCountInSource = parseResult.TotalLineCount;
            return parseResult.Blocks;
        }
    }
}
