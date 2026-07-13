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
                        OpenArchiveAsync = path => window._archiveDocumentController.LoadImagesFromArchiveAsync(path),
                        OpenPdfAsync = path => window._pdfDocumentController.LoadImagesFromPdfAsync(path),
                        OpenStorageFileAsync = (file, isInitial) =>
                            window._localImageDocumentController.LoadImageFromFileAsync(file, isInitial),
                        OpenFolderAsync = folder => window._localImageDocumentController.LoadImagesFromFolderAsync(folder),
                        SaveCurrentPositionAsync = () => window._bookmarkInteractionController.AddCurrentRecentAsync(true),
                        LoadExplorerFolder = path => window._explorerSidebarController.LoadFolder(path),
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
                        GetCurrentNavigatingPath = window._imageViewerController.GetCurrentNavigatingPath,
                        GetCurrentPdfPath = () => window._currentPdfPath,
                        GetCurrentArchivePath = () => window._archiveSession.CurrentPath,
                        GetCurrentEpubPath = () => window._currentEpubFilePath,
                        GetCurrentTextPath = () => window._currentTextFilePath,
                        IsWebDavMode = () => window._isWebDavMode,
                        GetCurrentIndex = () => window._imageViewerState.CurrentIndex,
                        GetImageEntries = () => window._imageViewerState.Entries
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
                            RefreshExplorer = () => window._explorerSidebarController.Refresh(),
                            IsTargetOpen = window._documentOpenStateQuery.IsExplorerOperationTargetOpen,
                            ReleaseCurrentDocumentAsync = (path, isDirectory) =>
                                window._explorerDocumentReleaseService.ReleaseForExplorerOperationAsync(path, isDirectory),
                            OpenLocalFilePathAsync = path =>
                                window._localDocumentOpenCoordinator.OpenExistingFilePathAsync(
                                    path,
                                    saveCurrentPositionBeforeOpen: false),
                            ClearViewer = () => window._explorerDocumentReleaseService.ResetViewerAfterExplorerOperation(),
                            ShowNotification = window.ShowNotification
                        });
                }

                public static void InitializeExplorerDocumentRelease(MainWindow window)
                {
                    window._explorerDocumentReleaseService = new ExplorerDocumentReleaseService(
                        new ExplorerDocumentReleaseHandlers
                        {
                            IsTargetOpen = window._documentOpenStateQuery.IsExplorerOperationTargetOpen,
                            CancelExtraction = window._sevenZipExtraction.CancelExtraction,
                            CancelImageLoading = () => window._imageViewerState.ImageLoadingCts?.Cancel(),
                            CancelPreloading = window._preloadManager.CancelAll,
                            CancelTextLoading = () => window._globalTextCts?.Cancel(),
                            CloseCurrentPdfAsync = () => window._pdfDocumentController.CloseCurrentPdfAsync(),
                            CloseCurrentEpubAsync = () => window._epubReaderController.CloseCurrentEpubAsync(),
                            CloseCurrentArchiveAsync = window._archiveDocumentController.CloseCurrentArchiveAsync,
                            CloseCurrentText = window.CloseCurrentText,
                            StopAnimatedImages = window._animatedWebpService.Stop,
                            StopFastNavigation = window._fastNavigationService.StopTimers,
                            ClearImageCache = window._imageCache.ClearAll,
                            ResetImageState = () =>
                            {
                                window._imageViewerState.ClearBitmaps();
                                window._imageViewerState.Entries = new List<Uviewer.Models.ImageEntry>();
                                window._imageViewerState.CurrentIndex = -1;
                                window._imageViewerState.IsCurrentViewSideBySide = false;
                            },
                            ApplyClearedImageUi = () =>
                            {
                                window.SwitchToImageMode();
                                window.ImageViewer.ShowEmptyState();
                                window.FileNameText.Text = Strings.FileSelectPlaceholder;
                                window.ImageInfoText.Text = string.Empty;
                                window.ImageIndexText.Text = string.Empty;
                                window.TextProgressText.Text = string.Empty;
                            }
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
                            CloseCurrentPdfAsync = () => window._pdfDocumentController.CloseCurrentPdfAsync(),
                            CloseCurrentEpubAsync = () => window._epubReaderController.CloseCurrentEpubAsync(),
                            DisplayCurrentImageAsync = window._imageViewerController.DisplayCurrentImageAsync,
                            LoadBitmapForPreloadAsync = window._imageViewerController.LoadBitmapForPreloadAsync,
                            GetCurrentIndex = () => window._imageViewerState.CurrentIndex,
                            SetCurrentIndex = value => window._imageViewerState.CurrentIndex = value,
                            GetImageEntries = () => window._imageViewerState.Entries,
                            SetImageEntries = value => window._imageViewerState.Entries = value ?? new List<Uviewer.Models.ImageEntry>(),
                            IsPdfOpen = () => window._pdfDocumentController.HasOpenDocument,
                            GetZoomLevel = () => window._zoomLevel,
                            GetCurrentBitmap = () => window._imageViewerState.CurrentBitmap,
                            GetLeftBitmap = () => window._imageViewerState.LeftBitmap,
                            GetRightBitmap = () => window._imageViewerState.RightBitmap,
                            IsSharpenEnabled = () => window._imageViewerState.IsSharpenEnabled,
                            CancelImageLoading = () => window._imageViewerState.ImageLoadingCts?.Cancel(),
                            CancelTextLoading = () => window._globalTextCts?.Cancel(),
                            InvalidateMainCanvas = () => window.MainCanvas?.Invalidate(),
                            SetWindowTitle = value => window.Title = value,
                            SetStatusText = value => window.FileNameText.Text = value
                        });
                }

                public static void InitializeLocalImageController(MainWindow window)
                {
                    window._localImageDocumentController = new LocalImageDocumentController(
                        window._sevenZipExtraction,
                        window._preloadManager,
                        window._imageViewerState,
                        window.DispatcherQueue,
                        new LocalImageDocumentHandlers
                        {
                            CloseCurrentArchiveAsync = () => window._archiveDocumentController.CloseCurrentArchiveAsync(),
                            CloseCurrentPdfAsync = () => window._pdfDocumentController.CloseCurrentPdfAsync(),
                            CloseCurrentEpubAsync = () => window._epubReaderController.CloseCurrentEpubAsync(),
                            CloseCurrentText = window.CloseCurrentText,
                            DisplayCurrentImageAsync = window._imageViewerController.DisplayCurrentImageAsync,
                            CancelImageLoading = () => window._imageViewerState.ImageLoadingCts?.Cancel(),
                            CancelTextLoading = () => window._globalTextCts?.Cancel(),
                            CancelExplorerThumbnailLoading = window._explorerState.CancelThumbnailLoading,
                            PrepareForImageLoad = window._imageViewerController.PrepareForImageLoad,
                            RefreshCurrentStatusBar = () =>
                            {
                                if (window._imageViewerState.CurrentBitmap == null ||
                                    window._imageViewerState.CurrentIndex < 0 ||
                                    window._imageViewerState.CurrentIndex >= window._imageViewerState.Entries.Count)
                                {
                                    return;
                                }

                                window._imageViewerController.UpdateStatusBar(
                                    window._imageViewerState.Entries[window._imageViewerState.CurrentIndex],
                                    window._imageViewerState.CurrentBitmap);
                            },
                            SetStatusText = value => window.FileNameText.Text = value
                        });
                }

                public static void InitializePdfController(MainWindow window)
                {
                    window._pdfDocumentController = new PdfDocumentController(
                        window._documentSessionTracker,
                        window._documentSearchService,
                        window._preloadManager,
                        window._imageCache,
                        window._imageViewerState,
                        window._imageViewportNavigationService,
                        window._fastNavigationService,
                        window._tocService,
                        new PdfDocumentHandlers
                        {
                            IsWindowClosing = () => window._isWindowClosing,
                            CloseCurrentArchiveAsync = () => window._archiveDocumentController.CloseCurrentArchiveAsync(),
                            CloseCurrentEpubAsync = () => window._epubReaderController.CloseCurrentEpubAsync(),
                            DisplayCurrentImageAsync = window._imageViewerController.DisplayCurrentImageAsync,
                            LoadBitmapForPreloadAsync = window._imageViewerController.LoadBitmapForPreloadAsync,
                            GetPendingPdfPageIndex = () => window._pendingPdfPageIndex,
                            SetPendingPdfPageIndex = value => window._pendingPdfPageIndex = value,
                            GetZoomLevel = () => window._zoomLevel,
                            GetMainCanvas = () => window.MainCanvas,
                            CancelImageLoading = () => window._imageViewerState.ImageLoadingCts?.Cancel(),
                            SwitchToImageMode = window.SwitchToImageMode,
                            UpdateStatusBar = window._imageViewerController.UpdateStatusBar,
                            SetPdfTocVisible = isVisible =>
                                window.DispatcherQueue.TryEnqueue(() => window.MainToolbar.SetPdfTocVisible(isVisible)),
                            SetPdfGoToPageVisible = window.MainToolbar.SetPdfGoToPageVisible,
                            SetSideBySideToolbarVisible = window.MainToolbar.SetSideBySideToolbarVisible,
                            SetSharpenControlsVisible = window.MainToolbar.SetSharpenControlsVisible,
                            SetPdfTocTitle = window.MainToolbar.SetPdfTocTitle,
                            SetPdfTocItems = window.MainToolbar.SetPdfTocItems,
                            ScrollPdfTocIntoView = window.MainToolbar.ScrollPdfTocIntoView,
                            HidePdfTocFlyout = window.MainToolbar.HidePdfTocFlyout,
                            SetZoomLevel = value => window._zoomLevel = value,
                            ResetImageViewportNavigation = direction => window._imageViewportNavigationService.Reset(scrollDirection: direction),
                            InvalidateMainCanvas = () => window.MainCanvas?.Invalidate(),
                            ApplyPdfClosedUi = () =>
                            {
                                window.DispatcherQueue.TryEnqueue(() =>
                                {
                                    window.MainToolbar.SetPdfTocVisible(false);
                                    window.MainToolbar.SetPdfGoToPageVisible(false);
                                    window.Title = "Uviewer - Image & Text Viewer";
                                });
                            },
                            SetTitle = value => window.Title = value,
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
                        NavigateEpubAsync = direction => window._epubReaderController.NavigateEpubAsync(direction),
                        NavigateAozoraPage = window.NavigateAozoraPage,
                        NavigateTextPage = window.NavigateTextPage,
                        NavigatePreviousImageAsync = () => window._imageViewerController.NavigateToPreviousAsync(),
                        NavigateNextImageAsync = () => window._imageViewerController.NavigateToNextAsync()
                    });
                }
            }
        }
    }
}
