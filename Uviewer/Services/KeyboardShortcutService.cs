using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace Uviewer.Services
{
    public class KeyboardShortcutService : IKeyboardShortcutService
    {
        private readonly ShortcutRouter _router;
        private readonly KeyboardShortcutFeatureController _featureController;

        public KeyboardShortcutService()
            : this(new ShortcutRouter(), new KeyboardShortcutFeatureController())
        {
        }

        internal KeyboardShortcutService(ShortcutRouter router, KeyboardShortcutFeatureController featureController)
        {
            _router = router;
            _featureController = featureController;
        }

        public async Task HandlePreviewKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions)
        {
            if (actions.HandleDeleteDialogKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (IsTextInput(e.OriginalSource))
            {
                return;
            }

            var input = new ShortcutInput(e.Key, IsCtrlPressed());
            var context = CreateContext(actions);
            if (!_router.TryRoutePreviewKeyDown(input, context, out var route))
            {
                return;
            }

            e.Handled = true;
            await _featureController.ExecuteAsync(route, actions);
        }

        public async Task HandleKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions)
        {
            if (e.Handled || IsTextInput(e.OriginalSource))
            {
                return;
            }

            var input = new ShortcutInput(e.Key, IsCtrlPressed());
            var context = CreateContext(actions);
            if (!_router.TryRouteKeyDown(input, context, out var route))
            {
                return;
            }

            e.Handled = true;
            await _featureController.ExecuteAsync(route, actions);
        }

        private static bool IsTextInput(object originalSource)
        {
            // Input templates can raise key events from a child element. Leave editing
            // and ComboBox navigation keys to the control instead of viewer shortcuts.
            var source = originalSource as DependencyObject;
            while (source != null)
            {
                if (source is TextBox or PasswordBox or NumberBox or ComboBox or ComboBoxItem)
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static bool IsCtrlPressed()
        {
            return Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down);
        }

        private static ShortcutContext CreateContext(IKeyboardShortcutActions actions)
        {
            return new ShortcutContext
            {
                IsColorPickerOpen = actions.IsColorPickerOpen,
                IsFullscreen = actions.IsFullscreen,
                IsEpubMode = actions.IsEpubMode,
                IsTextMode = actions.IsTextMode,
                IsAozoraMode = actions.IsAozoraMode,
                IsVerticalMode = actions.IsVerticalMode,
                ShouldInvertControls = actions.ShouldInvertControls,
                CurrentEpubChapterIndex = actions.CurrentEpubChapterIndex,
                EpubSpineCount = actions.EpubSpineCount,
                CurrentImageIndex = actions.CurrentImageIndex,
                ImageEntriesCount = actions.ImageEntriesCount,
                HasPdfDocument = actions.HasPdfDocument,
                IsAboutDialogActive = actions.IsAboutDialogActive,
                IsDeleteDialogOpen = actions.IsDeleteDialogOpen,
                IsSearchOverlayOpen = actions.IsSearchOverlayOpen,
                CanSearchCurrentDocument = actions.CanSearchCurrentDocument
            };
        }
    }
}
