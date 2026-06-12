using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using Windows.Graphics;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class AppSettingsService
    {
        private readonly string _windowSettingsFile;

        public AppSettingsService()
        {
            _windowSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "window_settings.txt");
        }

        public AppSettings LoadSettings(DisplayArea primaryArea)
        {
            var settings = new AppSettings();

            // Default resolution (70% of primary display if file not loaded)
            int defaultWidth = (int)(primaryArea.WorkArea.Width * 0.7);
            int defaultHeight = (int)(primaryArea.WorkArea.Height * 0.7);
            int defaultX = (primaryArea.WorkArea.Width - defaultWidth) / 2;
            int defaultY = (primaryArea.WorkArea.Height - defaultHeight) / 2;
            
            settings.LastNonMaximizedRect = new RectInt32(defaultX, defaultY, defaultWidth, defaultHeight);

            if (File.Exists(_windowSettingsFile))
            {
                try
                {
                    var lines = File.ReadAllLines(_windowSettingsFile);
                    if (lines.Length >= 4 &&
                        int.TryParse(lines[0], out int x) &&
                        int.TryParse(lines[1], out int y) &&
                        int.TryParse(lines[2], out int width) &&
                        int.TryParse(lines[3], out int height))
                    {
                        var savedRect = new RectInt32(x, y, width, height);
                        var targetArea = DisplayArea.GetFromRect(savedRect, DisplayAreaFallback.Nearest);
                        
                        int screenWidth = targetArea.WorkArea.Width;
                        int screenHeight = targetArea.WorkArea.Height;

                        if (width > screenWidth || height > screenHeight)
                        {
                            System.Diagnostics.Debug.WriteLine("Window size larger than screen. Capping to screen size.");
                            width = Math.Min(width, screenWidth);
                            height = Math.Min(height, screenHeight);
                        }

                        if (x + width < targetArea.WorkArea.X || x > targetArea.WorkArea.X + screenWidth)
                            x = targetArea.WorkArea.X + (screenWidth - width) / 2;
                        if (y + height < targetArea.WorkArea.Y || y > targetArea.WorkArea.Y + screenHeight)
                            y = targetArea.WorkArea.Y + (screenHeight - height) / 2;

                        width = Math.Max(400, width);
                        height = Math.Max(300, height);

                        settings.LastNonMaximizedRect = new RectInt32(x, y, width, height);

                        if (lines.Length >= 5 && lines[4].Trim() == "1") settings.IsMaximized = true;
                        if (lines.Length >= 6 && lines[5].Trim() == "1") settings.SharpenEnabled = true;
                        if (lines.Length >= 7 && lines[6].Trim() == "1") settings.IsSideBySideMode = true;
                        if (lines.Length >= 8 && lines[7].Trim() == "0") settings.NextImageOnRight = false;
                        if (lines.Length >= 10 && lines[9].Trim() == "1") settings.MatchControlDirection = true;
                        if (lines.Length >= 9 && int.TryParse(lines[8], out int themeVal)) settings.Theme = (ElementTheme)themeVal;
                        if (lines.Length >= 11 && lines[10].Trim() == "0") settings.AllowMultipleInstances = false;
                        if (lines.Length >= 12 && lines[11].Trim() == "0") settings.IsSidebarVisible = false;
                        if (lines.Length >= 13 && lines[12].Trim() == "0") settings.IsPinned = false;
                        if (lines.Length >= 14 && lines[13].Trim() == "1") settings.IsAlwaysOnTop = true;
                        if (lines.Length >= 15 && lines[14].Trim() == "1") settings.AutoDoublePageForArchive = true;
                        if (lines.Length >= 16 && lines[15].Trim() == "1") settings.IsRegistered = true;
                        if (lines.Length >= 17 && double.TryParse(lines[16], out double uFactor)) settings.UpscaleFactor = uFactor;
                        if (lines.Length >= 18 && double.TryParse(lines[17], out double sAmount)) settings.SharpenAmount = sAmount;
                        if (lines.Length >= 19 && double.TryParse(lines[18], out double unAmount)) settings.UnsharpAmount = unAmount;
                        if (lines.Length >= 20 && double.TryParse(lines[19], out double unRadius)) settings.UnsharpRadius = unRadius;
                        if (lines.Length >= 21 && double.TryParse(lines[20], out double sThreshold)) settings.SharpenThreshold = sThreshold;
                        if (lines.Length >= 22 && double.TryParse(lines[21], out double thumbnailSize)) settings.ExplorerThumbnailSize = Math.Clamp(thumbnailSize, 64, 180);
                        if (lines.Length >= 23 && lines[22].Trim() == "1") settings.ShowFolderThumbnails = true;
                        if (lines.Length >= 24 && !string.IsNullOrWhiteSpace(lines[23]))
                        {
                            settings.ExternalProgramPath = lines[23];
                        }
                        
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading window settings: {ex.Message}");
                }
            }
            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(_windowSettingsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new string[]
                {
                    settings.LastNonMaximizedRect.X.ToString(),
                    settings.LastNonMaximizedRect.Y.ToString(),
                    settings.LastNonMaximizedRect.Width.ToString(),
                    settings.LastNonMaximizedRect.Height.ToString(),
                    settings.IsMaximized ? "1" : "0",
                    settings.SharpenEnabled ? "1" : "0",
                    settings.IsSideBySideMode ? "1" : "0",
                    settings.NextImageOnRight ? "1" : "0",
                    ((int)settings.Theme).ToString(),
                    settings.MatchControlDirection ? "1" : "0",
                    settings.AllowMultipleInstances ? "1" : "0",
                    settings.IsSidebarVisible ? "1" : "0",
                    settings.IsPinned ? "1" : "0",
                    settings.IsAlwaysOnTop ? "1" : "0",
                    settings.AutoDoublePageForArchive ? "1" : "0",
                    settings.IsRegistered ? "1" : "0",
                    settings.UpscaleFactor.ToString("F2"),
                    settings.SharpenAmount.ToString("F2"),
                    settings.UnsharpAmount.ToString("F2"),
                    settings.UnsharpRadius.ToString("F2"),
                    settings.SharpenThreshold.ToString("F3"),
                    settings.ExplorerThumbnailSize.ToString("F0"),
                    settings.ShowFolderThumbnails ? "1" : "0",
                    settings.ExternalProgramPath ?? ""
                };

                File.WriteAllLines(_windowSettingsFile, lines);
                System.Diagnostics.Debug.WriteLine($"Window settings saved: Max={settings.IsMaximized}, RestoreRect={settings.LastNonMaximizedRect.X},{settings.LastNonMaximizedRect.Y},{settings.LastNonMaximizedRect.Width}x{settings.LastNonMaximizedRect.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings: {ex.Message}");
            }
        }
    }
}
