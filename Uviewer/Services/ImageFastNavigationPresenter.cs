using System;
using System.Threading;
using System.Threading.Tasks;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal sealed class ImageFastNavigationPresenter
    {
        private readonly IImageFastNavigationHost _host;
        private readonly Func<Task> _displayCurrentImageAsync;

        public ImageFastNavigationPresenter(
            IImageFastNavigationHost host,
            Func<Task> displayCurrentImageAsync)
        {
            _host = host;
            _displayCurrentImageAsync = displayCurrentImageAsync;
        }

        public void UpdateFastNavigationUI()
        {
            if (_host.CurrentIndex < 0 || _host.CurrentIndex >= _host.ImageEntries.Count)
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
                    // A failed/empty load does not publish a new status bar.
                    // Clear only our placeholder; preserve successful image info.
                    if (_host.ImageInfoText.Text == Strings.FastNavText)
                        _host.ImageInfoText.Text = string.Empty;
                });

            _host.FileNameText.Text = _host.FastNavigationService.DisplayName;
            _host.ImageIndexText.Text = _host.FastNavigationService.GetImageIndexMessage();
            _host.TextProgressText.Text = "";
            _host.ImageInfoText.Text = Strings.FastNavText;
        }

        public async Task ResetFastNavigationAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            if (_host.CurrentIndex >= 0 && _host.CurrentIndex < _host.ImageEntries.Count)
            {
                _host.Signal7zJump();
                await _displayCurrentImageAsync();
            }

            if (token.IsCancellationRequested) return;
            // FastNavigationService owns overlay cleanup for success, failure,
            // and transitions back to normal navigation.
            _host.MainCanvas?.Invalidate();
        }
    }
}
