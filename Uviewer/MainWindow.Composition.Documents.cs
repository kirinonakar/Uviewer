using System;
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
