using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
            return originalSource is TextBox || originalSource is PasswordBox || originalSource is NumberBox;
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
                IsSearchOverlayOpen = actions.IsSearchOverlayOpen,
                CanSearchCurrentDocument = actions.CanSearchCurrentDocument
            };
        }
    }
}
