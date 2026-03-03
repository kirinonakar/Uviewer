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
        public static string TextSizeDownTooltip => GetString("TextSizeDownTooltip");
        public static string TextSizeUpTooltip => GetString("TextSizeUpTooltip");
        public static string VerticalTooltip => GetString("VerticalTooltip");
        public static string ThemeTooltip => GetString("ThemeTooltip");
        public static string FullscreenTooltip => GetString("FullscreenTooltip");
        public static string CloseWindowTooltip => GetString("CloseWindowTooltip");
        public static string ToggleViewTooltip => GetString("ToggleViewTooltip");
        public static string ListViewTooltip => GetString("ListViewTooltip");
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
        public static string ChangeUiFont => GetString("ChangeUiFont");
        public static string UIFontSelectionTitle => GetString("UIFontSelectionTitle");

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
        public static string EpubLoadError(string msg) => string.Format(GetString("EpubLoadError"), msg);
        public static string EpubParseError(string msg) => string.Format(GetString("EpubParseError"), msg);
        public static string EpubPageInfo(int p, int tp, int l, int tl, int c, int tc) => string.Format(GetString("EpubPageInfo"), p, tp, l, tl, c, tc);
    }
}
