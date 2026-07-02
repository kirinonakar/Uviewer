using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageExifDialogService
    {
        private readonly ImageExifService _imageExifService = new();

        public bool TryGetInspectableEntry(
            IReadOnlyList<ImageEntry> entries,
            int currentIndex,
            out ImageEntry entry)
        {
            entry = null!;

            if (entries.Count == 0 || currentIndex < 0 || currentIndex >= entries.Count)
            {
                return false;
            }

            entry = entries[currentIndex];
            return !entry.IsPdfEntry && FileExplorerService.IsImageEntry(entry);
        }

        public async Task ShowAsync(
            ImageEntry entry,
            ArchiveSession archiveSession,
            XamlRoot xamlRoot,
            ElementTheme requestedTheme,
            CancellationToken token = default)
        {
            var rows = await _imageExifService.BuildRowsAsync(
                entry,
                ResolveDisplayPath(entry, archiveSession),
                archiveSession,
                token);

            if (rows.Count == 0)
            {
                rows.Add(new KeyValuePair<string, string>(Strings.ExifNoMetadata, Strings.ExifUnavailable));
            }

            var dialog = new ContentDialog
            {
                Title = Strings.ExifDialogTitle,
                Content = CreateDialogContent(rows),
                CloseButtonText = Strings.ExifCloseButton,
                XamlRoot = xamlRoot,
                RequestedTheme = requestedTheme
            };

            await dialog.ShowAsync();
        }

        private static string? ResolveDisplayPath(ImageEntry entry, ArchiveSession archiveSession)
        {
            if (entry.IsWebDavEntry) return entry.WebDavPath;
            if (entry.IsArchiveEntry && !string.IsNullOrEmpty(archiveSession.CurrentPath))
            {
                return $"{archiveSession.CurrentPath} :: {entry.ArchiveEntryKey}";
            }

            return entry.FilePath;
        }

        private static ScrollViewer CreateDialogContent(IReadOnlyList<KeyValuePair<string, string>> rows)
        {
            var panel = new StackPanel
            {
                Width = 472,
                Spacing = 10
            };

            foreach (var row in rows)
            {
                panel.Children.Add(CreateExifRow(row.Key, row.Value));
            }

            return new ScrollViewer
            {
                Content = new Border
                {
                    Padding = new Thickness(0, 0, 12, 0),
                    Child = panel
                },
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private static FrameworkElement CreateExifRow(string label, string value)
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
    }
}
