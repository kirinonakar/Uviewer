using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private ImageViewerController _imageViewerController = null!;

        private void UpdateFastNavigationUI() => _imageViewerController.UpdateFastNavigationUI();

        private Task ResetFastNavigation() => _imageViewerController.ResetFastNavigationAsync();

        private void ZoomIn() => _imageViewerController.ZoomIn();

        private void ZoomOut() => _imageViewerController.ZoomOut();

        private void FitToWindow() => _imageViewerController.FitToWindow();

        private void ApplyZoom() => _imageViewerController.ApplyZoom();

        private Task DisplayCurrentImageAsync() => _imageViewerController.DisplayCurrentImageAsync();

        private void SyncSidebarSelection(ImageEntry entry) => _imageViewerController.SyncSidebarSelection(entry);

        private bool IsBitmapInCache(CanvasBitmap bitmap) => _imageViewerController.IsBitmapInCache(bitmap);

        private void OnAnimatedWebpFrameUpdated(object? sender, CanvasBitmap newBitmap) =>
            _imageViewerController.OnAnimatedWebpFrameUpdated(sender, newBitmap);

        private void OnAnimatedWebpAnimationStopped(object? sender, System.EventArgs e) =>
            _imageViewerController.OnAnimatedWebpAnimationStopped(sender, e);

        internal void UpdateSharpenButtonState() => _imageViewerController.UpdateSharpenButtonState();

        internal void UpdateSideBySideButtonState() => _imageViewerController.UpdateSideBySideButtonState();

        internal void UpdateNextImageSideButtonState() => _imageViewerController.UpdateNextImageSideButtonState();

        private void ShowImageUI() => _imageViewerController.ShowImageUI();

        private void UpdateStatusBar(ImageEntry entry, CanvasBitmap bitmap) =>
            _imageViewerController.UpdateStatusBar(entry, bitmap);

        private Task HandlePdfScrollAsync(double deltaX, double deltaY) =>
            _imageViewerController.HandlePdfScrollAsync(deltaX, deltaY);

        private void ClearImageResources() => _imageViewerController.ClearImageResources();

        private Task NavigateToPreviousAsync(bool isManualClick = false) =>
            _imageViewerController.NavigateToPreviousAsync(isManualClick);

        private async void OnSharpenParamsChanged() =>
            await _imageViewerController.OnSharpenParamsChangedAsync();

        private Task NavigateToNextAsync(bool isManualClick = false) =>
            _imageViewerController.NavigateToNextAsync(isManualClick);

        private void StartImagePreload(bool prioritizeNext) =>
            _imageViewerController.StartImagePreload(prioritizeNext);

        private string? GetCurrentNavigatingPath() => _imageViewerController.GetCurrentNavigatingPath();

        private Task NavigateToFileAsync(bool isNext) => _imageViewerController.NavigateToFileAsync(isNext);

        private Task<CanvasBitmap?> LoadBitmapForPreloadAsync(ImageEntry entry, CancellationToken token) =>
            _imageViewerController.LoadBitmapForPreloadAsync(entry, token);

    }
}
