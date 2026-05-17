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
        private readonly EpubDocumentService _epubDocumentService = new();
        private readonly EpubPaginationService _epubPaginationService = new();

        private bool _isEpubMode = false;
        public int PendingEpubChapterIndex { get; set; } = -1;
        public int PendingEpubPageIndex { get; set; } = -1;
        private int _pendingEpubStartBlockIndex = -1;

        private List<EpubWin2DPage> _epubWin2DPages = new();
        private int _currentEpubPageIndex = 0;
        private EpubWin2DPage? CurrentEpubWin2DPage => (_epubWin2DPages.Count > 0 && _currentEpubPageIndex >= 0 && _currentEpubPageIndex < _epubWin2DPages.Count) ? _epubWin2DPages[_currentEpubPageIndex] : null;
        private bool _isEpubShowingTwoPages = false;

        private Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache = new();
        private Dictionary<int, bool> _epubChapterHasText = new();
        private CancellationTokenSource? _epubPreloadCts;

        // 이미지 캐시는 _imageResourceService로 통합됨 (접두어 "epub:")

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
                _epubResizeTimer.Tick += async (s, e) =>
                {
                    try
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
                 if (!await CloseCurrentArchiveAsync()) return;
                 if (!await CloseCurrentPdfAsync()) return;
                 if (!await CloseCurrentEpubAsync()) return;

                 _currentEpubFilePath = file.Path;
                 _currentEpubDisplayName = entry?.DisplayName ?? file.Name;
                 
                 _epubPreloadCache.Clear();
                 var stream = await file.OpenStreamForReadAsync();
                 _currentEpubArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                
                 var packageInfo = await _epubDocumentService.LoadPackageInfoAsync(_currentEpubArchive, _epubArchiveLock);
                 if (string.IsNullOrEmpty(packageInfo.RootPath)) throw new Exception("Invalid container.xml");

                 _epubSpine = packageInfo.Spine;
                 _epubTocPath = packageInfo.TocPath;
                 
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


        // [안정성 수정] 동기 Wait → 비동기 WaitAsync로 전환하여 UI 프리징 방지
        private async Task<bool> CloseCurrentEpubAsync()
        {
            bool lockAcquired = await _epubArchiveLock.WaitAsync(TimeSpan.FromSeconds(10));
            if (!lockAcquired)
            {
                System.Diagnostics.Debug.WriteLine("EPUB lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

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
                _imageResourceService.ClearEpubEntries();
                _aozoraBlocks.Clear();
                ClearVerticalDisplayState();
                return true;
            }
            finally
            {
                _epubArchiveLock.Release();
            }
        }
        
        // [하위 호환] 동기 호출이 필요한 곳(Window.Closed 등)을 위한 래퍼
        private void CloseCurrentEpub()
        {
            _ = CloseCurrentEpubAsync();
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

        private async Task PreloadEpubChaptersAsync(int currentIndex)
        {
            _epubPreloadCts?.Cancel();
            _epubPreloadCts = new CancellationTokenSource();
            var token = _epubPreloadCts.Token;

            try
            {
                var indicesToPreload = _epubPageFlowService.GetPreloadChapterIndices(currentIndex, _epubSpine.Count);

                foreach (int idx in indicesToPreload)
                {
                    if (token.IsCancellationRequested) return;
                    if (_epubPreloadCache.ContainsKey(idx)) continue;
                    if (_currentEpubArchive == null) return;

                    string path = _epubSpine[idx];
                    string? html = await _epubDocumentService.ReadEntryTextAsync(_currentEpubArchive, path, _epubArchiveLock);
                    if (html == null) continue;

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

        private void UpdateEpubStatus()
        {
            if (!_isEpubMode) return;
            
            int currentPage = _currentEpubPageIndex + 1;
            int totalPages = _epubWin2DPages.Count;
            if (totalPages == 0) totalPages = 1;

            var pg = CurrentEpubWin2DPage;
            int currentLine = pg?.StartLine ?? 1;
            int totalLines = pg?.TotalLinesInChapter ?? 1;

            double totalProgress = _readingProgressService.CalculateEpubProgress(
                _currentEpubChapterIndex,
                _epubSpine.Count,
                currentPage,
                totalPages);

            if (ImageInfoText != null)
            {
                ImageInfoText.Text = Strings.LineInfo(currentLine, totalLines);
            }

            if (TextProgressText != null)
            {
                TextProgressText.Text = _readingProgressService.FormatPercent(totalProgress);
            }
            
            if (ImageIndexText != null)
            {
                ImageIndexText.Text = $"{currentPage} / {totalPages} (Ch.{_currentEpubChapterIndex + 1})";
            }

            _ = AddToRecentAsync(true);
        }

        // --- Core Rendering Logic ---

        private async Task<List<EpubWin2DPage>> RenderEpubPagesAsync(string html, string currentPath, int pinBlockIndex = -1)
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

            var result = await _epubPaginationService.CreatePagesAsync(
                new EpubPaginationRequest(
                    html,
                    currentPath,
                    _currentEpubChapterIndex,
                    viewport.Width,
                    viewport.Height,
                    _settingsManager.FontSize,
                    _isVerticalMode,
                    pinBlockIndex,
                    device),
                _epubDocumentService,
                PaginateAozoraPage,
                PaginateHorizontalAozoraPage,
                FindPreviousPageStart);

            _textTotalLineCountInSource = result.TotalLineCount;
            return result.Pages;
        }

        // FindPreviousEpubPageStart 제거됨 — aozora.cs의 FindPreviousPageStart(isVertical 파라미터)로 통합

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

            string cacheKey = Services.ImageResourceService.GetEpubCacheKey(imagePath);
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


        private async void EpubTouchOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
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

        private async void EpubTouchOverlay_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
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

        private void EpubPage_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
             if (!_isEpubMode) return;
        }

        private async Task LoadEpubImageForWin2DAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return;

            string cacheKey = Services.ImageResourceService.GetEpubCacheKey(imagePath);
            if (_imageResourceService.TryGetCached(cacheKey) != null) return;
            var archiveAtStart = _currentEpubArchive;
            var filePathAtStart = _currentEpubFilePath;
            if (archiveAtStart == null) return;

            var device = EpubTextCanvas?.Device ?? CanvasDevice.GetSharedDevice();
            bool IsStillCurrentEpub() =>
                ReferenceEquals(_currentEpubArchive, archiveAtStart) &&
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

                     int pageIndex = _epubPageFlowService.FindPageByLine(_epubWin2DPages, targetLine);
                     if (pageIndex >= 0 && pageIndex < _epubWin2DPages.Count)
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
                if (_currentEpubArchive == null) return;
                string path = _epubSpine[chapterIndex];
                string? html = await _epubDocumentService.ReadEntryTextAsync(_currentEpubArchive, path, _epubArchiveLock);
                if (html != null)
                {
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
                    if (_currentEpubArchive == null) return;
                    string path = _epubSpine[index];
                    string? html = await _epubDocumentService.ReadEntryTextAsync(_currentEpubArchive, path, _epubArchiveLock);
                    if (html == null) return;

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
                        if (_currentEpubArchive == null) return;
                        string? nextHtml = await _epubDocumentService.ReadEntryTextAsync(_currentEpubArchive, _epubSpine[nextIdx], _epubArchiveLock);
                        if (nextHtml != null)
                        {
                            var nextPages = await RenderEpubPagesAsync(nextHtml, _epubSpine[nextIdx]);
                            _epubPreloadCache[nextIdx] = nextPages;
                        }
                    }
                }

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
                    IsAutoDoublePageTallCandidate);
                
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

        private EpubImageSize? GetCachedEpubImageSize(string imagePath)
        {
            var bitmap = _imageResourceService.TryGetCached(Services.ImageResourceService.GetEpubCacheKey(imagePath));
            return bitmap == null ? null : new EpubImageSize(bitmap.Size.Width, bitmap.Size.Height);
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
            return new SolidColorBrush(_settingsManager.GetThemeForegroundColor());
        }
        
        private Brush GetEpubThemeBackground()
        {
             return new SolidColorBrush(_settingsManager.GetThemeBackgroundColor());
        }
        

        public async void JumpToEpubTocItem(EpubTocItem item)
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


        public void ClearEpubCache()
        {
            _epubPreloadCache.Clear();
        }

        private async Task<List<AozoraBindingModel>> GetEpubChapterAsAozoraBlocksAsync(int index)
        {
            if (index < 0 || index >= _epubSpine.Count) return new List<AozoraBindingModel>();
            if (_currentEpubArchive == null) return new List<AozoraBindingModel>();

            string path = _epubSpine[index];
            string? html = await _epubDocumentService.ReadEntryTextAsync(_currentEpubArchive, path, _epubArchiveLock);
            if (html == null) return new List<AozoraBindingModel>();

            var parseResult = _epubDocumentService.ParseHtmlToAozoraBlocks(html, path, index);
            _textTotalLineCountInSource = parseResult.TotalLineCount;
            return parseResult.Blocks;
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
