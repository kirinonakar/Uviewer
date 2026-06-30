using System;
using System.Threading.Tasks;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal sealed class ImageFastNavigationController
    {
        private readonly IImageViewerHost _host;
        private readonly Func<Task> _displayCurrentImageAsync;

        public ImageFastNavigationController(
            IImageViewerHost host,
            Func<Task> displayCurrentImageAsync)
        {
            _host = host;
            _displayCurrentImageAsync = displayCurrentImageAsync;
        }

        public void UpdateFastNavigationUI()
        {
            if (_host.CurrentIndex < 0 || _host.ImageEntries.Count == 0)
                return;

            var currentEntry = _host.ImageEntries[_host.CurrentIndex];
            string displayName = FileExplorerService.GetFormattedDisplayName(currentEntry.DisplayName, currentEntry.IsArchiveEntry);

            _host.FastNavigationService.UpdateState(
                _host.CurrentIndex,
                _host.ImageEntries.Count,
                displayName,
                _host.IsCurrentViewSideBySide);

            _host.Signal7zJump();

            _host.FastNavigationService.ShowOverlay(
                showCallback: () =>
                {
                    _host.FastNavText.Text = _host.FastNavigationService.GetOverlayMessage();
                    _host.FastNavOverlay.Visibility = Visibility.Visible;
                },
                hideCallback: () =>
                {
                    _host.FastNavOverlay.Visibility = Visibility.Collapsed;
                });

            _host.FileNameText.Text = _host.FastNavigationService.DisplayName;
            _host.ImageIndexText.Text = _host.FastNavigationService.GetImageIndexMessage();
            _host.TextProgressText.Text = "";
            _host.ImageInfoText.Text = "빠르게 넘어가는 중...";
        }

        public async Task ResetFastNavigationAsync()
        {
            _host.FastNavigationService.StopOverlayTimer();
            if (_host.CurrentIndex >= 0 && _host.CurrentIndex < _host.ImageEntries.Count)
            {
                _host.Signal7zJump();
                await _displayCurrentImageAsync();
            }

            _host.FastNavOverlay.Visibility = Visibility.Collapsed;
            _host.MainCanvas?.Invalidate();
        }
    }
}
