using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;
using Uviewer.Services;

namespace Uviewer.Dialogs
{
    public sealed partial class ColorPickerDialog : ContentDialog
    {
        public Color SelectedBackgroundColor { get; private set; }
        public Color SelectedForegroundColor { get; private set; }

        private (double h, double s, double l) _bgHsl;
        private (double h, double s, double l) _fgHsl;
        private bool _isUpdating = false;

        public ColorPickerDialog(Color currentBg, Color currentFg)
        {
            this.InitializeComponent();
            _bgHsl = TextSettingsManager.ToHsl(currentBg);
            _fgHsl = TextSettingsManager.ToHsl(currentFg);

            _isUpdating = true;
            BgHSlider.Value = _bgHsl.h;
            BgSSlider.Value = _bgHsl.s;
            BgLSlider.Value = _bgHsl.l;

            FgHSlider.Value = _fgHsl.h;
            FgSSlider.Value = _fgHsl.s;
            FgLSlider.Value = _fgHsl.l;
            _isUpdating = false;

            UpdatePreview();
        }

        private void BgSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdating) return;
            _bgHsl = (BgHSlider.Value, BgSSlider.Value, BgLSlider.Value);
            UpdatePreview();
        }

        private void FgSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdating) return;
            _fgHsl = (FgHSlider.Value, FgSSlider.Value, FgLSlider.Value);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            SelectedBackgroundColor = TextSettingsManager.FromHsl(_bgHsl.h, _bgHsl.s, _bgHsl.l);
            SelectedForegroundColor = TextSettingsManager.FromHsl(_fgHsl.h, _fgHsl.s, _fgHsl.l);

            if (PreviewBorder != null) PreviewBorder.Background = new SolidColorBrush(SelectedBackgroundColor);
            if (PreviewText != null)
            {
                PreviewText.Foreground = new SolidColorBrush(SelectedForegroundColor);
                PreviewText.Text = Strings.Preview + " - Abc 가나다 123";
            }
        }
    }
}
