using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private readonly ImageExifService _imageExifService = new();

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
            var rows = await _imageExifService.BuildRowsAsync(
                entry,
                GetEntryDisplayPath(entry),
                _archiveSession,
                _imageLoadingCts?.Token ?? default);

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

        private string? GetEntryDisplayPath(ImageEntry entry)
        {
            if (entry.IsWebDavEntry) return entry.WebDavPath;
            if (entry.IsArchiveEntry && !string.IsNullOrEmpty(_archiveSession.CurrentPath))
            {
                return $"{_archiveSession.CurrentPath} :: {entry.ArchiveEntryKey}";
            }

            return entry.FilePath;
        }
    }
}
