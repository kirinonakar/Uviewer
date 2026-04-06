using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Uviewer.Dialogs
{
    public sealed partial class FontPickerDialog : ContentDialog
    {
        private List<string> _allFonts;
        public string? SelectedFont => FontListView.SelectedItem as string;
        public string DialogTitle { get; }

        public FontPickerDialog(string currentFont, string title)
        {
            this.InitializeComponent();
            DialogTitle = title;

            _allFonts = CanvasTextFormat.GetSystemFontFamilies()
                .OrderBy(f => f)
                .ToList();

            FontListView.ItemsSource = _allFonts;

            if (!string.IsNullOrEmpty(currentFont))
            {
                var selected = _allFonts.FirstOrDefault(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                {
                    FontListView.SelectedItem = selected;
                    FontListView.ScrollIntoView(selected);
                }
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var filtered = _allFonts.Where(f => f.Contains(sender.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                FontListView.ItemsSource = filtered;
            }
        }

        private void StackPanel_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
        }
    }
}
