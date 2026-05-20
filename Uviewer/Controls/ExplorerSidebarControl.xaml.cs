using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Uviewer.Controls
{
    public sealed partial class ExplorerSidebarControl : UserControl
    {
        public ExplorerSidebarControl()
        {
            InitializeComponent();
        }

        internal T GetPart<T>(string name) where T : class
        {
            object? part = name switch
            {
                nameof(ToggleViewButton) => ToggleViewButton,
                nameof(ThumbnailSettingsTitleText) => ThumbnailSettingsTitleText,
                nameof(ThumbnailSizeLabel) => ThumbnailSizeLabel,
                nameof(ThumbnailSizeValueText) => ThumbnailSizeValueText,
                nameof(ThumbnailSizeSlider) => ThumbnailSizeSlider,
                nameof(FolderThumbnailsCheckBox) => FolderThumbnailsCheckBox,
                nameof(ParentFolderButton) => ParentFolderButton,
                nameof(SidebarFavoritesButton) => SidebarFavoritesButton,
                nameof(SidebarFavoritesFlyout) => SidebarFavoritesFlyout,
                nameof(SidebarAddToFavoritesButton) => SidebarAddToFavoritesButton,
                nameof(SidebarFavoritesPivot) => SidebarFavoritesPivot,
                nameof(SidebarFileFavoritesPivotItem) => SidebarFileFavoritesPivotItem,
                nameof(SidebarFileFavoritesList) => SidebarFileFavoritesList,
                nameof(SidebarFolderFavoritesPivotItem) => SidebarFolderFavoritesPivotItem,
                nameof(SidebarFolderFavoritesList) => SidebarFolderFavoritesList,
                nameof(SidebarRecentButton) => SidebarRecentButton,
                nameof(SidebarRecentFlyout) => SidebarRecentFlyout,
                nameof(SidebarRecentList) => SidebarRecentList,
                nameof(BrowseFolderButton) => BrowseFolderButton,
                nameof(SortByDateButton) => SortByDateButton,
                nameof(SortIcon) => SortIcon,
                nameof(SortByNameMenu) => SortByNameMenu,
                nameof(SortByDateDescMenu) => SortByDateDescMenu,
                nameof(SortByDateAscMenu) => SortByDateAscMenu,
                nameof(WebDavButton) => WebDavButton,
                nameof(WebDavFlyout) => WebDavFlyout,
                nameof(WebDavPanel) => WebDavPanel,
                nameof(AddWebDavButton) => AddWebDavButton,
                nameof(CurrentPathText) => CurrentPathText,
                nameof(FileListView) => FileListView,
                nameof(FileGridView) => FileGridView,
                _ => null
            };

            return part as T
                ?? throw new System.InvalidOperationException($"Sidebar part '{name}' was not found.");
        }
    }
}
