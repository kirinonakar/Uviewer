using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Text.Json;
using Windows.UI;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class TextSettingsManager
    {
        private readonly string _settingsFilePath;

        // --- Settings Properties ---
        public double FontSize { get; set; } = 18;
        public string FontFamily { get; set; } = "Yu Gothic";
        public string UIFontFamily { get; set; } = "";
        public int ThemeIndex { get; set; } = 0; // 0: White, 1: Beige, 2: Dark, 3: Custom
        public string Language { get; set; } = "Auto";
        public bool IsVerticalMode { get; set; } = false;
        public Color? CustomBackgroundColor { get; set; }
        public Color? CustomForegroundColor { get; set; }
        public string EncodingName { get; set; } = "Auto";

        public TextSettingsManager(string settingsFilePath)
        {
            _settingsFilePath = settingsFilePath;
        }

        // --- Load & Save ---
        public void Load()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize(json, typeof(TextSettings), TextSettingsContext.Default) as TextSettings;

                    if (settings != null)
                    {
                        FontSize = Math.Clamp(settings.FontSize, 8, 72);
                        FontFamily = settings.FontFamily ?? "Yu Gothic";
                        ThemeIndex = settings.ThemeIndex;
                        IsVerticalMode = settings.IsVerticalMode;
                        Language = settings.Language ?? "Auto";
                        UIFontFamily = settings.UIFontFamily ?? "";

                        if (!string.IsNullOrEmpty(settings.CustomBackgroundColor))
                            CustomBackgroundColor = ParseHexColor(settings.CustomBackgroundColor);
                        if (!string.IsNullOrEmpty(settings.CustomForegroundColor))
                            CustomForegroundColor = ParseHexColor(settings.CustomForegroundColor);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading text settings: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                var settings = new TextSettings
                {
                    FontSize = FontSize,
                    FontFamily = FontFamily,
                    ThemeIndex = ThemeIndex,
                    IsVerticalMode = IsVerticalMode,
                    CustomBackgroundColor = CustomBackgroundColor?.ToString(),
                    CustomForegroundColor = CustomForegroundColor?.ToString(),
                    Language = Language,
                    UIFontFamily = UIFontFamily
                };

                var settingsDir = Path.GetDirectoryName(_settingsFilePath);
                if (settingsDir != null && !Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var json = JsonSerializer.Serialize(settings, typeof(TextSettings), TextSettingsContext.Default);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving text settings: {ex.Message}");
            }
        }

        // --- Theme Helpers ---
        public Brush GetThemeBackground()
        {
            if (ThemeIndex == 0) return new SolidColorBrush(Colors.White);
            if (ThemeIndex == 1) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 235)); // Beige
            if (ThemeIndex == 3 && CustomBackgroundColor.HasValue) return new SolidColorBrush(CustomBackgroundColor.Value);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)); // Dark
        }

        public Brush GetThemeForeground()
        {
            if (ThemeIndex == 2) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204)); // Dark theme
            if (ThemeIndex == 3 && CustomForegroundColor.HasValue) return new SolidColorBrush(CustomForegroundColor.Value);
            return new SolidColorBrush(Colors.Black); // Default White/Beige theme text color
        }

        // --- Color Utilities ---
        public static Color ParseHexColor(string hex)
        {
            try
            {
                if (hex.StartsWith("#")) hex = hex.Substring(1);
                if (hex.Length == 8) // ARGB
                {
                    byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
                else if (hex.Length == 6) // RGB
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch { }
            return Colors.White;
        }

        public static (double h, double s, double l) ToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h, s, l = (max + min) / 2.0;

            if (max == min) { h = s = 0; }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6.0;
            }
            return (h * 360, s * 100, l * 100);
        }

        public static Color FromHsl(double h, double s, double l)
        {
            h /= 360.0; s /= 100.0; l /= 100.0;
            double r, g, b;
            if (s == 0) { r = g = b = l; }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }
            return Color.FromArgb(255, (byte)Math.Clamp(r * 255, 0, 255), (byte)Math.Clamp(g * 255, 0, 255), (byte)Math.Clamp(b * 255, 0, 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }
}
