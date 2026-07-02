using Microsoft.UI.Xaml;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static partial class ShellComposition
            {
                public static void InitializeToolbar(MainWindow window)
                {
                    window.MainToolbar.ImageOptions = window.ImageOptions;
                    window._mainToolbarController = new MainToolbarController(
                        window.MainToolbar,
                        new MainToolbarControllerHandlers
                        {
                            ChangeFont = () => window._documentReaderController.FontMenu_Click(window.MainToolbar, new RoutedEventArgs()),
                            ApplyEncodingAsync = encoding => window._documentReaderController.ApplyEncodingSelectionAsync(encoding),
                            ChangeColors = () => window._documentReaderController.ColorsMenu_Click(window.MainToolbar, new RoutedEventArgs()),
                            ChangeUiFont = () => window._documentReaderController.UiFontMenu_Click(window.MainToolbar, new RoutedEventArgs()),
                            SelectExternalProgramAsync = () => window._externalProgramSettingsController.SelectExternalProgramAsync(),
                            ApplyLanguageAsync = language => window._documentReaderController.ApplyLanguageSelectionAsync(language),
                            SetMatchControlDirection = isChecked =>
                            {
                                window._matchControlDirection = isChecked;
                                window._windowSettingsCoordinator.SaveWindowSettings();
                            },
                            SetAllowMultipleInstances = isChecked =>
                            {
                                window._allowMultipleInstances = isChecked;
                                window._windowSettingsCoordinator.SaveWindowSettings();
                            },
                            SetAutoDoublePageForArchive = isChecked =>
                            {
                                window._autoDoublePageForArchive = isChecked;
                                window._windowSettingsCoordinator.SaveWindowSettings();
                                _ = window.DisplayCurrentImageAsync();
                            },
                            ShowAboutAsync = () => window.ShowAboutDialog(),
                            ToggleGlobalTheme = () => window._windowChromeController.ToggleGlobalTheme(),
                            TogglePin = window.TogglePin,
                            ToggleAlwaysOnTop = window.ToggleAlwaysOnTop,
                            ToggleSidebar = window.ToggleSidebar,
                            AddToFavoritesAsync = () => window._bookmarkInteractionController.AddCurrentFavoriteAsync(),
                            OpenFileAsync = () => window._fileOpenController.OpenFileAsync(),
                            OpenFolderAsync = () => window._fileOpenController.OpenFolderAsync(),
                            HandleFavoriteClickedAsync = item => window._bookmarkInteractionController.HandleFavoriteClickedAsync(item),
                            HandleFavoriteRemoveClickedAsync = item => window._bookmarkInteractionController.HandleFavoriteRemoveClickedAsync(item),
                            HandleFavoritePinClickedAsync = item => window._bookmarkInteractionController.HandleFavoritePinClickedAsync(item),
                            HandleRecentClickedAsync = item => window._bookmarkInteractionController.HandleRecentClickedAsync(item),
                            HandleRecentRemoveClickedAsync = item => window._bookmarkInteractionController.HandleRecentRemoveClickedAsync(item),
                            ShowPdfToc = () => window._pdfDocumentController.ShowToc(),
                            OpenPdfTocItem = item => window._pdfDocumentController.OpenTocItem(item),
                            ShowGoToPage = () => window._documentReaderController.GoToPageButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            SearchRequested = window.SearchButton_RightTapped,
                            ZoomOut = window.ZoomOut,
                            ZoomIn = window.ZoomIn,
                            ZoomFit = window.FitToWindow,
                            ZoomActual = () => window._imageViewerController.ZoomActual(),
                            ToggleAozora = () => window._documentReaderController.AozoraToggleButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            ToggleVertical = () => window._documentReaderController.VerticalToggleButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            ToggleFont = () => window._documentReaderController.FontToggleButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            SetDefaultFont1 = () => window._documentReaderController.SetDefaultFont1MenuItem_Click(window.MainToolbar, new RoutedEventArgs()),
                            SetDefaultFont2 = () => window._documentReaderController.SetDefaultFont2MenuItem_Click(window.MainToolbar, new RoutedEventArgs()),
                            ResetDefaultFonts = () => window._documentReaderController.ResetDefaultFontsMenuItem_Click(window.MainToolbar, new RoutedEventArgs()),
                            ShowTextToc = () => window._documentReaderController.TocButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            TextTocItemClicked = window._documentReaderController.TocListView_ItemClick,
                            TextSizeDown = () => window._documentReaderController.TextSizeDownButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            TextSizeUp = () => window._documentReaderController.TextSizeUpButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            ToggleTextTheme = () => window._documentReaderController.ThemeToggleButton_Click(window.MainToolbar, new RoutedEventArgs()),
                            ToggleSideBySide = () => window._imageViewerController.ToggleSideBySide(),
                            ToggleNextImageSide = () => window._imageViewerController.ToggleNextImageSide(),
                            ToggleSharpeningAsync = () => window._imageViewerController.ToggleSharpeningAsync(),
                            ResetSharpenParams = window.ImageOptions.Reset,
                            NavigatePreviousFileAsync = () => window.NavigateToFileAsync(false),
                            NavigatePreviousPageAsync = () => window._documentNavigationCoordinator.NavigatePreviousAsync(),
                            NavigateNextPageAsync = () => window._documentNavigationCoordinator.NavigateNextAsync(),
                            NavigateNextFileAsync = () => window.NavigateToFileAsync(true),
                            ToggleFullscreen = window.ToggleFullscreen,
                            CloseWindow = window.RequestWindowClose,
                            ShowNotification = (message, icon, color) => window.ShowNotification(message, icon, color)
                        });
                }

                public static void InitializeExtractedControlEvents(MainWindow window)
                {
                    window._controlEventBinder = new MainWindowControlEventBinder(
                        window.ImageViewer,
                        window.TextReader,
                        window.EpubReader,
                        new ExplorerSidebarControlParts
                        {
                            ToggleViewButton = window.ToggleViewButton,
                            ThumbnailSizeSlider = window.ThumbnailSizeSlider,
                            FolderThumbnailsCheckBox = window.FolderThumbnailsCheckBox,
                            ParentFolderButton = window.ParentFolderButton,
                            SidebarAddToFavoritesButton = window.SidebarAddToFavoritesButton,
                            BrowseFolderButton = window.BrowseFolderButton,
                            SortByDateButton = window.SortByDateButton,
                            WebDavFlyout = window.WebDavFlyout,
                            AddWebDavButton = window.AddWebDavButton,
                            FileListView = window.FileListView,
                            FileGridView = window.FileGridView,
                            SidebarFileFavoritesList = window.SidebarFileFavoritesList,
                            SidebarFolderFavoritesList = window.SidebarFolderFavoritesList,
                            SidebarRecentList = window.SidebarRecentList
                        },
                        new MainWindowControlEventHandlers
                        {
                            ImageViewerController = window._imageViewerController,
                            DocumentReaderController = window._documentReaderController,
                            EpubReaderController = window._epubReaderController,
                            FileOpenController = window._fileOpenController,
                            ExplorerSidebarController = window._explorerSidebarController,
                            BookmarkInteractionController = window._bookmarkInteractionController,
                            WebDavFlyoutOpened = window.WebDavFlyout_Opened,
                            AddWebDavButtonClicked = window.AddWebDavButton_Click,
                            MainCanvasCreateResources = window.MainCanvas_CreateResources,
                            MainCanvasDraw = window.MainCanvas_Draw,
                            LeftCanvasCreateResources = window.LeftCanvas_CreateResources,
                            LeftCanvasDraw = window.LeftCanvas_Draw,
                            RightCanvasCreateResources = window.RightCanvas_CreateResources,
                            RightCanvasDraw = window.RightCanvas_Draw,
                            ShowNotification = (message, icon, color) => window.ShowNotification(message, icon, color)
                        });
                }
            }
        }
    }
}
