using Microsoft.UI.Xaml;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class ImageFeatureComposition
            {
                public static void InitializeController(MainWindow window)
                {
                    window._imageViewerController = new ImageViewerController(new ImageViewerHostAdapter(window));
                    window._imageViewportNavigationService = new ImageViewportNavigationService(
                        window.DispatcherQueue,
                        window.RerenderPdfCurrentPageAsync);
                }

                public static void InitializeNavigation(MainWindow window)
                {
                    window._fastNavigationService = new FastNavigationService(window.DispatcherQueue);
                    window._imageNavigationCoordinator = new ImageNavigationCoordinator(new ImageNavigationHandlers
                    {
                        GetImageEntries = () => window._imageEntries,
                        GetCurrentIndex = () => window._currentIndex,
                        SetCurrentIndex = value => window._currentIndex = value,
                        IsCurrentViewSideBySide = () => window._isCurrentViewSideBySide,
                        SetScrollDirection = value => window._imageViewportNavigationService.ScrollDirection = value,
                        FastNavigationService = window._fastNavigationService,
                        ResetFastNavigationAsync = window.ResetFastNavigation,
                        UpdateFastNavigationUi = window.UpdateFastNavigationUI,
                        DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                        SaveCurrentPositionAsync = () => window.AddToRecentAsync(true),
                        ShouldPreloadAfterNavigate = () => window._archiveSession.CurrentArchive != null || window._currentPdfDocument != null,
                        StartPreload = window.StartImagePreload,
                        FocusViewer = () => window.RootGrid.Focus(FocusState.Programmatic)
                    });

                    window._animatedWebpService = new AnimatedWebpService(window._sharpeningService, window.DispatcherQueue);
                    window._animatedWebpService.FrameUpdated += window.OnAnimatedWebpFrameUpdated;
                    window._animatedWebpService.AnimationStopped += window.OnAnimatedWebpAnimationStopped;
                }

                public static void InitializePipeline(MainWindow window)
                {
                    window._imageCache = new ImageCacheManager(window.DispatcherQueue);
                    window._preloadManager = new PreloadManager(window._imageCache, window.DispatcherQueue);
                    window._imageBitmapLoader = new ImageBitmapLoader(window._imageCache, window._sharpeningService, window.DispatcherQueue);
                    window._imageDoublePageDecisionService = new ImageDoublePageDecisionService(window._imageCache);
                }

                public static void WireImageOptions(MainWindow window)
                {
                    window.ImageOptions.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName != null && !e.PropertyName.EndsWith("Text"))
                        {
                            window.OnSharpenParamsChanged();
                        }
                    };
                }
            }
        }
    }
}
