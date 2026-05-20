using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace Uviewer.Services
{
    internal sealed class TextPreviewKeyNavigationContext
    {
        internal bool IsTextMode { get; init; }
        internal bool IsVerticalMode { get; init; }
        internal bool IsAozoraMode { get; init; }
        internal bool IsMarkdownRenderMode { get; init; }
        internal bool ShouldInvertControls { get; init; }
        internal int AozoraBlockCount { get; init; }

        internal Func<Task> GoToVerticalStartAsync { get; init; } = null!;
        internal Func<Task> GoToVerticalEndAsync { get; init; } = null!;
        internal Func<Task> GoToAozoraStartAsync { get; init; } = null!;
        internal Func<Task> GoToAozoraEndAsync { get; init; } = null!;
        internal Action GoToTextStart { get; init; } = null!;
        internal Action GoToTextEnd { get; init; } = null!;
        internal Action ShowGoToLineDialog { get; init; } = null!;
        internal Action<int> NavigateVerticalPage { get; init; } = null!;
        internal Action<int> NavigateAozoraPage { get; init; } = null!;
        internal Action<int> NavigateTextPage { get; init; } = null!;
        internal Action IncreaseTextSize { get; init; } = null!;
        internal Action DecreaseTextSize { get; init; } = null!;
        internal Action ToggleAozoraMode { get; init; } = null!;
        internal Func<Task> ToggleVerticalModeAsync { get; init; } = null!;
        internal Action ToggleFont { get; init; } = null!;
        internal Action ToggleSidebar { get; init; } = null!;
        internal Action ToggleTheme { get; init; } = null!;
        internal Action ToggleGlobalTheme { get; init; } = null!;
    }

    internal sealed class TextPreviewKeyNavigationService
    {
        internal async Task HandleAsync(KeyRoutedEventArgs e, TextPreviewKeyNavigationContext context)
        {
            if (e.Handled || !context.IsTextMode) return;

            switch (e.Key)
            {
                case VirtualKey.Home:
                    await GoToStartAsync(context);
                    e.Handled = true;
                    return;

                case VirtualKey.End:
                    await GoToEndAsync(context);
                    e.Handled = true;
                    return;

                case VirtualKey.G:
                    context.ShowGoToLineDialog();
                    e.Handled = true;
                    return;

                case VirtualKey.Left:
                    NavigateHorizontal(context, isLeftKey: true);
                    e.Handled = true;
                    return;

                case VirtualKey.Right:
                    NavigateHorizontal(context, isLeftKey: false);
                    e.Handled = true;
                    return;

                case VirtualKey.Add:
                case (VirtualKey)187:
                    context.IncreaseTextSize();
                    e.Handled = true;
                    return;

                case VirtualKey.Subtract:
                case (VirtualKey)189:
                    context.DecreaseTextSize();
                    e.Handled = true;
                    return;

                case VirtualKey.A:
                    context.ToggleAozoraMode();
                    e.Handled = true;
                    return;

                case VirtualKey.V when !context.IsMarkdownRenderMode:
                    await context.ToggleVerticalModeAsync();
                    e.Handled = true;
                    return;

                case VirtualKey.F:
                    context.ToggleFont();
                    e.Handled = true;
                    return;

                case VirtualKey.B:
                    if (IsCtrlPressed()) context.ToggleSidebar();
                    else context.ToggleTheme();
                    e.Handled = true;
                    return;

                case VirtualKey.D:
                    if (!IsCtrlPressed())
                    {
                        context.ToggleGlobalTheme();
                        e.Handled = true;
                    }
                    return;
            }
        }

        private static async Task GoToStartAsync(TextPreviewKeyNavigationContext context)
        {
            if (context.IsVerticalMode)
            {
                await context.GoToVerticalStartAsync();
            }
            else if (context.IsAozoraMode && context.AozoraBlockCount > 0)
            {
                await context.GoToAozoraStartAsync();
            }
            else
            {
                context.GoToTextStart();
            }
        }

        private static async Task GoToEndAsync(TextPreviewKeyNavigationContext context)
        {
            if (context.IsVerticalMode)
            {
                await context.GoToVerticalEndAsync();
            }
            else if (context.IsAozoraMode && context.AozoraBlockCount > 0)
            {
                await context.GoToAozoraEndAsync();
            }
            else
            {
                context.GoToTextEnd();
            }
        }

        private static void NavigateHorizontal(TextPreviewKeyNavigationContext context, bool isLeftKey)
        {
            if (context.IsVerticalMode)
            {
                context.NavigateVerticalPage(isLeftKey ? 1 : -1);
                return;
            }

            int dir = isLeftKey
                ? (context.ShouldInvertControls ? 1 : -1)
                : (context.ShouldInvertControls ? -1 : 1);

            if (context.IsAozoraMode)
            {
                context.NavigateAozoraPage(dir);
            }
            else
            {
                context.NavigateTextPage(dir);
            }
        }

        private static bool IsCtrlPressed()
        {
            return Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down);
        }
    }
}
