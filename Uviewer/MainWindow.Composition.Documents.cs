using System;
using System.Collections.Generic;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class DocumentFeatureComposition
            {
                public static void InitializeLocalOpenCoordinator(MainWindow window)
                {
                    window._localDocumentOpenCoordinator = new LocalDocumentOpenCoordinator(new LocalDocumentOpenHandlers
                    {
                        OpenArchiveAsync = window.LoadImagesFromArchiveAsync,
                        OpenPdfAsync = window.LoadImagesFromPdfAsync,
                        OpenStorageFileAsync = window.LoadImageFromFileAsync,
                        OpenFolderAsync = window.LoadImagesFromFolderAsync,
                        SaveCurrentPositionAsync = () => window.AddToRecentAsync(true),
                        LoadExplorerFolder = window.LoadExplorerFolder,
                        LoadExplorerFolderInBackground = window.LoadExplorerFolderInBackground,
                        ShouldLoadExplorerFolder = folderPath =>
                            !string.Equals(folderPath, window._currentExplorerPath, StringComparison.OrdinalIgnoreCase),
                        HideEmptyState = () =>
                        {
                            if (window.EmptyStatePanel != null) window.EmptyStatePanel.Visibility = Visibility.Collapsed;
                        }
                    });
                }

                public static void InitializeFileOpenController(MainWindow window)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    window._fileOpenController = new FileOpenController(
                        hwnd,
                        window._localDocumentOpenCoordinator,
                        window.ShowNotification);
                }

                public static void InitializeDocumentOpenStateQuery(MainWindow window)
                {
                    window._documentOpenStateQuery = new DocumentOpenStateQuery(new DocumentOpenStateQueryHandlers
                    {
                        GetCurrentNavigatingPath = window.GetCurrentNavigatingPath,
                        GetCurrentPdfPath = () => window._currentPdfPath,
                        GetCurrentArchivePath = () => window._archiveSession.CurrentPath,
                        GetCurrentEpubPath = () => window._currentEpubFilePath,
                        GetCurrentTextPath = () => window._currentTextFilePath,
                        IsWebDavMode = () => window._isWebDavMode,
                        GetCurrentIndex = () => window._currentIndex,
                        GetImageEntries = () => window._imageEntries
                    });
                }

                public static void InitializeExplorerItemOperations(MainWindow window)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                    window._externalProgramSettingsController = new ExternalProgramSettingsController(
                        hwnd,
                        () => window.RootGrid.XamlRoot,
                        () => window.RootGrid.ActualTheme,
                        () => window._externalProgramPath,
                        value => window._externalProgramPath = value,
                        window.MainToolbar.SetExternalProgramPath,
                        () => window._windowSettingsCoordinator.SaveWindowSettings(),
                        message => window.ShowNotification(message));

                    window._explorerItemOperationController = new ExplorerItemOperationController(
                        window._explorerItemLaunchService,
                        new ExplorerItemOperationHandlers
                        {
                            GetXamlRoot = () => window.RootGrid.XamlRoot,
                            GetRequestedTheme = () => window.RootGrid.ActualTheme,
                            GetExternalProgramPath = () => window._externalProgramPath,
                            SelectExternalProgramAsync = window._externalProgramSettingsController.SelectExternalProgramAsync,
                            RefreshExplorer = window.RefreshExplorer,
                            IsTargetOpen = window._documentOpenStateQuery.IsExplorerOperationTargetOpen,
                            ReleaseCurrentDocumentAsync = window.ReleaseCurrentDocumentForExplorerOperationAsync,
                            OpenLocalFilePathAsync = window.OpenLocalFilePathAsync,
                            ClearViewer = window.ClearViewerAfterExplorerDeletion,
                            ShowNotification = window.ShowNotification
                        });
                }

                public static void InitializeArchiveController(MainWindow window)
                {
                    window._archiveDocumentController = new ArchiveDocumentController(
                        window._archiveSession,
                        window._sevenZipExtraction,
                        window._preloadManager,
                        window._imageCache,
                        window._imageViewerState,
                        window._fastNavigationService,
                        window.DispatcherQueue,
                        new ArchiveDocumentHandlers
                        {
                            CloseCurrentPdfAsync = window.CloseCurrentPdfAsync,
                            CloseCurrentEpubAsync = window.CloseCurrentEpubAsync,
                            DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                            LoadBitmapForPreloadAsync = window.LoadBitmapForPreloadAsync,
                            GetCurrentIndex = () => window._currentIndex,
                            SetCurrentIndex = value => window._currentIndex = value,
                            GetImageEntries = () => window._imageEntries,
                            SetImageEntries = value => window._imageEntries = value ?? new List<Uviewer.Models.ImageEntry>(),
                            IsPdfOpen = () => window._currentPdfDocument != null,
                            GetZoomLevel = () => window._zoomLevel,
                            GetCurrentBitmap = () => window._currentBitmap,
                            GetLeftBitmap = () => window._leftBitmap,
                            GetRightBitmap = () => window._rightBitmap,
                            IsSharpenEnabled = () => window._sharpenEnabled,
                            CancelImageLoading = () => window._imageLoadingCts?.Cancel(),
                            CancelTextLoading = () => window._globalTextCts?.Cancel(),
                            InvalidateMainCanvas = () => window.MainCanvas?.Invalidate(),
                            SetWindowTitle = value => window.Title = value,
                            SetStatusText = value => window.FileNameText.Text = value
                        });
                }

                public static void InitializeNavigation(MainWindow window)
                {
                    window._documentNavigationCoordinator = new DocumentNavigationCoordinator(new DocumentNavigationHandlers
                    {
                        IsVerticalMode = () => window._isVerticalMode,
                        IsEpubMode = () => window._isEpubMode,
                        IsTextMode = () => window._isTextMode,
                        IsAozoraMode = () => window._isAozoraMode,
                        NavigateVerticalPage = window.NavigateVerticalPage,
                        NavigateEpubAsync = window.NavigateEpubAsync,
                        NavigateAozoraPage = window.NavigateAozoraPage,
                        NavigateTextPage = window.NavigateTextPage,
                        NavigatePreviousImageAsync = () => window.NavigateToPreviousAsync(),
                        NavigateNextImageAsync = () => window.NavigateToNextAsync()
                    });
                }
            }
        }
    }
}
