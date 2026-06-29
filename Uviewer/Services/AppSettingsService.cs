using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Windows.Graphics;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class AppSettingsService
    {
        private readonly string _settingsDirectory;
        private readonly string _settingsFile;
        private readonly string _legacyWindowSettingsFile;

        public AppSettingsService()
        {
            _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer");
            _settingsFile = Path.Combine(_settingsDirectory, "window_settings.json");
            _legacyWindowSettingsFile = Path.Combine(_settingsDirectory, "window_settings.txt");
        }

        public bool HasPersistedSettings => File.Exists(_settingsFile) || File.Exists(_legacyWindowSettingsFile);

        public AppSettings LoadSettings() => LoadSettings(DisplayArea.Primary);

        public AppSettings LoadSettings(DisplayArea primaryArea)
        {
            var defaults = CreateDefaultSettings(primaryArea);

            if (File.Exists(_settingsFile))
            {
                return LoadJsonSettings(defaults);
            }

            if (File.Exists(_legacyWindowSettingsFile) &&
                TryLoadLegacySettings(primaryArea, out var legacySettings))
            {
                SaveSettings(legacySettings);
                System.Diagnostics.Debug.WriteLine("Legacy window_settings.txt migrated to window_settings.json.");
                return legacySettings;
            }

            return defaults;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(_settingsDirectory))
                {
                    Directory.CreateDirectory(_settingsDirectory);
                }

                var document = ToDocument(settings);
                var json = JsonSerializer.Serialize(document, typeof(AppSettingsDocument), AppSettingsJsonContext.Default);
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine($"Window settings saved: Max={settings.IsMaximized}, RestoreRect={settings.LastNonMaximizedRect.X},{settings.LastNonMaximizedRect.Y},{settings.LastNonMaximizedRect.Width}x{settings.LastNonMaximizedRect.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings: {ex.Message}");
            }
        }

        public void SaveRegistrationStatus(bool isRegistered, bool allowMultipleInstances)
        {
            var settings = LoadSettings();
            settings.IsRegistered = isRegistered;
            settings.AllowMultipleInstances = allowMultipleInstances;
            SaveSettings(settings);
        }

        private AppSettings LoadJsonSettings(AppSettings defaults)
        {
            try
            {
                var json = File.ReadAllText(_settingsFile);
                var document = JsonSerializer.Deserialize(json, typeof(AppSettingsDocument), AppSettingsJsonContext.Default) as AppSettingsDocument;
                if (document == null)
                {
                    return defaults;
                }

                return NormalizeSettings(FromDocument(document, defaults));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading window settings JSON: {ex.Message}");
                return defaults;
            }
        }

        private bool TryLoadLegacySettings(DisplayArea primaryArea, out AppSettings settings)
        {
            settings = CreateDefaultSettings(primaryArea);

            try
            {
                var lines = File.ReadAllLines(_legacyWindowSettingsFile);
                if (lines.Length < 4 ||
                    !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                    !int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                    !int.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                {
                    return false;
                }

                settings.LastNonMaximizedRect = new RectInt32(x, y, width, height);
                if (lines.Length >= 5 && lines[4].Trim() == "1") settings.IsMaximized = true;
                if (lines.Length >= 6 && lines[5].Trim() == "1") settings.SharpenEnabled = true;
                if (lines.Length >= 7 && lines[6].Trim() == "1") settings.IsSideBySideMode = true;
                if (lines.Length >= 8 && lines[7].Trim() == "0") settings.NextImageOnRight = false;
                if (lines.Length >= 10 && lines[9].Trim() == "1") settings.MatchControlDirection = true;
                if (lines.Length >= 9 && int.TryParse(lines[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int themeVal)) settings.Theme = (ElementTheme)themeVal;
                if (lines.Length >= 11 && lines[10].Trim() == "0") settings.AllowMultipleInstances = false;
                if (lines.Length >= 12 && lines[11].Trim() == "0") settings.IsSidebarVisible = false;
                if (lines.Length >= 13 && lines[12].Trim() == "0") settings.IsPinned = false;
                if (lines.Length >= 14 && lines[13].Trim() == "1") settings.IsAlwaysOnTop = true;
                if (lines.Length >= 15 && lines[14].Trim() == "1") settings.AutoDoublePageForArchive = true;
                if (lines.Length >= 16 && lines[15].Trim() == "1") settings.IsRegistered = true;
                if (lines.Length >= 17 && TryParseDouble(lines[16], out double uFactor)) settings.UpscaleFactor = uFactor;
                if (lines.Length >= 18 && TryParseDouble(lines[17], out double sAmount)) settings.SharpenAmount = sAmount;
                if (lines.Length >= 19 && TryParseDouble(lines[18], out double unAmount)) settings.UnsharpAmount = unAmount;
                if (lines.Length >= 20 && TryParseDouble(lines[19], out double unRadius)) settings.UnsharpRadius = unRadius;
                if (lines.Length >= 21 && TryParseDouble(lines[20], out double sThreshold)) settings.SharpenThreshold = sThreshold;
                if (lines.Length >= 22 && TryParseDouble(lines[21], out double thumbnailSize)) settings.ExplorerThumbnailSize = thumbnailSize;
                if (lines.Length >= 23 && lines[22].Trim() == "1") settings.ShowFolderThumbnails = true;
                if (lines.Length >= 24 && !string.IsNullOrWhiteSpace(lines[23])) settings.ExternalProgramPath = lines[23];

                settings = NormalizeSettings(settings);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading legacy window settings: {ex.Message}");
                return false;
            }
        }

        private static AppSettings CreateDefaultSettings(DisplayArea primaryArea)
        {
            var settings = new AppSettings();
            int defaultWidth = (int)(primaryArea.WorkArea.Width * 0.7);
            int defaultHeight = (int)(primaryArea.WorkArea.Height * 0.7);
            int defaultX = primaryArea.WorkArea.X + (primaryArea.WorkArea.Width - defaultWidth) / 2;
            int defaultY = primaryArea.WorkArea.Y + (primaryArea.WorkArea.Height - defaultHeight) / 2;

            settings.LastNonMaximizedRect = new RectInt32(defaultX, defaultY, defaultWidth, defaultHeight);
            return settings;
        }

        private static AppSettings NormalizeSettings(AppSettings settings)
        {
            settings.LastNonMaximizedRect = NormalizeWindowRect(settings.LastNonMaximizedRect);
            settings.ExplorerThumbnailSize = Math.Clamp(settings.ExplorerThumbnailSize, 64, 180);

            if (!Enum.IsDefined(typeof(ElementTheme), settings.Theme))
            {
                settings.Theme = ElementTheme.Default;
            }

            if (string.IsNullOrWhiteSpace(settings.ExternalProgramPath))
            {
                settings.ExternalProgramPath = AppSettings.DefaultExternalProgramPath;
            }

            return settings;
        }

        private static RectInt32 NormalizeWindowRect(RectInt32 rect)
        {
            var targetArea = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest);
            int screenWidth = targetArea.WorkArea.Width;
            int screenHeight = targetArea.WorkArea.Height;
            int minWidth = Math.Min(400, screenWidth);
            int minHeight = Math.Min(300, screenHeight);

            int width = Math.Clamp(rect.Width, minWidth, screenWidth);
            int height = Math.Clamp(rect.Height, minHeight, screenHeight);
            int x = rect.X;
            int y = rect.Y;

            if (x + width < targetArea.WorkArea.X || x > targetArea.WorkArea.X + screenWidth)
            {
                x = targetArea.WorkArea.X + (screenWidth - width) / 2;
            }

            if (y + height < targetArea.WorkArea.Y || y > targetArea.WorkArea.Y + screenHeight)
            {
                y = targetArea.WorkArea.Y + (screenHeight - height) / 2;
            }

            return new RectInt32(x, y, width, height);
        }

        private static AppSettingsDocument ToDocument(AppSettings settings)
        {
            return new AppSettingsDocument
            {
                Version = AppSettingsDocument.CurrentVersion,
                Window = new AppWindowSettings
                {
                    X = settings.LastNonMaximizedRect.X,
                    Y = settings.LastNonMaximizedRect.Y,
                    Width = settings.LastNonMaximizedRect.Width,
                    Height = settings.LastNonMaximizedRect.Height,
                    Maximized = settings.IsMaximized
                },
                Viewer = new AppViewerSettings
                {
                    SideBySide = settings.IsSideBySideMode,
                    NextImageOnRight = settings.NextImageOnRight,
                    Sharpen = settings.SharpenEnabled,
                    MatchControlDirection = settings.MatchControlDirection,
                    AutoDoublePageForArchive = settings.AutoDoublePageForArchive
                },
                Explorer = new AppExplorerSettings
                {
                    ThumbnailSize = settings.ExplorerThumbnailSize,
                    ShowFolderThumbnails = settings.ShowFolderThumbnails,
                    SidebarVisible = settings.IsSidebarVisible,
                    Pinned = settings.IsPinned
                },
                App = new AppBehaviorSettings
                {
                    Theme = (int)settings.Theme,
                    AllowMultipleInstances = settings.AllowMultipleInstances,
                    AlwaysOnTop = settings.IsAlwaysOnTop,
                    Registered = settings.IsRegistered
                },
                ImageProcessing = new AppImageProcessingSettings
                {
                    UpscaleFactor = settings.UpscaleFactor,
                    SharpenAmount = settings.SharpenAmount,
                    SharpenThreshold = settings.SharpenThreshold,
                    UnsharpAmount = settings.UnsharpAmount,
                    UnsharpRadius = settings.UnsharpRadius
                },
                ExternalProgramPath = settings.ExternalProgramPath ?? AppSettings.DefaultExternalProgramPath
            };
        }

        private static AppSettings FromDocument(AppSettingsDocument document, AppSettings defaults)
        {
            var window = document.Window ?? new AppWindowSettings();
            var viewer = document.Viewer ?? new AppViewerSettings();
            var explorer = document.Explorer ?? new AppExplorerSettings();
            var app = document.App ?? new AppBehaviorSettings();
            var imageProcessing = document.ImageProcessing ?? new AppImageProcessingSettings();

            return new AppSettings
            {
                LastNonMaximizedRect = new RectInt32(window.X, window.Y, window.Width, window.Height),
                IsMaximized = window.Maximized,
                SharpenEnabled = viewer.Sharpen,
                IsSideBySideMode = viewer.SideBySide,
                NextImageOnRight = viewer.NextImageOnRight,
                Theme = (ElementTheme)app.Theme,
                MatchControlDirection = viewer.MatchControlDirection,
                AllowMultipleInstances = app.AllowMultipleInstances,
                IsSidebarVisible = explorer.SidebarVisible,
                IsPinned = explorer.Pinned,
                IsAlwaysOnTop = app.AlwaysOnTop,
                AutoDoublePageForArchive = viewer.AutoDoublePageForArchive,
                IsRegistered = app.Registered,
                UpscaleFactor = imageProcessing.UpscaleFactor,
                SharpenAmount = imageProcessing.SharpenAmount,
                SharpenThreshold = imageProcessing.SharpenThreshold,
                UnsharpAmount = imageProcessing.UnsharpAmount,
                UnsharpRadius = imageProcessing.UnsharpRadius,
                ExplorerThumbnailSize = explorer.ThumbnailSize,
                ShowFolderThumbnails = explorer.ShowFolderThumbnails,
                ExternalProgramPath = document.ExternalProgramPath ?? defaults.ExternalProgramPath
            };
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }
    }
}
