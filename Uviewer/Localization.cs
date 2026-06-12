using System;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Uviewer
{
    public static class Strings
    {
        private static ResourceLoader _loader = new ResourceLoader();

        private static string GetString(string key)
        {
            try
            {
                return _loader.GetString(key);
            }
            catch
            {
                return key;
            }
        }

        // Tooltips & General Strings
        public static string ToggleSidebarTooltip => GetString("ToggleSidebarTooltip");
        public static string OpenFileTooltip => GetString("OpenFileTooltip");
        public static string OpenFolderTooltip => GetString("OpenFolderTooltip");
        public static string ZoomOutTooltip => GetString("ZoomOutTooltip");
        public static string ZoomInTooltip => GetString("ZoomInTooltip");
        public static string ZoomFitTooltip => GetString("ZoomFitTooltip");
        public static string ZoomActualTooltip => GetString("ZoomActualTooltip");
        public static string SharpenTooltip => GetString("SharpenTooltip");
        public static string SideBySideTooltip => GetString("SideBySideTooltip");
        public static string NextImageSideTooltip => GetString("NextImageSideTooltip");
        public static string PrevFileTooltip => GetString("PrevFileTooltip");
        public static string NextFileTooltip => GetString("NextFileTooltip");
        public static string PrevPageTooltip => GetString("PrevPageTooltip");
        public static string NextPageTooltip => GetString("NextPageTooltip");
        public static string AozoraTooltip => GetString("AozoraTooltip");
        public static string FontTooltip => GetString("FontTooltip");
        public static string GoToPageTooltip => GetString("GoToPageTooltip");
        public static string PdfGoToPageTooltip => GetString("PdfGoToPageTooltip");
        public static string GoToPageTitle => GetString("GoToPageTitle");
        public static string SearchPlaceholder => GetString("SearchPlaceholder");
        public static string SearchPreviousTooltip => GetString("SearchPreviousTooltip");
        public static string SearchNextTooltip => GetString("SearchNextTooltip");
        public static string SearchNoMatches => GetString("SearchNoMatches");
        public static string SearchSearching => GetString("SearchSearching");
        public static string SearchUnavailable => GetString("SearchUnavailable");
        public static string TextSizeDownTooltip => GetString("TextSizeDownTooltip");
        public static string TextSizeUpTooltip => GetString("TextSizeUpTooltip");
        public static string VerticalTooltip => GetString("VerticalTooltip");
        public static string ThemeTooltip => GetString("ThemeTooltip");
        public static string FullscreenTooltip => GetString("FullscreenTooltip");
        public static string CloseWindowTooltip => GetString("CloseWindowTooltip");
        public static string ToggleViewTooltip => GetString("ToggleViewTooltip");
        public static string ListViewTooltip => GetString("ListViewTooltip");
        public static string RightClickSettingsHint => GetString("RightClickSettingsHint");
        public static string ThumbnailSettingsTitle => GetString("ThumbnailSettingsTitle");
        public static string ThumbnailSizeLabel => GetString("ThumbnailSizeLabel");
        public static string ShowFolderThumbnailsLabel => GetString("ShowFolderThumbnailsLabel");
        public static string ParentFolderTooltip => GetString("ParentFolderTooltip");
        public static string RecentTooltip => GetString("RecentTooltip");
        public static string NoRecentFiles => GetString("NoRecentFiles");
        public static string FavoritesTooltip => GetString("FavoritesTooltip");
        public static string NoFavorites => GetString("NoFavorites");
        public static string FavoritesFiles => GetString("FavoritesFiles");
        public static string FavoritesFolders => GetString("FavoritesFolders");
        public static string PinFavorite => GetString("PinFavorite");
        public static string UnpinFavorite => GetString("UnpinFavorite");
        public static string RemoveFavorite => GetString("RemoveFavorite");
        public static string ProgressLabel => GetString("ProgressLabel");
        public static string TocTooltip => GetString("TocTooltip");
        public static string TocTitle => GetString("TocTitle");
        public static string NoTocContent => GetString("NoTocContent");
        public static string BrowseFolderTooltip => GetString("BrowseFolderTooltip");
        public static string SettingsTooltip => GetString("SettingsTooltip");
        public static string LightModeTooltip => GetString("LightModeTooltip");
        public static string DarkModeTooltip => GetString("DarkModeTooltip");
        public static string ChangeFont => GetString("ChangeFont");
        public static string FontSelectionTitle => GetString("FontSelectionTitle");
        public static string FontSearchPlaceholder => GetString("FontSearchPlaceholder");
        public static string ChangeColors => GetString("ChangeColors");
        public static string BackgroundColor => GetString("BackgroundColor");
        public static string TextColor => GetString("TextColor");
        public static string Hue => GetString("Hue");
        public static string Saturation => GetString("Saturation");
        public static string Lightness => GetString("Lightness");
        public static string Preview => GetString("Preview");
        public static string MatchControlDirection => GetString("MatchControlDirection");
        public static string MatchControlDirectionTooltip => GetString("MatchControlDirectionTooltip");
        public static string AllowMultipleInstances => GetString("AllowMultipleInstances");
        public static string AllowMultipleInstancesTooltip => GetString("AllowMultipleInstancesTooltip");
        public static string CurrentPathPlaceholder => GetString("CurrentPathPlaceholder");
        public static string EmptyStateDrag => GetString("EmptyStateDrag");
        public static string EmptyStateClick => GetString("EmptyStateClick");
        public static string EmptyStateButton => GetString("EmptyStateButton");
        public static string FastNavText => GetString("FastNavText");
        public static string TextFastNavText => GetString("TextFastNavText");
        public static string CalculatingPages => GetString("CalculatingPages");
        public static string Paginating => GetString("Paginating");
        public static string FileSelectPlaceholder => GetString("FileSelectPlaceholder");
        public static string LoadImageError => GetString("LoadImageError");
        public static string AddedToFavoritesNotification => GetString("AddedToFavoritesNotification");
        public static string FavoritePositionUpdatedNotification => GetString("FavoritePositionUpdatedNotification");
        public static string Loading => GetString("Loading");
        public static string AddToFavorites => GetString("AddToFavorites");
        public static string DialogTitle => GetString("DialogTitle");
        public static string DialogPrimary => GetString("DialogPrimary");
        public static string DialogClose => GetString("DialogClose");
        public static string EncodingMenu => GetString("EncodingMenu");
        public static string EncAuto => GetString("EncAuto");
        public static string EncUtf8 => GetString("EncUtf8");
        public static string EncEucKr => GetString("EncEucKr");
        public static string EncSjis => GetString("EncSjis");
        public static string EncJohab => GetString("EncJohab");
        public static string WebDavTooltip => GetString("WebDavTooltip");
        public static string AddWebDavServer => GetString("AddWebDavServer");
        public static string WebDavServerName => GetString("WebDavServerName");
        public static string WebDavAddress => GetString("WebDavAddress");
        public static string WebDavPort => GetString("WebDavPort");
        public static string WebDavId => GetString("WebDavId");
        public static string WebDavPassword => GetString("WebDavPassword");
        public static string WebDavSave => GetString("WebDavSave");
        public static string WebDavCancel => GetString("WebDavCancel");
        public static string WebDavConnecting => GetString("WebDavConnecting");
        public static string WebDavConnectionFailed => GetString("WebDavConnectionFailed");
        public static string LanguageSelection => GetString("LanguageSelection");
        public static string LanguageAuto => GetString("LanguageAuto");
        public static string LanguageKorean => GetString("LanguageKorean");
        public static string LanguageEnglish => GetString("LanguageEnglish");
        public static string LanguageJapanese => GetString("LanguageJapanese");
        public static string LanguageChineseSimplified => GetString("LanguageChineseSimplified");
        public static string LanguageChineseTraditional => GetString("LanguageChineseTraditional");
        public static string LanguageVietnamese => GetString("LanguageVietnamese");
        public static string ChangeUiFont => GetString("ChangeUiFont");
        public static string UIFontSelectionTitle => GetString("UIFontSelectionTitle");
        public static string PinTooltip => GetString("PinTooltip");
        public static string AlwaysOnTopTooltip => GetString("AlwaysOnTopTooltip");
        public static string AutoDoublePageForArchive => GetString("AutoDoublePageForArchive");
        public static string FolderEmpty => GetString("FolderEmpty");
        public static string SortByNameTooltip => GetString("SortByNameTooltip");
        public static string SortByDateDescTooltip => GetString("SortByDateDescTooltip");
        public static string SortByDateAscTooltip => GetString("SortByDateAscTooltip");
        public static string ExifStatusBarTooltip => GetString("ExifStatusBarTooltip");
        public static string ExifDialogTitle => GetString("ExifDialogTitle");
        public static string ExifUnavailable => GetString("ExifUnavailable");
        public static string ExifNoImageSelected => GetString("ExifNoImageSelected");
        public static string ExifNoMetadata => GetString("ExifNoMetadata");
        public static string ExifFileName => GetString("ExifFileName");
        public static string ExifFilePath => GetString("ExifFilePath");
        public static string ExifFileSize => GetString("ExifFileSize");
        public static string ExifModified => GetString("ExifModified");
        public static string ExifDimensions => GetString("ExifDimensions");
        public static string ExifDateTaken => GetString("ExifDateTaken");
        public static string ExifCameraMaker => GetString("ExifCameraMaker");
        public static string ExifCameraModel => GetString("ExifCameraModel");
        public static string ExifOrientation => GetString("ExifOrientation");
        public static string ExifLatitude => GetString("ExifLatitude");
        public static string ExifLongitude => GetString("ExifLongitude");
        public static string ExifTitle => GetString("ExifTitle");
        public static string ExifRating => GetString("ExifRating");
        public static string ExifKeywords => GetString("ExifKeywords");
        public static string ExifExposureTime => GetString("ExifExposureTime");
        public static string ExifFNumber => GetString("ExifFNumber");
        public static string ExifIso => GetString("ExifIso");
        public static string ExifFocalLength => GetString("ExifFocalLength");
        public static string ExifCloseButton => GetString("ExifCloseButton");
        public static string About => GetString("About");
        public static string AboutTitle => GetString("AboutTitle");
        public static string Close => GetString("CloseWindowTooltip").Replace("(Esc)", "").Trim();
        public static string FileNotFound => GetString("FileNotFound");
        public static string ExternalProgramSettings => GetString("ExternalProgramSettings");
        public static string ExplorerOpenExternal => GetString("ExplorerOpenExternal");
        public static string ExplorerOpenInWindowsExplorer => GetString("ExplorerOpenInWindowsExplorer");
        public static string ExplorerRefresh => GetString("ExplorerRefresh");
        public static string ExplorerRename => GetString("ExplorerRename");
        public static string ExplorerDelete => GetString("ExplorerDelete");
        public static string RenamePrimary => GetString("RenamePrimary");
        public static string Cancel => GetString("DialogClose");
        public static string DeletePrimary => GetString("DeletePrimary");
        public static string InvalidFileName => GetString("InvalidFileName");
        public static string FileNameAlreadyExists => GetString("FileNameAlreadyExists");
        public static string RenameSucceeded => GetString("RenameSucceeded");
        public static string MovedToRecycleBin => GetString("MovedToRecycleBin");
        public static string ExternalProgramPathRequired => GetString("ExternalProgramPathRequired");

        // Sharpen & Upscale
        public static string SharpenSettingsTitle => GetString("SharpenSettingsTitle");
        public static string UpscaleFactorLabel => GetString("UpscaleFactorLabel");
        public static string SharpenAmountLabel => GetString("SharpenAmountLabel");
        public static string SharpenThresholdLabel => GetString("SharpenThresholdLabel");
        public static string UnsharpAmountLabel => GetString("UnsharpAmountLabel");
        public static string UnsharpRadiusLabel => GetString("UnsharpRadiusLabel");
        public static string ResetButton => GetString("ResetButton");

        public static string DefaultFont1Label => GetString("DefaultFont1Label");
        public static string DefaultFont2Label => GetString("DefaultFont2Label");
        public static string FontSelectionSlotTitle(int slot) => string.Format(GetString("FontSelectionSlotTitle"), slot);

        public static void Reload()
        {
            try
            {
                _loader = new ResourceLoader();
            }
            catch
            {
                _loader = new ResourceLoader();
            }
        }

        // Methods
        public static string LineInfo(int cur, int total) => string.Format(GetString("LineInfo"), cur, total);
        public static string SearchMatchCounter(int cur, int total) => string.Format(GetString("SearchMatchCounter"), cur, total);
        public static string EpubLoadError(string msg) => string.Format(GetString("EpubLoadError"), msg);
        public static string EpubParseError(string msg) => string.Format(GetString("EpubParseError"), msg);
        public static string EpubPageInfo(int p, int tp, int l, int tl, int c, int tc) => string.Format(GetString("EpubPageInfo"), p, tp, l, tl, c, tc);
        public static string ExternalProgramMenuWithName(string name) => string.Format(GetString("ExternalProgramMenuWithName"), name);
        public static string ExternalProgramConfiguredNotification(string name) => string.Format(GetString("ExternalProgramConfiguredNotification"), name);
        public static string ExternalProgramLaunchFailed(string msg) => string.Format(GetString("ExternalProgramLaunchFailed"), msg);
        public static string ExplorerOpenFailed(string msg) => string.Format(GetString("ExplorerOpenFailed"), msg);
        public static string DeleteConfirmation(string name) => string.Format(GetString("DeleteConfirmation"), name);
        public static string RenameFailed(string msg) => string.Format(GetString("RenameFailed"), msg);
        public static string DeleteFailed(string msg) => string.Format(GetString("DeleteFailed"), msg);
    }
}
