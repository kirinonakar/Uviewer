using Microsoft.UI.Xaml;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isImageManagerMode;
        private GridLength _savedSidebarWidth;
        private GridLength _savedSplitterWidth;
        private GridLength _savedViewerWidth;
        private Visibility _savedSidebarVisibility;
        private Visibility _savedImageViewerVisibility;
        private Visibility _savedTextReaderVisibility;
        private Visibility _savedEpubReaderVisibility;
        private Visibility _savedSplitterVisibility;

        private void ToggleImageManagerMode()
        {
            if (_isImageManagerMode)
            {
                _isImageManagerMode = false;
                SidebarColumn.Width = _savedSidebarWidth;
                SplitterColumn.Width = _savedSplitterWidth;
                ViewerColumn.Width = _savedViewerWidth;
                ExplorerSidebar.Visibility = _savedSidebarVisibility;
                ExplorerSidebar.SetImageManagerLayout(false, _isExplorerGrid);
                ImageViewer.Visibility = _savedImageViewerVisibility;
                TextReader.Visibility = _savedTextReaderVisibility;
                EpubReader.Visibility = _savedEpubReaderVisibility;
                SplitterGrid.Visibility = _savedSplitterVisibility;
            }
            else
            {
                _isImageManagerMode = true;
                _savedSidebarWidth = SidebarColumn.Width;
                _savedSplitterWidth = SplitterColumn.Width;
                _savedViewerWidth = ViewerColumn.Width;
                _savedSidebarVisibility = ExplorerSidebar.Visibility;
                _savedImageViewerVisibility = ImageViewer.Visibility;
                _savedTextReaderVisibility = TextReader.Visibility;
                _savedEpubReaderVisibility = EpubReader.Visibility;
                _savedSplitterVisibility = SplitterGrid.Visibility;

                ExplorerSidebar.Visibility = Visibility.Visible;
                ExplorerSidebar.SetImageManagerLayout(true, _isExplorerGrid);
                SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                ViewerColumn.Width = new GridLength(0);
                SplitterGrid.Visibility = Visibility.Collapsed;
                ImageViewer.Visibility = Visibility.Collapsed;
                TextReader.Visibility = Visibility.Collapsed;
                EpubReader.Visibility = Visibility.Collapsed;
            }

            MainToolbar.SetImageManagerMode(_isImageManagerMode);
        }
    }
}
