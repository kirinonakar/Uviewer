using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private async void FileNameText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                if (!TryGetCurrentImageEntry(out var entry))
                {
                    return;
                }

                await ShowImageExifDialogAsync(entry);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing EXIF: {ex.Message}");
                ShowNotification(Strings.ExifUnavailable, "\uE783", "Red");
            }
        }

        private bool TryGetCurrentImageEntry(out ImageEntry entry)
        {
            entry = null!;

            if (_imageEntries == null || _imageEntries.Count == 0)
            {
                return false;
            }

            if (_currentIndex < 0 || _currentIndex >= _imageEntries.Count)
            {
                return false;
            }

            entry = _imageEntries[_currentIndex];
            return !entry.IsPdfEntry && FileExplorerService.IsImageEntry(entry);
        }

        private async Task ShowImageExifDialogAsync(ImageEntry entry)
        {
            var rows = await BuildExifRowsAsync(entry);
            if (rows.Count == 0)
            {
                rows.Add(new KeyValuePair<string, string>(Strings.ExifNoMetadata, Strings.ExifUnavailable));
            }

            var panel = new StackPanel
            {
                Width = 480,
                Spacing = 8
            };

            foreach (var row in rows)
            {
                panel.Children.Add(CreateExifRow(row.Key, row.Value));
            }

            var dialog = new ContentDialog
            {
                Title = Strings.ExifDialogTitle,
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 520,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = Strings.ExifCloseButton,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            await dialog.ShowAsync();
        }

        private FrameworkElement CreateExifRow(string label, string value)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };

            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            return grid;
        }

        private async Task<List<KeyValuePair<string, string>>> BuildExifRowsAsync(ImageEntry entry)
        {
            var rows = new List<KeyValuePair<string, string>>();
            string? displayPath = GetEntryDisplayPath(entry);

            AddRow(rows, Strings.ExifFileName, entry.DisplayName);
            AddRow(rows, Strings.ExifFilePath, displayPath);

            if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
            {
                await AddFileExifRowsAsync(rows, entry.FilePath);
                return rows;
            }

            if (entry.IsArchiveEntry && _archiveSession.HasArchive && !string.IsNullOrEmpty(entry.ArchiveEntryKey))
            {
                var bytes = await _archiveSession.ReadEntryBytesAsync(entry.ArchiveEntryKey, _imageLoadingCts?.Token ?? default);
                if (bytes != null && bytes.Length > 0)
                {
                    using var memoryStream = new MemoryStream(bytes);
                    using var randomAccessStream = memoryStream.AsRandomAccessStream();
                    await AddDecoderExifRowsAsync(rows, randomAccessStream);
                }
            }

            return rows;
        }

        private string? GetEntryDisplayPath(ImageEntry entry)
        {
            if (entry.IsWebDavEntry) return entry.WebDavPath;
            if (entry.IsArchiveEntry && !string.IsNullOrEmpty(_archiveSession.CurrentPath))
            {
                return $"{_archiveSession.CurrentPath} :: {entry.ArchiveEntryKey}";
            }

            return entry.FilePath;
        }

        private async Task AddFileExifRowsAsync(List<KeyValuePair<string, string>> rows, string filePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            var basicProperties = await file.GetBasicPropertiesAsync();
            var imageProperties = await file.Properties.GetImagePropertiesAsync();

            AddRow(rows, Strings.ExifFileSize, FormatFileSize(basicProperties.Size));
            AddRow(rows, Strings.ExifModified, FormatDateTime(basicProperties.DateModified));

            if (imageProperties.Width > 0 && imageProperties.Height > 0)
            {
                AddRow(rows, Strings.ExifDimensions, $"{imageProperties.Width} × {imageProperties.Height}");
            }

            AddRow(rows, Strings.ExifDateTaken, FormatDateTime(imageProperties.DateTaken));
            AddRow(rows, Strings.ExifCameraMaker, imageProperties.CameraManufacturer);
            AddRow(rows, Strings.ExifCameraModel, imageProperties.CameraModel);
            AddRow(rows, Strings.ExifOrientation, imageProperties.Orientation.ToString());
            AddRow(rows, Strings.ExifLatitude, imageProperties.Latitude?.ToString("G", CultureInfo.CurrentCulture));
            AddRow(rows, Strings.ExifLongitude, imageProperties.Longitude?.ToString("G", CultureInfo.CurrentCulture));
            AddRow(rows, Strings.ExifTitle, imageProperties.Title);
            if (imageProperties.Rating > 0) AddRow(rows, Strings.ExifRating, imageProperties.Rating.ToString(CultureInfo.CurrentCulture));
            AddRow(rows, Strings.ExifKeywords, imageProperties.Keywords.Count > 0 ? string.Join(", ", imageProperties.Keywords) : null);

            using var stream = await file.OpenAsync(FileAccessMode.Read);
            await AddDecoderExifRowsAsync(rows, stream);
        }

        private async Task AddDecoderExifRowsAsync(List<KeyValuePair<string, string>> rows, IRandomAccessStream stream)
        {
            try
            {
                stream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(stream);

                if (!rows.Any(row => row.Key == Strings.ExifDimensions) && decoder.PixelWidth > 0 && decoder.PixelHeight > 0)
                {
                    AddRow(rows, Strings.ExifDimensions, $"{decoder.PixelWidth} × {decoder.PixelHeight}");
                }

                var propertyNames = new[]
                {
                    "System.Photo.ExposureTime",
                    "System.Photo.FNumber",
                    "System.Photo.ISOSpeed",
                    "System.Photo.FocalLength"
                };

                var properties = await decoder.BitmapProperties.GetPropertiesAsync(propertyNames);
                AddRow(rows, Strings.ExifExposureTime, FormatExposureTime(GetMetadataValue(properties, "System.Photo.ExposureTime")));
                AddRow(rows, Strings.ExifFNumber, FormatFNumber(GetMetadataValue(properties, "System.Photo.FNumber")));
                AddRow(rows, Strings.ExifIso, FormatMetadataValue(GetMetadataValue(properties, "System.Photo.ISOSpeed")));
                AddRow(rows, Strings.ExifFocalLength, FormatFocalLength(GetMetadataValue(properties, "System.Photo.FocalLength")));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXIF decoder read failed: {ex.Message}");
            }
        }

        private static object? GetMetadataValue(BitmapPropertySet properties, string name)
        {
            return properties.TryGetValue(name, out var typedValue) ? typedValue.Value : null;
        }

        private static void AddRow(List<KeyValuePair<string, string>> rows, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            rows.Add(new KeyValuePair<string, string>(label, value.Trim()));
        }

        private static string? FormatDateTime(DateTimeOffset value)
        {
            return value == default ? null : value.ToString("G", CultureInfo.CurrentCulture);
        }

        private static string FormatFileSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        private static string? FormatExposureTime(object? value)
        {
            if (!TryGetDouble(value, out var seconds) || seconds <= 0)
            {
                return FormatMetadataValue(value);
            }

            if (seconds < 1)
            {
                return $"1/{Math.Round(1 / seconds):0} s";
            }

            return $"{seconds:0.###} s";
        }

        private static string? FormatFNumber(object? value)
        {
            return TryGetDouble(value, out var number) && number > 0
                ? $"f/{number:0.#}"
                : FormatMetadataValue(value);
        }

        private static string? FormatFocalLength(object? value)
        {
            return TryGetDouble(value, out var millimeters) && millimeters > 0
                ? $"{millimeters:0.#} mm"
                : FormatMetadataValue(value);
        }

        private static bool TryGetDouble(object? value, out double result)
        {
            result = 0;

            switch (value)
            {
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case int i:
                    result = i;
                    return true;
                case uint ui:
                    result = ui;
                    return true;
                case long l:
                    result = l;
                    return true;
                case ulong ul:
                    result = ul;
                    return true;
                case short s:
                    result = s;
                    return true;
                case ushort us:
                    result = us;
                    return true;
                case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    return false;
            }
        }

        private static string? FormatMetadataValue(object? value)
        {
            return value switch
            {
                null => null,
                string text => string.IsNullOrWhiteSpace(text) ? null : text,
                string[] values => values.Length == 0 ? null : string.Join(", ", values),
                IEnumerable<string> values => string.Join(", ", values),
                Array values => string.Join(", ", values.Cast<object>()),
                DateTimeOffset dateTime => FormatDateTime(dateTime),
                IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
                _ => value.ToString()
            };
        }
    }
}
