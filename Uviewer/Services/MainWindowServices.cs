using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class MainWindowServices
    {
        public MainWindowServices(
            AppSettingsService appSettings,
            ZoomService zoom,
            ISharpeningService sharpening,
            IThumbnailService thumbnail,
            ImageStatusBarService imageStatusBar,
            SideBySideImageLoadService sideBySideImageLoad,
            IKeyboardShortcutService keyboardShortcut,
            TocService toc,
            DocumentSearchService documentSearch,
            SearchHighlightService searchHighlight,
            DocumentSearchCoordinatorService documentSearchCoordinator,
            DocumentSearchState documentSearchState,
            DocumentSessionTracker documentSessionTracker,
            ExplorerItemLaunchService explorerItemLaunch,
            ImageResourceService imageResource,
            ShutdownCoordinator shutdown,
            SevenZipExtractionCoordinator sevenZipExtraction,
            ArchiveSession archiveSession,
            ImageExifDialogService imageExifDialog,
            FavoritesService favorites,
            RecentService recent)
        {
            AppSettings = appSettings;
            Zoom = zoom;
            Sharpening = sharpening;
            Thumbnail = thumbnail;
            ImageStatusBar = imageStatusBar;
            SideBySideImageLoad = sideBySideImageLoad;
            KeyboardShortcut = keyboardShortcut;
            Toc = toc;
            DocumentSearch = documentSearch;
            SearchHighlight = searchHighlight;
            DocumentSearchCoordinator = documentSearchCoordinator;
            DocumentSearchState = documentSearchState;
            DocumentSessionTracker = documentSessionTracker;
            ExplorerItemLaunch = explorerItemLaunch;
            ImageResource = imageResource;
            Shutdown = shutdown;
            SevenZipExtraction = sevenZipExtraction;
            ArchiveSession = archiveSession;
            ImageExifDialog = imageExifDialog;
            Favorites = favorites;
            Recent = recent;
        }

        public AppSettingsService AppSettings { get; }
        public ZoomService Zoom { get; }
        public ISharpeningService Sharpening { get; }
        public IThumbnailService Thumbnail { get; }
        public ImageStatusBarService ImageStatusBar { get; }
        public SideBySideImageLoadService SideBySideImageLoad { get; }
        public IKeyboardShortcutService KeyboardShortcut { get; }
        public TocService Toc { get; }
        public DocumentSearchService DocumentSearch { get; }
        public SearchHighlightService SearchHighlight { get; }
        public DocumentSearchCoordinatorService DocumentSearchCoordinator { get; }
        public DocumentSearchState DocumentSearchState { get; }
        public DocumentSessionTracker DocumentSessionTracker { get; }
        public ExplorerItemLaunchService ExplorerItemLaunch { get; }
        public ImageResourceService ImageResource { get; }
        public ShutdownCoordinator Shutdown { get; }
        public SevenZipExtractionCoordinator SevenZipExtraction { get; }
        public ArchiveSession ArchiveSession { get; }
        public ImageExifDialogService ImageExifDialog { get; }
        public FavoritesService Favorites { get; }
        public RecentService Recent { get; }
    }
}
