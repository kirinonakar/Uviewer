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

        private void ApplyThumbnailSettingsToControls() =>
            _explorerSidebarController.ApplyThumbnailSettingsToControls();

        private void ApplyThumbnailSizeToFileItems() =>
            _explorerSidebarController.ApplyThumbnailSizeToFileItems();

        private void InitializeExplorerContextMenus()
        {
            if (_explorerSidebarController == null) return;
            _explorerSidebarController.InitializeContextMenus();
        }

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

        private Task HandleFileSelectionAsync(FileItem item) =>
            _explorerSidebarController.HandleFileSelectionAsync(item);

        private void RefreshExplorer() =>
            _explorerSidebarController.Refresh();

        private void UpdateSortIcon() =>
            _explorerSidebarController.UpdateSortIcon();

        private Task NavigateToParentFolderAsync() =>
            _explorerSidebarController.NavigateToParentFolderAsync();

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
