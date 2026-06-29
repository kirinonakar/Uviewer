using Windows.System;

namespace Uviewer.Services
{
    internal sealed class ShortcutRouter
    {
        public bool TryRoutePreviewKeyDown(ShortcutInput input, ShortcutContext context, out ShortcutRoute route)
        {
            route = default;

            if (context.IsColorPickerOpen && input.Key == VirtualKey.Escape)
            {
                return false;
            }

            if (input.CtrlPressed && input.Key == VirtualKey.F && context.CanSearchCurrentDocument)
            {
                return Set(out route, AppCommand.ShowSearchOverlay);
            }

            if (input.Key == VirtualKey.Escape)
            {
                if (context.IsSearchOverlayOpen) return Set(out route, AppCommand.HideSearchOverlay);
                if (context.IsAboutDialogActive) return Set(out route, AppCommand.HideAboutDialog);
                if (context.IsFullscreen) return Set(out route, AppCommand.ToggleFullscreen);
                return Set(out route, AppCommand.CloseApp);
            }

            if (input.Key == VirtualKey.F11)
            {
                return Set(out route, AppCommand.ToggleFullscreen);
            }

            if (input.Key == VirtualKey.F10)
            {
                return Set(out route, AppCommand.ToggleMaximizeRestore);
            }

            if (context.IsEpubMode && TryRouteEpubPreviewKeyDown(input, context, out route))
            {
                return true;
            }

            if (input.Key == VirtualKey.Space)
            {
                return Set(out route, context.IsTextMode ? AppCommand.HandledOnly : AppCommand.ToggleSideBySide);
            }

            if (!context.IsTextMode && !context.IsEpubMode &&
                TryRouteImagePreviewKeyDown(input, context, out route))
            {
                return true;
            }

            if (input.Key == VirtualKey.Up || input.Key == VirtualKey.PageUp)
            {
                return Set(out route, AppCommand.NavigateToFile, -1);
            }

            if (input.Key == VirtualKey.Down || input.Key == VirtualKey.PageDown)
            {
                return Set(out route, AppCommand.NavigateToFile, 1);
            }

            return false;
        }

        public bool TryRouteKeyDown(ShortcutInput input, ShortcutContext context, out ShortcutRoute route)
        {
            route = default;

            switch (input.Key)
            {
                case VirtualKey.S:
                    if (input.CtrlPressed)
                    {
                        return Set(out route, AppCommand.AddToFavorites);
                    }

                    if (!context.IsTextMode || context.IsAozoraMode || context.IsVerticalMode)
                    {
                        return Set(out route, AppCommand.ToggleSharpening);
                    }

                    return Set(out route, AppCommand.HandledOnly);

                case VirtualKey.G:
                    if (!context.IsTextMode && !context.IsEpubMode && context.HasPdfDocument)
                    {
                        return Set(out route, AppCommand.ShowGoToLineDialog);
                    }
                    break;

                case VirtualKey.Back:
                    return Set(out route, AppCommand.NavigateToParentFolder);

                case VirtualKey.O when input.CtrlPressed:
                    return Set(out route, AppCommand.OpenFile);

                case VirtualKey.B when input.CtrlPressed:
                    return Set(out route, AppCommand.ToggleSidebar);

                case VirtualKey.Add:
                case (VirtualKey)187:
                    return Set(out route, AppCommand.ZoomIn);

                case VirtualKey.Subtract:
                case (VirtualKey)189:
                    return Set(out route, AppCommand.ZoomOut);

                case VirtualKey.Number0:
                case VirtualKey.NumberPad0:
                    if (!input.CtrlPressed)
                    {
                        return Set(out route, AppCommand.FitToWindow);
                    }
                    break;

                case VirtualKey.Number1:
                case VirtualKey.NumberPad1:
                    if (!input.CtrlPressed)
                    {
                        return Set(out route, AppCommand.ZoomActual);
                    }
                    break;

                case VirtualKey.T when !input.CtrlPressed:
                    return Set(out route, AppCommand.ToggleAlwaysOnTop);

                case VirtualKey.D when !input.CtrlPressed:
                    return Set(out route, AppCommand.ToggleGlobalTheme);

                case (VirtualKey)192:
                    return Set(out route, AppCommand.TogglePin);
            }

            return false;
        }

        private static bool TryRouteEpubPreviewKeyDown(ShortcutInput input, ShortcutContext context, out ShortcutRoute route)
        {
            route = default;

            switch (input.Key)
            {
                case VirtualKey.Left:
                    return Set(out route, AppCommand.NavigateDocumentPage, -1);

                case VirtualKey.Right:
                    return Set(out route, AppCommand.NavigateDocumentPage, 1);

                case VirtualKey.G:
                    return Set(out route, AppCommand.ShowEpubGoToLineDialog);

                case VirtualKey.F:
                    return Set(out route, AppCommand.ToggleFont);

                case VirtualKey.V:
                    return Set(out route, AppCommand.ToggleVerticalMode);

                case VirtualKey.Subtract:
                case (VirtualKey)189:
                    return Set(out route, AppCommand.DecreaseTextSize);

                case VirtualKey.Add:
                case (VirtualKey)187:
                    return Set(out route, AppCommand.IncreaseTextSize);

                case VirtualKey.B:
                    return Set(out route, input.CtrlPressed ? AppCommand.ToggleSidebar : AppCommand.ToggleTheme);

                case VirtualKey.Home:
                    return Set(
                        out route,
                        context.CurrentEpubChapterIndex > 0 ? AppCommand.LoadEpubChapter : AppCommand.HandledOnly,
                        -1);

                case VirtualKey.End:
                    return Set(
                        out route,
                        context.CurrentEpubChapterIndex < context.EpubSpineCount - 1 ? AppCommand.LoadEpubChapter : AppCommand.HandledOnly,
                        1);
            }

            return false;
        }

        private static bool TryRouteImagePreviewKeyDown(ShortcutInput input, ShortcutContext context, out ShortcutRoute route)
        {
            route = default;

            if (input.Key == VirtualKey.Left)
            {
                return Set(out route, AppCommand.NavigateDocumentPage, context.ShouldInvertControls ? 1 : -1);
            }

            if (input.Key == VirtualKey.Right)
            {
                return Set(out route, AppCommand.NavigateDocumentPage, context.ShouldInvertControls ? -1 : 1);
            }

            if (context.ImageEntriesCount <= 0)
            {
                return false;
            }

            if (input.Key == VirtualKey.Home)
            {
                return Set(
                    out route,
                    context.CurrentImageIndex != 0 ? AppCommand.NavigateToFirstImage : AppCommand.HandledOnly);
            }

            if (input.Key == VirtualKey.End)
            {
                return Set(
                    out route,
                    context.CurrentImageIndex != context.ImageEntriesCount - 1 ? AppCommand.NavigateToLastImage : AppCommand.HandledOnly);
            }

            return false;
        }

        private static bool Set(out ShortcutRoute route, AppCommand command, int direction = 0)
        {
            route = new ShortcutRoute(command, direction);
            return true;
        }
    }
}
