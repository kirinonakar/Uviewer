using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
                Width = 472,
                Spacing = 10
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
                    Content = new Border
                    {
                        Padding = new Thickness(0, 0, 12, 0),
                        Child = panel
                    },
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
                ColumnSpacing = 12,
                MinWidth = 0
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            var valueBlock = new TextBox
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                AcceptsReturn = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                MinHeight = 32,
                MaxHeight = value.Length > 500 ? 240 : 96
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(valueBlock, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(
                valueBlock,
                value.Length > 500 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);

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
                    AddEmbeddedTextMetadataRows(rows, bytes);
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

            byte[] bytes = await File.ReadAllBytesAsync(filePath);
            AddEmbeddedTextMetadataRows(rows, bytes);

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

        private static void AddEmbeddedTextMetadataRows(List<KeyValuePair<string, string>> rows, byte[] bytes)
        {
            foreach (var metadata in ReadPngTextMetadata(bytes))
            {
                AddRow(rows, metadata.Key, metadata.Value);
            }

            foreach (var metadata in ReadJpegTextMetadata(bytes))
            {
                AddRow(rows, metadata.Key, metadata.Value);
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadPngTextMetadata(byte[] bytes)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes.Length < signature.Length || !signature.SequenceEqual(bytes.Take(signature.Length)))
            {
                yield break;
            }

            int offset = signature.Length;
            while (offset + 12 <= bytes.Length)
            {
                int length = ReadBigEndianInt32(bytes, offset);
                if (length < 0 || offset + 12L + length > bytes.Length)
                {
                    yield break;
                }

                string chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                int dataOffset = offset + 8;

                KeyValuePair<string, string>? row = chunkType switch
                {
                    "tEXt" => ReadPngTextChunk(bytes, dataOffset, length),
                    "zTXt" => ReadPngCompressedTextChunk(bytes, dataOffset, length),
                    "iTXt" => ReadPngInternationalTextChunk(bytes, dataOffset, length),
                    _ => null
                };

                if (row.HasValue)
                {
                    yield return row.Value;
                }

                offset += 12 + length;
            }
        }

        private static KeyValuePair<string, string>? ReadPngTextChunk(byte[] bytes, int offset, int length)
        {
            int separator = FindByte(bytes, offset, length, 0);
            if (separator <= offset) return null;

            string keyword = DecodeLatin1(bytes, offset, separator - offset);
            string value = DecodeLatin1(bytes, separator + 1, offset + length - separator - 1);
            return CreateEmbeddedMetadataRow(keyword, value);
        }

        private static KeyValuePair<string, string>? ReadPngCompressedTextChunk(byte[] bytes, int offset, int length)
        {
            int separator = FindByte(bytes, offset, length, 0);
            if (separator <= offset || separator + 2 >= offset + length) return null;

            string keyword = DecodeLatin1(bytes, offset, separator - offset);
            byte compressionMethod = bytes[separator + 1];
            if (compressionMethod != 0) return null;

            string? value = TryDecompressZlib(bytes, separator + 2, offset + length - separator - 2, Encoding.Latin1);
            return CreateEmbeddedMetadataRow(keyword, value);
        }

        private static KeyValuePair<string, string>? ReadPngInternationalTextChunk(byte[] bytes, int offset, int length)
        {
            int end = offset + length;
            int keywordEnd = FindByte(bytes, offset, length, 0);
            if (keywordEnd <= offset || keywordEnd + 3 >= end) return null;

            string keyword = DecodeLatin1(bytes, offset, keywordEnd - offset);
            byte compressionFlag = bytes[keywordEnd + 1];
            byte compressionMethod = bytes[keywordEnd + 2];
            if (compressionFlag > 1 || compressionMethod != 0) return null;

            int languageTagOffset = keywordEnd + 3;
            int languageTagEnd = FindByte(bytes, languageTagOffset, end - languageTagOffset, 0);
            if (languageTagEnd < languageTagOffset) return null;

            int translatedKeywordOffset = languageTagEnd + 1;
            int translatedKeywordEnd = FindByte(bytes, translatedKeywordOffset, end - translatedKeywordOffset, 0);
            if (translatedKeywordEnd < translatedKeywordOffset) return null;

            int textOffset = translatedKeywordEnd + 1;
            int textLength = end - textOffset;
            string? value = compressionFlag == 1
                ? TryDecompressZlib(bytes, textOffset, textLength, Encoding.UTF8)
                : Encoding.UTF8.GetString(bytes, textOffset, textLength);

            return CreateEmbeddedMetadataRow(keyword, value);
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadJpegTextMetadata(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            {
                yield break;
            }

            int offset = 2;
            while (offset + 4 <= bytes.Length)
            {
                if (bytes[offset] != 0xFF)
                {
                    yield break;
                }

                byte marker = bytes[offset + 1];
                offset += 2;

                if (marker == 0xD9 || marker == 0xDA)
                {
                    yield break;
                }

                if (offset + 2 > bytes.Length)
                {
                    yield break;
                }

                int segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
                if (segmentLength < 2 || offset + segmentLength > bytes.Length)
                {
                    yield break;
                }

                int dataOffset = offset + 2;
                int dataLength = segmentLength - 2;

                if (marker == 0xFE)
                {
                    string comment = DecodeBestEffort(bytes, dataOffset, dataLength);
                    var row = CreateEmbeddedMetadataRow("Comment", comment);
                    if (row.HasValue) yield return row.Value;
                }
                else if (marker == 0xE1)
                {
                    string text = DecodeBestEffort(bytes, dataOffset, dataLength);
                    if (text.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("parameters", StringComparison.OrdinalIgnoreCase))
                    {
                        var row = CreateEmbeddedMetadataRow("XMP", text);
                        if (row.HasValue) yield return row.Value;
                    }
                }

                offset += segmentLength;
            }
        }

        private static KeyValuePair<string, string>? CreateEmbeddedMetadataRow(string keyword, string? value)
        {
            if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string label = keyword.Trim();
            if (!label.StartsWith("PNG ", StringComparison.OrdinalIgnoreCase) &&
                !label.Equals("Comment", StringComparison.OrdinalIgnoreCase) &&
                !label.Equals("XMP", StringComparison.OrdinalIgnoreCase))
            {
                label = $"PNG {label}";
            }

            return new KeyValuePair<string, string>(label, value.Trim());
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                | (bytes[offset + 1] << 16)
                | (bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static int FindByte(byte[] bytes, int offset, int length, byte value)
        {
            int end = Math.Min(bytes.Length, offset + length);
            for (int i = offset; i < end; i++)
            {
                if (bytes[i] == value) return i;
            }

            return -1;
        }

        private static string DecodeLatin1(byte[] bytes, int offset, int length)
        {
            return Encoding.Latin1.GetString(bytes, offset, Math.Max(0, length));
        }

        private static string DecodeBestEffort(byte[] bytes, int offset, int length)
        {
            try
            {
                return Encoding.UTF8.GetString(bytes, offset, Math.Max(0, length)).Trim('\0');
            }
            catch
            {
                return DecodeLatin1(bytes, offset, length).Trim('\0');
            }
        }

        private static string? TryDecompressZlib(byte[] bytes, int offset, int length, Encoding encoding)
        {
            try
            {
                using var input = new MemoryStream(bytes, offset, length);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return encoding.GetString(output.ToArray());
            }
            catch
            {
                return null;
            }
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
