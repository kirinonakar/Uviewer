using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class TextReaderSettingsController
    {
        private readonly Func<TextSettingsManager> _getSettingsManager;
        private readonly TextUiSettingsService _uiSettingsService;
        private readonly TextDialogService _dialogService;
        private readonly ITextReaderViewHost _viewHost;
        private readonly Action _saveTextSettings;
        private readonly Func<Task> _refreshTextDisplayAsync;
        private readonly Func<UiFontApplyTargets> _createUiFontApplyTargets;
        private readonly Action<string, string, string> _showNotification;

        public TextReaderSettingsController(
            Func<TextSettingsManager> getSettingsManager,
            TextUiSettingsService uiSettingsService,
            TextDialogService dialogService,
            ITextReaderViewHost viewHost,
            Action saveTextSettings,
            Func<Task> refreshTextDisplayAsync,
            Func<UiFontApplyTargets> createUiFontApplyTargets,
            Action<string, string, string> showNotification)
        {
            _getSettingsManager = getSettingsManager ?? throw new ArgumentNullException(nameof(getSettingsManager));
            _uiSettingsService = uiSettingsService ?? throw new ArgumentNullException(nameof(uiSettingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _viewHost = viewHost ?? throw new ArgumentNullException(nameof(viewHost));
            _saveTextSettings = saveTextSettings ?? throw new ArgumentNullException(nameof(saveTextSettings));
            _refreshTextDisplayAsync = refreshTextDisplayAsync ?? throw new ArgumentNullException(nameof(refreshTextDisplayAsync));
            _createUiFontApplyTargets = createUiFontApplyTargets ?? throw new ArgumentNullException(nameof(createUiFontApplyTargets));
            _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
        }

        public void FontToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFont();
        }

        public void FontMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialog();
        }

        public async Task ShowFontPickerDialog()
        {
            var settingsManager = _getSettingsManager();
            var selectedFont = await _dialogService.ShowFontPickerAsync(settingsManager.FontFamily, Strings.FontSelectionTitle);
            if (selectedFont != null)
            {
                SetTextFont(selectedFont);
            }
        }

        public async void SetTextFont(string fontFamily)
        {
            try
            {
                _getSettingsManager().FontFamily = fontFamily;
                _saveTextSettings();
                await _refreshTextDisplayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SetTextFont: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void UiFontMenu_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowUiFontPickerDialog();
        }

        public async Task ShowUiFontPickerDialog()
        {
            var settingsManager = _getSettingsManager();
            var selectedFont = await _dialogService.ShowFontPickerAsync(settingsManager.UIFontFamily, Strings.UIFontSelectionTitle);
            if (selectedFont != null)
            {
                SetUiFont(selectedFont);
            }
        }

        public void SetUiFont(string fontFamily)
        {
            if (_uiSettingsService.ApplyUiFont(_getSettingsManager(), fontFamily, _createUiFontApplyTargets()))
            {
                _saveTextSettings();
            }
        }

        public async void ToggleFont()
        {
            try
            {
                _uiSettingsService.ToggleFont(_getSettingsManager());
                _saveTextSettings();
                await _refreshTextDisplayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleFont: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void UpdateFontSettingsMenu()
        {
            var settingsManager = _getSettingsManager();
            _viewHost.MainToolbar.UpdateDefaultFontMenu(settingsManager.DefaultFont1, settingsManager.DefaultFont2);
        }

        public void SetDefaultFont1MenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialogForDefault(1);
        }

        public void SetDefaultFont2MenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowFontPickerDialogForDefault(2);
        }

        public void ResetDefaultFontsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _uiSettingsService.ResetDefaultFonts(_getSettingsManager());
                UpdateFontSettingsMenu();
                _saveTextSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ResetDefaultFontsMenuItem_Click: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task ShowFontPickerDialogForDefault(int slot)
        {
            var settingsManager = _getSettingsManager();
            string currentFont = slot == 1 ? settingsManager.DefaultFont1 : settingsManager.DefaultFont2;
            var selectedFont = await _dialogService.ShowFontPickerAsync(currentFont, Strings.FontSelectionSlotTitle(slot));

            if (selectedFont != null)
            {
                _uiSettingsService.SetDefaultFont(settingsManager, slot, selectedFont);
                UpdateFontSettingsMenu();
                _saveTextSettings();
            }
        }

        public void TextSizeUpButton_Click(object sender, RoutedEventArgs e)
        {
            IncreaseTextSize();
        }

        public async void IncreaseTextSize()
        {
            try
            {
                var settingsManager = _getSettingsManager();
                _uiSettingsService.IncreaseFontSize(settingsManager);
                _viewHost.MainToolbar.SetTextSizeLevel(settingsManager.FontSize);
                _saveTextSettings();
                await _refreshTextDisplayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IncreaseTextSize: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void TextSizeDownButton_Click(object sender, RoutedEventArgs e)
        {
            DecreaseTextSize();
        }

        public async void DecreaseTextSize()
        {
            try
            {
                var settingsManager = _getSettingsManager();
                _uiSettingsService.DecreaseFontSize(settingsManager);
                _viewHost.MainToolbar.SetTextSizeLevel(settingsManager.FontSize);
                _saveTextSettings();
                await _refreshTextDisplayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DecreaseTextSize: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTheme();
        }

        public async void ToggleTheme()
        {
            try
            {
                _uiSettingsService.ToggleTheme(_getSettingsManager());
                _saveTextSettings();
                await _refreshTextDisplayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ToggleTheme: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }
    }
}
