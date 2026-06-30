using Microsoft.UI.Xaml;
using System;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private void HookMainToolbarEvents()
        {
            MainToolbar.ChangeFontRequested += (_, _) => _documentReaderController.FontMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.EncodingSelected += async (_, encoding) => await _documentReaderController.ApplyEncodingSelectionAsync(encoding);
            MainToolbar.ChangeColorsRequested += (_, _) => _documentReaderController.ColorsMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ChangeUiFontRequested += (_, _) => _documentReaderController.UiFontMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SelectExternalProgramRequested += async (_, _) => await _externalProgramSettingsController.SelectExternalProgramAsync();
            MainToolbar.LanguageSelected += async (_, language) => await _documentReaderController.ApplyLanguageSelectionAsync(language);
            MainToolbar.MatchControlDirectionChanged += (_, isChecked) => UpdateMatchControlDirection(isChecked);
            MainToolbar.AllowMultipleInstancesChanged += (_, isChecked) => UpdateAllowMultipleInstances(isChecked);
            MainToolbar.AutoDoublePageForArchiveChanged += (_, isChecked) => UpdateAutoDoublePageForArchive(isChecked);
            MainToolbar.AboutRequested += (_, _) => AboutMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.GlobalThemeToggleRequested += (_, _) => GlobalThemeToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PinToggleRequested += (_, _) => PinButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.AlwaysOnTopToggleRequested += (_, _) => AlwaysOnTopButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ToggleSidebarRequested += (_, _) => ToggleSidebarButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.AddToFavoritesRequested += (_, _) => AddToFavoritesMenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.OpenFileRequested += MainToolbar_OpenFileRequested;
            MainToolbar.OpenFolderRequested += MainToolbar_OpenFolderRequested;
            MainToolbar.FavoriteItemClicked += async (_, item) => await _bookmarkInteractionController.HandleFavoriteClickedAsync(item);
            MainToolbar.FavoriteRemoveClicked += async (_, item) => await _bookmarkInteractionController.HandleFavoriteRemoveClickedAsync(item);
            MainToolbar.FavoritePinClicked += async (_, item) => await _bookmarkInteractionController.HandleFavoritePinClickedAsync(item);
            MainToolbar.RecentItemClicked += async (_, item) => await _bookmarkInteractionController.HandleRecentClickedAsync(item);
            MainToolbar.RecentRemoveClicked += async (_, item) => await _bookmarkInteractionController.HandleRecentRemoveClickedAsync(item);
            MainToolbar.PdfTocRequested += (_, _) => PdfTocButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PdfTocItemClicked += PdfTocListView_ItemClick;
            MainToolbar.PdfGoToPageRequested += (_, _) => _documentReaderController.GoToPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SearchRequested += SearchButton_RightTapped;
            MainToolbar.ZoomOutRequested += (_, _) => ZoomOut();
            MainToolbar.ZoomInRequested += (_, _) => ZoomIn();
            MainToolbar.ZoomFitRequested += (_, _) => FitToWindow();
            MainToolbar.ZoomActualRequested += (_, _) => _imageViewerController.ZoomActual();
            MainToolbar.AozoraToggleRequested += (_, _) => _documentReaderController.AozoraToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.VerticalToggleRequested += (_, _) => _documentReaderController.VerticalToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.FontToggleRequested += (_, _) => _documentReaderController.FontToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SetDefaultFont1Requested += (_, _) => _documentReaderController.SetDefaultFont1MenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SetDefaultFont2Requested += (_, _) => _documentReaderController.SetDefaultFont2MenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ResetDefaultFontsRequested += (_, _) => _documentReaderController.ResetDefaultFontsMenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TocRequested += (_, _) => _documentReaderController.TocButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TocItemClicked += _documentReaderController.TocListView_ItemClick;
            MainToolbar.GoToPageRequested += (_, _) => _documentReaderController.GoToPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextSizeDownRequested += (_, _) => _documentReaderController.TextSizeDownButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextSizeUpRequested += (_, _) => _documentReaderController.TextSizeUpButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextThemeToggleRequested += (_, _) => _documentReaderController.ThemeToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SideBySideToggleRequested += (_, _) => _imageViewerController.ToggleSideBySide();
            MainToolbar.NextImageSideToggleRequested += (_, _) => _imageViewerController.ToggleNextImageSide();
            MainToolbar.SharpenToggleRequested += async (_, _) => await _imageViewerController.ToggleSharpeningAsync();
            MainToolbar.SharpenParamsResetRequested += (_, _) => ImageOptions.Reset();
            MainToolbar.PreviousFileRequested += (_, _) => PrevFileButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PreviousPageRequested += (_, _) => PrevPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.NextPageRequested += (_, _) => NextPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.NextFileRequested += (_, _) => NextFileButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.FullscreenToggleRequested += (_, _) => FullscreenButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.CloseWindowRequested += (_, _) => CloseWindowButton_Click(MainToolbar, new RoutedEventArgs());
        }

        private void MainToolbar_OpenFileRequested(object? sender, EventArgs e) =>
            _ = _fileOpenController.OpenFileAsync();

        private void MainToolbar_OpenFolderRequested(object? sender, EventArgs e) =>
            _ = _fileOpenController.OpenFolderAsync();

        private void UpdateMatchControlDirection(bool isChecked)
        {
            _matchControlDirection = isChecked;
            _windowSettingsCoordinator.SaveWindowSettings();
        }

        private void UpdateAllowMultipleInstances(bool isChecked)
        {
            _allowMultipleInstances = isChecked;
            _windowSettingsCoordinator.SaveWindowSettings();
        }

        private void UpdateAutoDoublePageForArchive(bool isChecked)
        {
            _autoDoublePageForArchive = isChecked;
            _windowSettingsCoordinator.SaveWindowSettings();
            _ = DisplayCurrentImageAsync();
        }
    }
}
