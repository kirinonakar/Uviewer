using System;

namespace Uviewer.Services
{
    internal sealed class ImageViewerControllerDependencies
    {
        public ImageViewerControllerDependencies(
            IImageViewerHost host,
            IImageBitmapLifetimeHost bitmapLifetimeHost,
            IImageDocumentEntryHost documentEntryHost,
            IImageExplorerNavigationHost explorerNavigationHost,
            IImageFastNavigationHost fastNavigationHost,
            IImageInputHost inputHost,
            IImagePdfPageDisplayHost pdfPageDisplayHost,
            IImagePresentationHost presentationHost,
            IImagePreloadHost preloadHost,
            IImageSideBySideDisplayHost sideBySideDisplayHost,
            IImageSingleDisplayHost singleDisplayHost,
            IImageViewingOptionsHost viewingOptionsHost,
            IImageZoomHost zoomHost)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            BitmapLifetimeHost = bitmapLifetimeHost ?? throw new ArgumentNullException(nameof(bitmapLifetimeHost));
            DocumentEntryHost = documentEntryHost ?? throw new ArgumentNullException(nameof(documentEntryHost));
            ExplorerNavigationHost = explorerNavigationHost ?? throw new ArgumentNullException(nameof(explorerNavigationHost));
            FastNavigationHost = fastNavigationHost ?? throw new ArgumentNullException(nameof(fastNavigationHost));
            InputHost = inputHost ?? throw new ArgumentNullException(nameof(inputHost));
            PdfPageDisplayHost = pdfPageDisplayHost ?? throw new ArgumentNullException(nameof(pdfPageDisplayHost));
            PresentationHost = presentationHost ?? throw new ArgumentNullException(nameof(presentationHost));
            PreloadHost = preloadHost ?? throw new ArgumentNullException(nameof(preloadHost));
            SideBySideDisplayHost = sideBySideDisplayHost ?? throw new ArgumentNullException(nameof(sideBySideDisplayHost));
            SingleDisplayHost = singleDisplayHost ?? throw new ArgumentNullException(nameof(singleDisplayHost));
            ViewingOptionsHost = viewingOptionsHost ?? throw new ArgumentNullException(nameof(viewingOptionsHost));
            ZoomHost = zoomHost ?? throw new ArgumentNullException(nameof(zoomHost));
        }

        public IImageViewerHost Host { get; }
        public IImageBitmapLifetimeHost BitmapLifetimeHost { get; }
        public IImageDocumentEntryHost DocumentEntryHost { get; }
        public IImageExplorerNavigationHost ExplorerNavigationHost { get; }
        public IImageFastNavigationHost FastNavigationHost { get; }
        public IImageInputHost InputHost { get; }
        public IImagePdfPageDisplayHost PdfPageDisplayHost { get; }
        public IImagePresentationHost PresentationHost { get; }
        public IImagePreloadHost PreloadHost { get; }
        public IImageSideBySideDisplayHost SideBySideDisplayHost { get; }
        public IImageSingleDisplayHost SingleDisplayHost { get; }
        public IImageViewingOptionsHost ViewingOptionsHost { get; }
        public IImageZoomHost ZoomHost { get; }
    }
}
