using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static MainWindowServices CreateCoreServices()
            {
                var sharpening = new SharpeningService();

                return new MainWindowServices(
                    new AppSettingsService(),
                    new ZoomService(),
                    sharpening,
                    new ThumbnailService(),
                    new ImageStatusBarService(),
                    new SideBySideImageLoadService(),
                    new KeyboardShortcutService(),
                    new TocService(),
                    new DocumentSearchService(),
                    new SearchHighlightService(),
                    new DocumentSearchCoordinatorService(),
                    new Uviewer.Models.DocumentSearchState(),
                    new DocumentSessionTracker(),
                    new ImageResourceService(sharpening),
                    new ShutdownCoordinator(),
                    new SevenZipExtractionCoordinator(),
                    new ArchiveSession(),
                    new FavoritesService(),
                    new RecentService());
            }
        }

        private void ApplyCoreServices(MainWindowServices services)
        {
            _appSettingsService = services.AppSettings;
            _zoomService = services.Zoom;
            _sharpeningService = services.Sharpening;
            _thumbnailService = services.Thumbnail;
            _imageStatusBarService = services.ImageStatusBar;
            _sideBySideImageLoadService = services.SideBySideImageLoad;
            _keyboardShortcutService = services.KeyboardShortcut;
            _tocService = services.Toc;
            _documentSearchService = services.DocumentSearch;
            _searchHighlightService = services.SearchHighlight;
            _documentSearchCoordinatorService = services.DocumentSearchCoordinator;
            _documentSearchState = services.DocumentSearchState;
            _documentSessionTracker = services.DocumentSessionTracker;
            _imageResourceService = services.ImageResource;
            _shutdownCoordinator = services.Shutdown;
            _sevenZipExtraction = services.SevenZipExtraction;
            _archiveSession = services.ArchiveSession;
            _favoritesService = services.Favorites;
            _recentService = services.Recent;
        }
    }
}
