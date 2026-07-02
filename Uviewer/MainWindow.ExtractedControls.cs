using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Uviewer.Controls;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private T SidebarPart<T>(string name) where T : class =>
            ExplorerSidebar.GetPart<T>(name);

        private FrameworkElement ToolbarGrid => MainToolbar;
        private FrameworkElement SidebarGrid => ExplorerSidebar;
        private Grid ImageArea => ImageViewer.ImageAreaElement;
        private StackPanel EmptyStatePanel => ImageViewer.EmptyStatePanelElement;
        private Grid FastNavOverlay => ImageViewer.FastNavOverlayElement;
        private TextBlock FastNavText => ImageViewer.FastNavTextElement;
        private CanvasControl MainCanvas => ImageViewer.MainCanvasElement;
        private Grid SideBySideGrid => ImageViewer.SideBySideGridElement;
        private CanvasControl LeftCanvas => ImageViewer.LeftCanvasElement;
        private CanvasControl RightCanvas => ImageViewer.RightCanvasElement;
        private Grid TextArea => TextReader.TextAreaElement;
        private ScrollViewer TextScrollViewer => TextReader.TextScrollViewerElement;
        private ItemsRepeater TextItemsRepeater => TextReader.TextItemsRepeaterElement;
        private CanvasControl AozoraTextCanvas => TextReader.AozoraTextCanvasElement;
        private Grid TextFastNavOverlay => TextReader.TextFastNavOverlayElement;
        private TextBlock TextFastNavText => TextReader.TextFastNavTextElement;
        private CanvasControl VerticalTextCanvas => TextReader.VerticalTextCanvasElement;
        private Grid EpubArea => EpubReader.EpubAreaElement;
        private Grid EpubImageHost => EpubReader.EpubImageHostElement;
        private Grid EpubTouchOverlay => EpubReader.EpubTouchOverlayElement;
        private CanvasControl EpubTextCanvas => EpubReader.EpubTextCanvasElement;
        private CanvasControl EpubCanvasDisplay => EpubReader.EpubCanvasDisplayElement;
        private CanvasControl EpubCanvasDisplayLeft => EpubReader.EpubCanvasDisplayLeftElement;
        private CanvasControl EpubCanvasDisplayRight => EpubReader.EpubCanvasDisplayRightElement;
        private ColumnDefinition EpubImageLeftColumn => EpubReader.EpubImageLeftColumnElement;
        private ColumnDefinition EpubImageRightColumn => EpubReader.EpubImageRightColumnElement;

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
        private Button WebDavButton => SidebarPart<Button>(nameof(WebDavButton));
        private Flyout WebDavFlyout => SidebarPart<Flyout>(nameof(WebDavFlyout));
        private StackPanel WebDavPanel => SidebarPart<StackPanel>(nameof(WebDavPanel));
        private Button AddWebDavButton => SidebarPart<Button>(nameof(AddWebDavButton));
        private TextBlock CurrentPathText => SidebarPart<TextBlock>(nameof(CurrentPathText));
        private ListView FileListView => SidebarPart<ListView>(nameof(FileListView));
        private GridView FileGridView => SidebarPart<GridView>(nameof(FileGridView));
    }
}
