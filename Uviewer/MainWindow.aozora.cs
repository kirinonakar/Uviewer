using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isAozoraMode = true;
        private bool _isMarkdownRenderMode = false;
        private List<AozoraBindingModel> _aozoraBlocks = new();
        private int _aozoraTotalLineCount = 0;
        private int _aozoraTotalLineCountInSource = 0;

        private struct AozoraPageInfo
        {
            public List<AozoraBindingModel> Blocks;
            public int StartLine;
        }

        // Win2D Rendering State
        private AozoraPageInfo _currentAozoraPageInfo;
        private int _currentAozoraStartBlockIndex = 0;
        private int _currentAozoraEndBlockIndex = 0;
        private Stack<int> _aozoraNavHistory = new();
        private Dictionary<string, CanvasBitmap> _aozoraImageCache = new();
        private HashSet<string> _knownMissingAozoraImages = new();

        // --- 이전 페이지 역산 최적화 및 백그라운드 캐시 ---
        private System.Threading.CancellationTokenSource? _backwardCacheCts;
        private Dictionary<int, int> _backwardPageCache = new();
        
        // 👉 GC 부하가 없는 값 타입(Struct) 고유 키 생성
        private struct BlockCacheKey : IEquatable<BlockCacheKey>
        {
            public int SourceLine;
            public int InlineCount;
            public int ContentHash; // 💡 한 줄이 여러 블록으로 쪼개졌을 때(문장 분리)를 위한 내용물 해시 추가

            public BlockCacheKey(int line, int count, int contentHash)
            {
                SourceLine = line;
                InlineCount = count;
                ContentHash = contentHash;
            }

            public bool Equals(BlockCacheKey other) => 
                SourceLine == other.SourceLine && 
                InlineCount == other.InlineCount && 
                ContentHash == other.ContentHash;

            public override int GetHashCode() => HashCode.Combine(SourceLine, InlineCount, ContentHash);
            public override bool Equals(object? obj) => obj is BlockCacheKey other && Equals(other);
        }

        // 👉 객체 참조가 아닌 구조체를 Key로 사용하여 복제된 객체도 캐시 적중 가능하게 함
        private System.Collections.Concurrent.ConcurrentDictionary<BlockCacheKey, float> _blockMeasureCache = new();

        // 캐시 무효화를 위한 상태 저장 (완벽한 방어)
        private float _lastCacheWidth = 0;
        private float _lastCacheHeight = 0;
        private double _lastCacheFontSize = 0;
        private bool _lastCacheIsVertical = false;
        private List<AozoraBindingModel>? _lastCachedBlocksRef = null;
        private int _lastCachedTargetIdx = -1;

        private void ValidateBackwardCache(float width, float height, double fontSize, bool isVertical, int currentIdx)
        {
            // 1. 레이아웃 변경 (창 크기, 폰트, 모드)
            bool layoutChanged = Math.Abs(_lastCacheWidth - width) > 1f ||
                                 Math.Abs(_lastCacheHeight - height) > 1f ||
                                 Math.Abs(_lastCacheFontSize - fontSize) > 0.01 ||
                                 _lastCacheIsVertical != isVertical;
                                 
            // 2. 문서 또는 챕터 변경 (배열 참조값 자체가 달라짐 = EPUB 챕터 이동 등)
            bool documentChanged = _lastCachedBlocksRef != _aozoraBlocks;
            
            // 3. 비정상적인 점프 (갑자기 300블록 이상 건너뜀 = 목차 이동, 북마크 등)
            bool jumped = _lastCachedTargetIdx >= 0 && Math.Abs(currentIdx - _lastCachedTargetIdx) > 300; 

            if (layoutChanged || documentChanged || jumped)
            {
                ClearBackwardCache();
                _aozoraNavHistory?.Clear();   // 잔재하는 히스토리 완벽 파기
                _verticalNavHistory?.Clear(); // 잔재하는 히스토리 완벽 파기

                _lastCacheWidth = width;
                _lastCacheHeight = height;
                _lastCacheFontSize = fontSize;
                _lastCacheIsVertical = isVertical;
                _lastCachedBlocksRef = _aozoraBlocks;
            }
            
            _lastCachedTargetIdx = currentIdx;
        }

        private void ClearBackwardCache()
        {
            _backwardCacheCts?.Cancel();
            lock (_backwardPageCache)
            {
                _backwardPageCache.Clear();
            }
            // 👉 렌더링 환경이 바뀌면 측정 캐시도 즉시 초기화
            _blockMeasureCache.Clear();
        }

        // O(log K) 이진 탐색을 제거하고, 가장 정확한 역방향 순차 탐색으로 교체합니다.
        // 1페이지 분량(약 30~50블록)만 탐색하고 즉시 종료되므로 매우 빠르며, 
        // 백그라운드 캐시와 결합되어 딜레이가 전혀 발생하지 않습니다.
        // O(log K) 이진 탐색을 제거하고, 가장 정확한 역방향 순차 스캔 방식으로 수정
        private int FindPreviousPageStart(int targetIdx, List<AozoraBindingModel> blocks, float maxWidth, float availHeight, Microsoft.Graphics.Canvas.ICanvasResourceCreator device, bool isVertical)
        {
            if (targetIdx <= 0) return 0;

            int bestStart = Math.Max(0, targetIdx - 1);
            int scanStart = Math.Max(0, targetIdx - 1);
            int safetyLimit = 300; // 가독성/성능을 위한 스캔 범위 제한

            // targetIdx 바로 직전 블록부터 거꾸로 올라가며 '한 페이지 분량'을 꽉 채우는 지점 찾기
            for (int i = scanStart; i >= 0 && safetyLimit > 0; i--, safetyLimit--)
            {
                int tempIdx = i;
                var pageBlocks = isVertical
                    ? PaginateAozoraPage(ref tempIdx, blocks, maxWidth, availHeight, device as Microsoft.Graphics.Canvas.CanvasDevice)
                    : PaginateHorizontalAozoraPage(ref tempIdx, blocks, maxWidth, availHeight, device as Microsoft.Graphics.Canvas.CanvasDevice);

                // 이 시작점(i)에서 나열했을 때, 끝나는 지점(tempIdx)이 targetIdx보다 뒤거나 같다면
                // 즉, targetIdx를 포함하거나 넘어서는 한 페이지가 만들어진다면 이곳이 '이전 페이지'의 후보입니다.
                if (tempIdx >= targetIdx)
                {
                    bestStart = i;
                }
                else
                {
                    // 만약 끝나는 지점이 targetIdx보다 앞이라면, 더 이상 거꾸로 올라가도 targetIdx에 닿지 않으므로 중단
                    break;
                }
            }
            return bestStart;
        }

        // 백그라운드에서 최대 10페이지 분량을 미리 계산하여 캐시에 적재합니다.
        private void StartBackwardPageCaching(int currentStartIdx, bool isVertical)
        {
            _backwardCacheCts?.Cancel();
            _backwardCacheCts = new System.Threading.CancellationTokenSource();
            var token = _backwardCacheCts.Token;

            if (currentStartIdx <= 0 || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

            float availWidth = isVertical ? (float)(VerticalTextCanvas?.ActualWidth ?? 1000) - 40 : (float)(AozoraTextCanvas?.ActualWidth ?? 1000) - 80;
            float availHeight = isVertical ? (float)(VerticalTextCanvas?.ActualHeight ?? 800) - 40 : (float)(AozoraTextCanvas?.ActualHeight ?? 800) - 40;
            var device = isVertical ? VerticalTextCanvas?.Device : AozoraTextCanvas?.Device;
            device ??= Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
            
            float maxWidth = availWidth;
            if (!isVertical)
                maxWidth = _isMarkdownRenderMode ? availWidth : Math.Min(availWidth, (float)GetUrlMaxWidth());

            // 💡 백그라운드 캐싱 시작 전 무조건 유효성 검증
            ValidateBackwardCache(maxWidth, availHeight, _settingsManager.FontSize, isVertical, currentStartIdx);

            var blocks = _aozoraBlocks;

            Task.Run(() =>
            {
                int targetIdx = currentStartIdx;
                
                for (int i = 0; i < 10; i++)
                {
                    if (token.IsCancellationRequested || targetIdx <= 0) break;

                    int prevStart = FindPreviousPageStart(targetIdx, blocks, maxWidth, availHeight, device, isVertical);
                    
                    if (token.IsCancellationRequested) break;

                    lock (_backwardPageCache)
                    {
                        _backwardPageCache[targetIdx] = prevStart;
                    }

                    if (prevStart >= targetIdx) break; 
                    targetIdx = prevStart;
                }
            }, token);
        }

        // Page Calculation
        private int _aozoraTotalPages = 0;
        private bool _isAozoraPageCalcCompleted = false;
        private System.Threading.CancellationTokenSource? _aozoraPageCalcCts;
        private int _aozoraCalculatedCurrentPage = 1;

        // Settings
        public class AozoraSettings
        {
            public bool IsAozoraModeEnabled { get; set; } = true;
        }

        private const string AozoraSettingsFilePath = "aozora_settings.json";
        private string GetAozoraSettingsFilePath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", AozoraSettingsFilePath);

        [System.Text.Json.Serialization.JsonSerializable(typeof(AozoraSettings))]
        public partial class AozoraSettingsContext : System.Text.Json.Serialization.JsonSerializerContext;

        private void LoadAozoraSettings()
        {
            try
            {
                var file = GetAozoraSettingsFilePath();
                if (System.IO.File.Exists(file))
                {
                    var json = System.IO.File.ReadAllText(file);
                    var settings = System.Text.Json.JsonSerializer.Deserialize(json, typeof(AozoraSettings), AozoraSettingsContext.Default) as AozoraSettings;
                    if (settings != null)
                    {
                        _isAozoraMode = settings.IsAozoraModeEnabled;
                    }
                }

                if (AozoraToggleButton != null)
                {
                    AozoraToggleButton.IsChecked = _isAozoraMode;
                }
            }
            catch { }
        }

        private void SaveAozoraSettings()
        {
            try
            {
                var settings = new AozoraSettings { IsAozoraModeEnabled = _isAozoraMode };
                var json = System.Text.Json.JsonSerializer.Serialize(settings, typeof(AozoraSettings), AozoraSettingsContext.Default);

                var file = GetAozoraSettingsFilePath();
                var dir = System.IO.Path.GetDirectoryName(file);
                if (dir != null) System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(file, json);
            }
            catch { }
        }

        private void AozoraToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleAozoraMode();
        }

        private async void ToggleAozoraMode()
        {
            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Visible;
            await Task.Delay(10);
            
            int currentLine = 1;

            if (_isAozoraMode)
            {
                // [추가] 수직 모드가 활성화되어 있다면 수직 모드의 현재 라인을 가장 먼저 우선시해야 함
                if (_isVerticalMode && _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                }
                // [수정] 가로 렌더링 대기 라인을 우선 확인
                else if (_aozoraPendingTargetLine > 1)
                {
                    currentLine = _aozoraPendingTargetLine;
                }
                else if (_currentAozoraPageInfo.Blocks != null && _currentAozoraPageInfo.Blocks.Count > 0)
                {
                    currentLine = _currentAozoraPageInfo.StartLine;
                }
            }
            else
            {
                // [수정] 세로 모드에서 넘어올 때의 방어 로직 추가
                if (_isVerticalMode && _pendingVerticalScrollLine.HasValue)
                {
                    currentLine = _pendingVerticalScrollLine.Value;
                }
                else if (_isVerticalMode && _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                }
                else if (TextScrollViewer != null)
                {
                    currentLine = GetTopVisibleLineIndex();
                }
            }

            _aozoraPendingTargetLine = currentLine > 0 ? currentLine : 1;
            _isAozoraMode = !_isAozoraMode;

            if (AozoraToggleButton != null) AozoraToggleButton.IsChecked = _isAozoraMode;
            SaveAozoraSettings();

            if (!string.IsNullOrEmpty(_currentTextContent))
            {
                CancelAndResetGlobalTextCts();
                string displayName = string.IsNullOrEmpty(_currentTextFilePath) ? "Document" : System.IO.Path.GetFileName(_currentTextFilePath);
                await DisplayLoadedText(_currentTextContent, displayName, _currentTextFilePath, _globalTextCts!.Token);
            }
        }

        private async Task ReloadTextDisplayFromCacheAsync(string fileName, int targetLine)
        {
            try
            {
                _aozoraPageCalcCts?.Cancel();
                _pageCalcCts?.Cancel();
                
                _aozoraPendingTargetLine = targetLine;
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                if (_isAozoraMode)
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;

                    await PrepareAozoraDisplayAsync(_currentTextContent, targetLine, token);
                    FileNameText.Text = FileExplorerService.GetFormattedDisplayName(fileName, _currentTextArchiveEntryKey != null, _currentArchivePath);
                }
                else
                {
                    if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
                    if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;

                    if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                    {
                        TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                    }

                    await LoadTextLinesProgressivelyAsync(_currentTextContent, targetLine, token);
                    UpdateTextStatusBar(fileName, _textTotalLineCountInSource, 1);

                    if (targetLine > 1)
                    {
                        await Task.Delay(50);
                        ScrollToLine(targetLine);
                        UpdateTextStatusBar();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReloadTextDisplayFromCacheAsync error: {ex.Message}");
            }
        }

        private int _aozoraPendingTargetLine = 1;

        private async Task PrepareAozoraDisplayAsync(string rawContent, int targetLine = 1, CancellationToken token = default)
{
    try
    {
        if (_aozoraPendingTargetLine != 1)
        {
            targetLine = _aozoraPendingTargetLine;
            // _aozoraPendingTargetLine = 1; // <--- 이 줄을 삭제하세요! (섣불리 지우면 위치를 잃어버립니다)
        }

        // 화면 전환 전 불필요한 UI 숨기기 (로딩 뷰가 있다면 여기서 띄워도 좋습니다)
        if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;
        if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Collapsed;
        if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;

        if (TextArea != null)
        {
            TextArea.Visibility = Visibility.Visible;
            TextArea.Background = _settingsManager.GetThemeBackground();
        }

        ClearBackwardCache(); // <-- 파일/챕터 변경 시 캐시 지우기 추가

        // [핵심 추가] 이미 파싱된 블록이 존재하고, 스케일링 설정(FontSize)이 동일하다면 불필요한 재파싱을 생략하여 즉시 렌더링
        if (_aozoraBlocks != null && _aozoraBlocks.Count > 0 && Math.Abs(_lastCacheFontSize - _settingsManager.FontSize) < 0.01)
        {
            _lastCacheFontSize = _settingsManager.FontSize;
            int startIdx = 0;
            if (targetLine > 1)
            {
                // O(log N) 이진 탐색으로 즉시 탐색
                int left = 0;
                int right = _aozoraBlocks.Count - 1;
                
                while (left <= right)
                {
                    int mid = left + (right - left) / 2;
                    if (_aozoraBlocks[mid].SourceLineNumber == targetLine)
                    {
                        startIdx = mid;
                        right = mid - 1; // 동일한 라인 넘버 중 가장 첫 번째 블록을 찾기 위함
                    }
                    else if (_aozoraBlocks[mid].SourceLineNumber < targetLine)
                    {
                        startIdx = mid;
                        left = mid + 1;
                    }
                    else
                    {
                        right = mid - 1;
                    }
                }
            }
            else if (targetLine < 0)
            {
                int targetPage = -targetLine;
                startIdx = Math.Min((targetPage - 1) * 30, Math.Max(0, _aozoraBlocks.Count - 1));
            }

            _currentAozoraStartBlockIndex = startIdx;

            if (AozoraTextCanvas != null)
            {
                AozoraTextCanvas.Visibility = Visibility.Visible;
                if (AozoraTextCanvas.ActualHeight == 0 || AozoraTextCanvas.ActualWidth == 0)
                {
                    await Task.Delay(50);
                }
            }

            RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
            _aozoraPendingTargetLine = 1;
            StartAozoraPageCalculationAsync();
            UpdateAozoraStatusBar();
            return; // 재파싱 없이 함수 즉시 종료
        }

        // 블록이 없다면 기존 파싱 로직 실행하기 위한 초기화
        _currentAozoraStartBlockIndex = 0;
        _currentAozoraEndBlockIndex = 0;
        _currentAozoraPageInfo = new AozoraPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };

        // 2. 전체 데이터 백그라운드 파싱 (UI 프리징 방지)
        await Task.Run(() =>
        {
            if (token.IsCancellationRequested) return;

            List<AozoraBindingModel> parsedBlocks;
            int sourceLineCount;

            if (_isMarkdownRenderMode)
            {
                parsedBlocks = AozoraParserService.ParseMarkdownContent(rawContent);
                sourceLineCount = parsedBlocks.Count;
            }
            else
            {
                // 서비스 클래스에서 Preprocess와 파싱, 그리고 TotalLineCount 반환을 한 번에 처리합니다.
                var result = AozoraParserService.ParseAozoraContent(rawContent, _settingsManager.FontSize);
                parsedBlocks = result.Blocks;
                sourceLineCount = result.SourceLineCount;
            }

            DispatcherQueue.TryEnqueue(() => { _lastCacheFontSize = _settingsManager.FontSize; });

            if (token.IsCancellationRequested) return;

            // 3. 파싱 완료 후 UI 스레드에서 화면 업데이트
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (token.IsCancellationRequested) return;

                _aozoraBlocks = parsedBlocks;
                _aozoraTotalLineCount = _aozoraBlocks.Count;
                _aozoraTotalLineCountInSource = sourceLineCount;
                _textTotalLineCountInSource = sourceLineCount;

                // 목표 라인(TargetLine) 인덱스 탐색
                int startIdx = 0;
                if (targetLine > 1)
                {
                    for (int i = 0; i < _aozoraBlocks.Count; i++)
                    {
                        if (_aozoraBlocks[i].SourceLineNumber >= targetLine)
                        {
                            startIdx = (_aozoraBlocks[i].SourceLineNumber == targetLine) ? i : (i > 0 ? i - 1 : 0);
                            break;
                        }
                    }
                }
                else if (targetLine < 0)
                {
                    int targetPage = -targetLine;
                    startIdx = Math.Min((targetPage - 1) * 30, Math.Max(0, _aozoraBlocks.Count - 1));
                }

                _currentAozoraStartBlockIndex = startIdx;

                if (AozoraTextCanvas != null)
                {
                    AozoraTextCanvas.Visibility = Visibility.Visible;
                    // 캔버스 크기가 아직 잡히지 않았다면 잠시 대기
                    if (AozoraTextCanvas.ActualHeight == 0 || AozoraTextCanvas.ActualWidth == 0)
                    {
                        await Task.Delay(50);
                    }
                }

                // 현재 화면에 보이는 만큼만 가상화 렌더링
                if (_aozoraBlocks.Count > 0)
                {
                    RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                }

                _aozoraPendingTargetLine = 1; // <--- 렌더링이 화면에 반영된 직후인 이 위치에서 초기화해 줍니다!

                // 렌더링 완료 후 남은 전체 페이지 계산 백그라운드 시작
                StartAozoraPageCalculationAsync();
                UpdateAozoraStatusBar();
            });

        }, token);
    }
    catch (TaskCanceledException)
    {
        // 토큰 취소 시 무시
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Aozora Load Error: {ex.Message}");
    }
}

        private void AozoraTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isTextMode || !_isAozoraMode) return;
            if (_aozoraBlocks.Count == 0) return;

            ClearBackwardCache(); // <-- 화면 크기 변경 시 캐시 지우기
            // 크기 변경 시 현재 위치 다시 렌더링
            RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
            StartAozoraPageCalculationAsync();
        }

        private void RenderAozoraDynamicPage(int startIdx)
        {
            if (AozoraTextCanvas == null || _aozoraBlocks == null || _aozoraBlocks.Count == 0)
            {
                _currentAozoraPageInfo = new AozoraPageInfo { Blocks = new List<AozoraBindingModel>(), StartLine = 1 };
                if (AozoraTextCanvas != null) AozoraTextCanvas.Invalidate();
                UpdateAozoraStatusBar();
                return;
            }

            startIdx = Math.Max(0, Math.Min(startIdx, _aozoraBlocks.Count - 1));
            _currentAozoraStartBlockIndex = startIdx;

            // [수정] 아래쪽 마진(marginBottom)을 10으로 확 줄여 하단 끝까지 텍스트를 밀어 넣습니다.
            float marginTop = 30, marginBottom = 10, marginRight = 40, marginLeft = 40;
            float availableHeight = (float)AozoraTextCanvas.ActualHeight;
            if (availableHeight < 100) availableHeight = (float)RootGrid.ActualHeight - 200;
            if (availableHeight < 100) availableHeight = 800;
            availableHeight -= (marginTop + marginBottom);

            float availableWidth = (float)AozoraTextCanvas.ActualWidth;
            if (availableWidth < 100) availableWidth = (float)RootGrid.ActualWidth - 100;
            if (availableWidth < 100) availableWidth = 1000;
            availableWidth -= (marginRight + marginLeft);

            float maxWidth = _isMarkdownRenderMode ? availableWidth : Math.Min(availableWidth, (float)GetUrlMaxWidth());

            int index = startIdx;
            var device = AozoraTextCanvas.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

            var pageBlocks = PaginateHorizontalAozoraPage(ref index, _aozoraBlocks, maxWidth, availableHeight, device);

            _currentAozoraEndBlockIndex = index > startIdx ? index - 1 : startIdx;
            _currentAozoraPageInfo = new AozoraPageInfo
            {
                Blocks = pageBlocks,
                StartLine = pageBlocks.Count > 0 ? pageBlocks[0].SourceLineNumber : 1
            };

            AozoraTextCanvas.Invalidate();
            UpdateAozoraStatusBar();
            StartBackwardPageCaching(_currentAozoraStartBlockIndex, false); // <-- 현재 페이지 렌더링 직후 백그라운드 캐싱 시작
        }

        private async void StartAozoraPageCalculationAsync()
        {
            if (!_isAozoraMode || _aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

            _aozoraPageCalcCts?.Cancel();
            _aozoraPageCalcCts = new System.Threading.CancellationTokenSource();
            var token = _aozoraPageCalcCts.Token;

            _isAozoraPageCalcCompleted = false;
            _aozoraTotalPages = 0;
            _aozoraCalculatedCurrentPage = 1;
            UpdateAozoraStatusBar();

            if (AozoraTextCanvas == null || AozoraTextCanvas.ActualHeight <= 0 || AozoraTextCanvas.ActualWidth <= 0) return;

            // [수정] 백그라운드 페이지 계산도 렌더링 마진(상하 40, 좌우 80)과 일치시킵니다.
            float availableWidth = (float)AozoraTextCanvas.ActualWidth - 80;
            float availableHeight = (float)AozoraTextCanvas.ActualHeight - 40; 

            float maxWidth = _isMarkdownRenderMode ? availableWidth : Math.Min(availableWidth, (float)GetUrlMaxWidth());
            var device = AozoraTextCanvas.Device;

            try
            {
                await Task.Run(async () =>
                {
                    int pageCount = 1;
                    float currentPageHeight = 0;
                    var blockToPageMap = new Dictionary<int, int>();

                    for (int i = 0; i < _aozoraBlocks.Count; i++)
                    {
                        if (token.IsCancellationRequested) return;
                        blockToPageMap[i] = pageCount;

                        var block = _aozoraBlocks[i];

                        // 페이지 시작 부분의 빈 줄(공백) 건너뛰기 - 빈 페이지 방지
                        if (currentPageHeight == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                        {
                            blockToPageMap[i] = pageCount;
                            continue;
                        }

                        if (block.HasImage || block.IsPageBreak)
                        {
                            if (currentPageHeight > 0)
                            {
                                pageCount++;
                                currentPageHeight = 0;
                            }
                            
                            if (block.IsPageBreak)
                            {
                                // 페이지 분리 기호는 그 자체로 페이지를 차지하지 않고 다음 블록으로 넘김
                                blockToPageMap[i] = pageCount;
                                continue;
                            }

                            // 이미지 존재 여부를 확인하여 없는 경우 페이지로 계산하지 않음
                            var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                            if (aozoraImg != null && !DoesAozoraImageExist(aozoraImg.Source))
                            {
                                blockToPageMap[i] = pageCount;
                                continue;
                            }

                            // 이미지는 한 페이지 전체 차지
                            pageCount++;
                            currentPageHeight = 0;
                            continue;
                        }

                        float fontSize = (float)(_settingsManager.FontSize * block.FontSizeScale);
                        float blockHeight = MeasureHorizontalBlockHeight(device, block, maxWidth, fontSize);

                        // [수정] Tolerance는 0.8배로 타협
                        float bottomTolerance = fontSize * 0.8f;

                        if (currentPageHeight > 0 && currentPageHeight + blockHeight > (availableHeight + bottomTolerance))
                        {
                            pageCount++;
                            currentPageHeight = 0;
                            blockToPageMap[i] = pageCount;
                        }

                        currentPageHeight += blockHeight;

                        if (i % 50 == 0) await Task.Delay(1, token);
                    }

                    if (token.IsCancellationRequested) return;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _aozoraTotalPages = pageCount;
                        _isAozoraPageCalcCompleted = true;

                        if (blockToPageMap.TryGetValue(_currentAozoraStartBlockIndex, out int cp))
                            _aozoraCalculatedCurrentPage = cp;
                        else
                            _aozoraCalculatedCurrentPage = 1;

                        UpdateAozoraStatusBar();
                    });
                }, token);
            }
            catch { }
        }

        private List<AozoraBindingModel> PaginateHorizontalAozoraPage(ref int index, List<AozoraBindingModel> blocks, float availableWidth, float availableHeight, CanvasDevice? device = null)
        {
            var pageBlocks = new List<AozoraBindingModel>();
            float usedHeight = 0;

            AozoraBindingModel? currentMergedBlock = null;
            float currentMergedBlockHeight = 0;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                // [추가] 페이지 시작 시 빈 줄(공백) 건너뛰기 - 빈 페이지 방지
                if (pageBlocks.Count == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                {
                    index++;
                    continue;
                }

                if (block.HasImage || block.IsPageBreak)
                {
                    // 현재 페이지에 이미 내용이 있다면 여기서 페이지를 마침 (분리 기호/이미지는 다음 페이지로)
                    if (pageBlocks.Count > 0) break;

                    if (block.IsPageBreak)
                    {
                        // 페이지 분리 기호 그 자체로는 빈 페이지를 만들지 않고 건너뜀
                        index++;
                        continue;
                    }

                    // 이미지 존재 여부를 확인하여 없는 경우 무시
                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null && !DoesAozoraImageExist(aozoraImg.Source))
                    {
                        index++;
                        continue;
                    }

                    // 이미지는 단독 페이지로 구성
                    pageBlocks.Add(block);
                    index++;
                    currentMergedBlock = null;
                    break;
                }

                if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                {
                    var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                    tempMerged.Inlines.AddRange(block.Inlines);

                    float fontSize = (float)(_settingsManager.FontSize * tempMerged.FontSizeScale);
                    float newHeight = MeasureHorizontalBlockHeight(device, tempMerged, availableWidth, fontSize);
                    float heightDiff = newHeight - currentMergedBlockHeight;

                    // [수정] Tolerance를 0.8배로 타협
                    float bottomToleranceMerged = fontSize * 0.8f;

                    if (usedHeight + heightDiff > (availableHeight + bottomToleranceMerged) && pageBlocks.Count > 0)
                    {
                        break;
                    }

                    pageBlocks[pageBlocks.Count - 1] = tempMerged;
                    currentMergedBlock = tempMerged;
                    usedHeight += heightDiff;
                    currentMergedBlockHeight = newHeight;
                    index++;
                    continue;
                }

                float fontSizeBase = (float)(_settingsManager.FontSize * block.FontSizeScale);
                float blockHeight = MeasureHorizontalBlockHeight(device, block, availableWidth, fontSizeBase);

                // [수정] 일반 블록 보정값 타협
                float bottomTolerance = fontSizeBase * 0.8f;

                bool isKeigakomi = block.BorderThickness.Top > 0 && block.BorderThickness.Bottom > 0 && block.BorderThickness.Left > 0 && block.BorderThickness.Right > 0;
                bool wasKeigakomi = pageBlocks.Count > 0 && 
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Top > 0 && 
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Bottom > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Left > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Right > 0;

                if (isKeigakomi && !wasKeigakomi) blockHeight += 20f; // 박스 진입 마진
                if (!isKeigakomi && wasKeigakomi) blockHeight += 20f + (fontSizeBase * 2.1f); // 박스 종료 마진

                if (pageBlocks.Count > 0 && usedHeight + blockHeight > (availableHeight + bottomTolerance))
                {
                    break;
                }

                var blockCopy = AozoraParserService.CloneBlockProperties(block, true);
                pageBlocks.Add(blockCopy);
                usedHeight += blockHeight;

                if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                {
                    currentMergedBlock = blockCopy;
                    currentMergedBlockHeight = blockHeight;
                }
                else
                {
                    currentMergedBlock = null;
                }

                index++;
                // [수정] 완전히 꽉 찼을 때만 루프 종료
                if (usedHeight >= availableHeight + bottomTolerance) break;
            }
            return pageBlocks;
        }

// 테이블 내부의 **볼드체** 및 <br> 줄바꿈 파싱을 위한 헬퍼 함수
private (string text, List<(int start, int length)> boldRanges) ParseTableInline(string rawText)
{
    if (string.IsNullOrEmpty(rawText)) return (" ", new List<(int, int)>());
    var boldRanges = new List<(int, int)>();
    string text = rawText;
    
    // ✅ 추가: <br>, <br/>, <br /> 태그를 모두 찾아 실제 줄바꿈 문자(\n)로 변환합니다.
    text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

    // **텍스트** 또는 __텍스트__ 패턴 매칭
    var match = Regex.Match(text, @"(\*\*|__)(.*?)\1");
    while (match.Success)
    {
        int start = match.Index;
        string inner = match.Groups[2].Value; // ** 내부의 글씨
        // 기호(**)를 제거하고 내부 글씨만 삽입
        text = text.Remove(match.Index, match.Length).Insert(match.Index, inner);
        boldRanges.Add((start, inner.Length));
        // 변경된 문자열 기준으로 다음 기호 탐색
        match = Regex.Match(text, @"(\*\*|__)(.*?)\1");
    }
    
    return (text, boldRanges);
}

        private float MeasureHorizontalBlockHeight(CanvasDevice? device, AozoraBindingModel block, float availableWidth, float fontSize)
        {
            // 👉 줄 번호와 내부 요소 개수, 그리고 첫 요소의 해시를 조합해 고유 키 생성
            // 복제된 블록이라도 같은 내용을 담고 있다면 완벽히 캐시 적중됩니다.
            int contentHash = block.Inlines.Count > 0 ? block.Inlines[0].GetHashCode() : 0;
            var cacheKey = new BlockCacheKey(block.SourceLineNumber, block.Inlines.Count, contentHash);
            
            if (_blockMeasureCache.TryGetValue(cacheKey, out float cachedHeight))
                return cachedHeight;

            if (device == null) return fontSize * 2.0f;

            // ✅ 테이블 '행(Row) 단위' 전용 높이 측정 로직
            if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
            {
                var row = block.TableRows[0];
                int colCount = row.Count;
                if (colCount == 0) return fontSize * 2.0f;

                float tableIndent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                float colWidth = (availableWidth - tableIndent) / colCount;

                using var tableFormat = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? _settingsManager.FontFamily,
                    WordWrapping = CanvasWordWrapping.Wrap, 
                    FontWeight = (block.TableRowIndex == 0) ? Microsoft.UI.Text.FontWeights.Bold : GetFontWeightForFamily(block.FontFamily ?? _settingsManager.FontFamily)
                };

                float maxCellHeight = 0;
                foreach (var cellText in row)
                {
                    var parsed = ParseTableInline(cellText);
                    using var cellLayout = new CanvasTextLayout(device, parsed.text, tableFormat, Math.Max(10, colWidth - 20), 0.0f);
                    foreach (var r in parsed.boldRanges) 
                        cellLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);

                    float h = (float)cellLayout.LayoutBounds.Height;
                    if (h > maxCellHeight) maxCellHeight = h;
                }
                
                float rowHeight = maxCellHeight + 20f; // 해당 행의 높이 (상하 패딩 10씩)
                
                // 표의 마지막 줄인 경우에만 다음 텍스트와의 여백을 추가
                if (block.TableRowIndex == block.TableRowCount - 1) rowHeight += 20f; 

                // 👉 절대 .Count 검사 없이 바로 저장 (ConcurrentDictionary.Count는 글로벌 락을 유발함)
                _blockMeasureCache[cacheKey] = rowHeight;
                return rowHeight; 
            }

            StringBuilder sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(s);
                else if (inline is AozoraRuby ruby)
                {
                    sb.Append(ruby.BaseText);
                    if (ruby.IsBold) boldRanges.Add((start, ruby.BaseText.Length));
                }
                else if (inline is AozoraBold bold)
                {
                    sb.Append(bold.Text);
                    boldRanges.Add((start, bold.Text.Length));
                }
                else if (inline is AozoraItalic italic)
                {
                    sb.Append(italic.Text);
                    italicRanges.Add((start, italic.Text.Length));
                }
                else if (inline is AozoraCode code) sb.Append(code.Text);
                else if (inline is AozoraTCY tcy)
                {
                    sb.Append(tcy.Text);
                    if (tcy.IsBold) boldRanges.Add((start, tcy.Text.Length));
                }
                else if (inline is AozoraLineBreak) sb.Append("\n");
            }


            string text = sb.ToString();
            if (string.IsNullOrEmpty(text)) text = " ";

            // 표/코드는 줄바꿈을 꺼서 표 틀어짐 방지 (헤더도 줄간격 좁게)
            float lineSpacing = (block.IsTable || block.HeadingLevel > 0) ? fontSize * 1.3f : fontSize * 2.1f;

            using var format = new CanvasTextFormat
            {
                   FontSize = fontSize,
                   FontFamily = block.FontFamily ?? _settingsManager.FontFamily,
                   FontWeight = GetFontWeightForFamily(block.FontFamily ?? _settingsManager.FontFamily),
                   Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                   // 👉 표/코드는 줄바꿈을 꺼서 표 틀어짐 방지
                   WordWrapping = block.IsTable ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap,
                   LineSpacing = lineSpacing,
                   VerticalAlignment = CanvasVerticalAlignment.Top
            };

            float indent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
            float actualAvailableWidth = availableWidth - indent - (float)block.Margin.Right;
            if (actualAvailableWidth < 100) actualAvailableWidth = 100;

            using var layout = new CanvasTextLayout(device, text, format, actualAvailableWidth, 0.0f);
            layout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont; // 이모지 컬러 활성화

            if (block.IsBold) layout.SetFontWeight(0, text.Length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in boldRanges) layout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in italicRanges) layout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

            // lineCount × lineSpacing: 블록 내/간 줄간격을 완전 통일하는 유일한 방법.
            // 이 값을 드로우의 currentY advance와 동일하게 맞춰야 일관된 레이아웃이 보장됨.
            int lineCount = layout.LineCount;

            float result = (lineCount * lineSpacing) + (float)block.Margin.Bottom;
            if (block.IsBlankLine) result = lineSpacing * 0.3f;
            
            // 👉 절대 .Count 검사 없이 바로 저장
            _blockMeasureCache[cacheKey] = result;

            return result;
        }

        private void AozoraTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
        }

        private void AozoraTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isAozoraMode) return;

            var ds = args.DrawingSession;
            var size = sender.Size;
            Color textColor = GetVerticalTextColor(); // 색상은 수직모드 함수와 동일 (블랙, 베이지, 다크)

            ds.Clear(GetVerticalBackgroundColor());

            if (_currentAozoraPageInfo.Blocks == null || _currentAozoraPageInfo.Blocks.Count == 0) return;

            var page = _currentAozoraPageInfo;

            // [수정] 그리기 시작점도 30으로 맞춥니다.
            float marginTop = 30;
            float marginLeft = 40;

            float currentY = marginTop;
            float availableWidth = (float)size.Width - 80; // Left & Right margin (40+40)
            float maxWidth = _isMarkdownRenderMode ? availableWidth : Math.Min(availableWidth, (float)GetUrlMaxWidth());

            var imgBlocks = page.Blocks.Where(b => b.HasImage).ToList();
            if (imgBlocks.Count > 0)
            {
                var src = imgBlocks[0].Inlines.OfType<AozoraImage>().First().Source;
                DrawHorizontalImage(ds, size, src);
                return;
            }

            bool isBoxing = false;
            float boxLeft = float.MaxValue;
            float boxRight = float.MinValue;
            float boxTop = 0f;
            float boxBottom = float.MaxValue;
            Color boxColor = Colors.Gray;
            float boxPad = 20f;

            for (int i = 0; i < page.Blocks.Count; i++)
            {
                var block = page.Blocks[i];

                float fontSize = (float)(_settingsManager.FontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;

                // ✅ 통합된 테이블 그래픽 그리기 로직 (행 단위)
                if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
                {
                    var row = block.TableRows[0];
                    int colCount = row.Count;
                    int r = block.TableRowIndex;
                    bool isHeader = (r == 0);
                    // 현재 페이지의 첫 블록이거나 이전 블록이 테이블이 아니면 상단 선을 닫아줌
                    bool isFirstOnPage = (i == 0) || !page.Blocks[i - 1].IsTable;

                    float tableIndent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                    float tableMaxWidth = maxWidth - tableIndent;
                    float tableDrawX = marginLeft + tableIndent;
                    float colWidth = tableMaxWidth / colCount;

                    using var tableFormat = new CanvasTextFormat
                    {
                        FontSize = fontSize,
                        FontFamily = block.FontFamily ?? _settingsManager.FontFamily,
                        WordWrapping = CanvasWordWrapping.Wrap,
                        FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : GetFontWeightForFamily(block.FontFamily ?? _settingsManager.FontFamily)
                    };

                    // 표 맨 위쪽 가로선 (전체 표의 첫 줄이거나, 페이지가 넘어가서 새로 시작될 때)
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

                    // 배경색 칠하기
                    if (isHeader)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128));
                    else if (r % 2 == 1)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, Microsoft.UI.ColorHelper.FromArgb(10, 128, 128, 128));

                    // 텍스트 및 좌우 세로선 그리기
                    for (int c = 0; c < colCount; c++)
                    {
                        float cellX = tableDrawX + (c * colWidth);
                        ds.DrawTextLayout(cellLayouts[c], cellX + 10, currentY + 10, textColor);
                        cellLayouts[c].Dispose();
                        ds.DrawLine(cellX, currentY, cellX, currentY + rowHeight, Colors.Gray, 1f); // 좌측 세로선
                    }
                    
                    // 맨 우측 세로선 닫기
                    ds.DrawLine(tableDrawX + tableMaxWidth, currentY, tableDrawX + tableMaxWidth, currentY + rowHeight, Colors.Gray, 1f);

                    currentY += rowHeight;
                    
                    // 행 아래쪽 가로선
                    ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, isHeader ? 2f : 1f);

                    // 표의 마지막 행일 경우 여백 띄우기
                    if (r == block.TableRowCount - 1)
                        currentY += 20f; 

                    continue; 
                }

                // lineSpacing 2.1: 루비(furigana) 공간을 충분히 확보하는 일본어 표준 행간 (테이블/헤더는 좁게)
                float lineSpacing = (block.IsTable || block.HeadingLevel > 0) ? fontSize * 1.3f : fontSize * 2.1f;

                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(s);
                    else if (inline is AozoraRuby ruby)
                    {
                        sb.Append(ruby.BaseText);
                        rubyRanges.Add((start, ruby.BaseText.Length, ruby.RubyText));
                        if (ruby.IsBold) boldRanges.Add((start, ruby.BaseText.Length));
                    }
                    else if (inline is AozoraBold bold)
                    {
                        sb.Append(bold.Text);
                        boldRanges.Add((start, bold.Text.Length));
                    }
                    else if (inline is AozoraItalic italic)
                    {
                        sb.Append(italic.Text);
                        italicRanges.Add((start, italic.Text.Length));
                    }
                    else if (inline is AozoraCode code) sb.Append(code.Text);
                    else if (inline is AozoraTCY tcy)
                    {
                        sb.Append(tcy.Text);
                        if (tcy.IsBold) boldRanges.Add((start, tcy.Text.Length));
                    }
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }



                string blockText = sb.ToString();
                float indent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                float actualMaxWidth = maxWidth - indent - (float)block.Margin.Right;

                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? _settingsManager.FontFamily,
                    FontWeight = GetFontWeightForFamily(block.FontFamily ?? _settingsManager.FontFamily),
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    // 👉 표/코드는 줄바꿈 끄기
                    WordWrapping = block.IsTable ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap, 
                    LineSpacing = lineSpacing,
                    VerticalAlignment = CanvasVerticalAlignment.Top
                };

                using var textLayout = new CanvasTextLayout(ds, blockText, format, actualMaxWidth, 0.0f);
                textLayout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont; // 이모지 컬러 활성화
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

                // 측정(MeasureHorizontalBlockHeight)과 동일한 lineCount×lineSpacing 공식.
                // 블록 내 줄간격 = lineSpacing, 블록 간 간격 = lineSpacing → 모든 줄이 동일 간격.
                int lineCount = textLayout.LineCount;
                float currentBlockHeight = block.IsBlankLine
                    ? lineSpacing * 0.3f
                    : lineCount * lineSpacing;

                var bounds = textLayout.LayoutBounds;
                float drawX = marginLeft + indent;
                if (block.Alignment == TextAlignment.Center) drawX = marginLeft + (maxWidth - (float)bounds.Width) / 2;
                else if (block.Alignment == TextAlignment.Right) drawX = marginLeft + maxWidth - (float)bounds.Width - (float)block.Margin.Right;

                bool isKeigakomi = block.BorderThickness.Top > 0 && block.BorderThickness.Bottom > 0 && block.BorderThickness.Left > 0 && block.BorderThickness.Right > 0;
                float currentW = (float)bounds.Width;
                if (block.IsBlankLine && currentW < fontSize) currentW = fontSize;

                if (isKeigakomi)
                {
                    if (!isBoxing)
                    {
                        currentY += boxPad;
                        isBoxing = true;
                        boxTop = currentY;
                        boxBottom = currentY + currentBlockHeight;
                        boxLeft = drawX + (float)bounds.X;
                        boxRight = drawX + (float)bounds.X + currentW;
                        boxColor = block.BorderColor ?? Colors.Gray;
                    }
                    else
                    {
                        boxTop = Math.Min(boxTop, currentY + (float)bounds.Y);
                        boxBottom = Math.Max(boxBottom, currentY + (float)bounds.Y + currentBlockHeight);
                        boxLeft = Math.Min(boxLeft, drawX + (float)bounds.X);
                        boxRight = Math.Max(boxRight, drawX + (float)bounds.X + currentW);
                    }
                }
                else if (!isKeigakomi && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                    currentY += boxPad + lineSpacing;
                }

                // ✅ 잉크의 실제 위치(DrawBounds)를 기준으로 타이트하게 코드 블록 배경 그리기
                if (block.BackgroundColor != null)
                {
                    var drawBounds = textLayout.DrawBounds;
                    float dbTop = (float)drawBounds.Top;
                    float dbHeight = (float)drawBounds.Height;

                    // 텍스트가 비어있거나 소문자만 있어 높이가 비정상적으로 작을 때를 대비한 최소값 보정
                    if (dbHeight < fontSize) dbHeight = fontSize;
                    
                    // 모델의 Padding 값을 반영하여 여백을 결정 (기존 하드코딩 4px에서 변경)
                    float padTop = (float)block.Padding.Top;
                    float padBottom = (float)block.Padding.Bottom;
                    float padLeft = (float)block.Padding.Left;
                    float padRight = (float)block.Padding.Right;

                    float bgTop = currentY + dbTop - padTop;
                    float bgHeight = dbHeight + padTop + padBottom;

                    ds.FillRectangle(drawX - padLeft, bgTop, currentW + padLeft + padRight, bgHeight, block.BackgroundColor.Value);
                }

                // 본문 그리기
                ds.DrawTextLayout(textLayout, drawX, currentY, textColor);

                // 밑줄(헤딩) 및 좌측 선(인용구) 배경/경계 그리기
                if (!isKeigakomi && block.BorderColor != null)
                {
                    // ✅ 실제 글자 잉크가 끝나는 위치(drawBounds.Bottom)를 기준으로 선을 그어, 폰트 크기가 바뀌어도 텍스트 바로 아래에 위치하도록 합니다.
                    var drawBounds = textLayout.DrawBounds;
                    float borderBottomY = currentY + (float)drawBounds.Bottom + 4f;
                    
                    if (block.BorderThickness.Bottom > 0)
                    {
                        // 헤더 하단 실선 그리기
                        ds.DrawLine(drawX, borderBottomY, drawX + currentW, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Bottom);
                    }
                    if (block.BorderThickness.Left > 0)
                    {
                        float quoteLeft = drawX - 15;
                        float actualTextTop = (float)Math.Min(drawBounds.Top, 0);
                        // 인용구 좌측 선도 안정적으로 그립니다.
                        ds.DrawLine(quoteLeft, currentY + actualTextTop, quoteLeft, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Left);
                    }
                }

                // 루비 그리기 (가로 모드: 글자 위쪽에 표시)
                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = _settingsManager.FontFamily,
                    FontWeight = GetFontWeightForFamily(_settingsManager.FontFamily),
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };

                var rubyRenderInfos = new List<HorizontalRubyRenderInfo>();
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;

                        // charBounds.Top = 해당 라인 박스의 layout 좌표 상단 (N번째 줄이면 N*lineSpacing)
                        // lineSpacing 안의 여분 공간은 글자 아래에 쌓임(Win2D 기본 동작).
                        // 따라서 루비는 라인 박스 상단 바로 위 = charBounds.Top - rubyFontSize - gap
                        float lineBoxTop = currentY + (float)charBounds.Top;
                        float rubyY = lineBoxTop - (rubyFontSize * 3f);

                        // 루비 X 중앙 정렬
                        float charCenter = drawX + (float)charBounds.Left + (float)charBounds.Width / 2.0f;

                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, 0.0f);
                        rubyLayout.Options = Microsoft.Graphics.Canvas.Text.CanvasDrawTextOptions.EnableColorFont; // 루비 이모지 컬러 활성화
                        if (block.IsBold || boldRanges.Any(br => ruby.start >= br.start && ruby.start < br.start + br.length))
                        {
                            rubyLayout.SetFontWeight(0, ruby.rubyText.Length, Microsoft.UI.Text.FontWeights.Bold);
                        }

                        float rubyWidth = (float)rubyLayout.LayoutBounds.Width;
                        float idealLeft = charCenter - (rubyWidth / 2.0f);

                        rubyRenderInfos.Add(new HorizontalRubyRenderInfo
                        {
                            Layout = rubyLayout,
                            IdealX = idealLeft,
                            Width = rubyWidth,
                            X = idealLeft,
                            Y = rubyY
                        });
                    }
                }

                ResolveHorizontalRubyOverlaps(rubyRenderInfos);

                foreach (var info in rubyRenderInfos)
                {
                    ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor);
                    info.Layout.Dispose();
                }

                // spacing 별도 없이 lineCount×lineSpacing이 모든 줄 간격을 포함 + Margin.Bottom 추가
                currentY += currentBlockHeight + (float)block.Margin.Bottom;

                if (i == page.Blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }

        private void AozoraTextCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (AozoraTextCanvas == null || !_isAozoraMode) return;
            var pt = e.GetCurrentPoint(AozoraTextCanvas).Position;
            var width = AozoraTextCanvas.ActualWidth;

            // 가로 모드는 좌클릭/터치 시 오른쪽 화면이 다음 페이지
            if (pt.X > width / 2) NavigateAozoraPage(1);
            else NavigateAozoraPage(-1);

            e.Handled = true;
            RootGrid.Focus(FocusState.Programmatic);
        }

        private void AozoraTextCanvas_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (AozoraTextCanvas == null || !_isAozoraMode) return;
            var delta = e.GetCurrentPoint(AozoraTextCanvas).Properties.MouseWheelDelta;
            if (delta > 0) NavigateAozoraPage(-1);
            else NavigateAozoraPage(1);
            e.Handled = true;
        }

        private void NavigateAozoraPage(int direction)
        {
            if (_aozoraBlocks == null || _aozoraBlocks.Count == 0) return;

            if (direction > 0)
            {
                if (_currentAozoraEndBlockIndex < _aozoraBlocks.Count - 1)
                {
                    // 💡 History Push 완전히 제거됨
                    RenderAozoraDynamicPage(_currentAozoraEndBlockIndex + 1);
                    if (_isAozoraPageCalcCompleted) _aozoraCalculatedCurrentPage++; 
                    UpdateAozoraStatusBar();
                }
            }
            else if (direction < 0)
            {
                if (_currentAozoraStartBlockIndex > 0)
                {
                    int targetIdx = _currentAozoraStartBlockIndex;
                    int bestStart = 0;

                    float availWidth = (float)(AozoraTextCanvas?.ActualWidth ?? 1000) - 80;
                    float availHeight = (float)(AozoraTextCanvas?.ActualHeight ?? 800) - 40;
                    float maxWidth = _isMarkdownRenderMode ? availWidth : Math.Min(availWidth, (float)GetUrlMaxWidth());
                    var device = AozoraTextCanvas?.Device ?? Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();

                    // 💡 이동 전 캐시 유효성 철저히 검증
                    ValidateBackwardCache(maxWidth, availHeight, _settingsManager.FontSize, false, targetIdx);

                    lock (_backwardPageCache)
                    {
                        if (!_backwardPageCache.TryGetValue(targetIdx, out bestStart))
                        {
                            bestStart = FindPreviousPageStart(targetIdx, _aozoraBlocks, maxWidth, availHeight, device, false);
                            _backwardPageCache[targetIdx] = bestStart;
                        }
                    }

                    RenderAozoraDynamicPage(bestStart);
                    if (_isAozoraPageCalcCompleted) _aozoraCalculatedCurrentPage = Math.Max(1, _aozoraCalculatedCurrentPage - 1); 
                    UpdateAozoraStatusBar();
                }
            }
        }

        private void UpdateAozoraStatusBar()
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;

            double progress = (_currentAozoraEndBlockIndex + 1) * 100.0 / _aozoraBlocks.Count;
            if (progress > 100) progress = 100;

            int startLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;

            ImageInfoText.Text = Strings.LineInfo(startLine, _aozoraTotalLineCountInSource);
            _ = AddToRecentAsync(true);
            TextProgressText.Text = $"{progress:F1}%";

            if (_isAozoraPageCalcCompleted)
            {
                int curPage = _aozoraCalculatedCurrentPage;
                if (curPage > _aozoraTotalPages) curPage = _aozoraTotalPages;
                ImageIndexText.Text = $"{curPage} / {_aozoraTotalPages}";
            }
            else
            {
                ImageIndexText.Text = Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
            }
        }

        public void JumpToAozoraLine(int targetLine)
        {
            if (!_isTextMode || !_isAozoraMode || _aozoraBlocks.Count == 0) return;

            if (_isVerticalMode)
            {
                _ = PrepareVerticalTextAsync(targetLine);
                return;
            }

            int left = 0;
            int right = _aozoraBlocks.Count - 1;
            int startIdx = 0;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (_aozoraBlocks[mid].SourceLineNumber == targetLine)
                {
                    startIdx = mid;
                    break;
                }
                else if (_aozoraBlocks[mid].SourceLineNumber < targetLine)
                {
                    startIdx = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            RenderAozoraDynamicPage(startIdx);
            StartAozoraPageCalculationAsync(); 
        }

        private class HorizontalRubyRenderInfo
        {
            public required CanvasTextLayout Layout;
            public float IdealX;
            public float Width;
            public float X;
            public float Y;
        }

        private void ResolveHorizontalRubyOverlaps(List<HorizontalRubyRenderInfo> rubies)
        {
            if (rubies.Count == 0) return;

            int startIndex = 0;
            while (startIndex < rubies.Count)
            {
                int endIndex = startIndex;
                float currentY = rubies[startIndex].Y;

                while (endIndex + 1 < rubies.Count && Math.Abs(rubies[endIndex + 1].Y - currentY) < 2.0f)
                {
                    endIndex++;
                }

                ResolveHorizontalRubyOverlapsInRow(rubies, startIndex, endIndex);
                startIndex = endIndex + 1;
            }
        }

        private void ResolveHorizontalRubyOverlapsInRow(List<HorizontalRubyRenderInfo> rubies, int start, int end)
        {
            float prevRight = -10000f; 

            int i = start;
            while (i <= end)
            {
                float clusterSumCenter = rubies[i].IdealX + rubies[i].Width / 2.0f;
                float clusterTotalWidth = rubies[i].Width;
                int clusterCount = 1;
                int clusterEnd = i;

                while (clusterEnd + 1 <= end)
                {
                    var next = rubies[clusterEnd + 1];
                    float currentHypotheticalLeft = (clusterSumCenter / clusterCount) - (clusterTotalWidth / 2.0f);
                    float currentHypotheticalRight = currentHypotheticalLeft + clusterTotalWidth;

                    if (currentHypotheticalRight > next.IdealX)
                    {
                        clusterEnd++;
                        clusterSumCenter += (next.IdealX + next.Width / 2.0f);
                        clusterTotalWidth += next.Width;
                        clusterCount++;
                    }
                    else break;
                }

                float finalLeft = (clusterSumCenter / clusterCount) - (clusterTotalWidth / 2.0f);

                if (finalLeft < prevRight) finalLeft = prevRight;

                for (int k = i; k <= clusterEnd; k++)
                {
                    rubies[k].X = finalLeft;
                    finalLeft += rubies[k].Width;
                }

                prevRight = finalLeft;
                i = clusterEnd + 1;
            }
        }

        private bool DoesAozoraImageExist(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            try
            {
                if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    if (_knownMissingAozoraImages.Contains(relativePath)) return false;

                    // If it's in the same folder, we can check _imageEntries for faster initial feedback
                    if (_imageEntries != null && !relativePath.Contains('/') && !relativePath.Contains('\\'))
                    {
                        string? fullRemotePath = ResolveWebDavImagePath(relativePath);
                        if (fullRemotePath != null)
                        {
                            if (!_imageEntries.Any(e => e.WebDavPath == fullRemotePath ||
                                string.Equals(e.WebDavPath, fullRemotePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                return false; // Definitely missing from current folder
                            }
                        }
                    }

                    return true; // Unknown, load it async later
                }
                if (_isEpubMode && _currentEpubArchive != null)
                {
                    string normPath = relativePath.Replace('\\', '/');
                    return _currentEpubArchive.Entries.Any(e =>
                        e.FullName.Replace('\\', '/') == normPath ||
                        string.Equals(e.FullName.Replace('\\', '/'), normPath, StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextArchiveEntryKey == null)
                {
                    // Local File
                    string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentTextFilePath)!, relativePath);
                    return System.IO.File.Exists(fullPath);
                }
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
                {
                    // Archive
                    string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                    string? baseDir = "";
                    int lastSlash = normKey.LastIndexOf('/');
                    if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                    string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                    string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                    targetKey = targetKey.Replace("/./", "/");

                    if (_currentArchive != null)
                    {
                        return _currentArchive.Entries.Any(e => e.Key != null &&
                               (e.Key.Replace('\\', '/') == targetKey ||
                                string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (_current7zArchive != null)
                    {
                        return _current7zArchive.Entries.Any(e => e.FileName != null &&
                               (e.FileName.Replace('\\', '/') == targetKey ||
                                string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
            catch { }
            return false;
        }

        private void DrawHorizontalImage(CanvasDrawingSession ds, Size canvasSize, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            if (_aozoraImageCache.TryGetValue(relativePath, out var bitmap))
            {
                if (bitmap == null) return;

                float canvasW = (float)canvasSize.Width;
                float canvasH = (float)canvasSize.Height;
                float imgW = (float)bitmap.Size.Width;
                float imgH = (float)bitmap.Size.Height;

                float scale = Math.Min(canvasW / imgW, canvasH / imgH);
                float drawW = imgW * scale;
                float drawH = imgH * scale;

                float drawX = (canvasW - drawW) / 2;
                float drawY = (canvasH - drawH) / 2;

                ds.DrawImage(bitmap, new Rect(drawX, drawY, drawW, drawH));
            }
            else
            {
                _aozoraImageCache[relativePath] = null!;
                _ = LoadAozoraImageAsync(relativePath);
            }
        }

        private async Task LoadAozoraImageAsync(string relativePath)
        {
            try
            {
                byte[]? bytes = null;

                if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavItemPath))
                {
                    string? fullRemotePath = ResolveWebDavImagePath(relativePath);
                    if (fullRemotePath != null)
                    {
                        var tempPath = await _webDavService.DownloadToTempFileAsync(fullRemotePath);
                        if (!string.IsNullOrEmpty(tempPath) && System.IO.File.Exists(tempPath))
                        {
                            bytes = await System.IO.File.ReadAllBytesAsync(tempPath);
                        }
                        else
                        {
                            // Mark as missing to prevent empty page on re-calculation
                            _knownMissingAozoraImages.Add(relativePath);
                            
                            // Re-calculate pages to remove the blank page
                            DispatcherQueue.TryEnqueue(() => 
                            {
                                int currentLine = 1;
                                if (_aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                                    currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                                
                                _ = PrepareAozoraDisplayAsync(_currentTextContent, currentLine, _globalTextCts?.Token ?? default);
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(_currentTextFilePath) && _currentTextArchiveEntryKey == null)
                {
                    // Local File
                    string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentTextFilePath)!, relativePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    }
                }
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentTextArchiveEntryKey))
                {
                    // Archive
                    string normKey = _currentTextArchiveEntryKey.Replace('\\', '/');
                    string? baseDir = "";
                    int lastSlash = normKey.LastIndexOf('/');
                    if (lastSlash >= 0) baseDir = normKey.Substring(0, lastSlash);

                    string subPath = relativePath.Replace('\\', '/').TrimStart('/');
                    string targetKey = string.IsNullOrEmpty(baseDir) ? subPath : (baseDir.TrimEnd('/') + "/" + subPath);
                    targetKey = targetKey.Replace("/./", "/");

                    await _archiveLock.WaitAsync();
                    try
                    {
                        if (_currentArchive != null)
                        {
                            var entry = _currentArchive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace('\\', '/') == targetKey)
                                     ?? _currentArchive.Entries.FirstOrDefault(e => e.Key != null && string.Equals(e.Key.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase));

                            if (entry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                using var es = entry.OpenEntryStream();
                                es.CopyTo(ms);
                                bytes = ms.ToArray();
                            }
                        }
                        else if (_current7zArchive != null)
                        {
                            var entry = _current7zArchive.Entries.FirstOrDefault(e => e.FileName != null && e.FileName.Replace('\\', '/') == targetKey)
                                     ?? _current7zArchive.Entries.FirstOrDefault(e => e.FileName != null && string.Equals(e.FileName.Replace('\\', '/'), targetKey, StringComparison.OrdinalIgnoreCase));

                            if (entry != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                entry.Extract(ms);
                                bytes = ms.ToArray();
                            }
                        }
                    }
                    finally { _archiveLock.Release(); }
                }

                if (bytes != null)
                {
                    if (AozoraTextCanvas == null) return;

                    var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    using (var writer = new Windows.Storage.Streams.DataWriter(winrtStream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    winrtStream.Seek(0);

                    var device = AozoraTextCanvas.Device;
                    var bitmap = await CanvasBitmap.LoadAsync(device, winrtStream);

                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        _aozoraImageCache[relativePath] = bitmap;
                        AozoraTextCanvas.Invalidate();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAozoraImageAsync failed: {ex.Message}");
            }
        }
		
		 // TOC Handlers

        public class TocItem
        {
            public string HeadingText { get; set; } = "";
            public int SourceLineNumber { get; set; }
            public Thickness Margin => new Thickness((HeadingLevel - 1) * 16, 0, 0, 0);
            public int HeadingLevel { get; set; }
            public object? Tag { get; set; }
        }

        private async void TocButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTextMode && !_isEpubMode) return;

            // Ensure TOC Title
            if (TocFlyout.Content is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock tb)
            {
                tb.Text = Strings.TocTitle;
            }

            List<TocItem> items = new();

            if (_currentPdfDocument != null && _pdfToc.Count > 0)
            {
                items = _pdfToc.ToList();
            }
            else if ((_isAozoraMode || _isVerticalMode) && _aozoraBlocks.Count > 0)
            {
                items = _aozoraBlocks
                    .Where(b => b.HeadingLevel > 0)
                    .Select(b => new TocItem
                    {
                        HeadingText = b.HeadingText,
                        SourceLineNumber = b.SourceLineNumber,
                        HeadingLevel = b.HeadingLevel
                    })
                    .ToList();
            }

            else if (_isEpubMode)
            {
                // EPUB Mode
                if (_epubToc != null && _epubToc.Count > 0)
                {
                    items = _epubToc.Select(t => new TocItem
                    {
                        HeadingText = t.Title,
                        HeadingLevel = t.Level > 0 ? t.Level : 1,
                        SourceLineNumber = -1,
                        Tag = t
                    }).ToList();
                }
            }
            else if (!string.IsNullOrEmpty(_currentTextContent))
            {
                // Scan raw text on demand for Normal Mode or if blocks are empty
                items = await Task.Run(() =>
                {
                    var list = new List<TocItem>();
                    var lines = _textLines.Count > 0 ? _textLines : SplitTextToLines(_currentTextContent); // Prefer split lines if available

                    // In standard mode, finding exact source line number might be tricky if _textLines is wrapped.
                    // But _textLines usually stores 1:1 if no wrap? No, MainWindow.text.cs implementation of SplitTextToLines:
                    // Wait, SplitTextToLines splits by wrapping width? 
                    // Let's check SplitTextToLines implementation in MainWindow.text.cs.
                    // If SplitTextToLines wraps connected lines, SourceLineNumber logic is complex.
                    // A safe bet is scanning the raw source lines.
                    var rawLines = _currentTextContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                    for (int i = 0; i < rawLines.Length; i++)
                    {
                        var line = rawLines[i].Trim();
                        int level = 0;
                        string text = "";

                        // Check Aozora
                        if (line.Contains("［＃大見出し］") || line.StartsWith("# ")) { level = 1; text = line.Replace("［＃大見出し］", "").TrimStart('#', ' '); }
                        else if (line.Contains("［＃中見出し］") || line.StartsWith("## ")) { level = 2; text = line.Replace("［＃中見出し］", "").TrimStart('#', ' '); }
                        else if (line.Contains("［＃小見出し］") || line.StartsWith("### ")) { level = 3; text = line.Replace("［＃小見出し］", "").TrimStart('#', ' '); }

                        if (level > 0)
                        {
                            // Clean tags
                            text = Regex.Replace(text, @"［＃[^］]+］|\[.*?\]|[#]", "").Trim();
                            list.Add(new TocItem { HeadingText = text, SourceLineNumber = i + 1, HeadingLevel = level });
                        }
                    }
                    return list;
                });
            }

            // Highlight current item and scroll
            int currentIndex = -1;

            if (_isEpubMode)
            {
                if (_currentEpubChapterIndex >= 0 && _currentEpubChapterIndex < _epubSpine.Count)
                {
                    string currentSpinePath = _epubSpine[_currentEpubChapterIndex];
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Tag is EpubTocItem epi)
                        {
                            string linkPath = epi.Link;
                            int hashIndex = linkPath.IndexOf('#');
                            if (hashIndex >= 0) linkPath = linkPath.Substring(0, hashIndex);

                            if (string.Equals(linkPath, currentSpinePath, StringComparison.OrdinalIgnoreCase))
                            {
                                currentIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
            else if (_currentPdfDocument != null && items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= _currentIndex)
                        currentIndex = i;
                    else
                        break;
                }
            }
            else
            {
                // Text / Aozora Mode
                int currentLine = 1;
                if (_isVerticalMode)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                }
                else if (_isAozoraMode)
                {
                    if (_currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                        currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }
                else
                {
                    currentLine = GetTopVisibleLineIndex();
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= currentLine)
                        currentIndex = i;
                    else
                        break;
                }
            }

            if (currentIndex >= 0 && currentIndex < items.Count)
            {
                items[currentIndex].HeadingText = "⮕ " + items[currentIndex].HeadingText;
            }

            if (items.Count == 0)
            {
                items.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
            }

            TocListView.ItemsSource = items;

            if (currentIndex >= 0)
            {
                // Ensure layout updated before scrolling
                this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        TocListView.ScrollIntoView(items[currentIndex], ScrollIntoViewAlignment.Leading);
                    }
                    catch { }
                });
            }
        }

        private void TocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                TocFlyout.Hide();

                if (_isEpubMode)
                {
                    if (item.Tag is EpubTocItem epubItem)
                    {
                        JumpToEpubTocItem(epubItem);
                    }
                }
                else if (item.Tag?.ToString() == "PDF")
                {
                    if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageEntries.Count)
                    {
                        _currentIndex = item.SourceLineNumber;
                        _ = DisplayCurrentImageAsync();
                    }
                }
                else if (item.SourceLineNumber > 0)
                {
                    if (_isVerticalMode)
                    {
                        _ = PrepareVerticalTextAsync(item.SourceLineNumber);
                    }
                    else if (_isAozoraMode)
                    {
                        JumpToAozoraLine(item.SourceLineNumber);
                    }
                    else
                    {
                        ScrollToLine(item.SourceLineNumber);
                    }
                }
            }
        }
    }
}