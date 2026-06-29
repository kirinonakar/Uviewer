namespace Uviewer.Services
{
    internal enum AppCommand
    {
        HandledOnly,
        ShowSearchOverlay,
        HideSearchOverlay,
        HideAboutDialog,
        ToggleFullscreen,
        ToggleMaximizeRestore,
        CloseApp,
        ShowEpubGoToLineDialog,
        ToggleFont,
        ToggleVerticalMode,
        DecreaseTextSize,
        IncreaseTextSize,
        ToggleSidebar,
        ToggleTheme,
        LoadEpubChapter,
        ToggleSideBySide,
        NavigateDocumentPage,
        NavigateToFirstImage,
        NavigateToLastImage,
        NavigateToFile,
        AddToFavorites,
        ToggleSharpening,
        ShowGoToLineDialog,
        NavigateToParentFolder,
        OpenFile,
        ZoomIn,
        ZoomOut,
        FitToWindow,
        ZoomActual,
        ToggleAlwaysOnTop,
        ToggleGlobalTheme,
        TogglePin
    }

    internal readonly record struct ShortcutRoute(AppCommand Command, int Direction = 0);
}
