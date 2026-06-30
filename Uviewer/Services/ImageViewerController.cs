using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.System;
using Windows.UI.Core;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal sealed class ImageViewerController
    {
        private readonly IImageViewerHost _host;
        private readonly ImageFastNavigationController _fastNavigationController;
        private readonly ImageZoomController _zoomController;

        public ImageViewerController(IImageViewerHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _fastNavigationController = new ImageFastNavigationController(_host, DisplayCurrentImageAsync);
            _zoomController = new ImageZoomController(_host);
        }

        public void UpdateFastNavigationUI() => _fastNavigationController.UpdateFastNavigationUI();

        public Task ResetFastNavigationAsync() => _fastNavigationController.ResetFastNavigationAsync();

        public void ZoomActual() => _zoomController.ZoomActual();

        public void ZoomIn() => _zoomController.ZoomIn();

        public void ZoomOut() => _zoomController.ZoomOut();

        public void FitToWindow() => _zoomController.FitToWindow();

        public void ApplyZoom() => _zoomController.ApplyZoom();

        public async Task DisplayCurrentImageAsync()
        {
            try
            {
                if (_host.ImageEntries.Count == 0)
                    return;

                if (_host.CurrentIndex < 0 || _host.CurrentIndex >= _host.ImageEntries.Count)
                {
                    _host.CurrentIndex = Math.Clamp(_host.CurrentIndex, 0, _host.ImageEntries.Count - 1);
                }

                int capturedIndexAtStart = _host.CurrentIndex;

                var oldCts = _host.ImageLoadingCts;
                _host.ImageLoadingCts = new CancellationTokenSource();
                var token = _host.ImageLoadingCts.Token;
                oldCts?.Cancel();
                oldCts?.Dispose();

                if (_host.ArchiveSession.IsSevenZipArchive)
                {
                    if (_host.SevenZipExtraction.ShouldSignalJump(_host.CurrentIndex, 2))
                    {
                        _host.Signal7zJump();
                    }
                }
                else
                {
                    _host.SevenZipExtraction.MarkCurrentIndex(_host.CurrentIndex);
                }

                _host.AnimatedWebpService.Stop();

                var entry = _host.ImageEntries[_host.CurrentIndex];

                if (entry.IsPdfEntry && _host.IsPdfMode)
                {
                    await DisplayPdfPageAsync(entry, capturedIndexAtStart, token);
                    return;
                }

                if (FileExplorerService.IsTextEntry(entry))
                {
                    await DisplayTextEntryAsync(entry);
                }
                else if (FileExplorerService.IsEpubEntry(entry))
                {
                    await DisplayEpubEntryAsync(entry, token);
                }
                else
                {
                    await DisplayImageEntryAsync(token);
                }

                _host.FocusRoot();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DisplayCurrentImageAsync: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task DisplayPdfPageAsync(ImageEntry entry, int capturedIndexAtStart, CancellationToken token)
        {
            _host.SwitchToImageMode();
            _host.IsCurrentViewSideBySide = false;

            CanvasBitmap? nextBitmap = _host.ImageCache.GetPreloadedImage(_host.CurrentIndex, _host.ZoomLevel);

            if (nextBitmap == null)
            {
                var tempOldBitmap = _host.CurrentBitmap;
                _host.CurrentBitmap = null;
                _host.MainCanvas?.Invalidate();

                if (tempOldBitmap != null && !IsBitmapInCache(tempOldBitmap))
                {
                    _host.ImageCache.SafeDisposeBitmap(tempOldBitmap);
                }

                nextBitmap = await _host.LoadPdfPageBitmapAsync(entry.PdfPageIndex, _host.MainCanvas!, token);

                if (nextBitmap != null)
                {
                    if (token.IsCancellationRequested || _host.CurrentIndex != capturedIndexAtStart)
                    {
                        _host.ImageCache.SafeDisposeBitmap(nextBitmap);
                        return;
                    }

                    _host.ImageCache.UpdateCache(
                        capturedIndexAtStart,
                        nextBitmap,
                        true,
                        _host.ZoomLevel,
                        _host.CurrentBitmap);
                }
            }

            if (nextBitmap != null && !token.IsCancellationRequested && _host.CurrentIndex == capturedIndexAtStart)
            {
                var oldBitmap = _host.CurrentBitmap;
                _host.CurrentBitmap = nextBitmap;
                _host.LeftBitmap = null;
                _host.RightBitmap = null;

                if (!_host.IsSeamlessScroll)
                {
                    _host.ImageViewportNavigationService.ResetPanForBitmap(
                        _host.MainCanvas!,
                        nextBitmap,
                        _host.ZoomLevel);
                }

                _host.MainCanvas?.Invalidate();
                ShowImageUI();
                UpdateStatusBar(entry, _host.CurrentBitmap);
                _ = _host.RerenderPdfCurrentPageAsync();

                if (oldBitmap != null && oldBitmap != nextBitmap && !IsBitmapInCache(oldBitmap))
                {
                    _host.ImageCache.SafeDisposeBitmap(oldBitmap);
                }
            }

            await _host.AddToRecentAsync(false);
            _host.FocusRoot();
        }

        private async Task DisplayTextEntryAsync(ImageEntry entry)
        {
            if (!_host.IsTextMode ||
                _host.CurrentTextFilePath != entry.FilePath ||
                _host.CurrentTextArchiveEntryKey != entry.ArchiveEntryKey)
            {
                await _host.LoadTextEntryAsync(entry);
            }
            else
            {
                if (_host.AozoraPendingTargetLine != 0)
                {
                    string fileName = Path.GetFileName(_host.CurrentTextFilePath ?? "");
                    await _host.ReloadTextDisplayFromCacheAsync(fileName, _host.AozoraPendingTargetLine);
                }
                else
                {
                    if (_host.IsVerticalMode) _host.InvalidateVerticalTextCanvas();
                    else if (_host.IsAozoraMode) _host.InvalidateAozoraTextCanvas();
                    else _host.TextScrollViewer?.InvalidateArrange();
                }
            }

            await _host.AddToRecentAsync(false);
        }

        private async Task DisplayEpubEntryAsync(ImageEntry entry, CancellationToken token)
        {
            if (!_host.IsEpubMode || _host.CurrentEpubFilePath != entry.FilePath)
            {
                await _host.LoadEpubEntryAsync(entry, token);
            }
            else
            {
                if (_host.PendingEpubChapterIndex >= 0 || _host.AozoraPendingTargetLine != 0)
                {
                    int targetChapter = _host.PendingEpubChapterIndex >= 0
                        ? _host.PendingEpubChapterIndex
                        : _host.CurrentEpubChapterIndex;

                    await _host.LoadEpubChapterAsync(
                        targetChapter,
                        _host.AozoraPendingTargetLine,
                        _host.PendingEpubStartBlockIndex,
                        _host.PendingEpubPageIndex);

                    _host.PendingEpubChapterIndex = -1;
                    _host.PendingEpubPageIndex = -1;
                    _host.AozoraPendingTargetLine = 0;
                    _host.PendingEpubStartBlockIndex = -1;
                }
                else
                {
                    if (_host.IsVerticalMode) _host.InvalidateVerticalTextCanvas();
                    else if (_host.CurrentEpubWin2DPage?.IsImagePage == true) _host.ShowEpubImagePage(_host.CurrentEpubWin2DPage);
                    else _host.InvalidateEpubTextCanvas();
                }
            }

            await _host.AddToRecentAsync(false);
        }

        private async Task DisplayImageEntryAsync(CancellationToken token)
        {
            _host.SwitchToImageMode();

            bool canSideBySide = await _host.ImageDoublePageDecisionService.ShouldUseSideBySideAsync(
                _host.ImageEntries,
                _host.CurrentIndex,
                _host.IsSideBySideMode,
                _host.AutoDoublePageForArchive,
                _host.ArchiveSession.HasArchive,
                _host.IsPdfMode,
                _host.ZoomLevel,
                _host.CurrentBitmap,
                LoadBitmapForPreloadAsync,
                token);

            _host.IsCurrentViewSideBySide = canSideBySide;

            if (canSideBySide)
            {
                await DisplaySideBySideImagesAsync(token);
            }
            else
            {
                await DisplaySingleImageAsync(token);
            }

            await _host.AddToRecentAsync(false);
        }

        public void SyncSidebarSelection(ImageEntry entry)
        {
            try
            {
                if (_host.FileItems.Count == 0) return;

                FileItem? item = null;

                if (_host.IsWebDavMode && entry.IsWebDavEntry && !entry.IsArchiveEntry)
                {
                    item = _host.FileItems.FirstOrDefault(f => f.IsWebDav && f.WebDavPath == entry.WebDavPath);
                }
                else
                {
                    string targetPath = entry.IsArchiveEntry ? (_host.ArchiveSession.CurrentPath ?? "") : (entry.FilePath ?? "");
                    if (string.IsNullOrEmpty(targetPath)) return;

                    item = _host.FileItems.FirstOrDefault(f =>
                        f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase) ||
                        (f.IsWebDav && targetPath.Equals($"WebDAV:{f.WebDavPath}", StringComparison.OrdinalIgnoreCase)));
                }

                if (item == null) return;

                var list = _host.IsExplorerGrid ? _host.FileGridView : _host.FileListView;
                if (list.SelectedItem != item)
                {
                    list.SelectedItem = item;
                    list.ScrollIntoView(item);
                }
            }
            catch { }
        }

        private async Task DisplaySingleImageAsync(CancellationToken token)
        {
            if (_host.CurrentIndex < 0 || _host.CurrentIndex >= _host.ImageEntries.Count) return;

            var entry = _host.ImageEntries[_host.CurrentIndex];
            _host.IsAnimatedFrameActive = false;
            _host.AnimatedWebpService.Stop();

            try
            {
                if (token.IsCancellationRequested) return;

                var bitmap = await LoadImageBitmapAsync(entry, _host.MainCanvas, token);

                if (token.IsCancellationRequested)
                {
                    if (bitmap != null && !IsBitmapInCache(bitmap))
                    {
                        _host.ImageCache.SafeDisposeBitmap(bitmap);
                    }
                    return;
                }

                if (bitmap != null)
                {
                    var oldBitmap = _host.CurrentBitmap;
                    _host.CurrentBitmap = bitmap;

                    if (_host.ZoomLevel <= 1.01)
                    {
                        _host.ZoomLevel = 1.0;
                        FitToWindow();
                    }

                    _host.ImageViewportNavigationService.ResetPanForBitmap(
                        _host.MainCanvas,
                        bitmap,
                        _host.ZoomLevel);
                    ShowImageUI();
                    UpdateStatusBar(entry, _host.CurrentBitmap);
                    UpdateSharpenButtonState();
                    _host.MainCanvas?.Invalidate();
                    SyncSidebarSelection(entry);

                    if (oldBitmap != null && !IsBitmapInCache(oldBitmap) && oldBitmap != bitmap)
                    {
                        _host.ImageCache.SafeDisposeBitmap(oldBitmap);
                    }
                }
                else
                {
                    _host.FileNameText.Text = Strings.LoadImageError;
                    return;
                }

                if (_host.AnimatedWebpService.IsAnimationSupported(entry))
                {
                    _host.FileNameText.Text += Strings.Loading;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            await _host.AnimatedWebpService.StartAsync(
                                entry,
                                _host.MainCanvas!,
                                token,
                                (float)_host.ImageOptions.UpscaleFactor,
                                (float)_host.ImageOptions.SharpenAmount,
                                (float)_host.ImageOptions.SharpenThreshold,
                                (float)_host.ImageOptions.UnsharpAmount,
                                (float)_host.ImageOptions.UnsharpRadius,
                                _host.SharpenEnabled);

                            _host.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (!token.IsCancellationRequested && _host.CurrentBitmap != null)
                                {
                                    UpdateStatusBar(entry, _host.CurrentBitmap);
                                }
                            });
                        }
                        catch
                        {
                            _host.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (token.IsCancellationRequested || _host.CurrentBitmap == null) return;
                                UpdateStatusBar(entry, _host.CurrentBitmap);
                            });
                        }
                    }, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _host.FileNameText.Text = $"이미지 로드 오류: {ex.Message}";
            }
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            if (bitmap == _host.CurrentBitmap || bitmap == _host.LeftBitmap || bitmap == _host.RightBitmap) return true;
            if (_host.ImageCache.IsBitmapInCache(bitmap)) return true;
            if (_host.AnimatedWebpService.IsBitmapInCache(bitmap)) return true;
            return false;
        }

        public void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap)
        {
            _host.IsAnimatedFrameActive = true;
            var oldBitmap = _host.CurrentBitmap;
            _host.CurrentBitmap = newBitmap;

            if (oldBitmap != null && oldBitmap != newBitmap && !IsBitmapInCache(oldBitmap))
            {
                _host.ImageCache.SafeDisposeBitmap(oldBitmap);
            }

            _host.MainCanvas?.Invalidate();
        }

        public void OnAnimatedWebpAnimationStopped(object? sender, EventArgs e)
        {
            try
            {
                _host.IsAnimatedFrameActive = false;
                var bitmap = _host.CurrentBitmap;
                if (bitmap != null && _host.AnimatedWebpService.IsBitmapInCache(bitmap))
                {
                    _host.CurrentBitmap = null;
                    _host.MainCanvas?.Invalidate();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping animated image: {ex.Message}");
            }
        }

        private async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            try
            {
                var pair = await _host.SideBySideImageLoadService.LoadAsync(
                    _host.ImageEntries,
                    _host.CurrentIndex,
                    _host.NextImageOnRight,
                    _host.LeftCanvas,
                    _host.RightCanvas,
                    LoadImageBitmapAsync,
                    ReleaseBitmapIfUnused,
                    token);

                if (pair == null || token.IsCancellationRequested)
                {
                    return;
                }

                var oldLeft = _host.LeftBitmap;
                var oldRight = _host.RightBitmap;

                _host.LeftBitmap = pair.LeftBitmap;
                _host.RightBitmap = pair.RightBitmap;
                _host.CurrentBitmap = pair.RightBitmap ?? pair.LeftBitmap;

                _host.ZoomLevel = 1.0;
                FitToWindow();
                ShowImageUI();

                var primaryEntry = _host.ImageEntries[_host.CurrentIndex];
                CanvasBitmap? primaryBitmap = pair.PrimaryBitmap ?? _host.CurrentBitmap;

                if (primaryBitmap != null)
                {
                    UpdateStatusBar(primaryEntry, primaryBitmap);
                }
                else if (_host.CurrentBitmap != null)
                {
                    UpdateStatusBar(primaryEntry, _host.CurrentBitmap);
                }

                SyncSidebarSelection(primaryEntry);

                if (oldLeft != null && !IsBitmapInCache(oldLeft) && oldLeft != pair.LeftBitmap && oldLeft != pair.RightBitmap)
                {
                    _host.ImageCache.SafeDisposeBitmap(oldLeft);
                }
                if (oldRight != null && !IsBitmapInCache(oldRight) && oldRight != pair.LeftBitmap && oldRight != pair.RightBitmap)
                {
                    _host.ImageCache.SafeDisposeBitmap(oldRight);
                }
            }
            catch (Exception ex)
            {
                _host.FileNameText.Text = $"이미지 로드 실패: {ex.Message}";
            }
        }

        private void ReleaseBitmapIfUnused(CanvasBitmap? bitmap)
        {
            if (bitmap != null && !IsBitmapInCache(bitmap))
            {
                _host.ImageCache.SafeDisposeBitmap(bitmap);
            }
        }

        private ImageBitmapLoaderContext CreateImageBitmapLoaderContext()
            => new(
                ImageEntries: _host.ImageEntries,
                CurrentIndex: _host.CurrentIndex,
                ZoomLevel: _host.ZoomLevel,
                SharpenEnabled: _host.SharpenEnabled,
                SharpenParams: _host.CreateSharpenParams(),
                IsPdfMode: _host.IsPdfMode,
                IsWebDavMode: _host.IsWebDavMode,
                ArchiveSession: _host.ArchiveSession,
                WebDavService: _host.WebDavService,
                MainCanvas: _host.MainCanvas,
                LoadPdfPageBitmapAsync: (pageIndex, canvas, token, isPreload) =>
                    _host.LoadPdfPageBitmapAsync(pageIndex, canvas, token, isPreload),
                InvalidateCanvas: () => _host.MainCanvas?.Invalidate());

        private ImageViewportNavigationContext CreateImageViewportNavigationContext()
            => new()
            {
                ImageEntries = _host.ImageEntries,
                ImageCache = _host.ImageCache,
                PreloadManager = _host.PreloadManager,
                MainCanvas = _host.MainCanvas,
                GetCurrentIndex = () => _host.CurrentIndex,
                SetCurrentIndex = value => _host.CurrentIndex = value,
                GetZoomLevel = () => _host.ZoomLevel,
                SetZoomLevel = value => _host.ZoomLevel = value,
                GetCurrentBitmap = () => _host.CurrentBitmap,
                SetCurrentBitmap = value => _host.CurrentBitmap = value,
                GetLeftBitmap = () => _host.LeftBitmap,
                GetRightBitmap = () => _host.RightBitmap,
                IsPdfMode = () => _host.IsPdfMode,
                IsSharpenEnabled = () => _host.SharpenEnabled,
                GetCancellationToken = () => _host.ImageLoadingCts?.Token ?? CancellationToken.None,
                LoadPdfPageBitmapAsync = (pageIndex, canvas, token) => _host.LoadPdfPageBitmapAsync(pageIndex, canvas, token),
                LoadImageBitmapAsync = LoadImageBitmapAsync,
                LoadBitmapForPreloadAsync = LoadBitmapForPreloadAsync,
                UpdateStatusBar = UpdateStatusBar,
                SyncSidebarSelection = SyncSidebarSelection,
                ApplyZoom = ApplyZoom,
                InvalidateCanvas = () => _host.MainCanvas?.Invalidate()
            };

        private Task<CanvasBitmap?> LoadImageBitmapAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token = default)
            => _host.ImageBitmapLoader.LoadImageBitmapAsync(entry, canvas, CreateImageBitmapLoaderContext(), token);

        public async Task ToggleSharpeningAsync()
        {
            try
            {
                _host.SharpenEnabled = !_host.SharpenEnabled;
                _host.ImageCache.ClearSharpenedCache(_host.CurrentBitmap, _host.LeftBitmap, _host.RightBitmap);
                _host.AnimatedWebpService.Stop();
                _host.ImageResourceService.Clear();
                RefreshReaderCanvasesAfterSharpenChange();

                UpdateSharpenButtonState();
                _host.SaveWindowSettings();

                await DisplayCurrentImageAsync();

                if (_host.ImageEntries.Count > 0)
                {
                    StartImagePreload(prioritizeNext: true);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SharpenButton_Click: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void UpdateSharpenButtonState()
        {
            _host.MainToolbar.SetSharpenState(_host.SharpenEnabled);
        }

        public void ToggleSideBySide()
        {
            if (_host.IsPdfMode) return;
            _host.IsSideBySideMode = !_host.IsSideBySideMode;

            UpdateSideBySideButtonState();
            _host.SaveWindowSettings();
            RefreshLayoutAfterSideBySideChange();
        }

        public void ToggleNextImageSide()
        {
            _host.NextImageOnRight = !_host.NextImageOnRight;
            UpdateNextImageSideButtonState();
            _host.SaveWindowSettings();
            RefreshLayoutAfterSideBySideChange(nextImageSideOnly: true);
        }

        private void RefreshLayoutAfterSideBySideChange(bool nextImageSideOnly = false)
        {
            if (_host.IsVerticalMode)
            {
                int currentLine = _host.CurrentVerticalStartLine;
                if (!nextImageSideOnly && _host.IsEpubMode)
                {
                    _ = _host.LoadEpubChapterAsync(_host.CurrentEpubChapterIndex, targetLine: currentLine);
                }
                else
                {
                    _ = _host.PrepareVerticalTextAsync(currentLine);
                }
            }
            else if (_host.IsEpubMode)
            {
                _host.SetEpubPageIndex(_host.CurrentEpubPageIndex);
            }
            else
            {
                _ = DisplayCurrentImageAsync();
            }
        }

        public void UpdateSideBySideButtonState()
        {
            _host.MainToolbar.SetSideBySideState(_host.IsSideBySideMode);
        }

        public void UpdateNextImageSideButtonState()
        {
            _host.MainToolbar.SetNextImageSideState(_host.NextImageOnRight);
        }

        public void ShowImageUI()
        {
            _host.EmptyStatePanel.Visibility = Visibility.Collapsed;

            bool shouldShowSideBySide =
                _host.IsCurrentViewSideBySide &&
                !_host.IsPdfMode &&
                _host.ImageEntries.Count > 1;

            if (shouldShowSideBySide)
            {
                _host.MainCanvas.Visibility = Visibility.Collapsed;
                _host.SideBySideGrid.Visibility = Visibility.Visible;
            }
            else
            {
                _host.MainCanvas.Visibility = Visibility.Visible;
                _host.SideBySideGrid.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap)
        {
            var content = _host.ImageStatusBarService.Create(
                entry,
                bitmap,
                _host.ArchiveSession.CurrentPath,
                _host.IsWebDavMode ? _host.CurrentWebDavItemPath : null,
                _host.IsCurrentViewSideBySide,
                _host.IsPdfMode,
                _host.CurrentIndex,
                _host.ImageEntries.Count);

            _host.FileNameText.Text = content.FileName;
            _host.ImageInfoText.Text = content.ImageInfo;
            _host.ImageIndexText.Text = content.ImageIndex;
            _host.TextProgressText.Text = content.TextProgress;
        }

        public void ImageAreaSizeChanged(SizeChangedEventArgs e)
        {
            _host.LastCanvasWidth = e.NewSize.Width;

            if (_host.CurrentBitmap != null &&
                (_host.MainCanvas.Visibility == Visibility.Visible ||
                 _host.SideBySideGrid.Visibility == Visibility.Visible))
            {
                ApplyZoom();
            }
        }

        public async Task HandlePointerWheelAsync(PointerRoutedEventArgs e)
        {
            try
            {
                var properties = e.GetCurrentPoint(_host.ImageArea).Properties;
                var wheelDelta = properties.MouseWheelDelta;
                var isHorizontal = properties.IsHorizontalMouseWheel;

                var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                if (ctrl.HasFlag(CoreVirtualKeyStates.Down))
                {
                    if (_host.CurrentBitmap != null && (!_host.IsCurrentViewSideBySide || _host.IsPdfMode))
                    {
                        double zoomMultiplier = Math.Exp(wheelDelta * 0.001);
                        var point = e.GetCurrentPoint(_host.ImageArea).Position;
                        _host.ImageViewportNavigationService.StartSmoothZoom(
                            CreateImageViewportNavigationContext(),
                            zoomMultiplier,
                            point);

                        e.Handled = true;
                        return;
                    }
                }

                if (_host.CurrentBitmap != null &&
                    (_host.IsPdfMode || (_host.ZoomLevel > 1.01 && !_host.IsCurrentViewSideBySide)))
                {
                    if (isHorizontal)
                    {
                        await HandlePdfScrollAsync(wheelDelta, 0);
                    }
                    else
                    {
                        await HandlePdfScrollAsync(0, wheelDelta);
                    }
                    e.Handled = true;
                    return;
                }

                if (Math.Abs(wheelDelta) >= 40)
                {
                    if (wheelDelta < 0) await NavigateToNextAsync();
                    else await NavigateToPreviousAsync();
                }

                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerWheelChanged: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ManipulationStarting(ManipulationStartingRoutedEventArgs e)
        {
            e.Container = _host.ImageArea;
            e.Mode = ManipulationModes.All;
        }

        public async Task ManipulationDeltaAsync(ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
                if (_host.CurrentBitmap == null || (_host.IsCurrentViewSideBySide && !_host.IsPdfMode)) return;

                if (e.Delta.Scale != 1.0f)
                {
                    _host.ImageViewportNavigationService.ZoomAtPosition(
                        CreateImageViewportNavigationContext(),
                        e.Delta.Scale,
                        e.Position);
                }

                await HandlePdfScrollAsync(e.Delta.Translation.X, e.Delta.Translation.Y);
                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_ManipulationDelta: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ManipulationCompleted()
        {
            _host.ImageViewportNavigationService.IsTransitioning = false;
            if (_host.IsPdfMode)
            {
                _ = _host.RerenderPdfCurrentPageAsync();
            }
        }

        public async Task HandlePdfScrollAsync(double deltaX, double deltaY)
        {
            await _host.ImageViewportNavigationService.HandleScrollAsync(
                CreateImageViewportNavigationContext(),
                deltaX,
                deltaY);
        }

        public async Task PointerPressedAsync(PointerRoutedEventArgs e)
        {
            try
            {
                if (_host.ImageEntries.Count <= 1)
                    return;

                var point = e.GetCurrentPoint(_host.ImageArea);
                if (!point.Properties.IsLeftButtonPressed)
                    return;

                if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
                {
                    if (_host.ZoomLevel > 1.01 || _host.IsPdfMode)
                        return;
                }

                double half = _host.ImageArea.ActualWidth * 0.5;
                if (point.Position.X < half)
                {
                    if (_host.ShouldInvertControls) await NavigateToNextAsync(true);
                    else await NavigateToPreviousAsync(true);
                }
                else
                {
                    if (_host.ShouldInvertControls) await NavigateToPreviousAsync(true);
                    else await NavigateToNextAsync(true);
                }
                e.Handled = true;
                _host.FocusRoot();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerPressed: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ClearImageResources()
        {
            _host.ImageLoadingCts?.Cancel();
            _host.PreloadManager.CancelAll();
            _host.ImageCache?.ClearAll();
            _host.AnimatedWebpService.Stop();

            _host.CurrentBitmap = null;
            _host.LeftBitmap = null;
            _host.RightBitmap = null;

            _host.MainCanvas?.Invalidate();
            _host.LeftCanvas?.Invalidate();
            _host.RightCanvas?.Invalidate();

            _host.FileNameText.Text = "";
            _host.ImageInfoText.Text = "";
            _host.ImageIndexText.Text = "";
        }

        public Task NavigateToPreviousAsync(bool isManualClick = false)
        {
            return _host.ImageNavigationCoordinator.NavigatePreviousAsync(isManualClick);
        }

        public async Task OnSharpenParamsChangedAsync()
        {
            try
            {
                if (_host.SharpenEnabled)
                {
                    _host.ImageCache?.ClearSharpenedCache(_host.CurrentBitmap, _host.LeftBitmap, _host.RightBitmap);
                    _host.AnimatedWebpService.Stop();
                    _host.ImageResourceService.Clear();
                    RefreshReaderCanvasesAfterSharpenChange();

                    await DisplayCurrentImageAsync();

                    if (_host.ImageEntries.Count > 0)
                    {
                        StartImagePreload(prioritizeNext: true);
                    }
                }

                _host.SaveWindowSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSharpenParamsChanged: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public Task NavigateToNextAsync(bool isManualClick = false)
        {
            return _host.ImageNavigationCoordinator.NavigateNextAsync(isManualClick);
        }

        public void StartImagePreload(bool prioritizeNext)
        {
            _ = _host.PreloadManager.StartPreloadAsync(
                _host.CurrentIndex,
                _host.ImageEntries,
                _host.IsPdfMode,
                _host.ZoomLevel,
                _host.CurrentBitmap,
                _host.LeftBitmap,
                _host.RightBitmap,
                LoadBitmapForPreloadAsync,
                () => _host.MainCanvas?.Invalidate(),
                prioritizeNext: prioritizeNext,
                requireSharpening: _host.SharpenEnabled);
        }

        public string? GetCurrentNavigatingPath()
        {
            if (_host.IsWebDavMode) return _host.CurrentWebDavItemPath;
            if (_host.ArchiveSession.HasArchive && !string.IsNullOrEmpty(_host.ArchiveSession.CurrentPath)) return _host.ArchiveSession.CurrentPath;
            if (_host.IsEpubMode && !string.IsNullOrEmpty(_host.CurrentEpubFilePath)) return _host.CurrentEpubFilePath;
            if (_host.IsTextMode && !string.IsNullOrEmpty(_host.CurrentTextFilePath)) return _host.CurrentTextFilePath;
            if (_host.ImageEntries.Count > 0 && _host.CurrentIndex >= 0 && _host.CurrentIndex < _host.ImageEntries.Count)
            {
                return _host.ImageEntries[_host.CurrentIndex].FilePath;
            }

            return null;
        }

        public async Task NavigateToFileAsync(bool isNext)
        {
            await _host.AddToRecentAsync(true);
            string? currentPath = GetCurrentNavigatingPath();
            if (string.IsNullOrEmpty(currentPath)) return;

            var nextItem = FileExplorerService.GetNextNavigableFile(
                _host.FileItems,
                currentPath,
                isNext,
                _host.IsWebDavMode);

            if (nextItem != null)
            {
                if (_host.ImageEntries.Count > 0 && !nextItem.IsDirectory && !nextItem.IsArchive && !nextItem.IsPdf)
                {
                    int index = _host.ImageEntries.FindIndex(e => e.FilePath == nextItem.FullPath);
                    if (index != -1)
                    {
                        _host.CurrentIndex = index;
                        await DisplayCurrentImageAsync();
                        SyncExplorerSelection(nextItem);
                        _host.FocusRoot();
                        return;
                    }
                }

                await _host.HandleFileSelectionAsync(nextItem);
                SyncExplorerSelection(nextItem);
            }

            _host.FocusRoot();
        }

        private void SyncExplorerSelection(FileItem item)
        {
            var list = _host.IsExplorerGrid ? _host.FileGridView : _host.FileListView;
            list.SelectedItem = item;
            list.ScrollIntoView(item);
        }

        public Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, CancellationToken token)
            => _host.ImageBitmapLoader.LoadBitmapForPreloadAsync(entry, CreateImageBitmapLoaderContext(), token);

        private void RefreshReaderCanvasesAfterSharpenChange()
        {
            if (_host.IsEpubMode)
            {
                if (_host.CurrentEpubWin2DPage?.IsImagePage == true)
                {
                    _host.ShowEpubImagePage(_host.CurrentEpubWin2DPage);
                }
                else
                {
                    _host.InvalidateEpubTextCanvas();
                }
            }

            if (_host.IsVerticalMode) _host.InvalidateVerticalTextCanvas();
            if (_host.IsAozoraMode) _host.InvalidateAozoraTextCanvas();
        }
    }
}
