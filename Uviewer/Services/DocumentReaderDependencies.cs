using System;

namespace Uviewer.Services
{
    internal sealed class DocumentReaderDependencies
    {
        public DocumentReaderDependencies(
            IReaderAppStateHost appStateHost,
            ITextReaderViewHost viewHost,
            IImageNavigationHost imageNavigationHost,
            IEpubNavigationHost epubNavigationHost,
            IDocumentSearchHost searchHost,
            IReaderLibraryHost libraryHost)
        {
            AppStateHost = appStateHost ?? throw new ArgumentNullException(nameof(appStateHost));
            ViewHost = viewHost ?? throw new ArgumentNullException(nameof(viewHost));
            ImageNavigationHost = imageNavigationHost ?? throw new ArgumentNullException(nameof(imageNavigationHost));
            EpubNavigationHost = epubNavigationHost ?? throw new ArgumentNullException(nameof(epubNavigationHost));
            SearchHost = searchHost ?? throw new ArgumentNullException(nameof(searchHost));
            LibraryHost = libraryHost ?? throw new ArgumentNullException(nameof(libraryHost));
        }

        public IReaderAppStateHost AppStateHost { get; }
        public ITextReaderViewHost ViewHost { get; }
        public IImageNavigationHost ImageNavigationHost { get; }
        public IEpubNavigationHost EpubNavigationHost { get; }
        public IDocumentSearchHost SearchHost { get; }
        public IReaderLibraryHost LibraryHost { get; }
    }
}
