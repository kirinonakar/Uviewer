using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageViewerController
    {
        private readonly IImageViewerRuntimeHost _host;
        private readonly ImageBitmapLifetimeCoordinator _bitmapLifetimeCoordinator;
        private readonly ImageDocumentEntryCoordinator _documentEntryCoordinator;
        private readonly ImageExplorerNavigationCoordinator _explorerNavigationCoordinator;
        private readonly ImageFastNavigationPresenter _fastNavigationPresenter;
        private readonly ImageInputCoordinator _inputCoordinator;
        private readonly ImagePdfPageDisplayCoordinator _pdfPageDisplayCoordinator;
        private readonly ImagePreloadCoordinator _preloadCoordinator;
        private readonly ImageViewerPresenter _presenter;
        private readonly ImageSideBySideDisplayCoordinator _sideBySideDisplayCoordinator;
        private readonly ImageSingleDisplayCoordinator _singleDisplayCoordinator;
        private readonly ImageViewingOptionsCoordinator _viewingOptionsCoordinator;
        private readonly ImageZoomCoordinator _zoomCoordinator;

        public ImageViewerController(ImageViewerControllerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            _host = dependencies.Host;
            _bitmapLifetimeCoordinator = new ImageBitmapLifetimeCoordinator(dependencies.BitmapLifetimeHost);
            _documentEntryCoordinator = new ImageDocumentEntryCoordinator(dependencies.DocumentEntryHost);
            _explorerNavigationCoordinator = new ImageExplorerNavigationCoordinator(
                dependencies.ExplorerNavigationHost,
                DisplayCurrentImageAsync);
            _fastNavigationPresenter = new ImageFastNavigationPresenter(
                dependencies.FastNavigationHost,
                DisplayCurrentImageAsync);
            _inputCoordinator = new ImageInputCoordinator(
                dependencies.InputHost,
                CreateImageViewportNavigationContext,
                NavigateToPreviousAsync,
                NavigateToNextAsync,
                ApplyZoom);
            _pdfPageDisplayCoordinator = new ImagePdfPageDisplayCoordinator(
                dependencies.PdfPageDisplayHost,
                IsBitmapInCache,
                ShowImageUI,
                UpdateStatusBar);
            _presenter = new ImageViewerPresenter(dependencies.PresentationHost);
            _preloadCoordinator = new ImagePreloadCoordinator(dependencies.PreloadHost);
            _sideBySideDisplayCoordinator = new ImageSideBySideDisplayCoordinator(
                dependencies.SideBySideDisplayHost,
                LoadImageBitmapAsync,
                ReleaseBitmapIfUnused,
                IsBitmapInCache,
                FitToWindow,
                ShowImageUI,
                UpdateStatusBar,
                SyncSidebarSelection);
            _singleDisplayCoordinator = new ImageSingleDisplayCoordinator(
                dependencies.SingleDisplayHost,
                LoadImageBitmapAsync,
                IsBitmapInCache,
                FitToWindow,
                ShowImageUI,
                UpdateStatusBar,
                UpdateSharpenButtonState,
                SyncSidebarSelection);
            _viewingOptionsCoordinator = new ImageViewingOptionsCoordinator(
                dependencies.ViewingOptionsHost,
                DisplayCurrentImageAsync,
                StartImagePreload,
                UpdateSharpenButtonState,
                UpdateSideBySideButtonState,
                UpdateNextImageSideButtonState);
            _zoomCoordinator = new ImageZoomCoordinator(dependencies.ZoomHost);
        }

        public void UpdateFastNavigationUI() => _fastNavigationPresenter.UpdateFastNavigationUI();

        public Task ResetFastNavigationAsync() => _fastNavigationPresenter.ResetFastNavigationAsync();

        public void ZoomActual() => _zoomCoordinator.ZoomActual();

        public void ZoomIn() => _zoomCoordinator.ZoomIn();

        public void ZoomOut() => _zoomCoordinator.ZoomOut();

        public void FitToWindow() => _zoomCoordinator.FitToWindow();

        public void ApplyZoom() => _zoomCoordinator.ApplyZoom();

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
            await _pdfPageDisplayCoordinator.DisplayPdfPageAsync(entry, capturedIndexAtStart, token);
        }

        private async Task DisplayTextEntryAsync(ImageEntry entry)
        {
            await _documentEntryCoordinator.DisplayTextEntryAsync(entry);
        }

        private async Task DisplayEpubEntryAsync(ImageEntry entry, CancellationToken token)
        {
            await _documentEntryCoordinator.DisplayEpubEntryAsync(entry, token);
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
            _explorerNavigationCoordinator.SyncSidebarSelection(entry);
        }

        private async Task DisplaySingleImageAsync(CancellationToken token)
        {
            await _singleDisplayCoordinator.DisplaySingleImageAsync(token);
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            return _bitmapLifetimeCoordinator.IsBitmapInCache(bitmap);
        }

        public void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap)
        {
            _bitmapLifetimeCoordinator.OnAnimatedWebpFrameUpdated(sender, newBitmap);
        }

        public void OnAnimatedWebpAnimationStopped(object? sender, EventArgs e)
        {
            _bitmapLifetimeCoordinator.OnAnimatedWebpAnimationStopped(sender, e);
        }

        private async Task DisplaySideBySideImagesAsync(CancellationToken token)
        {
            await _sideBySideDisplayCoordinator.DisplaySideBySideImagesAsync(token);
        }

        private void ReleaseBitmapIfUnused(CanvasBitmap? bitmap)
        {
            _bitmapLifetimeCoordinator.ReleaseBitmapIfUnused(bitmap);
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
            await _viewingOptionsCoordinator.ToggleSharpeningAsync();
        }

        public void UpdateSharpenButtonState()
        {
            _presenter.UpdateSharpenButtonState();
        }

        public void ToggleSideBySide()
        {
            _viewingOptionsCoordinator.ToggleSideBySide();
        }

        public void ToggleNextImageSide()
        {
            _viewingOptionsCoordinator.ToggleNextImageSide();
        }

        public void UpdateSideBySideButtonState()
        {
            _presenter.UpdateSideBySideButtonState();
        }

        public void UpdateNextImageSideButtonState()
        {
            _presenter.UpdateNextImageSideButtonState();
        }

        public void ShowImageUI()
        {
            _presenter.ShowImageUI();
        }

        public void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap)
        {
            _presenter.UpdateStatusBar(entry, bitmap);
        }

        public void ImageAreaSizeChanged(SizeChangedEventArgs e) =>
            _inputCoordinator.ImageAreaSizeChanged(e);

        public Task HandlePointerWheelAsync(PointerRoutedEventArgs e) =>
            _inputCoordinator.HandlePointerWheelAsync(e);

        public void ManipulationStarting(ManipulationStartingRoutedEventArgs e) =>
            _inputCoordinator.ManipulationStarting(e);

        public Task ManipulationDeltaAsync(ManipulationDeltaRoutedEventArgs e) =>
            _inputCoordinator.ManipulationDeltaAsync(e);

        public void ManipulationCompleted() =>
            _inputCoordinator.ManipulationCompleted();

        public Task HandlePdfScrollAsync(double deltaX, double deltaY) =>
            _inputCoordinator.HandleScrollAsync(deltaX, deltaY);

        public Task PointerPressedAsync(PointerRoutedEventArgs e) =>
            _inputCoordinator.PointerPressedAsync(e);

        public void ClearImageResources()
        {
            _bitmapLifetimeCoordinator.ClearImageResources();
        }

        public Task NavigateToPreviousAsync(bool isManualClick = false)
        {
            return _host.ImageNavigationCoordinator.NavigatePreviousAsync(isManualClick);
        }

        public async Task OnSharpenParamsChangedAsync()
        {
            await _viewingOptionsCoordinator.OnSharpenParamsChangedAsync();
        }

        public Task NavigateToNextAsync(bool isManualClick = false)
        {
            return _host.ImageNavigationCoordinator.NavigateNextAsync(isManualClick);
        }

        public void StartImagePreload(bool prioritizeNext)
        {
            _preloadCoordinator.StartImagePreload(prioritizeNext, LoadBitmapForPreloadAsync);
        }

        public string? GetCurrentNavigatingPath()
        {
            return _explorerNavigationCoordinator.GetCurrentNavigatingPath();
        }

        public async Task NavigateToFileAsync(bool isNext)
        {
            await _explorerNavigationCoordinator.NavigateToFileAsync(isNext);
        }

        public Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, CancellationToken token)
            => _preloadCoordinator.LoadBitmapForPreloadAsync(entry, CreateImageBitmapLoaderContext, token);

    }
}
