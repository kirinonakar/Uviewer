using Microsoft.UI.Xaml;
using System;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private void HookMainToolbarEvents()
        {
            MainToolbar.ChangeFontRequested += (_, _) => FontMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.EncodingSelected += async (_, encoding) => await ApplyEncodingSelectionAsync(encoding);
            MainToolbar.ChangeColorsRequested += (_, _) => ColorsMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ChangeUiFontRequested += (_, _) => UiFontMenu_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.LanguageSelected += async (_, language) => await ApplyLanguageSelectionAsync(language);
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
            MainToolbar.FavoriteItemClicked += BookmarkList_ItemClicked;
            MainToolbar.FavoriteRemoveClicked += BookmarkList_RemoveClicked;
            MainToolbar.FavoritePinClicked += BookmarkList_PinClicked;
            MainToolbar.RecentItemClicked += RecentList_ItemClicked;
            MainToolbar.RecentRemoveClicked += RecentList_RemoveClicked;
            MainToolbar.PdfTocRequested += (_, _) => PdfTocButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PdfTocItemClicked += PdfTocListView_ItemClick;
            MainToolbar.PdfGoToPageRequested += (_, _) => GoToPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SearchRequested += SearchButton_RightTapped;
            MainToolbar.ZoomOutRequested += (_, _) => ZoomOutButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ZoomInRequested += (_, _) => ZoomInButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ZoomFitRequested += (_, _) => ZoomFitButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ZoomActualRequested += (_, _) => ZoomActualButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.AozoraToggleRequested += (_, _) => AozoraToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.VerticalToggleRequested += (_, _) => VerticalToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.FontToggleRequested += (_, _) => FontToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SetDefaultFont1Requested += (_, _) => SetDefaultFont1MenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SetDefaultFont2Requested += (_, _) => SetDefaultFont2MenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.ResetDefaultFontsRequested += (_, _) => ResetDefaultFontsMenuItem_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TocRequested += (_, _) => TocButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TocItemClicked += TocListView_ItemClick;
            MainToolbar.GoToPageRequested += (_, _) => GoToPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextSizeDownRequested += (_, _) => TextSizeDownButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextSizeUpRequested += (_, _) => TextSizeUpButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.TextThemeToggleRequested += (_, _) => ThemeToggleButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SideBySideToggleRequested += (_, _) => SideBySideButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.NextImageSideToggleRequested += (_, _) => NextImageSideButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SharpenToggleRequested += (_, _) => SharpenButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.SharpenParamsResetRequested += (_, _) => SharpenParams_Reset_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PreviousFileRequested += (_, _) => PrevFileButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.PreviousPageRequested += (_, _) => PrevPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.NextPageRequested += (_, _) => NextPageButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.NextFileRequested += (_, _) => NextFileButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.FullscreenToggleRequested += (_, _) => FullscreenButton_Click(MainToolbar, new RoutedEventArgs());
            MainToolbar.CloseWindowRequested += (_, _) => CloseWindowButton_Click(MainToolbar, new RoutedEventArgs());
        }

        private void MainToolbar_OpenFileRequested(object? sender, EventArgs e) =>
            OpenFileButton_Click(MainToolbar, new RoutedEventArgs());

        private void MainToolbar_OpenFolderRequested(object? sender, EventArgs e) =>
            OpenFolderButton_Click(MainToolbar, new RoutedEventArgs());

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
