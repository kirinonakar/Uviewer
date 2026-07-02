using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Uviewer.Controls;
using Windows.Foundation;

namespace Uviewer.Services
{
    internal sealed class ExplorerSidebarControlParts
    {
        public Button ToggleViewButton { get; init; } = null!;
        public Slider ThumbnailSizeSlider { get; init; } = null!;
        public CheckBox FolderThumbnailsCheckBox { get; init; } = null!;
        public Button ParentFolderButton { get; init; } = null!;
        public Button SidebarAddToFavoritesButton { get; init; } = null!;
        public Button BrowseFolderButton { get; init; } = null!;
        public Button SortByDateButton { get; init; } = null!;
        public Flyout WebDavFlyout { get; init; } = null!;
        public Button AddWebDavButton { get; init; } = null!;
        public ListView FileListView { get; init; } = null!;
        public GridView FileGridView { get; init; } = null!;
        public BookmarkListControl SidebarFileFavoritesList { get; init; } = null!;
        public BookmarkListControl SidebarFolderFavoritesList { get; init; } = null!;
        public BookmarkListControl SidebarRecentList { get; init; } = null!;
    }

    internal sealed class MainWindowControlEventHandlers
    {
        public ImageViewerController ImageViewerController { get; init; } = null!;
        public DocumentReaderController DocumentReaderController { get; init; } = null!;
        public EpubReaderController EpubReaderController { get; init; } = null!;
        public FileOpenController FileOpenController { get; init; } = null!;
        public ExplorerSidebarController ExplorerSidebarController { get; init; } = null!;
        public BookmarkInteractionController BookmarkInteractionController { get; init; } = null!;
        public EventHandler<object> WebDavFlyoutOpened { get; init; } = null!;
        public RoutedEventHandler AddWebDavButtonClicked { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs> MainCanvasCreateResources { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasDrawEventArgs> MainCanvasDraw { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs> LeftCanvasCreateResources { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasDrawEventArgs> LeftCanvasDraw { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs> RightCanvasCreateResources { get; init; } = null!;
        public TypedEventHandler<CanvasControl, CanvasDrawEventArgs> RightCanvasDraw { get; init; } = null!;
        public Action<string, string, string> ShowNotification { get; init; } = null!;
    }

    internal sealed class MainWindowControlEventBinder
    {
        private readonly ImageViewerControl _imageViewer;
        private readonly TextReaderControl _textReader;
        private readonly EpubReaderControl _epubReader;
        private readonly ExplorerSidebarControlParts _sidebar;
        private readonly MainWindowControlEventHandlers _handlers;

        public MainWindowControlEventBinder(
            ImageViewerControl imageViewer,
            TextReaderControl textReader,
            EpubReaderControl epubReader,
            ExplorerSidebarControlParts sidebar,
            MainWindowControlEventHandlers handlers)
        {
            _imageViewer = imageViewer ?? throw new ArgumentNullException(nameof(imageViewer));
            _textReader = textReader ?? throw new ArgumentNullException(nameof(textReader));
            _epubReader = epubReader ?? throw new ArgumentNullException(nameof(epubReader));
            _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

            HookImageViewerEvents();
            HookTextReaderEvents();
            HookEpubReaderEvents();
            HookSidebarEvents();
        }

        private void HookImageViewerEvents()
        {
            var image = _handlers.ImageViewerController;

            _imageViewer.ImageAreaSizeChanged += (_, e) => image.ImageAreaSizeChanged(e);
            _imageViewer.ImageAreaPointerWheelChanged += (_, e) => RunAsync(() => image.HandlePointerWheelAsync(e));
            _imageViewer.ImageAreaPointerPressed += (_, e) => RunAsync(() => image.PointerPressedAsync(e));
            _imageViewer.ImageAreaManipulationStarting += (_, e) => image.ManipulationStarting(e);
            _imageViewer.ImageAreaManipulationDelta += (_, e) => RunAsync(() => image.ManipulationDeltaAsync(e));
            _imageViewer.ImageAreaManipulationCompleted += (_, _) => image.ManipulationCompleted();
            _imageViewer.OpenFileRequested += (_, _) => RunAsync(_handlers.FileOpenController.OpenFileAsync);
            _imageViewer.MainCanvasCreateResources += _handlers.MainCanvasCreateResources;
            _imageViewer.MainCanvasDraw += _handlers.MainCanvasDraw;
            _imageViewer.LeftCanvasCreateResources += _handlers.LeftCanvasCreateResources;
            _imageViewer.LeftCanvasDraw += _handlers.LeftCanvasDraw;
            _imageViewer.RightCanvasCreateResources += _handlers.RightCanvasCreateResources;
            _imageViewer.RightCanvasDraw += _handlers.RightCanvasDraw;
        }

        private void HookTextReaderEvents()
        {
            var reader = _handlers.DocumentReaderController;

            _textReader.TextItemsRepeaterElementPrepared += reader.TextItemsRepeater_ElementPrepared;
            _textReader.TextAreaPointerPressed += reader.TextArea_PointerPressed;
            _textReader.TextAreaPointerWheelChanged += reader.TextArea_PointerWheelChanged;
            _textReader.TextAreaSizeChanged += reader.TextArea_SizeChanged;
            _textReader.TextScrollViewerViewChanged += reader.TextScrollViewer_ViewChanged;
            _textReader.TextScrollViewerSizeChanged += reader.TextScrollViewer_SizeChanged;
            _textReader.AozoraTextCanvasCreateResources += reader.AozoraTextCanvas_CreateResources;
            _textReader.AozoraTextCanvasDraw += reader.AozoraTextCanvas_Draw;
            _textReader.AozoraTextCanvasPointerPressed += reader.AozoraTextCanvas_PointerPressed;
            _textReader.AozoraTextCanvasPointerWheelChanged += reader.AozoraTextCanvas_PointerWheelChanged;
            _textReader.AozoraTextCanvasSizeChanged += reader.AozoraTextCanvas_SizeChanged;
            _textReader.VerticalTextCanvasCreateResources += reader.VerticalTextCanvas_CreateResources;
            _textReader.VerticalTextCanvasDraw += reader.VerticalTextCanvas_Draw;
            _textReader.VerticalTextCanvasPointerPressed += reader.VerticalTextCanvas_PointerPressed;
            _textReader.VerticalTextCanvasPointerWheelChanged += reader.VerticalTextCanvas_PointerWheelChanged;
            _textReader.VerticalTextCanvasSizeChanged += reader.VerticalTextCanvas_SizeChanged;
        }

        private void HookEpubReaderEvents()
        {
            var epub = _handlers.EpubReaderController;

            _epubReader.EpubAreaSizeChanged += epub.EpubArea_SizeChanged;
            _epubReader.EpubTextCanvasCreateResources += epub.EpubTextCanvas_CreateResources;
            _epubReader.EpubTextCanvasSizeChanged += epub.EpubTextCanvas_SizeChanged;
            _epubReader.EpubTextCanvasDraw += epub.EpubTextCanvas_Draw;
            _epubReader.EpubCanvasDisplayDraw += epub.EpubCanvasDisplay_Draw;
            _epubReader.EpubCanvasDisplayLeftDraw += epub.EpubCanvasDisplayLeft_Draw;
            _epubReader.EpubCanvasDisplayRightDraw += epub.EpubCanvasDisplayRight_Draw;
            _epubReader.EpubTouchOverlayPointerPressed += epub.EpubTouchOverlay_PointerPressed;
            _epubReader.EpubTouchOverlayPointerWheelChanged += epub.EpubTouchOverlay_PointerWheelChanged;
        }

        private void HookSidebarEvents()
        {
            var explorer = _handlers.ExplorerSidebarController;
            var bookmarks = _handlers.BookmarkInteractionController;

            _sidebar.ToggleViewButton.Click += (_, _) => explorer.ToggleViewMode();
            _sidebar.ThumbnailSizeSlider.ValueChanged += (_, e) => explorer.HandleThumbnailSizeChanged(e.NewValue);
            _sidebar.FolderThumbnailsCheckBox.Checked += (_, _) =>
                explorer.HandleFolderThumbnailsChanged(_sidebar.FolderThumbnailsCheckBox.IsChecked == true);
            _sidebar.FolderThumbnailsCheckBox.Unchecked += (_, _) =>
                explorer.HandleFolderThumbnailsChanged(_sidebar.FolderThumbnailsCheckBox.IsChecked == true);
            _sidebar.ParentFolderButton.Click += (_, _) => explorer.HandleParentFolderClick();
            _sidebar.SidebarAddToFavoritesButton.Click += (_, _) => RunAsync(() => bookmarks.AddCurrentFavoriteAsync());
            _sidebar.BrowseFolderButton.Click += (_, _) => explorer.HandleBrowseFolderClick();
            _sidebar.SortByDateButton.Click += (_, _) => explorer.CycleSortMode();
            _sidebar.WebDavFlyout.Opened += _handlers.WebDavFlyoutOpened;
            _sidebar.AddWebDavButton.Click += _handlers.AddWebDavButtonClicked;

            _sidebar.FileListView.SelectionChanged += (_, _) =>
                explorer.HandleSelectionChanged(_sidebar.FileListView.SelectedItem as Models.FileItem);
            _sidebar.FileGridView.SelectionChanged += (_, _) =>
                explorer.HandleSelectionChanged(_sidebar.FileGridView.SelectedItem as Models.FileItem);
            _sidebar.FileListView.ItemClick += (_, e) => explorer.HandleItemClick(e.ClickedItem as Models.FileItem);
            _sidebar.FileGridView.ItemClick += (_, e) => explorer.HandleItemClick(e.ClickedItem as Models.FileItem);
            _sidebar.FileListView.PreviewKeyDown += (_, e) => explorer.HandleListPreviewKeyDown(e);
            _sidebar.FileGridView.PreviewKeyDown += (_, e) => explorer.HandleGridPreviewKeyDown(e);
            _sidebar.FileListView.RightTapped += (_, e) => explorer.HandleRightTapped(e);
            _sidebar.FileGridView.RightTapped += (_, e) => explorer.HandleRightTapped(e);

            _sidebar.SidebarFileFavoritesList.ItemClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoriteClickedAsync(item));
            _sidebar.SidebarFileFavoritesList.RemoveClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoriteRemoveClickedAsync(item));
            _sidebar.SidebarFileFavoritesList.PinClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoritePinClickedAsync(item));
            _sidebar.SidebarFolderFavoritesList.ItemClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoriteClickedAsync(item));
            _sidebar.SidebarFolderFavoritesList.RemoveClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoriteRemoveClickedAsync(item));
            _sidebar.SidebarFolderFavoritesList.PinClicked += (_, item) => RunAsync(() => bookmarks.HandleFavoritePinClickedAsync(item));
            _sidebar.SidebarRecentList.ItemClicked += (_, item) => RunAsync(() => bookmarks.HandleRecentClickedAsync(item));
            _sidebar.SidebarRecentList.RemoveClicked += (_, item) => RunAsync(() => bookmarks.HandleRecentRemoveClickedAsync(item));
        }

        private async void RunAsync(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Control event failed: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }
    }
}
