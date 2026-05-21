using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Uviewer.Controls;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private T SidebarPart<T>(string name) where T : class =>
            ExplorerSidebar.GetPart<T>(name);

        private FrameworkElement ToolbarGrid => MainToolbar;
        private FrameworkElement SidebarGrid => ExplorerSidebar;

        private Button ToggleViewButton => SidebarPart<Button>(nameof(ToggleViewButton));
        private TextBlock ThumbnailSettingsTitleText => SidebarPart<TextBlock>(nameof(ThumbnailSettingsTitleText));
        private TextBlock ThumbnailSizeLabel => SidebarPart<TextBlock>(nameof(ThumbnailSizeLabel));
        private TextBlock ThumbnailSizeValueText => SidebarPart<TextBlock>(nameof(ThumbnailSizeValueText));
        private Slider ThumbnailSizeSlider => SidebarPart<Slider>(nameof(ThumbnailSizeSlider));
        private CheckBox FolderThumbnailsCheckBox => SidebarPart<CheckBox>(nameof(FolderThumbnailsCheckBox));
        private Button ParentFolderButton => SidebarPart<Button>(nameof(ParentFolderButton));
        private Button SidebarFavoritesButton => SidebarPart<Button>(nameof(SidebarFavoritesButton));
        private Flyout SidebarFavoritesFlyout => SidebarPart<Flyout>(nameof(SidebarFavoritesFlyout));
        private Button SidebarAddToFavoritesButton => SidebarPart<Button>(nameof(SidebarAddToFavoritesButton));
        private Pivot SidebarFavoritesPivot => SidebarPart<Pivot>(nameof(SidebarFavoritesPivot));
        private PivotItem SidebarFileFavoritesPivotItem => SidebarPart<PivotItem>(nameof(SidebarFileFavoritesPivotItem));
        private BookmarkListControl SidebarFileFavoritesList => SidebarPart<BookmarkListControl>(nameof(SidebarFileFavoritesList));
        private PivotItem SidebarFolderFavoritesPivotItem => SidebarPart<PivotItem>(nameof(SidebarFolderFavoritesPivotItem));
        private BookmarkListControl SidebarFolderFavoritesList => SidebarPart<BookmarkListControl>(nameof(SidebarFolderFavoritesList));
        private Button SidebarRecentButton => SidebarPart<Button>(nameof(SidebarRecentButton));
        private Flyout SidebarRecentFlyout => SidebarPart<Flyout>(nameof(SidebarRecentFlyout));
        private BookmarkListControl SidebarRecentList => SidebarPart<BookmarkListControl>(nameof(SidebarRecentList));
        private Button BrowseFolderButton => SidebarPart<Button>(nameof(BrowseFolderButton));
        private Button SortByDateButton => SidebarPart<Button>(nameof(SortByDateButton));
        private FontIcon SortIcon => SidebarPart<FontIcon>(nameof(SortIcon));
        private RadioMenuFlyoutItem SortByNameMenu => SidebarPart<RadioMenuFlyoutItem>(nameof(SortByNameMenu));
        private RadioMenuFlyoutItem SortByDateDescMenu => SidebarPart<RadioMenuFlyoutItem>(nameof(SortByDateDescMenu));
        private RadioMenuFlyoutItem SortByDateAscMenu => SidebarPart<RadioMenuFlyoutItem>(nameof(SortByDateAscMenu));
        private Button WebDavButton => SidebarPart<Button>(nameof(WebDavButton));
        private Flyout WebDavFlyout => SidebarPart<Flyout>(nameof(WebDavFlyout));
        private StackPanel WebDavPanel => SidebarPart<StackPanel>(nameof(WebDavPanel));
        private Button AddWebDavButton => SidebarPart<Button>(nameof(AddWebDavButton));
        private TextBlock CurrentPathText => SidebarPart<TextBlock>(nameof(CurrentPathText));
        private ListView FileListView => SidebarPart<ListView>(nameof(FileListView));
        private GridView FileGridView => SidebarPart<GridView>(nameof(FileGridView));

        private void HookExtractedControlEvents()
        {
            ToggleViewButton.Click += ToggleExplorerViewButton_Click;
            ThumbnailSizeSlider.ValueChanged += ThumbnailSizeSlider_ValueChanged;
            FolderThumbnailsCheckBox.Checked += FolderThumbnailsCheckBox_Changed;
            FolderThumbnailsCheckBox.Unchecked += FolderThumbnailsCheckBox_Changed;
            ParentFolderButton.Click += ParentFolderButton_Click;
            SidebarFavoritesButton.Click += SidebarFavoritesButton_Click;
            SidebarAddToFavoritesButton.Click += AddToFavoritesMenuItem_Click;
            SidebarRecentButton.Click += SidebarRecentButton_Click;
            BrowseFolderButton.Click += BrowseFolderButton_Click;
            SortByNameMenu.Click += SortByName_Click;
            SortByDateDescMenu.Click += SortByDateDesc_Click;
            SortByDateAscMenu.Click += SortByDateAsc_Click;
            WebDavFlyout.Opened += WebDavFlyout_Opened;
            AddWebDavButton.Click += AddWebDavButton_Click;
            FileListView.SelectionChanged += FileListView_SelectionChanged;
            FileListView.ItemClick += FileItem_ItemClick;
            FileListView.PreviewKeyDown += FileListView_PreviewKeyDown;
            FileGridView.SelectionChanged += FileGridView_SelectionChanged;
            FileGridView.ItemClick += FileItem_ItemClick;
            FileGridView.PreviewKeyDown += FileGridView_PreviewKeyDown;

            SidebarFileFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            SidebarFileFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            SidebarFileFavoritesList.PinClicked += BookmarkList_PinClicked;
            SidebarFolderFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            SidebarFolderFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            SidebarFolderFavoritesList.PinClicked += BookmarkList_PinClicked;
            SidebarRecentList.ItemClicked += RecentList_ItemClicked;
            SidebarRecentList.RemoveClicked += RecentList_RemoveClicked;
        }
    }
}
