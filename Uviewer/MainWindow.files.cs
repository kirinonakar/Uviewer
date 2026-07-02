using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private Services.ArchiveSession _archiveSession = null!;

        #region File Operations

        private async void PrevFileButton_Click(object sender, RoutedEventArgs e)
        {
            try { await NavigateToFileAsync(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

        private async void NextFileButton_Click(object sender, RoutedEventArgs e)
        {
            try { await NavigateToFileAsync(true); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowNotification(ex.Message, "\uE783", "Red"); }
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _documentNavigationCoordinator.NavigatePreviousAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrevPageButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _documentNavigationCoordinator.NavigateNextAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NextPageButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async Task LoadImageFromFileAsync(StorageFile file, bool isInitial = false)
            => await _localImageDocumentController.LoadImageFromFileAsync(file, isInitial);

        private async Task LoadImagesFromFolderAsync(StorageFolder folder)
            => await _localImageDocumentController.LoadImagesFromFolderAsync(folder);

        private void RefreshCurrentImageStatusBar()
        {
            if (_currentBitmap == null || _currentIndex < 0 || _currentIndex >= _imageEntries.Count)
            {
                return;
            }

            UpdateStatusBar(_imageEntries[_currentIndex], _currentBitmap);
        }

        private Task LoadImagesFromArchiveAsync(string archivePath)
            => _archiveDocumentController.LoadImagesFromArchiveAsync(archivePath);

        private async Task<bool> CloseCurrentArchiveAsync()
            => await _archiveDocumentController.CloseCurrentArchiveAsync();

        #endregion

        #region Folder Explorer
        
        private void LoadExplorerFolder(string path) =>
            _explorerSidebarController.LoadFolder(path);

        private void ToggleExplorerViewButton_Click(object sender, RoutedEventArgs e)
        {
            _explorerSidebarController.ToggleViewMode();
        }

        private void ApplyThumbnailSettingsToControls() =>
            _explorerSidebarController.ApplyThumbnailSettingsToControls();

        private void ApplyThumbnailSizeToFileItems() =>
            _explorerSidebarController.ApplyThumbnailSizeToFileItems();

        private void InitializeExplorerContextMenus()
        {
            if (_explorerSidebarController == null) return;
            _explorerSidebarController.InitializeContextMenus();
        }

        private void ExplorerView_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
            _explorerSidebarController.HandleRightTapped(e);

        private async Task ReleaseCurrentDocumentForExplorerOperationAsync(string targetPath, bool targetIsDirectory)
        {
            var shouldClose = IsExplorerOperationTargetOpen(targetPath, targetIsDirectory);

            if (!shouldClose) return;

            _sevenZipExtraction.CancelExtraction();
            _imageLoadingCts?.Cancel();
            _preloadManager.CancelAll();
            _globalTextCts?.Cancel();

            await _pdfDocumentController.CloseCurrentPdfAsync();
            await _epubReaderController.CloseCurrentEpubAsync();
            await CloseCurrentArchiveAsync();
            CloseCurrentText();

            ClearViewerAfterExplorerDeletion();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private bool IsExplorerOperationTargetOpen(string targetPath, bool targetIsDirectory)
            => _documentOpenStateQuery.IsExplorerOperationTargetOpen(targetPath, targetIsDirectory);

        private void ClearViewerAfterExplorerDeletion()
        {
            _animatedWebpService.Stop();
            _fastNavigationService?.StopTimers();
            _imageCache?.ClearAll();
            _imageViewerState.ClearBitmaps();
            _imageEntries = new List<ImageEntry>();
            _currentIndex = -1;
            _isCurrentViewSideBySide = false;

            SwitchToImageMode();
            EmptyStatePanel.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Visible;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            FastNavOverlay.Visibility = Visibility.Collapsed;
            FileNameText.Text = Strings.FileSelectPlaceholder;
            ImageInfoText.Text = string.Empty;
            ImageIndexText.Text = string.Empty;
            TextProgressText.Text = string.Empty;

            MainCanvas?.Invalidate();
            LeftCanvas?.Invalidate();
            RightCanvas?.Invalidate();
        }

        private async Task OpenLocalFilePathAsync(string path)
        {
            await _localDocumentOpenCoordinator.OpenExistingFilePathAsync(path, saveCurrentPositionBeforeOpen: false);
        }

        private void UpdateToggleViewButtonTooltip()
        {
            _explorerSidebarController.UpdateToggleViewButtonTooltip();
        }

        private void ThumbnailSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _explorerSidebarController.HandleThumbnailSizeChanged(e.NewValue);
        }

        private void FolderThumbnailsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _explorerSidebarController.HandleFolderThumbnailsChanged(FolderThumbnailsCheckBox?.IsChecked == true);
        }

        private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _explorerSidebarController.HandleSelectionChanged(FileListView.SelectedItem as FileItem);

        private void FileGridView_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _explorerSidebarController.HandleSelectionChanged(FileGridView.SelectedItem as FileItem);

        private void FileGridView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) =>
            _explorerSidebarController.HandleGridPreviewKeyDown(e);

        private void FileListView_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) =>
            _explorerSidebarController.HandleListPreviewKeyDown(e);

        private Task HandleFileSelectionAsync(FileItem item) =>
            _explorerSidebarController.HandleFileSelectionAsync(item);

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e) =>
            _explorerSidebarController.HandleBrowseFolderClick();

        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            _explorerSidebarController.CycleSortMode();
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.Name);
        }

        private void SortByDateDesc_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.DateDesc);
        }

        private void SortByDateAsc_Click(object sender, RoutedEventArgs e)
        {
            ApplyExplorerSortMode(ExplorerSortMode.DateAsc);
        }

        private void ApplyExplorerSortMode(ExplorerSortMode sortMode)
        {
            _explorerSidebarController.ApplySortMode(sortMode);
        }

        private void RefreshExplorer() =>
            _explorerSidebarController.Refresh();

        private void UpdateSortIcon() =>
            _explorerSidebarController.UpdateSortIcon();

        private void ParentFolderButton_Click(object sender, RoutedEventArgs e) =>
            _explorerSidebarController.HandleParentFolderClick();

        private async void AddToFavoritesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _bookmarkInteractionController.AddCurrentFavoriteAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddToFavoritesMenuItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private void SidebarFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private void SidebarRecentButton_Click(object sender, RoutedEventArgs e)
        {
            // Flyout is opened automatically by Button
        }

        private void FileItem_ItemClick(object sender, ItemClickEventArgs e) =>
            _explorerSidebarController.HandleItemClick(e.ClickedItem as FileItem);

        private Task NavigateToParentFolderAsync() =>
            _explorerSidebarController.NavigateToParentFolderAsync();

        private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSidebar();
        }

        private void ToggleSidebar()
        {
            if (_windowState.IsSidebarVisible && !_windowState.IsFullscreen)
            {
                _windowState.SidebarWidth = (int)SidebarColumn.Width.Value > 200 ? (int)SidebarColumn.Width.Value : 320;
            }
            if ((int)SidebarColumn.Width.Value > 200)
            {
                _windowState.IsSidebarVisible = true;
            }
            _windowState.IsSidebarVisible = !_windowState.IsSidebarVisible;
            SidebarGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            if (SplitterGrid != null)
            {
                SplitterGrid.Visibility = _windowState.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            SidebarColumn.Width = _windowState.IsSidebarVisible ? new GridLength(_windowState.SidebarWidth) : new GridLength(0);
            _windowSettingsCoordinator.SaveWindowSettings();
        }

        #endregion


        #region Sidebar Resizing

        private void Splitter_ResizeCompleted(object? sender, EventArgs e)
        {
            if (SidebarColumn.Width.IsAbsolute && SidebarColumn.Width.Value > 200)
            {
                _windowState.SidebarWidth = (int)SidebarColumn.Width.Value;
            }
        }

        #endregion

    }
}
