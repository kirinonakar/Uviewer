using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SharpCompress.Archives;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // Window settings
        private string _windowSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "window_settings.txt");

        // Fast navigation detection
        private DateTime _lastNavigationTime = DateTime.MinValue;
        private readonly TimeSpan _fastNavigationThreshold = TimeSpan.FromMilliseconds(40); // 40ms threshold
        private bool _isFastNavigation = false;
        private CancellationTokenSource? _fastNavigationResetCts;
        private DispatcherQueueTimer? _fastNavOverlayTimer;
        private CancellationTokenSource? _imageLoadingCts;


        #region Fast Navigation

        private bool DetectFastNavigation()
        {
            var now = DateTime.Now;
            var timeSinceLastNavigation = now - _lastNavigationTime;
            _lastNavigationTime = now;

            // 이전의 리셋 예약 취소
            _fastNavigationResetCts?.Cancel();
            _fastNavigationResetCts = new CancellationTokenSource();
            var token = _fastNavigationResetCts.Token;

            if (timeSinceLastNavigation < _fastNavigationThreshold)
            {
                _isFastNavigation = true;

                // 50ms 동안 추가 입력이 없으면 이미지를 로드하도록 예약
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(50, token);
                        if (!token.IsCancellationRequested)
                        {
                            await ResetFastNavigation();
                        }
                    }
                    catch (OperationCanceledException) { /* 무시 */ }
                });

                return true;
            }

            _isFastNavigation = false;
            return false;
        }

        private void ShowFastNavigationOverlay()
        {
            if (_currentIndex < 0 || _imageEntries.Count == 0)
                return;


            FastNavText.Text = $"빠른 탐색 중... ({_currentIndex + 1}/{_imageEntries.Count})";
            FastNavOverlay.Visibility = Visibility.Visible;


            _fastNavOverlayTimer?.Stop();
            _fastNavOverlayTimer ??= DispatcherQueue.CreateTimer();
            _fastNavOverlayTimer.Interval = TimeSpan.FromMilliseconds(200);
            _fastNavOverlayTimer.Tick += (s, e) =>
            {
                _fastNavOverlayTimer?.Stop();
                FastNavOverlay.Visibility = Visibility.Collapsed;
            };
            _fastNavOverlayTimer.Start();
        }

        private async Task ResetFastNavigation()
        {
            // 타이머와 오버레이를 먼저 끈다
            DispatcherQueue.TryEnqueue(async () =>
            {
                _isFastNavigation = false;
                FastNavOverlay.Visibility = Visibility.Collapsed;

                // 중요: 빠른 탐색이 끝난 시점의 인덱스 이미지를 실제로 로드함
                if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    // 단순히 DisplayCurrentImageAsync를 호출하는 것보다 
                    // 현재 상태가 "빠른 탐색"이 아님을 명시하고 호출
                    await DisplayCurrentImageAsync();
                }
            });
        }

        private void ShowFilenameOnly()
        {
            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count)
                return;

            ShowFastNavigationOverlay();

            var currentEntry = _imageEntries[_currentIndex];

            // Don't hide images during fast navigation - just update text
            // Images will stay visible showing the last loaded image

            // Update the filename text directly
            FileNameText.Text = GetFormattedDisplayName(currentEntry.DisplayName, currentEntry.IsArchiveEntry);

            // Update status bar with filename and index
            TextProgressText.Text = ""; // Clear for image mode
            if (_isSideBySideMode)
            {
                int displayIndex = (_currentIndex / 2) + 1;
                int totalPairs = (_imageEntries.Count + 1) / 2;
                ImageIndexText.Text = $"{displayIndex} / {totalPairs} (B)";
            }
            else
            {
                ImageIndexText.Text = $"{_currentIndex + 1} / {_imageEntries.Count}";
            }

            ImageInfoText.Text = "빠르게 넘어가는 중...";
        }

        #endregion


        #region Zoom

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            FitToWindow();
        }

        private void ZoomActualButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap != null)
            {
                // Calculate what zoom level would give us 1:1
                var containerWidth = ImageArea.ActualWidth;
                var containerHeight = ImageArea.ActualHeight;

                if (containerWidth > 0 && containerHeight > 0)
                {
                    var fitRatio = Math.Min(containerWidth / _currentBitmap.Size.Width,
                                            containerHeight / _currentBitmap.Size.Height);
                    
                    if (_currentPdfDocument != null)
                    {
                        // Fit to width
                        _zoomLevel = containerWidth / (_currentBitmap.Size.Width * fitRatio);
                    }
                    else
                    {
                        _zoomLevel = 1.0 / fitRatio; // Actual size
                    }
                    ApplyZoom();
                }
            }
        }

        private void ZoomIn()
        {
            _zoomLevel = Math.Min(_zoomLevel + ZoomStep, MaxZoom);
            ApplyZoom();
        }

        private void ZoomOut()
        {
            _zoomLevel = Math.Max(_zoomLevel - ZoomStep, MinZoom);
            ApplyZoom();
        }

        private void FitToWindow()
        {
            _zoomLevel = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_currentBitmap == null || ImageArea.ActualWidth <= 0 || ImageArea.ActualHeight <= 0)
                return;

            var containerWidth = ImageArea.ActualWidth;
            var containerHeight = ImageArea.ActualHeight;

            // Trigger canvas redraw for new zoom level
            if (!_isSideBySideMode || _currentPdfDocument != null)
            {
                MainCanvas?.Invalidate();
            }
            else
            {
                LeftCanvas?.Invalidate();
                RightCanvas?.Invalidate();
            }

            // Update zoom level display (relative to fit size)
            ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";
        }

        #endregion

        #region Drag and Drop

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;

            if (e.DragUIOverride != null)
            {
                e.DragUIOverride.Caption = "이미지 열기";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();

                if (items.Count > 0)
                {
                    var item = items[0];

                    if (item is StorageFile file)
                    {
                        var ext = Path.GetExtension(file.Name).ToLowerInvariant();

                        if (SupportedArchiveExtensions.Contains(ext))
                        {
                            await LoadImagesFromArchiveAsync(file.Path);
                        }
                        else if (SupportedEpubExtensions.Contains(ext))
                        {
                            await LoadEpubFileAsync(file);
                        }
                        else if (SupportedPdfExtensions.Contains(ext))
                        {
                            await LoadImagesFromPdfAsync(file.Path);
                        }
                        else if (SupportedImageExtensions.Contains(ext) || SupportedTextExtensions.Contains(ext))
                        {
                            await LoadImageFromFileAsync(file);
                        }

                        // Update explorer
                        var folder = Path.GetDirectoryName(file.Path);
                        if (folder != null)
                        {
                            LoadExplorerFolder(folder);
                        }
                    }
                    else if (item is StorageFolder folder)
                    {
                        LoadExplorerFolder(folder.Path);
                        await LoadImagesFromFolderAsync(folder);
                    }
                }
            }
        }

        #endregion


        #region Window Settings

        private bool LoadWindowSettings(AppWindow appWindow)
        {
            try
            {
                // 1. 현재 모니터의 해상도 정보 먼저 가져오기 (기본값 계산용)
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int screenWidth = displayArea.WorkArea.Width;
                int screenHeight = displayArea.WorkArea.Height;

                // 모니터 해상도의 70% 크기 계산
                int defaultWidth = (int)(screenWidth * 0.7);
                int defaultHeight = (int)(screenHeight * 0.7);
                int defaultX = (screenWidth - defaultWidth) / 2;
                int defaultY = (screenHeight - defaultHeight) / 2;

                if (File.Exists(_windowSettingsFile))
                {
                    var lines = File.ReadAllLines(_windowSettingsFile);
                    if (lines.Length >= 4 &&
                        int.TryParse(lines[0], out int x) &&
                        int.TryParse(lines[1], out int y) &&
                        int.TryParse(lines[2], out int width) &&
                        int.TryParse(lines[3], out int height))
                    {
                        // 2. [검사] 저장된 크기가 해상도의 90%를 초과하는지 확인
                        if (width > screenWidth * 0.9 || height > screenHeight * 0.9)
                        {
                            System.Diagnostics.Debug.WriteLine("Window size too large. Resetting to 70% of screen.");
                            width = defaultWidth;
                            height = defaultHeight;
                            x = defaultX;
                            y = defaultY;
                        }
                        else
                        {
                            // 최소 크기 제한만 적용
                            width = Math.Max(400, width);
                            height = Math.Max(300, height);
                        }

                        // 3. 설정 적용
                        _lastNonMaximizedRect = new Windows.Graphics.RectInt32(x, y, width, height);
                        appWindow.MoveAndResize(_lastNonMaximizedRect);

                        // 최대화 상태 및 기타 버튼 설정 복원
                        bool shouldMaximize = lines.Length >= 5 && lines[4].Trim() == "1";
                        if (shouldMaximize)
                        {
                            Activated += RestoreMaximizedStateOnce;
                        }

                        if (lines.Length >= 6 && lines[5].Trim() == "1") _sharpenEnabled = true;
                        if (lines.Length >= 7 && lines[6].Trim() == "1") _isSideBySideMode = true;
                        if (lines.Length >= 8 && lines[7].Trim() == "0") _nextImageOnRight = false;
                        if (lines.Length >= 10 && lines[9].Trim() == "1") _matchControlDirection = true;
                        if (lines.Length >= 9 && int.TryParse(lines[8], out int themeVal))
                        {
                            SetTheme((ElementTheme)themeVal);
                        }
                        
                        if (lines.Length >= 11 && lines[10].Trim() == "0") _allowMultipleInstances = false;

                        if (MatchControlDirectionMenuItem != null) MatchControlDirectionMenuItem.IsChecked = _matchControlDirection;
                        if (AllowMultipleInstancesMenuItem != null) AllowMultipleInstancesMenuItem.IsChecked = _allowMultipleInstances;
                        UpdateSharpenButtonState();
                        UpdateSideBySideButtonState();
                        UpdateNextImageSideButtonState();

                        return true;
                    }
                }

                // 파일이 없거나 읽기 실패 시 기본값(70%)으로 설정
                _lastNonMaximizedRect = new Windows.Graphics.RectInt32(defaultX, defaultY, defaultWidth, defaultHeight);
                appWindow.MoveAndResize(_lastNonMaximizedRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading window settings: {ex.Message}");
                MessageBox(IntPtr.Zero, $"Window Settings Error:\n{ex.Message}", "Uviewer Warning", 0x30);
            }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private void RestoreMaximizedStateOnce(object sender, WindowActivatedEventArgs e)
        {
            // Only restore when window is activated (not deactivated)
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                return;

            Activated -= RestoreMaximizedStateOnce;
            try
            {
                if (_isFullscreen) return;
                var appWindow = this.AppWindow;
                if (appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    overlapped.Maximize();
                }
                else
                {
                    appWindow.SetPresenter(OverlappedPresenter.Create());
                    (appWindow.Presenter as OverlappedPresenter)?.Maximize();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring maximized state: {ex.Message}");
            }
        }

        private void SaveWindowSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(_windowSettingsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var appWindow = this.AppWindow;
                bool isMaximized = false;

                // 1. 현재 최대화 상태인지 확인
                if (!_isFullscreen && appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    isMaximized = overlapped.State == OverlappedPresenterState.Maximized;
                }

                // 2. [수정] 복잡한 조건문 필요 없이, 저장할 좌표는 
                //    항상 'AppWindow_Changed'가 정교하게 관리해둔 Rect를 사용합니다.
                int saveX = _lastNonMaximizedRect.X;
                int saveY = _lastNonMaximizedRect.Y;
                int saveWidth = _lastNonMaximizedRect.Width;
                int saveHeight = _lastNonMaximizedRect.Height;

                var settings = new string[]
                {
            saveX.ToString(),
            saveY.ToString(),
            saveWidth.ToString(),
            saveHeight.ToString(),
            isMaximized ? "1" : "0", // 상태만 현재 상태(최대화 여부)를 저장
            _sharpenEnabled ? "1" : "0",
            _isSideBySideMode ? "1" : "0",
            _nextImageOnRight ? "1" : "0",
            ((int)_currentTheme).ToString(),
            _matchControlDirection ? "1" : "0",
            _allowMultipleInstances ? "1" : "0"
                };

                File.WriteAllLines(_windowSettingsFile, settings);
                System.Diagnostics.Debug.WriteLine($"Window settings saved: Max={isMaximized}, RestoreRect={saveX},{saveY},{saveWidth}x{saveHeight}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings: {ex.Message}");
            }
        }

        #endregion
    }
}