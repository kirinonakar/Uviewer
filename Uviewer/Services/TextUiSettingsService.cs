using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Uviewer.Controls;

namespace Uviewer.Services
{
    internal sealed record UiFontApplyTargets(
        IReadOnlyList<Control?> Controls,
        IReadOnlyList<TextBlock?> TextBlocks,
        MainToolbarControl MainToolbar,
        FrameworkElement? ThemeRefreshRoot,
        Action RefreshDynamicItems);

    internal sealed class TextUiSettingsService
    {
        public void ApplyLanguage(TextSettingsManager settingsManager, string language)
        {
            settingsManager.Language = language;
            try
            {
                if (language == "Auto" || string.IsNullOrEmpty(language))
                {
                    var systemLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
                    Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                        systemLanguages.Count > 0 ? systemLanguages[0] : string.Empty;
                }
                else
                {
                    Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language apply error: {ex.Message}");
            }
        }

        public bool ApplyUiFont(
            TextSettingsManager settingsManager,
            string fontFamily,
            UiFontApplyTargets targets)
        {
            if (string.IsNullOrEmpty(fontFamily) || fontFamily == "Unknown")
            {
                settingsManager.UIFontFamily = string.Empty;
                return false;
            }

            FontFamily resolvedFont;
            try
            {
                resolvedFont = new FontFamily(fontFamily);
            }
            catch
            {
                return false;
            }

            settingsManager.UIFontFamily = fontFamily;

            foreach (var control in targets.Controls)
            {
                if (control != null) control.FontFamily = resolvedFont;
            }

            foreach (var textBlock in targets.TextBlocks)
            {
                if (textBlock != null) textBlock.FontFamily = resolvedFont;
            }

            targets.MainToolbar.ApplyUiFont(resolvedFont);
            targets.RefreshDynamicItems();
            ApplyFontResources(resolvedFont, targets.ThemeRefreshRoot);
            return true;
        }

        public void ToggleFont(TextSettingsManager settingsManager)
        {
            settingsManager.FontFamily = settingsManager.FontFamily == settingsManager.DefaultFont1
                ? settingsManager.DefaultFont2
                : settingsManager.DefaultFont1;
        }

        public void IncreaseFontSize(TextSettingsManager settingsManager)
        {
            settingsManager.FontSize = Math.Min(72, settingsManager.FontSize + 2);
        }

        public void DecreaseFontSize(TextSettingsManager settingsManager)
        {
            settingsManager.FontSize = Math.Max(8, settingsManager.FontSize - 2);
        }

        public void ToggleTheme(TextSettingsManager settingsManager)
        {
            int maxThemes = settingsManager.CustomBackgroundColor.HasValue ? 4 : 3;
            settingsManager.ThemeIndex = (settingsManager.ThemeIndex + 1) % maxThemes;
        }

        public void ResetDefaultFonts(TextSettingsManager settingsManager)
        {
            settingsManager.DefaultFont1 = "Yu Gothic";
            settingsManager.DefaultFont2 = "Yu Mincho";
        }

        public void SetDefaultFont(TextSettingsManager settingsManager, int slot, string fontFamily)
        {
            if (slot == 1)
            {
                settingsManager.DefaultFont1 = fontFamily;
            }
            else
            {
                settingsManager.DefaultFont2 = fontFamily;
            }
        }

        private static void ApplyFontResources(FontFamily fontFamily, FrameworkElement? themeRefreshRoot)
        {
            try
            {
                var resources = Application.Current.Resources;
                resources["ContentControlThemeFontFamily"] = fontFamily;
                resources["ControlContentThemeFontFamily"] = fontFamily;
                resources["TextControlFontFamily"] = fontFamily;
                resources["ComboBoxPlaceholderTextThemeFontFamily"] = fontFamily;
                resources["ContentPresenterFontFamily"] = fontFamily;
                resources["ListViewItemFontFamily"] = fontFamily;
                resources["GridViewItemFontFamily"] = fontFamily;
                resources["MenuFlyoutItemFontFamily"] = fontFamily;
                resources["PickerPlaceholderTextFontFamily"] = fontFamily;

                if (themeRefreshRoot != null)
                {
                    var currentTheme = themeRefreshRoot.RequestedTheme;
                    themeRefreshRoot.RequestedTheme = currentTheme == ElementTheme.Dark
                        ? ElementTheme.Light
                        : ElementTheme.Dark;
                    themeRefreshRoot.RequestedTheme = currentTheme;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Font resource update error: {ex.Message}");
            }
        }
    }
}
