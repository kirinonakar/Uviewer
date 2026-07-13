using Microsoft.Graphics.Canvas;
using Uviewer.Models;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal sealed class ImageViewerPresenter
    {
        private readonly IImagePresentationHost _host;

        public ImageViewerPresenter(IImagePresentationHost host)
        {
            _host = host;
        }

        public void UpdateSharpenButtonState()
        {
            _host.MainToolbar.SetSharpenState(_host.SharpenEnabled);
        }

        public void UpdateSideBySideButtonState()
        {
            _host.MainToolbar.SetSideBySideState(_host.IsSideBySideMode);
        }

        public void UpdateNextImageSideButtonState()
        {
            _host.MainToolbar.SetNextImageSideState(_host.NextImageOnRight);
        }

        public void PrepareForImageLoad()
        {
            _host.MainCanvas.Visibility = Visibility.Collapsed;
            _host.SideBySideGrid.Visibility = Visibility.Collapsed;
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
    }
}
