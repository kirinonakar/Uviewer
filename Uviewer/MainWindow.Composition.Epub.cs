using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class EpubFeatureComposition
            {
                public static void Initialize(MainWindow window)
                {
                    window._epubReaderController = new EpubReaderController(window);
                }
            }
        }
    }
}
