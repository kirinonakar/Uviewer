using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private sealed class ExplorerSidebarHostAdapter : IExplorerSidebarHost
        {
            private readonly MainWindow _window;

            public ExplorerSidebarHostAdapter(MainWindow window)
            {
                _window = window;
            }

            public ObservableCollection<FileItem> FileItems => _window._fileItems;
            public IReadOnlyList<ImageEntry> ImageEntries => _window._imageEntries;
            public int CurrentIndex { get => _window._currentIndex; set => _window._currentIndex = value; }
            public bool IsNavigatingRecent => _window._isNavigatingRecent;
            public bool IsExplorerGrid => _window._isExplorerGrid;
            public ExplorerSortMode ExplorerSortMode => _window._explorerSortMode;
            public string? CurrentExplorerPath => _window._currentExplorerPath;
            public bool IsWebDavMode => _window._isWebDavMode;
            public string? CurrentWebDavPath => _window._currentWebDavPath;
            public double ExplorerThumbnailSize
            {
                get => _window._explorerThumbnailSize;
                set => _window._explorerThumbnailSize = System.Math.Clamp(value, 64, 180);
            }

            public bool ShowFolderThumbnails
            {
                get => _window._showFolderThumbnails;
                set => _window._showFolderThumbnails = value;
            }

            public FileItem? ExplorerContextItem
            {
                get => _window._explorerContextItem;
                set => _window._explorerContextItem = value;
            }

            public ListView FileListView => _window.FileListView;
            public GridView FileGridView => _window.FileGridView;
            public Button ToggleViewButton => _window.ToggleViewButton;
            public Button SortByDateButton => _window.SortByDateButton;
            public FontIcon SortIcon => _window.SortIcon;
            public Slider ThumbnailSizeSlider => _window.ThumbnailSizeSlider;
            public TextBlock ThumbnailSizeValueText => _window.ThumbnailSizeValueText;
            public CheckBox FolderThumbnailsCheckBox => _window.FolderThumbnailsCheckBox;
            public TextBlock CurrentPathText => _window.CurrentPathText;

            public void ClearWebDavForLocalExplorer()
            {
                _window.DisconnectWebDav();
                _window._currentWebDavItemPath = null;
            }

            public void SyncSidebarSelection(ImageEntry entry) =>
                _window.SyncSidebarSelection(entry);

            public void RefreshPointerCursor() =>
                _window._windowChromeController?.RefreshPointerCursor();

            public void SaveWindowSettings() =>
                _window._windowSettingsCoordinator?.SaveWindowSettings();

            public void ShowNotification(string message, string icon = "\uE735", string color = "Gold") =>
                _window.ShowNotification(message, icon, color);

            public bool IsCurrentFile(string path) =>
                _window._documentOpenStateQuery.IsCurrentFile(path);

            public void ToggleSideBySide() =>
                _window._imageViewerController.ToggleSideBySide();

            public Task HandleWebDavFileSelectionAsync(FileItem item) =>
                _window.HandleWebDavFileSelectionAsync(item);

            public Task LoadWebDavFolderAsync(string remotePath) =>
                _window.LoadWebDavFolderAsync(remotePath);

            public Task OpenLocalDocumentAsync(string path, bool saveCurrentPositionBeforeOpen) =>
                _window._localDocumentOpenCoordinator.OpenExistingFilePathAsync(
                    path,
                    saveCurrentPositionBeforeOpen);

            public Task NavigateToPreviousImageAsync() =>
                _window.NavigateToPreviousAsync();

            public Task NavigateToNextImageAsync() =>
                _window.NavigateToNextAsync();

            public Task DisplayCurrentImageAsync() =>
                _window.DisplayCurrentImageAsync();
        }
    }
}
