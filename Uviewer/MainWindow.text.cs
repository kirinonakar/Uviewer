using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private readonly TextReaderState _textReaderState = new();
        private List<TextLine> _textLines
        {
            get => _textReaderState.Lines;
            set => _textReaderState.Lines = value ?? new List<TextLine>();
        }

        private string _currentTextContent
        {
            get => _textReaderState.Content;
            set => _textReaderState.Content = value ?? string.Empty;
        }

        private TextSettingsManager _settingsManager = null!;
        private bool _isTextMode = false;
        private int _textTotalLineCountInSource
        {
            get => _textReaderState.TotalLineCountInSource;
            set => _textReaderState.TotalLineCountInSource = value;
        }

        private bool _isTextLinesFullyLoaded
        {
            get => _textReaderState.LinesFullyLoaded;
            set => _textReaderState.LinesFullyLoaded = value;
        }

        private CancellationTokenSource? _globalTextCts => _textReaderState.GlobalCts;
        private bool _textInputInitialized = false;
        private string? _currentTextFilePath
        {
            get => _textReaderState.FilePath;
            set => _textReaderState.FilePath = value;
        }

        private string? _currentTextArchiveEntryKey
        {
            get => _textReaderState.ArchiveEntryKey;
            set => _textReaderState.ArchiveEntryKey = value;
        }

        private int _lastRecentSaveLine
        {
            get => _textReaderState.LastRecentSaveLine;
            set => _textReaderState.LastRecentSaveLine = value;
        }

        private async void EncodingItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is ToggleMenuFlyoutItem item && item.Tag is string tag)
                {
                    _settingsManager.EncodingName = tag;

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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in EncodingItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
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
                _textReaderState.CancelGlobalLoad();
                _textReaderState.CancelPageCalculation(); // [추가] 모드 전환 시 뒤에서 돌고 있는 페이지 계산 작업 강제 종료
            }
            catch { }
            _textReaderState.RestartGlobalLoad();
        }

        private async Task LoadTextFileAsync(StorageFile file)
        {
            // Save position of current file before switching
            await AddToRecentAsync(true);

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
                SyncSidebarSelection(new ImageEntry { FilePath = file.Path, DisplayName = file.Name });
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"텍스트 로드 실패: {ex.Message}";
            }
            finally
            {
                _isNavigatingRecent = false;
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
                SyncSidebarSelection(entry);
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"아카이브 텍스트 로드 실패: {ex.Message}";
            }
            finally
            {
                _isNavigatingRecent = false;
            }
        }

        private async Task DisplayLoadedText(string content, string name, string? uniquePath = null, CancellationToken token = default)
        {
            var preparedText = _textDisplayPreparationService.Prepare(content, name);
            content = preparedText.Content;
            _currentTextContent = content; // Save for reload
            _tocService.SetProvider(new TextTocProvider(content));
            _ = _tocService.LoadTocAsync(token);

            _aozoraBlocks.Clear(); // [핵심 수정] 새 파일을 로드할 때 이전 파일의 블록 캐시를 제거합니다.
            _imageResourceService.ClearTextEntries(); // 텍스트 이미지 캐시 및 누락 목록 초기화

            // [추가] 이전 파일의 스크롤 추적 기록을 초기화하여 엉뚱한 위치가 자동 저장되는 것을 방지합니다.
            _lastRecentSaveLine = -1;

            _isMarkdownRenderMode = preparedText.IsMarkdownRenderMode;
            if (VerticalToggleButton != null)
            {
                VerticalToggleButton.IsEnabled = preparedText.CanUseVerticalMode;
                VerticalToggleButton.IsChecked = preparedText.CanUseVerticalMode && _isVerticalMode;
            }

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

        private int GetSavedStartLine(string name, string? path)
        {
            return _textResumeService.GetSavedStartLine(
                _recentService.RecentItems,
                _favoritesService.Favorites,
                name,
                path);
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
            SharpenButton.Visibility = Visibility.Visible;
            SharpenSeparator.Visibility = Visibility.Visible;

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
            if (_settingsManager == null)
            {
                _settingsManager = new TextSettingsManager(GetTextSettingsFilePath());
            }

            _settingsManager.Load();

            _isVerticalMode = _settingsManager.IsVerticalMode;
            if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = _isVerticalMode;

            if (TextSizeLevelText != null)
            {
                TextSizeLevelText.Text = _settingsManager.FontSize.ToString();
            }

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

        private void SaveTextSettings()
        {
            if (_settingsManager != null)
            {
                _settingsManager.IsVerticalMode = _isVerticalMode;
                _settingsManager.Save();
            }
        }

        private void SwitchToImageMode()
        {
            _isTextMode = false;
            _isEpubMode = false; // Reset Epub mode
            DisableVerticalModeForImageDocument();

            ImageArea.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;
            EpubArea.Visibility = Visibility.Collapsed;
            VerticalTextCanvas.Visibility = Visibility.Collapsed;

            ImageToolbarPanel.Visibility = Visibility.Visible;
            TextToolbarPanel.Visibility = Visibility.Collapsed;
            SideBySideToolbarPanel.Visibility = (_currentPdfDocument != null) ? Visibility.Collapsed : Visibility.Visible;
            SharpenButton.Visibility = (_currentPdfDocument != null) ? Visibility.Collapsed : Visibility.Visible;
            SharpenSeparator.Visibility = (_currentPdfDocument != null) ? Visibility.Collapsed : Visibility.Visible;

            // 핀치 줌과 스와이프를 위해 조작 모드 활성화 (PDF 및 일반 이미지 모두)
            ImageArea.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.All;
            if (MainCanvas != null) MainCanvas.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.All;

            UpdateSideBySideButtonState();
            UpdateNextImageSideButtonState();
        }

        private void DisableVerticalModeForImageDocument()
        {
            _isVerticalMode = false; // Images and PDFs always use horizontal/image layout.

            if (_verticalKeyAttached && RootGrid != null)
            {
                RootGrid.PreviewKeyDown -= RootGrid_Vertical_PreviewKeyDown;
                _verticalKeyAttached = false;
            }

            if (VerticalToggleButton != null) VerticalToggleButton.IsChecked = false;
            if (VerticalTextCanvas != null) VerticalTextCanvas.Visibility = Visibility.Collapsed;
        }

        private void CloseCurrentText()
        {
            _textReaderState.ClearDocument();
            _aozoraBlocks.Clear();
        }

        /// <summary>
        /// Progressive loading for simple text mode - loads initial chunk first, then rest in background
        /// </summary>
        private async Task LoadTextLinesProgressivelyAsync(string content, int targetLine = 1, CancellationToken token = default)
        {
            var loadPlan = _textLineLoadService.CreatePlan(content, targetLine);
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

                    TextItemsRepeater.ItemsSource = _textLines;
                    if (targetLine > 1)
                    {
                        await Task.Delay(50);
                        ScrollToLine(targetLine);
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

                            // [핵심 수정] 픽셀 오프셋 대신 정확한 라인 번호를 기억
                            int currentLineToRestore = GetTopVisibleLineIndex();
                            if (currentLineToRestore < targetLine) currentLineToRestore = targetLine;

                            _textLines.AddRange(restTextLines);
                            _isTextLinesFullyLoaded = true;

                            if (TextItemsRepeater != null)
                            {
                                var currentSource = _textLines;
                                TextItemsRepeater.ItemsSource = null;
                                TextItemsRepeater.ItemsSource = currentSource;

                                // [핵심 수정] Source 교체 후 픽셀 복구가 아닌 라인 기반 정확한 1회 이동 수행
                                if (currentLineToRestore > 1)
                                {
                                    ScrollToLine(currentLineToRestore);
                                }
                                else
                                {
                                    TextScrollViewer?.ChangeView(null, 0, null, true);
                                }
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
                        ScrollToLine(targetLine);
                    }
                }
                if (TextArea != null) TextArea.Background = _settingsManager.GetThemeBackground();
            }

            StartPageCalculationAsync();
            if (TextFastNavOverlay != null) TextFastNavOverlay.Visibility = Visibility.Collapsed;
        }

        private double GetUrlMaxWidth()
        {
            return _textLineLayoutService.CalculateReadableMaxWidth(
                TextArea?.ActualWidth ?? 0,
                _settingsManager.FontSize);
        }

        private Windows.UI.Text.FontWeight GetFontWeightForFamily(string fontFamily)
        {
            return _textLineLayoutService.GetFontWeightForFamily(fontFamily);
        }

        private async Task RefreshTextDisplay(bool resetScroll = false)
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

        private void ColorsMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowColorPickerDialog();
        }

        private async void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            try
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LanguageItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void ApplyLanguage(string lang)
        {
            _settingsManager.Language = lang;
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
            string current = _settingsManager.Language;
            if (string.IsNullOrEmpty(current)) current = "Auto";

            if (LangAutoItem != null) LangAutoItem.IsChecked = current == "Auto";
            if (LangKoItem != null) LangKoItem.IsChecked = current == "ko-KR";
            if (LangEnItem != null) LangEnItem.IsChecked = current == "en-US";
            if (LangJaItem != null) LangJaItem.IsChecked = current == "ja-JP";
            if (LangZhHansItem != null) LangZhHansItem.IsChecked = current == "zh-Hans";
            if (LangZhHantItem != null) LangZhHantItem.IsChecked = current == "zh-Hant";
            if (LangViItem != null) LangViItem.IsChecked = current == "vi-VN";
        }


        private async Task ShowColorPickerDialog()
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

        private (double h, double s, double l) ToHsl(Color color) => TextSettingsManager.ToHsl(color);

        private Color FromHsl(double h, double s, double l) => TextSettingsManager.FromHsl(h, s, l);

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
            var selectedFont = await _textDialogService.ShowFontPickerAsync(_settingsManager.FontFamily, Strings.FontSelectionTitle);
            if (selectedFont != null)
            {
                SetTextFont(selectedFont);
            }
        }

        private async void SetTextFont(string fontFamily)
        {
            try
            {
                _settingsManager.FontFamily = fontFamily;
                SaveTextSettings();
                await RefreshTextDisplay();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SetTextFont: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void UiFontMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowUiFontPickerDialog();
        }

        private async Task ShowUiFontPickerDialog()
        {
            var selectedFont = await _textDialogService.ShowFontPickerAsync(_settingsManager.UIFontFamily, Strings.UIFontSelectionTitle);
            if (selectedFont != null)
            {
                SetUiFont(selectedFont);
            }
        }

        private void SetUiFont(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily) || fontFamily == "Unknown")
            {
                _settingsManager.UIFontFamily = "";
                return; // Or reset to system default
            }

            _settingsManager.UIFontFamily = fontFamily;
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
            try
            {
                if (_settingsManager.FontFamily == _settingsManager.DefaultFont1)
                    _settingsManager.FontFamily = _settingsManager.DefaultFont2;
                else
                    _settingsManager.FontFamily = _settingsManager.DefaultFont1;

                SaveTextSettings();
                await RefreshTextDisplay();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleFont: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void UpdateFontSettingsMenu()
        {
            if (SetDefaultFont1MenuItem != null) SetDefaultFont1MenuItem.Text = $"{Strings.DefaultFont1Label}: {_settingsManager.DefaultFont1}";
            if (SetDefaultFont2MenuItem != null) SetDefaultFont2MenuItem.Text = $"{Strings.DefaultFont2Label}: {_settingsManager.DefaultFont2}";
            if (ResetDefaultFontsMenuItem != null) ResetDefaultFontsMenuItem.Text = Strings.ResetButton;
        }

        private void SetDefaultFont1MenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialogForDefault(1);
        }

        private void SetDefaultFont2MenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialogForDefault(2);
        }

        private async void ResetDefaultFontsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settingsManager.DefaultFont1 = "Yu Gothic";
                _settingsManager.DefaultFont2 = "Yu Mincho";
                UpdateFontSettingsMenu();
                SaveTextSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ResetDefaultFontsMenuItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task ShowFontPickerDialogForDefault(int slot)
        {
            string currentFont = (slot == 1) ? _settingsManager.DefaultFont1 : _settingsManager.DefaultFont2;
            var selectedFont = await _textDialogService.ShowFontPickerAsync(currentFont, Strings.FontSelectionSlotTitle(slot));
            
            if (selectedFont != null)
            {
                if (slot == 1) _settingsManager.DefaultFont1 = selectedFont;
                else _settingsManager.DefaultFont2 = selectedFont;

                UpdateFontSettingsMenu();
                SaveTextSettings();
            }
        }

        private void TextSizeUpButton_Click(object sender, RoutedEventArgs e)
        {
            IncreaseTextSize();
        }

        private async void IncreaseTextSize()
        {
            try
            {
                _settingsManager.FontSize += 2;
                if (_settingsManager.FontSize > 72) _settingsManager.FontSize = 72;
                TextSizeLevelText.Text = _settingsManager.FontSize.ToString();
                SaveTextSettings();
                await RefreshTextDisplay();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IncreaseTextSize: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void TextSizeDownButton_Click(object sender, RoutedEventArgs e)
        {
            DecreaseTextSize();
        }

        private async void DecreaseTextSize()
        {
            try
            {
                _settingsManager.FontSize -= 2;
                if (_settingsManager.FontSize < 8) _settingsManager.FontSize = 8;
                TextSizeLevelText.Text = _settingsManager.FontSize.ToString();
                SaveTextSettings();
                await RefreshTextDisplay();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DecreaseTextSize: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTheme();
        }

        private async void ToggleTheme()
        {
            try
            {
                // [수정] 유저가 설정한 색(Custom: 3)이 있는 경우, 0(White) -> 1(Beige) -> 2(Dark) -> 3(Custom) 순으로 순환하도록 변경합니다.
                int maxThemes = _settingsManager.CustomBackgroundColor.HasValue ? 4 : 3;
                _settingsManager.ThemeIndex = (_settingsManager.ThemeIndex + 1) % maxThemes;
                SaveTextSettings();
                await RefreshTextDisplay();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleTheme: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void GoToPageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowGoToLineDialog();
        }

        private bool CanSearchCurrentDocument =>
            (_isTextMode && _textReaderState.HasContent) ||
            (_isEpubMode && _epubSession.HasDocument) ||
            (_currentPdfDocument != null && !string.IsNullOrEmpty(_currentPdfPath));

        private void SearchButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowSearchOverlay(sender as FrameworkElement);
        }

        private void ShowSearchOverlay(FrameworkElement? anchor = null)
        {
            if (!CanSearchCurrentDocument)
            {
                ShowNotification(Strings.SearchUnavailable, "\uE721", "Gray");
                return;
            }

            if (_currentPdfDocument != null)
            {
                DisableVerticalModeForImageDocument();
            }

            if (_currentPdfDocument != null)
            {
                anchor = IsVisibleSearchAnchor(anchor)
                    ? anchor
                    : IsVisibleSearchAnchor(PdfGoToPageButton)
                        ? PdfGoToPageButton
                        : IsVisibleSearchAnchor(ImageToolbarPanel)
                            ? ImageToolbarPanel
                            : RootGrid;
            }
            else
            {
                anchor ??= GoToPageButton;
            }

            if (anchor != null)
            {
                _searchOverlayService.Show(anchor, RootGrid);
            }
        }

        private static bool IsVisibleSearchAnchor(FrameworkElement? element)
            => element != null &&
               element.Visibility == Visibility.Visible &&
               element.ActualWidth > 0 &&
               element.ActualHeight > 0;

        private void SetActiveSearchQuery(string? query)
        {
            if (_currentPdfDocument != null)
            {
                DisableVerticalModeForImageDocument();
            }

            _documentSearchState.SetQuery(query);

            InvalidateSearchHighlights();

            if (_currentPdfDocument != null && _documentSearchState.HasQuery)
            {
                _ = RefreshPdfSearchHighlightsAsync(_currentIndex);
            }
        }

        private async Task RefreshPdfSearchHighlightsAsync(int pageIndex, int currentMatchIndex = -1)
        {
            if (_currentPdfDocument == null || string.IsNullOrEmpty(_currentPdfPath) || !_documentSearchState.HasQuery)
            {
                return;
            }

            DisableVerticalModeForImageDocument();

            var token = _documentSearchState.RestartPdfHighlightSearch();

            string pdfPath = _currentPdfPath;
            string query = _documentSearchState.Query!;

            try
            {
                var highlights = await _searchHighlightService.FindPdfHighlightsAsync(pdfPath, pageIndex, query, token);
                if (token.IsCancellationRequested) return;
                if (!string.Equals(_currentPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase)) return;
                if (_currentIndex != pageIndex) return;
                if (!string.Equals(_documentSearchState.Query, query, StringComparison.Ordinal)) return;

                _documentSearchState.SetPdfHighlights(highlights, pageIndex, currentMatchIndex);
                MainCanvas?.Invalidate();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF search highlight error: {ex.Message}");
            }
        }

        private void InvalidateSearchHighlights()
        {
            ApplySearchHighlightsToRealizedText();
            MainCanvas?.Invalidate();
            AozoraTextCanvas?.Invalidate();
            VerticalTextCanvas?.Invalidate();
            EpubTextCanvas?.Invalidate();
        }

        private void ApplySearchHighlightsToRealizedText()
        {
            if (TextItemsRepeater == null || _textLines == null || _textLines.Count == 0) return;

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(TextItemsRepeater);
                for (int i = 0; i < childCount; i++)
                {
                    if (VisualTreeHelper.GetChild(TextItemsRepeater, i) is TextBlock tb)
                    {
                        int index = TextItemsRepeater.GetElementIndex(tb);
                        if (index >= 0 && index < _textLines.Count)
                        {
                            ApplySearchHighlightsToTextBlock(tb, _textLines[index].Content, index + 1);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ApplySearchHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber)
        {
            _textSearchHighlightPresenterService.ApplyToTextBlock(
                textBlock,
                content,
                lineNumber,
                _documentSearchState,
                _currentEpubChapterIndex);
        }

        private DocumentSearchMatch? GetActiveSearchMatchFor(DocumentSearchKind kind)
        {
            return _documentSearchState.GetActiveMatchFor(kind, _currentEpubChapterIndex);
        }

        private async Task<IReadOnlyList<DocumentSearchMatch>> SearchCurrentDocumentAsync(string query, CancellationToken token)
        {
            if (_currentPdfDocument != null && !string.IsNullOrEmpty(_currentPdfPath))
            {
                DisableVerticalModeForImageDocument();
                return await _documentSearchService.SearchPdfAsync(_currentPdfPath, query, token);
            }

            if (_isEpubMode && _epubSession.HasDocument)
            {
                string cacheKey = $"epub:{_currentEpubFilePath ?? _currentEpubDisplayName ?? string.Empty}:{_epubSpine.Count}";
                return await _documentSearchService.SearchEpubAsync(
                    cacheKey,
                    _epubSession,
                    _epubDocumentService,
                    query,
                    token);
            }

            if (_isTextMode)
            {
                return _textDocumentSearchService.Search(
                    _textReaderState,
                    _isAozoraMode,
                    _aozoraBlocks,
                    _isMarkdownRenderMode,
                    _settingsManager.EncodingName,
                    query);
            }

            return Array.Empty<DocumentSearchMatch>();
        }

        private long GetCurrentSearchPosition()
        {
            if (_currentPdfDocument != null)
            {
                return _currentIndex + 1;
            }

            if (_isEpubMode)
            {
                int line = CurrentEpubWin2DPage?.StartLine ?? 1;
                return DocumentSearchService.CreateEpubSortKey(_currentEpubChapterIndex, line);
            }

            if (_isVerticalMode)
            {
                return Math.Max(1, _currentVerticalPageInfo.StartLine);
            }

            if (_isAozoraMode)
            {
                return Math.Max(1, _currentAozoraPageInfo.StartLine);
            }

            return GetTopVisibleLineIndex();
        }

        private async Task NavigateToSearchMatchAsync(DocumentSearchMatch match)
        {
            _documentSearchState.SetActiveMatch(match);

            switch (match.Kind)
            {
                case DocumentSearchKind.Pdf:
                    if (_currentPdfDocument != null && match.PageIndex >= 0 && match.PageIndex < _imageEntries.Count)
                    {
                        _documentSearchState.SetPdfMatchIndex(match.MatchIndex);
                        _currentIndex = match.PageIndex;
                        await DisplayCurrentImageAsync();
                        await RefreshPdfSearchHighlightsAsync(match.PageIndex, match.MatchIndex);
                    }
                    break;

                case DocumentSearchKind.Epub:
                    await NavigateToEpubSearchMatchAsync(match);
                    break;

                case DocumentSearchKind.Text:
                    await NavigateToTextSearchMatchAsync(match);
                    break;
            }

            if (match.Kind != DocumentSearchKind.Pdf)
            {
                InvalidateSearchHighlights();
            }
        }

        private async Task NavigateToTextSearchMatchAsync(DocumentSearchMatch match)
        {
            int line = Math.Max(1, match.LineNumber);

            if (_isVerticalMode)
            {
                await PrepareVerticalTextAsync(line, match.BlockIndex);
                return;
            }

            if (_isAozoraMode && _aozoraBlocks.Count > 0)
            {
                int targetIdx = _textBlockDocumentService.FindStartBlockIndex(_aozoraBlocks, line, match.BlockIndex);
                await RenderAozoraDynamicPage(targetIdx);
                UpdateAozoraStatusBar();
                return;
            }

            ScrollToLine(line);
            UpdateTextStatusBar();
        }

        private async Task NavigateToEpubSearchMatchAsync(DocumentSearchMatch match)
        {
            int chapterIndex = Math.Clamp(match.EpubChapterIndex, 0, Math.Max(0, _epubSpine.Count - 1));
            int line = Math.Max(1, match.LineNumber);

            if (chapterIndex != _currentEpubChapterIndex)
            {
                _currentEpubChapterIndex = chapterIndex;
                await LoadEpubChapterAsync(chapterIndex, targetLine: line, targetBlockIndex: match.BlockIndex);
                return;
            }

            if (_isVerticalMode)
            {
                await LoadEpubChapterAsync(chapterIndex, targetLine: line, targetBlockIndex: match.BlockIndex);
                return;
            }

            int pageIndex = FindEpubSearchPageIndex(match);
            if (pageIndex >= 0)
            {
                SetEpubPageIndex(pageIndex);
            }
        }

        private int FindEpubSearchPageIndex(DocumentSearchMatch match)
        {
            if (_epubWin2DPages.Count == 0) return -1;

            if (match.BlockIndex >= 0)
            {
                for (int i = _epubWin2DPages.Count - 1; i >= 0; i--)
                {
                    if (_epubWin2DPages[i].StartBlockIndex <= match.BlockIndex)
                    {
                        return i;
                    }
                }
            }

            return _epubPageFlowService.FindPageByLine(_epubWin2DPages, match.LineNumber);
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
                    await RenderVerticalDynamicPageAsync(0);
                    UpdateTextStatusBar();
                }
                else if (_isAozoraMode && _aozoraBlocks.Count > 0)
                {
                    await RenderAozoraDynamicPage(0);
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
                    await RenderVerticalDynamicPageAsync(999999);
                    UpdateTextStatusBar();
                }
                else if (_isAozoraMode && _aozoraBlocks.Count > 0)
                {
                    // Start rendering from slightly before the end to fill the last page
                    int lastIdx = Math.Max(0, _aozoraBlocks.Count - 5);
                    await RenderAozoraDynamicPage(lastIdx);
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
            else if (e.Key == Windows.System.VirtualKey.V && !_isMarkdownRenderMode)
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

            var result = await _textDialogService.ShowGoToLineAsync(currentLine, totalLines, title);
            if (result.HasValue)
            {
                await GoToLine(result.Value.ToString());
            }
        }

        private async Task GoToLine(string lineText)
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
                int targetIdx = _textBlockDocumentService.FindStartBlockIndex(_aozoraBlocks, line);

                await RenderAozoraDynamicPage(targetIdx);
                UpdateAozoraStatusBar();
            }
            else if (TextScrollViewer != null)
            {
                ScrollToLine(line);
                UpdateTextStatusBar();
            }
        }

       private void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
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

            _textViewportService.NavigatePage(TextScrollViewer, _settingsManager.FontSize, direction);
            UpdateTextStatusBar();
        }

        private void UpdateTextStatusBar(string? fileName = null, int? totalLines = null, int? currentPage = null)
        {
            if (!_isTextMode && !_isEpubMode) return;
            if (_isVerticalMode) { UpdateVerticalStatusBar(); return; }
            if (_isAozoraMode) { UpdateAozoraStatusBar(); return; }
            if (_isEpubMode) { UpdateEpubStatus(); return; }

            if (TextScrollViewer == null) return;

            var content = _textStatusBarService.Create(
                fileName,
                _currentTextArchiveEntryKey != null,
                totalLines,
                _textReaderState,
                TextScrollViewer,
                GetTopVisibleLineIndex());

            if (content.FileName != null) FileNameText.Text = content.FileName;
            ImageInfoText.Text = content.LineInfo;
            TextProgressText.Text = content.ProgressText;
            ImageIndexText.Text = content.PageInfo;

            if (content.CurrentLine != _lastRecentSaveLine)
            {
                _lastRecentSaveLine = content.CurrentLine;
                _ = AddToRecentAsync(true);
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
                _ = RenderAozoraDynamicPage(_currentAozoraStartBlockIndex);
                StartAozoraPageCalculationAsync();
            }
        }

        private async void StartPageCalculationAsync()
        {
            try
            {
                if (_isAozoraMode) return;
                if (TextScrollViewer == null) return;

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

        private int GetTopVisibleLineIndex()
        {
            if (TextItemsRepeater == null || TextScrollViewer == null) return 1;
            if (_textLines == null || _textLines.Count == 0) return 1;

            return _textViewportService.GetTopVisibleLineIndex(
                TextItemsRepeater,
                TextScrollViewer,
                _textLines.Count,
                _settingsManager.FontSize);
        }

        private async void ScrollToLine(int line)
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
