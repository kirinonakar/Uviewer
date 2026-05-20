using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Uviewer.Services
{
    internal static class ImageToolbarStateService
    {
        internal static void ApplySharpenState(ToggleButton sharpenButton, FontIcon sharpenIcon, bool isEnabled)
        {
            sharpenButton.IsChecked = isEnabled;

            if (isEnabled)
            {
                sharpenIcon.FontWeight = FontWeights.Bold;
                sharpenButton.Foreground = new SolidColorBrush(Colors.White);

                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) &&
                    accent is Brush brush)
                {
                    sharpenButton.Background = brush;
                }

                return;
            }

            sharpenIcon.FontWeight = FontWeights.Normal;
            sharpenButton.ClearValue(Control.ForegroundProperty);
            sharpenButton.ClearValue(Control.BackgroundProperty);
        }

        internal static void ApplySideBySideState(Button sideBySideButton, TextBlock sideBySideText, bool isEnabled)
        {
            if (isEnabled)
            {
                sideBySideText.Text = "2";
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) &&
                    accent is Brush brush)
                {
                    sideBySideButton.Foreground = brush;
                }

                return;
            }

            sideBySideText.Text = "1";
            sideBySideButton.ClearValue(Button.ForegroundProperty);
        }

        internal static void ApplyNextImageSideState(FontIcon nextImageSideText, bool nextImageOnRight)
        {
            nextImageSideText.Glyph = nextImageOnRight
                ? "\uE111"
                : "\uE112";
        }
    }
}
