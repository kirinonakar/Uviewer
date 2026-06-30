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

        private void HookExtractedControlEvents()
        {
            HookImageViewerControlEvents();
            HookTextReaderControlEvents();
            HookEpubReaderControlEvents();

            ToggleViewButton.Click += ToggleExplorerViewButton_Click;
            ThumbnailSizeSlider.ValueChanged += ThumbnailSizeSlider_ValueChanged;
            FolderThumbnailsCheckBox.Checked += FolderThumbnailsCheckBox_Changed;
            FolderThumbnailsCheckBox.Unchecked += FolderThumbnailsCheckBox_Changed;
            ParentFolderButton.Click += ParentFolderButton_Click;
            SidebarFavoritesButton.Click += SidebarFavoritesButton_Click;
            SidebarAddToFavoritesButton.Click += AddToFavoritesMenuItem_Click;
            SidebarRecentButton.Click += SidebarRecentButton_Click;
            BrowseFolderButton.Click += BrowseFolderButton_Click;
            SortByDateButton.Click += SortButton_Click;
            WebDavFlyout.Opened += WebDavFlyout_Opened;
            AddWebDavButton.Click += AddWebDavButton_Click;
            FileListView.SelectionChanged += FileListView_SelectionChanged;
            FileListView.ItemClick += FileItem_ItemClick;
            FileListView.PreviewKeyDown += FileListView_PreviewKeyDown;
            FileGridView.SelectionChanged += FileGridView_SelectionChanged;
            FileGridView.ItemClick += FileItem_ItemClick;
            FileGridView.PreviewKeyDown += FileGridView_PreviewKeyDown;
            FileListView.RightTapped += ExplorerView_RightTapped;
            FileGridView.RightTapped += ExplorerView_RightTapped;
            InitializeExplorerContextMenus();

            SidebarFileFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            SidebarFileFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            SidebarFileFavoritesList.PinClicked += BookmarkList_PinClicked;
            SidebarFolderFavoritesList.ItemClicked += BookmarkList_ItemClicked;
            SidebarFolderFavoritesList.RemoveClicked += BookmarkList_RemoveClicked;
            SidebarFolderFavoritesList.PinClicked += BookmarkList_PinClicked;
            SidebarRecentList.ItemClicked += RecentList_ItemClicked;
            SidebarRecentList.RemoveClicked += RecentList_RemoveClicked;
        }

        private void HookImageViewerControlEvents()
        {
            ImageViewer.ImageAreaSizeChanged += (_, e) => _imageViewerController.ImageAreaSizeChanged(e);
            ImageViewer.ImageAreaPointerWheelChanged += async (_, e) => await _imageViewerController.HandlePointerWheelAsync(e);
            ImageViewer.ImageAreaPointerPressed += async (_, e) => await _imageViewerController.PointerPressedAsync(e);
            ImageViewer.ImageAreaManipulationStarting += (_, e) => _imageViewerController.ManipulationStarting(e);
            ImageViewer.ImageAreaManipulationDelta += async (_, e) => await _imageViewerController.ManipulationDeltaAsync(e);
            ImageViewer.ImageAreaManipulationCompleted += (_, _) => _imageViewerController.ManipulationCompleted();
            ImageViewer.OpenFileRequested += OpenFileButton_Click;
            ImageViewer.MainCanvasCreateResources += MainCanvas_CreateResources;
            ImageViewer.MainCanvasDraw += MainCanvas_Draw;
            ImageViewer.LeftCanvasCreateResources += LeftCanvas_CreateResources;
            ImageViewer.LeftCanvasDraw += LeftCanvas_Draw;
            ImageViewer.RightCanvasCreateResources += RightCanvas_CreateResources;
            ImageViewer.RightCanvasDraw += RightCanvas_Draw;
        }

        private void HookEpubReaderControlEvents()
        {
            EpubReader.EpubAreaSizeChanged += _epubReaderController.EpubArea_SizeChanged;
            EpubReader.EpubTextCanvasCreateResources += _epubReaderController.EpubTextCanvas_CreateResources;
            EpubReader.EpubTextCanvasSizeChanged += _epubReaderController.EpubTextCanvas_SizeChanged;
            EpubReader.EpubTextCanvasDraw += _epubReaderController.EpubTextCanvas_Draw;
            EpubReader.EpubCanvasDisplayDraw += _epubReaderController.EpubCanvasDisplay_Draw;
            EpubReader.EpubCanvasDisplayLeftDraw += _epubReaderController.EpubCanvasDisplayLeft_Draw;
            EpubReader.EpubCanvasDisplayRightDraw += _epubReaderController.EpubCanvasDisplayRight_Draw;
            EpubReader.EpubTouchOverlayPointerPressed += _epubReaderController.EpubTouchOverlay_PointerPressed;
            EpubReader.EpubTouchOverlayPointerWheelChanged += _epubReaderController.EpubTouchOverlay_PointerWheelChanged;
        }

        private void HookTextReaderControlEvents()
        {
            TextReader.TextItemsRepeaterElementPrepared += _documentReaderController.TextItemsRepeater_ElementPrepared;
            TextReader.TextAreaPointerPressed += _documentReaderController.TextArea_PointerPressed;
            TextReader.TextAreaPointerWheelChanged += _documentReaderController.TextArea_PointerWheelChanged;
            TextReader.TextAreaSizeChanged += _documentReaderController.TextArea_SizeChanged;
            TextReader.TextScrollViewerViewChanged += _documentReaderController.TextScrollViewer_ViewChanged;
            TextReader.TextScrollViewerSizeChanged += _documentReaderController.TextScrollViewer_SizeChanged;
            TextReader.AozoraTextCanvasCreateResources += _documentReaderController.AozoraTextCanvas_CreateResources;
            TextReader.AozoraTextCanvasDraw += _documentReaderController.AozoraTextCanvas_Draw;
            TextReader.AozoraTextCanvasPointerPressed += _documentReaderController.AozoraTextCanvas_PointerPressed;
            TextReader.AozoraTextCanvasPointerWheelChanged += _documentReaderController.AozoraTextCanvas_PointerWheelChanged;
            TextReader.AozoraTextCanvasSizeChanged += _documentReaderController.AozoraTextCanvas_SizeChanged;
            TextReader.VerticalTextCanvasCreateResources += _documentReaderController.VerticalTextCanvas_CreateResources;
            TextReader.VerticalTextCanvasDraw += _documentReaderController.VerticalTextCanvas_Draw;
            TextReader.VerticalTextCanvasPointerPressed += _documentReaderController.VerticalTextCanvas_PointerPressed;
            TextReader.VerticalTextCanvasPointerWheelChanged += _documentReaderController.VerticalTextCanvas_PointerWheelChanged;
            TextReader.VerticalTextCanvasSizeChanged += _documentReaderController.VerticalTextCanvas_SizeChanged;
        }
    }
}
