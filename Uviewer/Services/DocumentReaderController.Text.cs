using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    internal sealed partial class DocumentReaderController
    {
        internal readonly TextReaderState _textReaderState = new();
        internal readonly TextPreviewKeyNavigationService _textPreviewKeyNavigationService = new();
        internal List<TextLine> _textLines
        {
            get => _textReaderState.Lines;
            set => _textReaderState.Lines = value ?? new List<TextLine>();
        }

        internal string _currentTextContent
        {
            get => _textReaderState.Content;
            set => _textReaderState.Content = value ?? string.Empty;
        }

        internal TextSettingsManager _settingsManager = null!;
        internal bool _isTextMode = false;
        internal bool _isCurrentTextPlainModeLocked = false;
        private int _textContentLoadGeneration;
        private RangeObservableCollection<TextLine>? _progressiveTextItems;
        private const int PlainTextLockedPrecisePaginationLineLimit = 10000;
        private const int PlainTextLockedPrecisePaginationCharacterLimit = 2_000_000;
        internal int _textTotalLineCountInSource
        {
            get => _textReaderState.TotalLineCountInSource;
            set => _textReaderState.TotalLineCountInSource = value;
        }

        internal bool _isTextLinesFullyLoaded
        {
            get => _textReaderState.LinesFullyLoaded;
            set => _textReaderState.LinesFullyLoaded = value;
        }

        internal CancellationTokenSource? _globalTextCts => _textReaderState.GlobalCts;
        internal bool _textInputInitialized = false;
        internal string? _currentTextFilePath
        {
            get => _textReaderState.FilePath;
            set => _textReaderState.FilePath = value;
        }

        internal string? _currentTextArchiveEntryKey
        {
            get => _textReaderState.ArchiveEntryKey;
            set => _textReaderState.ArchiveEntryKey = value;
        }

        internal int _lastRecentSaveLine
        {
            get => _textReaderState.LastRecentSaveLine;
            set => _textReaderState.LastRecentSaveLine = value;
        }

        internal async void EncodingItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string tag)
            {
                await ApplyEncodingSelectionAsync(tag);
            }
        }

        internal async Task ApplyEncodingSelectionAsync(string tag)
        {
            try
            {
                _settingsManager.EncodingName = tag;

                MainToolbar.SetEncodingSelection(tag);

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
                else if (_currentTextArchiveEntryKey != null && _archiveSession.HasArchive)
                {
                    try
                    {
                        var entry = new ImageEntry { ArchiveEntryKey = _currentTextArchiveEntryKey, DisplayName = FileNameText.Text };
                        await LoadTextFromArchiveEntryAsync(entry);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in EncodingItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal void InitializeText()
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

        internal void CancelAndResetGlobalTextCts()
        {
            try
            {
                _textReaderState.CancelGlobalLoad();
                _textReaderState.CancelPageCalculation(); // [추가] 모드 전환 시 뒤에서 돌고 있는 페이지 계산 작업 강제 종료
            }
            catch { }
            _textReaderState.RestartGlobalLoad();
        }

        internal async Task LoadTextFileAsync(StorageFile file)
        {
            // Save position of current file before switching
            await AddToRecentAsync(true);

            int loadGeneration = BeginTextContentLoad();
            _isNavigatingRecent = true; // [추가] 로드 및 위치 복원 완료 전까지 자동 저장 차단
            try
            {
                InitializeText();
                _textReaderState.SetLocalSource(file.Path);
                // No reset here, DisplayLoadedText will handle it after using the value

                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                SwitchToTextMode();
                string content = await _textDocumentLoadService.ReadLocalFileAsync(file, _settingsManager.EncodingName, token);
                if (token.IsCancellationRequested) return;

                await DisplayLoadedText(content, file.Name, file.Path, token);

                if (token.IsCancellationRequested) return;
                await RevealTextContentAsync(loadGeneration, token);
                SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
            }
            catch (Exception ex)
            {
                RevealTextContentAfterFailure(loadGeneration);
                FileNameText.Text = $"텍스트 로드 실패: {ex.Message}";
            }
            finally
            {
                _isNavigatingRecent = false;
            }
        }

        internal async Task LoadTextEntryAsync(ImageEntry entry)
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

        internal async Task LoadTextFromArchiveEntryAsync(ImageEntry entry)
        {
            int loadGeneration = BeginTextContentLoad();
            _isNavigatingRecent = true; // [추가] 로드 및 위치 복원 완료 전까지 자동 저장 차단
            try
            {
                InitializeText();
                _textReaderState.SetArchiveSource(entry.ArchiveEntryKey); // Store entry key for relative path resolution
                                                                          // No reset here, DisplayLoadedText will handle it

                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                SwitchToTextMode();
                string content = await _textDocumentLoadService.ReadArchiveEntryAsync(entry, _settingsManager.EncodingName, token);

                if (token.IsCancellationRequested) return;
                await DisplayLoadedText(content, entry.DisplayName, null, token);

                if (token.IsCancellationRequested) return;
                await RevealTextContentAsync(loadGeneration, token);
                SyncSidebarSelection(entry);
            }
            catch (Exception ex)
            {
                RevealTextContentAfterFailure(loadGeneration);
                FileNameText.Text = $"아카이브 텍스트 로드 실패: {ex.Message}";
            }
            finally
            {
                _isNavigatingRecent = false;
            }
        }

        internal async Task DisplayLoadedText(string content, string name, string? uniquePath = null, CancellationToken token = default)
        {
            var preparedText = _textDisplayPreparationService.Prepare(content, name);
            content = preparedText.Content;
            _isCurrentTextPlainModeLocked = RequiresPlainTextModeLock(name, uniquePath);
            if (_isCurrentTextPlainModeLocked)
            {
                ApplyPlainTextModeLock();
            }
            else
            {
                MainToolbar.SetAozoraToggleState(isChecked: _isAozoraMode, isEnabled: true);
            }

            _currentTextContent = content; // Save for reload
            _tocService.SetProvider(new TextTocProvider(content));
            _ = _tocService.LoadTocAsync(token);

            _aozoraBlocks.Clear(); // [핵심 수정] 새 파일을 로드할 때 이전 파일의 블록 캐시를 제거합니다.
            _aozoraParseGeneration++;
            _isAozoraParsePartial = false;
            _imageResourceService.ClearTextEntries(); // 텍스트 이미지 캐시 및 누락 목록 초기화

            // [추가] 이전 파일의 스크롤 추적 기록을 초기화하여 엉뚱한 위치가 자동 저장되는 것을 방지합니다.
            _lastRecentSaveLine = -1;

            _isMarkdownRenderMode = preparedText.IsMarkdownRenderMode;
            bool canUseVerticalMode = preparedText.CanUseVerticalMode && !_isCurrentTextPlainModeLocked;
            MainToolbar.SetVerticalToggleState(
                isChecked: canUseVerticalMode && _isVerticalMode,
                isEnabled: canUseVerticalMode);

            // Unified Target Line Logic
            int targetLine = 1;
            int targetBlockIdx = -1;
            if (_aozoraPendingTargetLine > 0)
            {
                targetLine = _aozoraPendingTargetLine;
                _aozoraPendingTargetLine = 0; // Reset to 0 instead of 1 to avoid conflicts
                targetBlockIdx = _aozoraPendingTargetBlockIndex;
                _aozoraPendingTargetBlockIndex = -1; // Reset
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

            if (_isVerticalMode && !_isMarkdownRenderMode)
            {
                if (TextArea != null) TextArea.Background = _settingsManager.GetThemeBackground();
                await PrepareVerticalTextAsync(targetLine, targetBlockIdx, token);
                if (token.IsCancellationRequested) return;
                if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Visible;

                FileNameText.Text = FileExplorerService.GetFormattedDisplayName(name, _currentTextArchiveEntryKey != null);
                UpdateTextStatusBar();
                return;
            }

            if (_isAozoraMode)
            {
                // Use page-based container display with target line restoration
                await PrepareAozoraDisplayAsync(content, targetLine, targetBlockIdx, token);
                if (token.IsCancellationRequested) return;
                if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Visible;

                FileNameText.Text = FileExplorerService.GetFormattedDisplayName(name, _currentTextArchiveEntryKey != null);
            }
            else
            {
                // Ensure default template
                if (TextItemsRepeater != null && RootGrid.Resources.TryGetValue("TextItemTemplate", out var template))
                {
                    TextItemsRepeater.ItemTemplate = (DataTemplate)template;
                }

                // [핵심 수정] ScrollToLine이 정상 작동하도록 가상화 컨테이너를 먼저 화면에 표시(Visible)합니다.
                if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;

                // Progressive loading for large files
                await LoadTextLinesProgressivelyAsync(content, targetLine, token);

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

        private int BeginTextContentLoad()
        {
            int generation = ++_textContentLoadGeneration;
            if (TextArea != null) TextArea.Opacity = 0;
            return generation;
        }

        private async Task RevealTextContentAsync(int generation, CancellationToken token)
        {
            // Plain text needs one frame after ScrollToLine, while Win2D modes need one
            // frame after Invalidate, before an old/top-of-document buffer is hidden.
            await Task.Delay(16, token);
            if (!token.IsCancellationRequested && generation == _textContentLoadGeneration && TextArea != null)
            {
                TextArea.Opacity = 1;
            }
        }

        private void RevealTextContentAfterFailure(int generation)
        {
            if (generation == _textContentLoadGeneration && TextArea != null)
            {
                TextArea.Opacity = 1;
            }
        }

        internal bool IsPlainTextModeLockedDocumentActive() => _isTextMode && _isCurrentTextPlainModeLocked;

        internal void ApplyPlainTextModeLock()
        {
            _isAozoraMode = false;
            _isVerticalMode = false;
            _aozoraPendingTargetLine = 0;
            _aozoraPendingTargetBlockIndex = -1;
            _pendingVerticalScrollLine = null;
            _pendingVerticalStartBlockIndex = -1;

            _aozoraPageCalcCts?.Cancel();
            _verticalPageCalcCts?.Cancel();
            _currentVerticalRenderCts?.Cancel();
            _textReaderState.CancelPageCalculation();
            ClearBackwardCache();

            if (_verticalKeyAttached && RootGrid != null)
            {
                RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                _verticalKeyAttached = false;
            }

            MainToolbar.SetAozoraToggleState(isChecked: false, isEnabled: false);
            MainToolbar.SetVerticalToggleState(isChecked: false, isEnabled: false);

            if (AozoraTextCanvas != null) AozoraTextCanvas.Visibility = Visibility.Collapsed;
            if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
            if (TextScrollViewer != null) TextScrollViewer.Visibility = Visibility.Visible;
        }

        private static bool RequiresPlainTextModeLock(string name, string? uniquePath)
        {
            string source = !string.IsNullOrWhiteSpace(uniquePath) ? uniquePath : name;
            string extension = Path.GetExtension(source);
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".toml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
        }

        internal int GetSavedStartLine(string name, string? path)
        {
            return _textResumeService.GetSavedStartLine(
                _recentService.RecentItems,
                _favoritesService.Favorites,
                name,
                path);
        }

        internal async Task RestoreTextPositionAsync(string name)
        {
            // For Aozora mode, position is now restored progressively via targetLine in PrepareAozoraDisplayAsync
            if (_isAozoraMode) return;

            try
            {
                // Wait for layout update for normal mode
                await Task.Delay(100);

                if (TextScrollViewer != null)
                {
                    var recent = _recentService.RecentItems.OrderByDescending(r => r.AccessedAt).FirstOrDefault(r => r.Name == name);
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

        internal void SwitchToTextMode()
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
            MainToolbar.SetImageToolbarVisible(false);
            MainToolbar.SetTextToolbarVisible(true);
            MainToolbar.SetSideBySideToolbarVisible(false);
            MainToolbar.SetSharpenControlsVisible(true);

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

        internal void LoadTextSettings()
        {
            if (_settingsManager == null)
            {
                _settingsManager = new TextSettingsManager(GetTextSettingsFilePath());
            }

            _settingsManager.Load();

            _isVerticalMode = _settingsManager.IsVerticalMode;
            MainToolbar.SetVerticalToggleState(isChecked: _isVerticalMode);

            MainToolbar.SetTextSizeLevel(_settingsManager.FontSize);

            if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily))
            {
                this.DispatcherQueue.TryEnqueue(() => SetUiFont(_settingsManager.UIFontFamily));
            }

            try
            {
                ApplyLanguage(_settingsManager.Language);
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(100);
                    Strings.Reload();
                    ApplyLocalization();
                    UpdateLanguageMenuCheckmark();
                    UpdateFontSettingsMenu();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language load error: {ex.Message}");
            }

            LoadAozoraSettings(); // Load Aozora settings
        }

        internal void SaveTextSettings()
        {
            if (_settingsManager != null)
            {
                if (!_isCurrentTextPlainModeLocked)
                {
                    _settingsManager.IsVerticalMode = _isVerticalMode;
                }

                _settingsManager.Save();
            }
        }

        internal void SwitchToImageMode()
        {
            _isTextMode = false;
            _isEpubMode = false; // Reset Epub mode
            DisableVerticalModeForImageDocument();

            ImageArea.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Collapsed;
            VerticalTextCanvas.Visibility = Visibility.Collapsed;

            MainToolbar.SetImageToolbarVisible(true);
            MainToolbar.SetTextToolbarVisible(false);
            MainToolbar.SetSideBySideToolbarVisible(_currentPdfDocument == null);
            MainToolbar.SetSharpenControlsVisible(_currentPdfDocument == null);

            // 핀치 줌과 스와이프를 위해 조작 모드 활성화 (PDF 및 일반 이미지 모두)
            ImageArea.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.All;
            if (MainCanvas != null) MainCanvas.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.All;

            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();
        }

        internal void DisableVerticalModeForImageDocument()
        {
            _isVerticalMode = false; // Images and PDFs always use horizontal/image layout.

            if (_verticalKeyAttached && RootGrid != null)
            {
                RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                _verticalKeyAttached = false;
            }

            MainToolbar.SetVerticalToggleState(isChecked: false);
            if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
        }

        internal void CloseCurrentText()
        {
            _textReaderState.ClearDocument();
            _aozoraBlocks.Clear();
            _aozoraParseGeneration++;
            _isAozoraParsePartial = false;
        }

        /// <summary>
        /// Progressive loading for simple text mode - loads initial chunk first, then rest in background
        /// </summary>
        internal async Task LoadTextLinesProgressivelyAsync(string content, int targetLine = 1, CancellationToken token = default)
        {
            var loadPlan = await _textLineLoadService.CreatePlanAsync(
                content,
                targetLine,
                _isCurrentTextPlainModeLocked,
                token);
            _textTotalLineCountInSource = loadPlan.TotalLineCount;
            _isTextLinesFullyLoaded = false;

            var brush = _settingsManager.GetThemeForeground();
            var maxW = GetUrlMaxWidth();
            var lineStyle = new TextLineStyle(
                _settingsManager.FontSize,
                _settingsManager.FontFamily,
                brush,
                maxW);

            if (loadPlan.RequiresProgressiveLoad)
            {
                _textLines = await _textLineLoadService.CreateInitialLinesAsync(loadPlan, lineStyle, token);

                if (TextItemsRepeater != null)
                {
                    TextItemsRepeater.ItemsSource = null;

                    if (targetLine <= 1 && TextScrollViewer != null)
                    {
                        TextScrollViewer.ChangeView(null, 0, null, true);
                    }

                    _progressiveTextItems = new RangeObservableCollection<TextLine>(_textLines);
                    TextItemsRepeater.ItemsSource = _progressiveTextItems;
                    if (targetLine > 1)
                    {
                        await Task.Delay(50);
                        await ScrollToLineAsync(targetLine);
                    }
                }
                if (TextArea != null) TextArea.Background = _settingsManager.GetThemeBackground();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        var restTextLines = _textLineLoadService.CreateRemainingLines(loadPlan, lineStyle, token);

                        if (token.IsCancellationRequested) return;

                        if (targetLine > 1) await Task.Delay(400, token);

                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            if (_isAozoraMode) return; 

                            _textLines.AddRange(restTextLines);
                            _isTextLinesFullyLoaded = true;

                            if (TextItemsRepeater != null &&
                                _progressiveTextItems != null &&
                                ReferenceEquals(TextItemsRepeater.ItemsSource, _progressiveTextItems))
                            {
                                // Append notification preserves realized elements and
                                // the ScrollViewer offset; no hide/rebind/restore cycle.
                                _progressiveTextItems.AddRange(restTextLines);
                            }

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
                _textLines = await _textLineLoadService.CreateAllLinesAsync(loadPlan, lineStyle, token);
                _isTextLinesFullyLoaded = true;
                _progressiveTextItems = null;

                if (TextItemsRepeater != null)
                {
                    TextItemsRepeater.ItemsSource = null;

                    if (targetLine <= 1 && TextScrollViewer != null)
                    {
                        TextScrollViewer.ChangeView(null, 0, null, true);
                    }

                    TextItemsRepeater.ItemsSource = _textLines;
                    if (targetLine > 1)
                    {
                        await Task.Delay(50);
                        await ScrollToLineAsync(targetLine);
                    }
                }
                if (TextArea != null) TextArea.Background = _settingsManager.GetThemeBackground();
            }

            StartPageCalculationAsync();
            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
        }

        internal double GetUrlMaxWidth()
        {
            return _textLineLayoutService.CalculateReadableMaxWidth(
                TextArea?.ActualWidth ?? 0,
                _settingsManager.FontSize);
        }

        internal Windows.UI.Text.FontWeight GetFontWeightForFamily(string fontFamily)
        {
            return _textLineLayoutService.GetFontWeightForFamily(fontFamily);
        }

        internal async Task RefreshTextDisplay(bool resetScroll = false)
        {
            if (_isEpubMode)
            {
                TriggerEpubResize();
                return;
            }
            if (_isVerticalMode)
            {
                if (TextArea != null) TextArea.Background = _settingsManager.GetThemeBackground();
                TriggerVerticalResize();
                return;
            }

            if (_isAozoraMode && !string.IsNullOrEmpty(_currentTextContent))
            {
                // [핵심 수정] 잦은 클릭 중 위치를 잃지 않도록 이미 펜딩 중인 타겟이 있다면 그것을 계속 사용합니다.
                if (resetScroll)
                {
                    _aozoraPendingTargetLine = 1;
                }
                else if (_aozoraPendingTargetLine <= 0)
                {
                    // 현재 UI가 유효할 때만 캡처 (리셋된 0번 블록을 캡처하는 것 방지)
                    if (_aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                    {
                        _aozoraPendingTargetLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                    }
                }

                // [추가] 이전 렌더링 작업이 있다면 취소하여 레이스 컨디션 방지
                CancelAndResetGlobalTextCts();
                var token = _globalTextCts!.Token;

                // Re-calculate pages with new font size/settings
                // targetLine 인자로 _aozoraPendingTargetLine을 명시적으로 전달
                await PrepareAozoraDisplayAsync(_currentTextContent, _aozoraPendingTargetLine, -1, token);

                if (TextArea != null)
                    TextArea.Background = _settingsManager.GetThemeBackground();

                return;
            }

            // Store current scroll ratio before updating
            double scrollRatio = 0;
            if (TextScrollViewer != null && TextScrollViewer.ScrollableHeight > 0)
            {
                scrollRatio = TextScrollViewer.VerticalOffset / TextScrollViewer.ScrollableHeight;
            }

            // Apply current settings to all lines - process in background for large files
            var brush = _settingsManager.GetThemeForeground();
            var bg = _settingsManager.GetThemeBackground();
            var maxW = GetUrlMaxWidth();
            var lineStyle = new TextLineStyle(
                _settingsManager.FontSize,
                _settingsManager.FontFamily,
                brush,
                maxW);

            await _textLineLayoutService.UpdateLinesAsync(_textLines, lineStyle);

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

        internal void ColorsMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowColorPickerDialog();
        }

        internal async void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string lang)
            {
                await ApplyLanguageSelectionAsync(lang);
            }
        }

        internal async Task ApplyLanguageSelectionAsync(string lang)
        {
            try
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LanguageItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        internal void ApplyLanguage(string lang)
        {
            _textUiSettingsService.ApplyLanguage(_settingsManager, lang);
        }

        internal void UpdateLanguageMenuCheckmark()
        {
            string current = _settingsManager.Language;
            if (string.IsNullOrEmpty(current)) current = "Auto";

            MainToolbar.SetLanguageSelection(current);
        }


        internal async Task ShowColorPickerDialog()
        {
            var currentBg = _settingsManager.CustomBackgroundColor ?? ((SolidColorBrush)_settingsManager.GetThemeBackground()).Color;
            var currentFg = _settingsManager.CustomForegroundColor ?? ((SolidColorBrush)_settingsManager.GetThemeForeground()).Color;

            _isColorPickerOpen = true;
            try
            {
                var result = await _textDialogService.ShowColorPickerAsync(currentBg, currentFg);
                if (result.HasValue)
                {
                    _settingsManager.CustomBackgroundColor = result.Value.bg;
                    _settingsManager.CustomForegroundColor = result.Value.fg;
                    _settingsManager.ThemeIndex = 3; // Custom
                    SaveTextSettings();
                    await RefreshTextDisplay();
                }
            }
            finally
            {
                _isColorPickerOpen = false;
            }
        }

        internal void FontToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _textReaderSettingsController.FontToggleButton_Click(sender, e);
        }

        internal void FontMenu_Click(object sender, RoutedEventArgs e)
        {
            _textReaderSettingsController.FontMenu_Click(sender, e);
        }

        internal Task ShowFontPickerDialog() => _textReaderSettingsController.ShowFontPickerDialog();

        internal void SetTextFont(string fontFamily) => _textReaderSettingsController.SetTextFont(fontFamily);

        internal void UiFontMenu_Click(object sender, RoutedEventArgs e)
        {
            _textReaderSettingsController.UiFontMenu_Click(sender, e);
        }

        internal Task ShowUiFontPickerDialog() => _textReaderSettingsController.ShowUiFontPickerDialog();

        internal void SetUiFont(string fontFamily) => _textReaderSettingsController.SetUiFont(fontFamily);

        internal UiFontApplyTargets CreateUiFontApplyTargets()
        {
            return new UiFontApplyTargets(
                Controls: new Control?[]
                {
                    RootFontControl,
                    FileListView,
                    FileGridView,
                    SidebarFavoritesPivot
                },
                TextBlocks: new TextBlock?[]
                {
                    CurrentPathText,
                    NotificationText,
                    FileNameText
                },
                MainToolbar: MainToolbar,
                ThemeRefreshRoot: RootGrid,
                RefreshDynamicItems: () =>
                {
                    UpdateFavoritesMenu();
                    UpdateRecentMenu();
                    UpdateWebDavServerList();
                });
        }



        internal void ToggleFont() => _textReaderSettingsController.ToggleFont();

        internal void UpdateFontSettingsMenu() => _textReaderSettingsController.UpdateFontSettingsMenu();

        internal void SetDefaultFont1MenuItem_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.SetDefaultFont1MenuItem_Click(sender, e);

        internal void SetDefaultFont2MenuItem_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.SetDefaultFont2MenuItem_Click(sender, e);

        internal void ResetDefaultFontsMenuItem_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.ResetDefaultFontsMenuItem_Click(sender, e);

        internal Task ShowFontPickerDialogForDefault(int slot) =>
            _textReaderSettingsController.ShowFontPickerDialogForDefault(slot);

        internal void TextSizeUpButton_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.TextSizeUpButton_Click(sender, e);

        internal void IncreaseTextSize() => _textReaderSettingsController.IncreaseTextSize();

        internal void TextSizeDownButton_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.TextSizeDownButton_Click(sender, e);

        internal void DecreaseTextSize() => _textReaderSettingsController.DecreaseTextSize();

        internal void ThemeToggleButton_Click(object sender, RoutedEventArgs e) =>
            _textReaderSettingsController.ThemeToggleButton_Click(sender, e);

        internal void ToggleTheme() => _textReaderSettingsController.ToggleTheme();

        internal void GoToPageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowGoToLineDialog();
        }

        internal async void RootGrid_Text_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            await _textPreviewKeyNavigationService.HandleAsync(
                e,
                CreateTextPreviewKeyNavigationContext());
        }

        internal TextPreviewKeyNavigationContext CreateTextPreviewKeyNavigationContext()
        {
            return new TextPreviewKeyNavigationContext
            {
                IsTextMode = _isTextMode,
                IsVerticalMode = _isVerticalMode,
                IsAozoraMode = _isAozoraMode,
                IsMarkdownRenderMode = _isMarkdownRenderMode,
                ShouldInvertControls = ShouldInvertControls,
                AozoraBlockCount = _aozoraBlocks.Count,
                GoToVerticalStartAsync = async () =>
                {
                    await RenderVerticalDynamicPageAsync(0);
                    UpdateTextStatusBar();
                },
                GoToVerticalEndAsync = async () =>
                {
                    await RenderVerticalDynamicPageAsync(999999);
                    UpdateTextStatusBar();
                },
                GoToAozoraStartAsync = async () =>
                {
                    await NavigateToAozoraLineAsync(1);
                },
                GoToAozoraEndAsync = async () =>
                {
                    int lastIdx = Math.Max(0, _aozoraBlocks.Count - 5);
                    await RenderAozoraDynamicPage(lastIdx);
                    UpdateAozoraStatusBar();
                },
                GoToTextStart = () => TextScrollViewer?.ChangeView(null, 0, null),
                GoToTextEnd = () => TextScrollViewer?.ChangeView(null, TextScrollViewer.ExtentHeight, null),
                ShowGoToLineDialog = () => _ = ShowGoToLineDialog(),
                NavigateVerticalPage = NavigateVerticalPage,
                NavigateAozoraPage = NavigateAozoraPage,
                NavigateTextPage = NavigateTextPage,
                IncreaseTextSize = IncreaseTextSize,
                DecreaseTextSize = DecreaseTextSize,
                ToggleAozoraMode = ToggleAozoraMode,
                ToggleVerticalModeAsync = ToggleVerticalModeFromShortcutAsync,
                ToggleFont = ToggleFont,
                ToggleSidebar = ToggleSidebar,
                ToggleTheme = ToggleTheme,
                ToggleGlobalTheme = () => _windowShellController.ToggleGlobalTheme()
            };
        }

        internal async Task ToggleVerticalModeFromShortcutAsync()
        {
            if (IsPlainTextModeLockedDocumentActive())
            {
                ApplyPlainTextModeLock();
                return;
            }

            await AddToRecentAsync(true);
            _isVerticalMode = !_isVerticalMode;
            MainToolbar.SetVerticalToggleState(isChecked: _isVerticalMode);
            SaveTextSettings();
            ToggleVerticalMode();
        }

        internal async Task ShowGoToLineDialog()
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
                totalLines = _textBlockDocumentService.CountNormalizedLines(_currentTextContent);
                _textTotalLineCountInSource = totalLines;
                currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
            }
            else if (TextScrollViewer != null)
            {
                totalLines = _textLines.Count;
                currentLine = GetTopVisibleLineIndex();
            }

            if (currentLine < 1) currentLine = 1;

            var result = await _textDialogService.ShowGoToLineAsync(currentLine, totalLines, title);
            if (result.HasValue)
            {
                await GoToLine(result.Value.ToString());
            }
        }

        internal async Task GoToLine(string lineText)
        {
            if (!int.TryParse(lineText, out int line) || line < 1) return;

            if (_currentPdfDocument != null)
            {
                int pageIndex = line - 1;
                if (pageIndex >= 0 && pageIndex < (int)_currentPdfDocument.PageCount)
                {
                    _currentIndex = pageIndex;
                    await DisplayCurrentImageAsync();
                }
                return;
            }

            if (_isVerticalMode)
            {
                await PrepareVerticalTextAsync(line);
                return;
            }

            if (_isAozoraMode && _aozoraBlocks.Count > 0)
            {
                await NavigateToAozoraLineAsync(line);
            }
            else if (TextScrollViewer != null)
            {
                ScrollToLine(line);
                UpdateTextStatusBar();
            }
        }

        internal void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (_isAozoraMode)
            {
                return;
            }

            if (args.Element is TextBlock tb && _textLines.Count > args.Index)
            {
                var line = _textLines[args.Index];
                _textLinePresenterService.ApplyToTextBlock(tb, line, args.Index + 1, ApplySearchHighlightsToTextBlock);
            }
        }
        // --- Input Handling ---

        internal void TextArea_PointerPressed(object sender, PointerRoutedEventArgs e)
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

        internal void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
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

        internal void NavigateTextPage(int direction)
        {
            if (TextScrollViewer == null) return;

            _textViewportService.NavigatePage(TextScrollViewer, _settingsManager.FontSize, direction);
            UpdateTextStatusBar();
        }

        internal void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null)
        {
            if (!_isTextMode && !_isEpubMode) return;
            if (_isVerticalMode) { UpdateVerticalStatusBar(); return; }
            if (_isAozoraMode) { UpdateAozoraStatusBar(); return; }
            if (_isEpubMode) { UpdateEpubStatus(); return; }

            if (TextScrollViewer == null) return;

            _readingProgressController.UpdatePlainText(
                fileName,
                _currentTextArchiveEntryKey != null,
                totalLines,
                _textReaderState,
                TextScrollViewer,
                GetTopVisibleLineIndex());
        }

        internal void TextScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateTextStatusBar();
        }

        internal void TextScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-calc max width if needed, but it is bound to line prop.
            if (_isTextMode && !_isAozoraMode)
            {
                StartPageCalculationAsync();
            }
        }

        internal void TextArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Handle TextArea resize for window drag resizing in Aozora mode.
            // AozoraTextCanvas_SizeChanged handles the canvas itself;
            // this covers the outer Grid container resize event wired in XAML.
            if (!_isAozoraMode) return;
            if (AozoraTextCanvas != null)
            {
                _ = RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                StartAozoraPageCalculationAsync();
            }
        }

        internal async void StartPageCalculationAsync()
        {
            try
            {
                if (_isAozoraMode) return;
                if (TextScrollViewer == null) return;
                if (ShouldSkipPrecisePlainTextPagination())
                {
                    _textReaderState.CancelPageCalculation();
                    _textPageCalculationService.CompleteFallback(_textReaderState, TextScrollViewer);
                    UpdateTextStatusBar();
                    return;
                }

                var token = _textReaderState.RestartPageCalculation();
                UpdateTextStatusBar(); 

                bool calculated = await _textPageCalculationService.CalculateAsync(
                    _textReaderState,
                    TextScrollViewer,
                    _settingsManager.FontSize,
                    _settingsManager.FontFamily ?? "Segoe UI",
                    token);

                if (calculated)
                {
                    DispatcherQueue.TryEnqueue(() => UpdateTextStatusBar());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating pages: {ex.Message}");
                if (TextScrollViewer != null)
                {
                    _textPageCalculationService.CompleteFallback(_textReaderState, TextScrollViewer);
                    DispatcherQueue.TryEnqueue(() => UpdateTextStatusBar());
                }
            }
        }

        private bool ShouldSkipPrecisePlainTextPagination()
        {
            return _isCurrentTextPlainModeLocked &&
                (_textLines.Count > PlainTextLockedPrecisePaginationLineLimit ||
                 _currentTextContent.Length > PlainTextLockedPrecisePaginationCharacterLimit);
        }

        internal int GetTopVisibleLineIndex()
        {
            if (TextItemsRepeater == null || TextScrollViewer == null) return 1;
            if (_textLines == null || _textLines.Count == 0) return 1;

            return _textViewportService.GetTopVisibleLineIndex(
                TextItemsRepeater,
                TextScrollViewer,
                _textLines.Count,
                _settingsManager.FontSize);
        }

        internal void ScrollToLine(int line)
        {
            _ = ScrollToLineAsync(line);
        }

        internal async Task ScrollToLineAsync(int line)
        {
            try
            {
                if (TextItemsRepeater == null || TextScrollViewer == null) return;
                if (_textLines == null || _textLines.Count == 0) return;

                await _textViewportService.ScrollToLineAsync(
                    TextItemsRepeater,
                    TextScrollViewer,
                    line,
                    _textLines.Count,
                    _settingsManager.FontSize);
                UpdateTextStatusBar();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ScrollToLine: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }
    }
}
