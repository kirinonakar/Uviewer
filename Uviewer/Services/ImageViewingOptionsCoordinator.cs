using System;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class ImageViewingOptionsCoordinator
    {
        private readonly IImageViewingOptionsHost _host;
        private readonly Func<Task> _displayCurrentImageAsync;
        private readonly Action<bool> _startPreload;
        private readonly Action _updateSharpenButtonState;
        private readonly Action _updateSideBySideButtonState;
        private readonly Action _updateNextImageSideButtonState;

        public ImageViewingOptionsCoordinator(
            IImageViewingOptionsHost host,
            Func<Task> displayCurrentImageAsync,
            Action<bool> startPreload,
            Action updateSharpenButtonState,
            Action updateSideBySideButtonState,
            Action updateNextImageSideButtonState)
        {
            _host = host;
            _displayCurrentImageAsync = displayCurrentImageAsync;
            _startPreload = startPreload;
            _updateSharpenButtonState = updateSharpenButtonState;
            _updateSideBySideButtonState = updateSideBySideButtonState;
            _updateNextImageSideButtonState = updateNextImageSideButtonState;
        }

        public async Task ToggleSharpeningAsync()
        {
            try
            {
                _host.SharpenEnabled = !_host.SharpenEnabled;
                ResetSharpenedResources();
                RefreshReaderCanvasesAfterSharpenChange();

                _updateSharpenButtonState();
                _host.SaveWindowSettings();

                await _displayCurrentImageAsync();
                StartPreloadIfPossible();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SharpenButton_Click: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ToggleSideBySide()
        {
            if (_host.IsPdfMode) return;
            _host.IsSideBySideMode = !_host.IsSideBySideMode;

            _updateSideBySideButtonState();
            _host.SaveWindowSettings();
            RefreshLayoutAfterSideBySideChange();
        }

        public void ToggleNextImageSide()
        {
            _host.NextImageOnRight = !_host.NextImageOnRight;
            _updateNextImageSideButtonState();
            _host.SaveWindowSettings();
            RefreshLayoutAfterSideBySideChange(nextImageSideOnly: true);
        }

        public async Task OnSharpenParamsChangedAsync()
        {
            try
            {
                if (_host.SharpenEnabled)
                {
                    ResetSharpenedResources();
                    RefreshReaderCanvasesAfterSharpenChange();

                    await _displayCurrentImageAsync();
                    StartPreloadIfPossible();
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

        private void ResetSharpenedResources()
        {
            _host.ImageCache.ClearSharpenedCache(_host.CurrentBitmap, _host.LeftBitmap, _host.RightBitmap);
            _host.AnimatedWebpService.Stop();
            _host.ImageResourceService.Clear();
        }

        private void StartPreloadIfPossible()
        {
            if (_host.ImageEntries.Count > 0)
            {
                _startPreload(true);
            }
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
                _ = _displayCurrentImageAsync();
            }
        }

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
