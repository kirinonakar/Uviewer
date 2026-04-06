using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.UI;
using Uviewer.Dialogs;

namespace Uviewer.Services
{
    public class TextDialogService
    {
        private readonly FrameworkElement _rootElement;

        public TextDialogService(FrameworkElement rootElement)
        {
            _rootElement = rootElement;
        }

        private XamlRoot XamlRoot => _rootElement.XamlRoot;
        private ElementTheme RequestedTheme => _rootElement.ActualTheme;

        public async Task<(Color bg, Color fg)?> ShowColorPickerAsync(Color currentBg, Color currentFg)
        {
            var dialog = new ColorPickerDialog(currentBg, currentFg)
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.RequestedTheme
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                return (dialog.SelectedBackgroundColor, dialog.SelectedForegroundColor);
            }
            return null;
        }

        public async Task<string?> ShowFontPickerAsync(string currentFont, string title)
        {
            var dialog = new FontPickerDialog(currentFont, title)
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.RequestedTheme
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                return dialog.SelectedFont;
            }
            return null;
        }

        public async Task<int?> ShowGoToLineAsync(int currentLine, int totalLines, string title)
        {
            var dialog = new GoToLineDialog(currentLine, totalLines, title)
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.RequestedTheme
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (int.TryParse(dialog.EnteredText, out int line))
                {
                    return line;
                }
            }
            return null;
        }
    }
}
