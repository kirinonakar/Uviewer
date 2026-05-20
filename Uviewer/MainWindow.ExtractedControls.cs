using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Uviewer.Controls;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private T ToolbarPart<T>(string name) where T : class =>
            MainToolbar.GetPart<T>(name);

        private T SidebarPart<T>(string name) where T : class =>
            ExplorerSidebar.GetPart<T>(name);

        private FrameworkElement ToolbarGrid => MainToolbar;
        private FrameworkElement SidebarGrid => ExplorerSidebar;

        private Button SettingsButton => ToolbarPart<Button>(nameof(SettingsButton));
        private MenuFlyoutItem ChangeFontMenuItem => ToolbarPart<MenuFlyoutItem>(nameof(ChangeFontMenuItem));
        private MenuFlyoutSubItem EncodingMenuItem => ToolbarPart<MenuFlyoutSubItem>(nameof(EncodingMenuItem));
        private ToggleMenuFlyoutItem EncAutoItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(EncAutoItem));
        private ToggleMenuFlyoutItem EncUtf8Item => ToolbarPart<ToggleMenuFlyoutItem>(nameof(EncUtf8Item));
        private ToggleMenuFlyoutItem EncEucKrItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(EncEucKrItem));
        private ToggleMenuFlyoutItem EncSjisItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(EncSjisItem));
        private ToggleMenuFlyoutItem EncJohabItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(EncJohabItem));
        private MenuFlyoutItem ChangeColorsMenuItem => ToolbarPart<MenuFlyoutItem>(nameof(ChangeColorsMenuItem));
        private MenuFlyoutItem ChangeUiFontMenuItem => ToolbarPart<MenuFlyoutItem>(nameof(ChangeUiFontMenuItem));
        private MenuFlyoutSubItem LanguageMenuItem => ToolbarPart<MenuFlyoutSubItem>(nameof(LanguageMenuItem));
        private ToggleMenuFlyoutItem LangAutoItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangAutoItem));
        private ToggleMenuFlyoutItem LangKoItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangKoItem));
        private ToggleMenuFlyoutItem LangEnItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangEnItem));
        private ToggleMenuFlyoutItem LangJaItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangJaItem));
        private ToggleMenuFlyoutItem LangZhHansItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangZhHansItem));
        private ToggleMenuFlyoutItem LangZhHantItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangZhHantItem));
        private ToggleMenuFlyoutItem LangViItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(LangViItem));
        private ToggleMenuFlyoutItem MatchControlDirectionMenuItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(MatchControlDirectionMenuItem));
        private ToggleMenuFlyoutItem AllowMultipleInstancesMenuItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(AllowMultipleInstancesMenuItem));
        private ToggleMenuFlyoutItem AutoDoublePageForArchiveMenuItem => ToolbarPart<ToggleMenuFlyoutItem>(nameof(AutoDoublePageForArchiveMenuItem));
        private MenuFlyoutItem AboutMenuItem => ToolbarPart<MenuFlyoutItem>(nameof(AboutMenuItem));
        private ToggleButton GlobalThemeToggleButton => ToolbarPart<ToggleButton>(nameof(GlobalThemeToggleButton));
        private FontIcon ThemeIcon => ToolbarPart<FontIcon>(nameof(ThemeIcon));
        private ToggleButton PinButton => ToolbarPart<ToggleButton>(nameof(PinButton));
        private FontIcon PinIcon => ToolbarPart<FontIcon>(nameof(PinIcon));
        private ToggleButton AlwaysOnTopButton => ToolbarPart<ToggleButton>(nameof(AlwaysOnTopButton));
        private FontIcon AlwaysOnTopIcon => ToolbarPart<FontIcon>(nameof(AlwaysOnTopIcon));

        private Button ToggleSidebarButton => ToolbarPart<Button>(nameof(ToggleSidebarButton));
        private Button FavoritesButton => ToolbarPart<Button>(nameof(FavoritesButton));
        private Flyout FavoritesFlyout => ToolbarPart<Flyout>(nameof(FavoritesFlyout));
        private Button AddToFavoritesButton => ToolbarPart<Button>(nameof(AddToFavoritesButton));
        private Pivot FavoritesPivot => ToolbarPart<Pivot>(nameof(FavoritesPivot));
        private PivotItem FileFavoritesPivotItem => ToolbarPart<PivotItem>(nameof(FileFavoritesPivotItem));
        private BookmarkListControl FileFavoritesList => ToolbarPart<BookmarkListControl>(nameof(FileFavoritesList));
        private PivotItem FolderFavoritesPivotItem => ToolbarPart<PivotItem>(nameof(FolderFavoritesPivotItem));
        private BookmarkListControl FolderFavoritesList => ToolbarPart<BookmarkListControl>(nameof(FolderFavoritesList));
        private Button RecentButton => ToolbarPart<Button>(nameof(RecentButton));
        private Flyout RecentFlyout => ToolbarPart<Flyout>(nameof(RecentFlyout));
        private BookmarkListControl RecentList => ToolbarPart<BookmarkListControl>(nameof(RecentList));
        private Button OpenFileButton => ToolbarPart<Button>(nameof(OpenFileButton));
        private Button OpenFolderButton => ToolbarPart<Button>(nameof(OpenFolderButton));

        private StackPanel ImageToolbarPanel => ToolbarPart<StackPanel>(nameof(ImageToolbarPanel));
        private Button PdfTocButton => ToolbarPart<Button>(nameof(PdfTocButton));
        private Flyout PdfTocFlyout => ToolbarPart<Flyout>(nameof(PdfTocFlyout));
        private ListView PdfTocListView => ToolbarPart<ListView>(nameof(PdfTocListView));
        private Button PdfGoToPageButton => ToolbarPart<Button>(nameof(PdfGoToPageButton));
        private AppBarSeparator PdfSeparator => ToolbarPart<AppBarSeparator>(nameof(PdfSeparator));
        private Button ZoomOutButton => ToolbarPart<Button>(nameof(ZoomOutButton));
        private TextBlock ZoomLevelText => ToolbarPart<TextBlock>(nameof(ZoomLevelText));
        private Button ZoomInButton => ToolbarPart<Button>(nameof(ZoomInButton));
        private Button ZoomFitButton => ToolbarPart<Button>(nameof(ZoomFitButton));
        private Button ZoomActualButton => ToolbarPart<Button>(nameof(ZoomActualButton));

        private StackPanel TextToolbarPanel => ToolbarPart<StackPanel>(nameof(TextToolbarPanel));
        private ToggleButton AozoraToggleButton => ToolbarPart<ToggleButton>(nameof(AozoraToggleButton));
        private ToggleButton VerticalToggleButton => ToolbarPart<ToggleButton>(nameof(VerticalToggleButton));
        private Button FontToggleButton => ToolbarPart<Button>(nameof(FontToggleButton));
        private MenuFlyoutItem SetDefaultFont1MenuItem => ToolbarPart<MenuFlyoutItem>(nameof(SetDefaultFont1MenuItem));
        private MenuFlyoutItem SetDefaultFont2MenuItem => ToolbarPart<MenuFlyoutItem>(nameof(SetDefaultFont2MenuItem));
        private MenuFlyoutItem ResetDefaultFontsMenuItem => ToolbarPart<MenuFlyoutItem>(nameof(ResetDefaultFontsMenuItem));
        private Button TocButton => ToolbarPart<Button>(nameof(TocButton));
        private Flyout TocFlyout => ToolbarPart<Flyout>(nameof(TocFlyout));
        private ListView TocListView => ToolbarPart<ListView>(nameof(TocListView));
        private Button GoToPageButton => ToolbarPart<Button>(nameof(GoToPageButton));
        private Button TextSizeDownButton => ToolbarPart<Button>(nameof(TextSizeDownButton));
        private TextBlock TextSizeLevelText => ToolbarPart<TextBlock>(nameof(TextSizeLevelText));
        private Button TextSizeUpButton => ToolbarPart<Button>(nameof(TextSizeUpButton));
        private Button ThemeToggleButton => ToolbarPart<Button>(nameof(ThemeToggleButton));

        private StackPanel SideBySideToolbarPanel => ToolbarPart<StackPanel>(nameof(SideBySideToolbarPanel));
        private Button SideBySideButton => ToolbarPart<Button>(nameof(SideBySideButton));
        private TextBlock SideBySideText => ToolbarPart<TextBlock>(nameof(SideBySideText));
        private Button NextImageSideButton => ToolbarPart<Button>(nameof(NextImageSideButton));
        private FontIcon NextImageSideText => ToolbarPart<FontIcon>(nameof(NextImageSideText));
        private AppBarSeparator SharpenSeparator => ToolbarPart<AppBarSeparator>(nameof(SharpenSeparator));
        private ToggleButton SharpenButton => ToolbarPart<ToggleButton>(nameof(SharpenButton));
        private TextBlock SharpenSettingsTitleText => ToolbarPart<TextBlock>(nameof(SharpenSettingsTitleText));
        private TextBlock UpscaleLabel => ToolbarPart<TextBlock>(nameof(UpscaleLabel));
        private TextBlock SharpenAmountLabel => ToolbarPart<TextBlock>(nameof(SharpenAmountLabel));
        private TextBlock SharpenThresholdLabel => ToolbarPart<TextBlock>(nameof(SharpenThresholdLabel));
        private TextBlock UnsharpAmountLabel => ToolbarPart<TextBlock>(nameof(UnsharpAmountLabel));
        private TextBlock UnsharpRadiusLabel => ToolbarPart<TextBlock>(nameof(UnsharpRadiusLabel));
        private Button SharpenParamsResetButton => ToolbarPart<Button>(nameof(SharpenParamsResetButton));
        private FontIcon SharpenIcon => ToolbarPart<FontIcon>(nameof(SharpenIcon));
        private Button PrevFileButton => ToolbarPart<Button>(nameof(PrevFileButton));
        private Button PrevPageButton => ToolbarPart<Button>(nameof(PrevPageButton));
        private Button NextPageButton => ToolbarPart<Button>(nameof(NextPageButton));
        private Button NextFileButton => ToolbarPart<Button>(nameof(NextFileButton));
        private Button FullscreenButton => ToolbarPart<Button>(nameof(FullscreenButton));
        private FontIcon FullscreenIcon => ToolbarPart<FontIcon>(nameof(FullscreenIcon));
        private Button CloseWindowButton => ToolbarPart<Button>(nameof(CloseWindowButton));

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
            ChangeFontMenuItem.Click += FontMenu_Click;
            EncAutoItem.Click += EncodingItem_Click;
            EncUtf8Item.Click += EncodingItem_Click;
            EncEucKrItem.Click += EncodingItem_Click;
            EncSjisItem.Click += EncodingItem_Click;
            EncJohabItem.Click += EncodingItem_Click;
            ChangeColorsMenuItem.Click += ColorsMenu_Click;
            ChangeUiFontMenuItem.Click += UiFontMenu_Click;
            LangAutoItem.Click += LanguageItem_Click;
            LangKoItem.Click += LanguageItem_Click;
            LangEnItem.Click += LanguageItem_Click;
            LangJaItem.Click += LanguageItem_Click;
            LangZhHansItem.Click += LanguageItem_Click;
            LangZhHantItem.Click += LanguageItem_Click;
            LangViItem.Click += LanguageItem_Click;
            MatchControlDirectionMenuItem.Click += MatchControlDirectionMenuItem_Click;
            AllowMultipleInstancesMenuItem.Click += AllowMultipleInstancesMenuItem_Click;
            AutoDoublePageForArchiveMenuItem.Click += AutoDoublePageForArchiveMenuItem_Click;
            AboutMenuItem.Click += AboutMenu_Click;

            GlobalThemeToggleButton.Click += GlobalThemeToggleButton_Click;
            PinButton.Click += PinButton_Click;
            AlwaysOnTopButton.Click += AlwaysOnTopButton_Click;
            ToggleSidebarButton.Click += ToggleSidebarButton_Click;
            FavoritesButton.Click += FavoritesButton_Click;
            AddToFavoritesButton.Click += AddToFavoritesMenuItem_Click;
            RecentButton.Click += RecentButton_Click;
            OpenFileButton.Click += OpenFileButton_Click;
            OpenFolderButton.Click += OpenFolderButton_Click;

            FileFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            FileFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            FileFavoritesList.PinClicked += BookmarkList_PinClicked;
            FolderFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            FolderFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            FolderFavoritesList.PinClicked += BookmarkList_PinClicked;
            RecentList.ItemClicked += RecentList_ItemClicked;
            RecentList.RemoveClicked += RecentList_RemoveClicked;

            PdfTocButton.Click += PdfTocButton_Click;
            PdfTocListView.ItemClick += PdfTocListView_ItemClick;
            PdfGoToPageButton.Click += GoToPageButton_Click;
            PdfGoToPageButton.RightTapped += SearchButton_RightTapped;
            ZoomOutButton.Click += ZoomOutButton_Click;
            ZoomInButton.Click += ZoomInButton_Click;
            ZoomFitButton.Click += ZoomFitButton_Click;
            ZoomActualButton.Click += ZoomActualButton_Click;

            AozoraToggleButton.Click += AozoraToggleButton_Click;
            VerticalToggleButton.Click += VerticalToggleButton_Click;
            FontToggleButton.Click += FontToggleButton_Click;
            SetDefaultFont1MenuItem.Click += SetDefaultFont1MenuItem_Click;
            SetDefaultFont2MenuItem.Click += SetDefaultFont2MenuItem_Click;
            ResetDefaultFontsMenuItem.Click += ResetDefaultFontsMenuItem_Click;
            TocButton.Click += TocButton_Click;
            TocListView.ItemClick += TocListView_ItemClick;
            GoToPageButton.Click += GoToPageButton_Click;
            GoToPageButton.RightTapped += SearchButton_RightTapped;
            TextSizeDownButton.Click += TextSizeDownButton_Click;
            TextSizeUpButton.Click += TextSizeUpButton_Click;
            ThemeToggleButton.Click += ThemeToggleButton_Click;

            SideBySideButton.Click += SideBySideButton_Click;
            NextImageSideButton.Click += NextImageSideButton_Click;
            SharpenButton.Click += SharpenButton_Click;
            SharpenParamsResetButton.Click += SharpenParams_Reset_Click;
            PrevFileButton.Click += PrevFileButton_Click;
            PrevPageButton.Click += PrevPageButton_Click;
            NextPageButton.Click += NextPageButton_Click;
            NextFileButton.Click += NextFileButton_Click;
            FullscreenButton.Click += FullscreenButton_Click;
            CloseWindowButton.Click += CloseWindowButton_Click;

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
