using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class TextFeatureComposition
            {
                public static void Initialize(MainWindow window)
                {
                    window._documentReaderController = new DocumentReaderController(window);
                    window._searchOverlayService = new SearchOverlayService(
                        window.SearchCurrentDocumentAsync,
                        window.NavigateToSearchMatchAsync,
                        window.GetCurrentSearchPosition,
                        window.SetActiveSearchQuery);
                    window.LoadTextSettings();
                }
            }
        }
    }
}
