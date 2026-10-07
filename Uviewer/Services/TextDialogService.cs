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

        public async Task<(int wrapLength, TextAlignment alignment, bool twoColumns)?> ShowTextOptionsAsync(int wrapLength, TextAlignment alignment, bool twoColumns)
        {
            var slider = new Slider { Minimum = 10, Maximum = 120, StepFrequency = 1, Value = wrapLength };
            var headerLabel = new TextBlock { Text = $"{Strings.TextWrapLength}: {wrapLength}" };
            slider.Header = headerLabel;
            slider.ValueChanged += (_, e) => headerLabel.Text = $"{Strings.TextWrapLength}: {(int)e.NewValue}";
            var alignmentPicker = new ComboBox
            {
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                ItemsSource = new[] { Strings.TextAlignLeft, Strings.TextAlignCenter, Strings.TextAlignRight },
                SelectedIndex = alignment == TextAlignment.Center ? 1 : alignment == TextAlignment.Right ? 2 : 0
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(alignmentPicker, Strings.TextAlignmentLabel);
            var alignmentPanel = new StackPanel { Spacing = 8 };
            alignmentPanel.Children.Add(new TextBlock { Text = Strings.TextAlignmentLabel });
            alignmentPanel.Children.Add(alignmentPicker);
            var panel = new StackPanel { Spacing = 12, Width = 360 };
            panel.Children.Add(new TextBlock { Text = Strings.TextOptionsHint, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
            panel.Children.Add(slider);
            panel.Children.Add(alignmentPanel);
            var twoColumnCheckBox = new CheckBox
            {
                Content = Strings.TextHorizontalTwoColumns,
                IsChecked = twoColumns
            };
            ToolTipService.SetToolTip(twoColumnCheckBox, Strings.TextHorizontalTwoColumnsHint);
            panel.Children.Add(twoColumnCheckBox);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = RequestedTheme,
                Title = Strings.TextOptions,
                Content = panel,
                PrimaryButtonText = Strings.TextOptionsApply,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary
            };
            // Keep the measured content size stable while the ComboBox moves its
            // selected item into and out of the popup. The content is not laid out
            // yet when the dialog opens, so ActualHeight is still 0 in the Opened
            // event; using it directly would collapse the panel and leave the dialog
            // empty. Capture the first height reported after layout instead.
            void CaptureContentHeight(object? sender, SizeChangedEventArgs e)
            {
                if (e.NewSize.Height <= 0) return;
                panel.SizeChanged -= CaptureContentHeight;
                panel.Height = e.NewSize.Height;
            }

            panel.SizeChanged += CaptureContentHeight;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            return ((int)slider.Value, alignmentPicker.SelectedIndex switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            }, twoColumnCheckBox.IsChecked == true);
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
