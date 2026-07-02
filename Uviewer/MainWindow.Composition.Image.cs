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
                    var imageStatePort = new ImageStatePort(window);
                    var imageBitmapLifetimePort = new ImageBitmapLifetimePort(window);
                    var imageInputPort = new ImageInputPort(window);
                    var imagePdfPageDisplayPort = new ImagePdfPageDisplayPort(window);
                    var imageSideBySideDisplayPort = new ImageSideBySideDisplayPort(window);
                    var imageSingleDisplayPort = new ImageSingleDisplayPort(window);
                    var imageZoomPort = new ImageZoomPort(window);
                    var imageNavigationPort = new ImageNavigationPort(window);
                    var imageDocumentOpenPort = new ImageDocumentOpenPort(window);
                    var imageUiPort = new ImageUiPort(window);
                    var imageSettingsPort = new ImageSettingsPort(window);

                    window._imageViewerController = new ImageViewerController(
                        new ImageViewerControllerDependencies(
                            host: imageStatePort,
                            bitmapLifetimeHost: imageBitmapLifetimePort,
                            documentEntryHost: imageDocumentOpenPort,
                            explorerNavigationHost: imageNavigationPort,
                            fastNavigationHost: imageNavigationPort,
                            inputHost: imageInputPort,
                            pdfPageDisplayHost: imagePdfPageDisplayPort,
                            presentationHost: imageUiPort,
                            preloadHost: imageStatePort,
                            sideBySideDisplayHost: imageSideBySideDisplayPort,
                            singleDisplayHost: imageSingleDisplayPort,
                            viewingOptionsHost: imageSettingsPort,
                            zoomHost: imageZoomPort));
                    window._imageViewportNavigationService = new ImageViewportNavigationService(
                        window.DispatcherQueue,
                        window.RerenderPdfCurrentPageAsync);
                }

                public static void InitializeNavigation(MainWindow window)
                {
                    window._fastNavigationService = new FastNavigationService(window.DispatcherQueue);
                    window._imageNavigationCoordinator = new ImageNavigationCoordinator(new ImageNavigationHandlers
                    {
                        GetImageEntries = () => window._imageViewerState.Entries,
                        GetCurrentIndex = () => window._imageViewerState.CurrentIndex,
                        SetCurrentIndex = value => window._imageViewerState.CurrentIndex = value,
                        IsCurrentViewSideBySide = () => window._imageViewerState.IsCurrentViewSideBySide,
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
