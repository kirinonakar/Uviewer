using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.Storage.Pickers;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal interface IExplorerSidebarHost
    {
        ObservableCollection<FileItem> FileItems { get; }
        IReadOnlyList<ImageEntry> ImageEntries { get; }
        int CurrentIndex { get; set; }
        bool IsNavigatingRecent { get; }
        bool IsExplorerGrid { get; }
        ExplorerSortMode ExplorerSortMode { get; }
        string? CurrentExplorerPath { get; }
        bool IsWebDavMode { get; }
        string? CurrentWebDavPath { get; }
        double ExplorerThumbnailSize { get; set; }
        bool ShowFolderThumbnails { get; set; }
        FileItem? ExplorerContextItem { get; set; }

        ListView FileListView { get; }
        GridView FileGridView { get; }
        Button ToggleViewButton { get; }
        Button SortByDateButton { get; }
        FontIcon SortIcon { get; }
        Slider ThumbnailSizeSlider { get; }
        TextBlock ThumbnailSizeValueText { get; }
        CheckBox FolderThumbnailsCheckBox { get; }
        TextBlock CurrentPathText { get; }

        void ClearWebDavForLocalExplorer();
        void SyncSidebarSelection(ImageEntry entry);
        void RefreshPointerCursor();
        void SaveWindowSettings();
        void ShowNotification(string message, string icon = "\uE735", string color = "Gold");
        bool IsCurrentFile(string path);
        void ToggleSideBySide();
        Task HandleWebDavFileSelectionAsync(FileItem item);
        Task LoadWebDavFolderAsync(string remotePath);
        Task OpenLocalDocumentAsync(string path, bool saveCurrentPositionBeforeOpen);
        Task NavigateToPreviousImageAsync();
        Task NavigateToNextImageAsync();
        Task DisplayCurrentImageAsync();
    }

    internal sealed class ExplorerSidebarController
    {
        private readonly ExplorerController _explorerController;
        private readonly ExplorerItemOperationController _itemOperationController;
        private readonly IExplorerSidebarHost _host;
        private readonly IntPtr _windowHandle;

        public ExplorerSidebarController(
            ExplorerController explorerController,
            ExplorerItemOperationController itemOperationController,
            IExplorerSidebarHost host,
            IntPtr windowHandle)
        {
            _explorerController = explorerController ?? throw new ArgumentNullException(nameof(explorerController));
            _itemOperationController = itemOperationController ?? throw new ArgumentNullException(nameof(itemOperationController));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _windowHandle = windowHandle;
        }

        public void LoadFolder(string path)
        {
            if (_host.IsWebDavMode)
            {
                _host.ClearWebDavForLocalExplorer();
            }

            _explorerController.LoadFolder(
                path,
                currentPath => _host.CurrentPathText.Text = currentPath,
                ex => _host.CurrentPathText.Text = $"오류: {ex.Message}",
                () =>
                {
                    ApplyThumbnailSizeToFileItems();
                    SyncCurrentExplorerSelection();
                });
        }

        private void SyncCurrentExplorerSelection()
        {
            if (_host.CurrentIndex >= 0 && _host.CurrentIndex < _host.ImageEntries.Count)
            {
                _host.SyncSidebarSelection(_host.ImageEntries[_host.CurrentIndex]);
            }
        }

        public void ToggleViewMode()
        {
            _explorerController.ToggleViewMode();
            UpdateExplorerView();
        }

        private void UpdateExplorerView()
        {
            if (_host.IsExplorerGrid)
            {
                _host.FileListView.Visibility = Visibility.Collapsed;
                _host.FileGridView.Visibility = Visibility.Visible;

                if (_host.ToggleViewButton.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE8B9";
                }
            }
            else
            {
                _host.FileListView.Visibility = Visibility.Visible;
                _host.FileGridView.Visibility = Visibility.Collapsed;

                if (_host.ToggleViewButton.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE80A";
                }
            }

            ApplyToggleViewButtonTooltip();
        }

        public void UpdateToggleViewButtonTooltip() =>
            ApplyToggleViewButtonTooltip();

        public void ApplyThumbnailSettingsToControls()
        {
            _host.ExplorerThumbnailSize = Math.Clamp(_host.ExplorerThumbnailSize, 64, 180);
            ApplyExplorerThumbnailOptions();

            if (Math.Abs(_host.ThumbnailSizeSlider.Value - _host.ExplorerThumbnailSize) > 0.1)
            {
                _host.ThumbnailSizeSlider.Value = _host.ExplorerThumbnailSize;
            }

            _host.ThumbnailSizeValueText.Text = $"{_host.ExplorerThumbnailSize:F0}px";
            _host.FolderThumbnailsCheckBox.IsChecked = _host.ShowFolderThumbnails;

            ApplyThumbnailSizeToFileItems();
        }

        private void ApplyExplorerThumbnailOptions()
        {
            _explorerController.ThumbnailDecodePixelWidth = Math.Max(
                200,
                (int)Math.Ceiling(_host.ExplorerThumbnailSize * 2));
            _explorerController.ShowFolderThumbnails = _host.ShowFolderThumbnails;
        }

        public void ApplyThumbnailSizeToFileItems()
        {
            foreach (var item in _host.FileItems)
            {
                item.ApplyThumbnailSize(_host.ExplorerThumbnailSize);
            }
        }

        public void InitializeContextMenus()
        {
            _host.FileListView.ContextFlyout = CreateContextFlyout();
            _host.FileGridView.ContextFlyout = CreateContextFlyout();
        }

        private MenuFlyout CreateContextFlyout()
        {
            var flyout = new MenuFlyout();

            var openExternalItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenExternal, Icon = new FontIcon { Glyph = "\uE8E5" } };
            var openDefaultItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenDefault, Icon = new FontIcon { Glyph = "\uE8E5" } };
            var openExplorerItem = new MenuFlyoutItem { Text = Strings.ExplorerOpenInWindowsExplorer, Icon = new FontIcon { Glyph = "\uED25" } };
            var refreshItem = new MenuFlyoutItem { Text = Strings.ExplorerRefresh, Icon = new FontIcon { Glyph = "\uE72C" } };
            var renameItem = new MenuFlyoutItem { Text = Strings.ExplorerRename, Icon = new FontIcon { Glyph = "\uE8AC" } };
            var deleteItem = new MenuFlyoutItem { Text = Strings.ExplorerDelete, Icon = new FontIcon { Glyph = "\uE74D" } };

            openExternalItem.Click += async (_, _) => await _itemOperationController.OpenWithExternalProgramAsync(GetContextItem());
            openDefaultItem.Click += (_, _) => _itemOperationController.OpenWithDefaultProgram(GetContextItem());
            openExplorerItem.Click += (_, _) => _itemOperationController.OpenInWindowsExplorer(GetContextItem());
            refreshItem.Click += (_, _) => Refresh();
            renameItem.Click += async (_, _) => await _itemOperationController.RenameAsync(GetContextItem());
            deleteItem.Click += async (_, _) => await _itemOperationController.DeleteAsync(GetContextItem());

            flyout.Opening += (_, _) =>
            {
                var item = GetContextItem();
                var hasLocalItem = item != null && !item.IsWebDav;
                var canModify = hasLocalItem && !item!.IsParentDirectory;
                var canOpen = hasLocalItem && !item!.IsParentDirectory;

                openExternalItem.IsEnabled = canOpen;
                openDefaultItem.IsEnabled = canOpen;
                openExplorerItem.IsEnabled = hasLocalItem;
                renameItem.IsEnabled = canModify;
                deleteItem.IsEnabled = canModify;
            };

            flyout.Items.Add(openExternalItem);
            flyout.Items.Add(openDefaultItem);
            flyout.Items.Add(openExplorerItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(refreshItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(renameItem);
            flyout.Items.Add(deleteItem);

            return flyout;
        }

        public void HandleRightTapped(RightTappedRoutedEventArgs e)
        {
            _host.ExplorerContextItem = FindExplorerItemFromSource(e.OriginalSource as DependencyObject);
            _host.RefreshPointerCursor();
        }

        private FileItem? GetContextItem()
        {
            if (_host.ExplorerContextItem != null)
            {
                return _host.ExplorerContextItem;
            }

            return _host.FileGridView.Visibility == Visibility.Visible
                ? _host.FileGridView.SelectedItem as FileItem
                : _host.FileListView.SelectedItem as FileItem;
        }

        public void HandleThumbnailSizeChanged(double newValue)
        {
            _host.ExplorerThumbnailSize = Math.Clamp(newValue, 64, 180);
            ApplyExplorerThumbnailOptions();
            ApplyThumbnailSizeToFileItems();
            _host.ThumbnailSizeValueText.Text = $"{_host.ExplorerThumbnailSize:F0}px";
            _host.SaveWindowSettings();
        }

        public void HandleFolderThumbnailsChanged(bool isChecked)
        {
            _host.ShowFolderThumbnails = isChecked;
            ApplyExplorerThumbnailOptions();
            _explorerController.RefreshThumbnails(clearExisting: false);
            _host.SaveWindowSettings();
        }

        public void HandleSelectionChanged(FileItem? item)
        {
            _ = HandleSelectionChangedAsync(item);
        }

        public void HandleGridPreviewKeyDown(KeyRoutedEventArgs e)
        {
            _ = HandleGridPreviewKeyDownAsync(e);
        }

        public void HandleListPreviewKeyDown(KeyRoutedEventArgs e)
        {
            _ = HandleListPreviewKeyDownAsync(e);
        }

        public void HandleBrowseFolderClick()
        {
            _ = BrowseAndLoadFolderAsync();
        }

        public void HandleParentFolderClick()
        {
            _ = NavigateToParentFolderWithNotificationAsync();
        }

        public void HandleItemClick(FileItem? item)
        {
            _ = HandleItemClickAsync(item);
        }

        private bool IsCurrentFile(string path) => _host.IsCurrentFile(path);

        public async Task HandleFileSelectionAsync(FileItem item)
        {
            if (_host.IsNavigatingRecent) return;

            if (item.IsWebDav)
            {
                await _host.HandleWebDavFileSelectionAsync(item);
                return;
            }

            if (item.IsDirectory)
            {
                if (!Directory.Exists(item.FullPath))
                {
                    _host.ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    Refresh();
                }

                return;
            }

            if (item.IsArchive || item.IsPdf || item.IsImage || item.IsText || item.IsEpub)
            {
                if (!File.Exists(item.FullPath))
                {
                    _host.ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                    Refresh();
                    return;
                }

                await _host.OpenLocalDocumentAsync(
                    item.FullPath,
                    saveCurrentPositionBeforeOpen: true);
            }
        }

        public void ApplySortMode(ExplorerSortMode sortMode)
        {
            _explorerController.SetSortMode(sortMode);
            UpdateSortIcon();
            Refresh();
        }

        public void CycleSortMode()
        {
            var nextMode = _host.ExplorerSortMode switch
            {
                ExplorerSortMode.Name => ExplorerSortMode.DateDesc,
                ExplorerSortMode.DateDesc => ExplorerSortMode.DateAsc,
                _ => ExplorerSortMode.Name
            };

            ApplySortMode(nextMode);
        }

        public void Refresh()
        {
            if (_host.IsWebDavMode && !string.IsNullOrEmpty(_host.CurrentWebDavPath))
            {
                _ = _host.LoadWebDavFolderAsync(_host.CurrentWebDavPath);
            }
            else if (!string.IsNullOrEmpty(_host.CurrentExplorerPath))
            {
                LoadFolder(_host.CurrentExplorerPath);
            }
        }

        public void UpdateSortIcon()
        {
            switch (_host.ExplorerSortMode)
            {
                case ExplorerSortMode.DateDesc:
                    _host.SortIcon.Glyph = "\uE1FD";
                    ToolTipService.SetToolTip(_host.SortByDateButton, Strings.SortByDateDescTooltip);
                    break;

                case ExplorerSortMode.DateAsc:
                    _host.SortIcon.Glyph = "\uE110";
                    ToolTipService.SetToolTip(_host.SortByDateButton, Strings.SortByDateAscTooltip);
                    break;

                default:
                    _host.SortIcon.Glyph = "\uE174";
                    ToolTipService.SetToolTip(_host.SortByDateButton, Strings.SortByNameTooltip);
                    break;
            }
        }

        public async Task NavigateToParentFolderAsync()
        {
            if (_host.IsWebDavMode &&
                !string.IsNullOrEmpty(_host.CurrentWebDavPath) &&
                _host.CurrentWebDavPath != "/")
            {
                var parentPath = _host.CurrentWebDavPath.TrimEnd('/');
                var lastSlash = parentPath.LastIndexOf('/');
                var parent = lastSlash > 0 ? parentPath.Substring(0, lastSlash + 1) : "/";
                await _host.LoadWebDavFolderAsync(parent);
                return;
            }

            if (!string.IsNullOrEmpty(_host.CurrentExplorerPath))
            {
                var parentDir = Directory.GetParent(_host.CurrentExplorerPath);
                if (parentDir != null)
                {
                    LoadFolder(parentDir.FullName);
                }
            }
        }

        private async Task HandleSelectionChangedAsync(FileItem? item)
        {
            try
            {
                if (item == null || IsCurrentFile(item.FullPath)) return;

                await HandleFileSelectionAsync(item);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in explorer selection changed: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task HandleGridPreviewKeyDownAsync(KeyRoutedEventArgs e)
        {
            try
            {
                if (_host.ImageEntries.Count > 0)
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Enter:
                            if (_host.FileGridView.SelectedItem is FileItem item && item.IsDirectory)
                            {
                                await OpenDirectoryAsync(item);
                            }
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Left:
                            await _host.NavigateToPreviousImageAsync();
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Right:
                            await _host.NavigateToNextImageAsync();
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Up:
                            MoveSelection(-1);
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Down:
                            MoveSelection(1);
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Space:
                            _host.ToggleSideBySide();
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.Home:
                            _host.CurrentIndex = 0;
                            await _host.DisplayCurrentImageAsync();
                            e.Handled = true;
                            break;

                        case Windows.System.VirtualKey.End:
                            _host.CurrentIndex = _host.ImageEntries.Count - 1;
                            await _host.DisplayCurrentImageAsync();
                            e.Handled = true;
                            break;
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.Enter &&
                         _host.FileGridView.SelectedItem is FileItem item &&
                         item.IsDirectory)
                {
                    await OpenDirectoryAsync(item);
                    e.Handled = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileGridView_PreviewKeyDown: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task HandleListPreviewKeyDownAsync(KeyRoutedEventArgs e)
        {
            try
            {
                if (_host.ImageEntries.Count > 0)
                {
                    if (e.Key == Windows.System.VirtualKey.Home)
                    {
                        _host.CurrentIndex = 0;
                        await _host.DisplayCurrentImageAsync();
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Windows.System.VirtualKey.End)
                    {
                        _host.CurrentIndex = _host.ImageEntries.Count - 1;
                        await _host.DisplayCurrentImageAsync();
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key == Windows.System.VirtualKey.Enter &&
                    _host.FileListView.SelectedItem is FileItem item &&
                    item.IsDirectory)
                {
                    await OpenDirectoryAsync(item);
                    e.Handled = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileListView_PreviewKeyDown: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void MoveSelection(int direction)
        {
            if (_host.FileGridView.Visibility == Visibility.Visible)
            {
                var newIndex = _host.FileGridView.SelectedIndex + direction;
                if (newIndex >= 0 && newIndex < _host.FileItems.Count)
                {
                    _host.FileGridView.SelectedIndex = newIndex;
                    _host.FileGridView.ScrollIntoView(_host.FileGridView.SelectedItem);
                }
            }
            else
            {
                var newIndex = _host.FileListView.SelectedIndex + direction;
                if (newIndex >= 0 && newIndex < _host.FileItems.Count)
                {
                    _host.FileListView.SelectedIndex = newIndex;
                    _host.FileListView.ScrollIntoView(_host.FileListView.SelectedItem);
                }
            }
        }

        private async Task BrowseAndLoadFolderAsync()
        {
            try
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };

                WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    LoadFolder(folder.Path);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in BrowseFolderButton_Click: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task NavigateToParentFolderWithNotificationAsync()
        {
            try
            {
                await NavigateToParentFolderAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ParentFolderButton_Click: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task HandleItemClickAsync(FileItem? item)
        {
            try
            {
                if (item == null || !item.IsDirectory) return;

                await OpenDirectoryAsync(item);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FileItem_ItemClick: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void ApplyToggleViewButtonTooltip()
        {
            var baseTooltip = _host.IsExplorerGrid
                ? Strings.ListViewTooltip
                : Strings.ToggleViewTooltip;
            ToolTipService.SetToolTip(
                _host.ToggleViewButton,
                $"{baseTooltip}\n{Strings.RightClickSettingsHint}");
        }

        private async Task OpenDirectoryAsync(FileItem item)
        {
            if (item.IsWebDav && !string.IsNullOrEmpty(item.WebDavPath))
            {
                await _host.LoadWebDavFolderAsync(item.WebDavPath);
                return;
            }

            LoadFolder(item.FullPath);
        }

        private static FileItem? FindExplorerItemFromSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is FileItem item)
                {
                    return item;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }
}
