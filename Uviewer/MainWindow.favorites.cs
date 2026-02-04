using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // Favorites
        private ObservableCollection<FavoriteItem> _favorites = new();
        private const string FavoritesFilePath = "favorites.json";
        private string GetFavoritesFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", FavoritesFilePath);

        // Recent Images
        private ObservableCollection<RecentItem> _recentItems = new();
        private const string RecentFilePath = "recent.json";
        private string GetRecentFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", RecentFilePath);
        private const int MaxRecentItems = 5;

        public class FavoriteItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Type { get; set; } = ""; // "Folder", "File", "Archive"
            public string? ArchiveEntryKey { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
        }

        public class RecentItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Type { get; set; } = ""; // "Folder", "File", "Archive"
            public string? ArchiveEntryKey { get; set; }
            public DateTime AccessedAt { get; set; } = DateTime.Now;
        }


        #region Favorites

        private async Task LoadFavorites()
        {
            try
            {
                var favoritesFile = GetFavoritesFilePath();
                var favoritesDir = Path.GetDirectoryName(favoritesFile);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(favoritesDir) && !Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                System.Diagnostics.Debug.WriteLine($"Loading favorites from: {favoritesFile}");

                if (File.Exists(favoritesFile))
                {
                    var json = await File.ReadAllTextAsync(favoritesFile);
                    var favorites = System.Text.Json.JsonSerializer.Deserialize(json, typeof(List<FavoriteItem>), FavoritesContext.Default);
                    if (favorites != null)
                    {
                        _favorites.Clear();
                        foreach (var fav in (List<FavoriteItem>)favorites)
                        {
                            _favorites.Add(fav);
                        }
                        System.Diagnostics.Debug.WriteLine($"Loaded {_favorites.Count} favorites");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Favorites file does not exist");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading favorites: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task SaveFavorites()
        {
            try
            {
                var favoritesFile = GetFavoritesFilePath();
                var favoritesDir = Path.GetDirectoryName(favoritesFile);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(favoritesDir) && !Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(_favorites.ToList(), typeof(List<FavoriteItem>), FavoritesContext.Default);
                await File.WriteAllTextAsync(favoritesFile, json);
                System.Diagnostics.Debug.WriteLine($"Favorites saved to: {favoritesFile}");
                System.Diagnostics.Debug.WriteLine($"Saved {_favorites.Count} favorites");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving favorites: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateFavoritesMenu()
        {
            // Find the FavoritesPanel in the custom Flyout
            if (FavoritesPanel == null) return;

            // Remove all dynamic items (keep only the first 2: Add button and separator)
            while (FavoritesPanel.Children.Count > 2)
            {
                FavoritesPanel.Children.RemoveAt(FavoritesPanel.Children.Count - 1);
            }

            if (_favorites.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "즐겨찾기 없음",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                FavoritesPanel.Children.Add(emptyText);
                return;
            }

            foreach (var favorite in _favorites.OrderByDescending<FavoriteItem, DateTime>(f => f.CreatedAt))
            {
                // Capture the favorite for lambda closures
                var currentFavorite = favorite;

                // Create a Grid to hold the name and delete button
                var itemGrid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    MinHeight = 36
                };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Create TextBlock for the favorite name with left alignment and tooltip
                var nameTextBlock = new TextBlock
                {
                    Text = favorite.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = 340, // Reserve space for delete button (400 - 12 left margin - 8 right margin - 40 for button)
                    Margin = new Thickness(12, 0, 8, 0),
                    FontSize = 13
                };
                ToolTipService.SetToolTip(nameTextBlock, favorite.Name);

                // Create a transparent button overlay for clicking
                var nameButton = new Button
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                nameButton.Click += async (s, e) =>
                {
                    FavoritesFlyout.Hide();
                    await NavigateToFavoriteAsync(currentFavorite);
                };

                // Add both to a container grid in the first column
                var nameContainer = new Grid();
                nameContainer.Children.Add(nameTextBlock);
                nameContainer.Children.Add(nameButton);

                Grid.SetColumn(nameContainer, 0);
                itemGrid.Children.Add(nameContainer);

                // Create delete button with X icon - right aligned
                var deleteButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 14 }, // X icon
                    Width = 32,
                    Height = 32,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ToolTipService.SetToolTip(deleteButton, "삭제");
                deleteButton.Click += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Delete button clicked for: {currentFavorite.Name}");
                    await RemoveFavoriteAsync(currentFavorite);
                };
                Grid.SetColumn(deleteButton, 1);
                itemGrid.Children.Add(deleteButton);

                FavoritesPanel.Children.Add(itemGrid);
            }
        }

        private async Task AddToFavoritesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== AddToFavoritesAsync called ===");

                string name = "";
                string path = "";
                string type = "";
                string? archiveEntryKey = null;

                System.Diagnostics.Debug.WriteLine($"_currentArchive: {_currentArchive != null}");
                System.Diagnostics.Debug.WriteLine($"_currentArchivePath: {_currentArchivePath}");
                System.Diagnostics.Debug.WriteLine($"_currentExplorerPath: {_currentExplorerPath}");
                System.Diagnostics.Debug.WriteLine($"_currentIndex: {_currentIndex}");
                System.Diagnostics.Debug.WriteLine($"_imageEntries.Count: {_imageEntries.Count}");

                // Show current favorites
                System.Diagnostics.Debug.WriteLine($"Current favorites count: {_favorites.Count}");
                foreach (var fav in _favorites)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {fav.Name} ({fav.Type}): {fav.Path}");
                }

                if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    // Archive mode - add current image position
                    if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        var currentEntry = _imageEntries[_currentIndex];
                        name = $"{Path.GetFileName(_currentArchivePath)} - {currentEntry.DisplayName}";
                        path = _currentArchivePath;
                        type = "Archive";
                        archiveEntryKey = currentEntry.ArchiveEntryKey;
                        System.Diagnostics.Debug.WriteLine($"Archive mode: {name}");

                        // Also add the archive folder as a separate bookmark
                        var archiveFolder = Path.GetDirectoryName(_currentArchivePath);
                        if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                        {
                            var folderName = Path.GetFileName(archiveFolder);
                            if (string.IsNullOrEmpty(folderName))
                                folderName = archiveFolder;

                            // Check if folder bookmark already exists
                            var existingFolder = _favorites.FirstOrDefault(f => f.Path == archiveFolder && f.Type == "Folder");
                            if (existingFolder == null)
                            {
                                var folderFavorite = new FavoriteItem
                                {
                                    Name = folderName,
                                    Path = archiveFolder,
                                    Type = "Folder"
                                };
                                _favorites.Add(folderFavorite);
                                System.Diagnostics.Debug.WriteLine($"Added archive folder as favorite: {folderName}");
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(_currentExplorerPath))
                {
                    // Folder mode - add current folder
                    name = Path.GetFileName(_currentExplorerPath);
                    if (string.IsNullOrEmpty(name))
                        name = _currentExplorerPath;
                    path = _currentExplorerPath;
                    type = "Folder";
                    System.Diagnostics.Debug.WriteLine($"Folder mode: {name}");
                }
                else if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    // File mode - add current file
                    var currentEntry = _imageEntries[_currentIndex];
                    if (!string.IsNullOrEmpty(currentEntry.FilePath))
                    {
                        name = currentEntry.DisplayName;
                        path = currentEntry.FilePath;
                        type = "File";
                        System.Diagnostics.Debug.WriteLine($"File mode: {name}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Final values - Name: '{name}', Path: '{path}', Type: '{type}'");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                {
                    // Check if already exists - for archives, check both path and ArchiveEntryKey
                    FavoriteItem? existing = null;
                    if (type == "Archive")
                    {
                        existing = _favorites.FirstOrDefault(f => f.Path == path && f.Type == type && f.ArchiveEntryKey == archiveEntryKey);
                    }
                    else
                    {
                        existing = _favorites.FirstOrDefault(f => f.Path == path && f.Type == type);
                    }

                    if (existing != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Favorite already exists: {existing.Name}");
                        // Show message that it already exists
                        return;
                    }

                    var favorite = new FavoriteItem
                    {
                        Name = name,
                        Path = path,
                        Type = type,
                        ArchiveEntryKey = archiveEntryKey
                    };

                    _favorites.Add(favorite);
                    System.Diagnostics.Debug.WriteLine($"Added favorite: {favorite.Name}");
                    await SaveFavorites();
                    UpdateFavoritesMenu();
                    System.Diagnostics.Debug.WriteLine("Favorite added and saved successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Cannot add favorite - missing name or path");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding to favorites: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task RemoveFavoriteAsync(FavoriteItem favorite)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Removing favorite: {favorite.Name}");
                _favorites.Remove(favorite);
                await SaveFavorites();
                UpdateFavoritesMenu();
                System.Diagnostics.Debug.WriteLine($"Favorite removed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing favorite: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task NavigateToFavoriteAsync(FavoriteItem favorite)
        {
            try
            {
                switch (favorite.Type)
                {
                    case "Folder":
                        if (Directory.Exists(favorite.Path))
                        {
                            LoadExplorerFolder(favorite.Path);
                        }
                        break;
                    case "File":
                        if (File.Exists(favorite.Path))
                        {
                            var file = await StorageFile.GetFileFromPathAsync(favorite.Path);
                            await LoadImageFromFileAsync(file);
                        }
                        break;
                    case "Archive":
                        if (File.Exists(favorite.Path))
                        {
                            // First navigate to the archive file's folder
                            var archiveFolder = Path.GetDirectoryName(favorite.Path);
                            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                            {
                                LoadExplorerFolder(archiveFolder);

                                // Then open the archive and navigate to specific entry
                                if (!string.IsNullOrEmpty(favorite.ArchiveEntryKey))
                                {
                                    await LoadImagesFromArchiveAsync(favorite.Path);
                                    var entryIndex = _imageEntries.FindIndex(e => e.ArchiveEntryKey == favorite.ArchiveEntryKey);
                                    if (entryIndex >= 0)
                                    {
                                        _currentIndex = entryIndex;
                                        await DisplayCurrentImageAsync();
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to favorite: {ex.Message}");
            }
        }

        #endregion

        #region Recent Items

        private async Task LoadRecentItems()
        {
            try
            {
                var recentFile = GetRecentFilePath();
                var recentDir = Path.GetDirectoryName(recentFile);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(recentDir) && !Directory.Exists(recentDir))
                {
                    Directory.CreateDirectory(recentDir);
                }

                System.Diagnostics.Debug.WriteLine($"Loading recent items from: {recentFile}");

                if (File.Exists(recentFile))
                {
                    var json = await File.ReadAllTextAsync(recentFile);
                    var recentItems = System.Text.Json.JsonSerializer.Deserialize(json, typeof(List<RecentItem>), RecentContext.Default);
                    if (recentItems != null)
                    {
                        _recentItems.Clear();
                        foreach (var item in (List<RecentItem>)recentItems)
                        {
                            _recentItems.Add(item);
                        }
                        System.Diagnostics.Debug.WriteLine($"Loaded {_recentItems.Count} recent items");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Recent items file does not exist");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent items: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task SaveRecentItems()
        {
            try
            {
                var recentFile = GetRecentFilePath();
                var recentDir = Path.GetDirectoryName(recentFile);

                // Ensure directory exists
                if (!string.IsNullOrEmpty(recentDir) && !Directory.Exists(recentDir))
                {
                    Directory.CreateDirectory(recentDir);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(_recentItems.ToList(), typeof(List<RecentItem>), RecentContext.Default);
                await File.WriteAllTextAsync(recentFile, json);
                System.Diagnostics.Debug.WriteLine($"Recent items saved to: {recentFile}");
                System.Diagnostics.Debug.WriteLine($"Saved {_recentItems.Count} recent items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recent items: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateRecentMenu()
        {
            // Find the RecentPanel in the custom Flyout
            if (RecentPanel == null) return;

            // Remove all existing items
            RecentPanel.Children.Clear();

            if (_recentItems.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "최근 이미지 없음",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                RecentPanel.Children.Add(emptyText);
                return;
            }

            foreach (var recent in _recentItems.OrderByDescending<RecentItem, DateTime>(r => r.AccessedAt))
            {
                // Capture the recent item for lambda closures
                var currentRecent = recent;

                // Create a Grid to hold the name and delete button
                var itemGrid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    MinHeight = 36
                };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Create TextBlock for the recent item name with left alignment and tooltip
                var nameTextBlock = new TextBlock
                {
                    Text = recent.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = 340, // Reserve space for delete button (400 - 12 left margin - 8 right margin - 40 for button)
                    Margin = new Thickness(12, 0, 8, 0),
                    FontSize = 13
                };
                ToolTipService.SetToolTip(nameTextBlock, recent.Name);

                // Create a transparent button overlay for clicking
                var nameButton = new Button
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                nameButton.Click += async (s, e) =>
                {
                    RecentFlyout.Hide();
                    await NavigateToRecentAsync(currentRecent);
                };

                // Add both to a container grid in the first column
                var nameContainer = new Grid();
                nameContainer.Children.Add(nameTextBlock);
                nameContainer.Children.Add(nameButton);

                Grid.SetColumn(nameContainer, 0);
                itemGrid.Children.Add(nameContainer);

                // Create delete button with X icon - right aligned
                var deleteButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 14 }, // X icon
                    Width = 32,
                    Height = 32,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ToolTipService.SetToolTip(deleteButton, "삭제");
                deleteButton.Click += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Delete button clicked for recent: {currentRecent.Name}");
                    await RemoveRecentAsync(currentRecent);
                };
                Grid.SetColumn(deleteButton, 1);
                itemGrid.Children.Add(deleteButton);

                RecentPanel.Children.Add(itemGrid);
            }
        }

        private async Task AddToRecentAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== AddToRecentAsync called ===");

                string name = "";
                string path = "";
                string type = "";
                string? archiveEntryKey = null;

                System.Diagnostics.Debug.WriteLine($"_currentArchive: {_currentArchive != null}");
                System.Diagnostics.Debug.WriteLine($"_currentArchivePath: {_currentArchivePath}");
                System.Diagnostics.Debug.WriteLine($"_currentExplorerPath: {_currentExplorerPath}");
                System.Diagnostics.Debug.WriteLine($"_currentIndex: {_currentIndex}");
                System.Diagnostics.Debug.WriteLine($"_imageEntries.Count: {_imageEntries.Count}");

                if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    // Archive mode - add current image position
                    if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        var currentEntry = _imageEntries[_currentIndex];
                        name = $"{Path.GetFileName(_currentArchivePath)} - {currentEntry.DisplayName}";
                        path = _currentArchivePath;
                        type = "Archive";
                        archiveEntryKey = currentEntry.ArchiveEntryKey;
                        System.Diagnostics.Debug.WriteLine($"Archive mode: {name}");
                    }
                }
                else if (!string.IsNullOrEmpty(_currentExplorerPath))
                {
                    // Folder mode - add current folder
                    name = Path.GetFileName(_currentExplorerPath);
                    if (string.IsNullOrEmpty(name))
                        name = _currentExplorerPath;
                    path = _currentExplorerPath;
                    type = "Folder";
                    System.Diagnostics.Debug.WriteLine($"Folder mode: {name}");
                }
                else if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    // File mode - add current file
                    var currentEntry = _imageEntries[_currentIndex];
                    if (!string.IsNullOrEmpty(currentEntry.FilePath))
                    {
                        name = currentEntry.DisplayName;
                        path = currentEntry.FilePath;
                        type = "File";
                        System.Diagnostics.Debug.WriteLine($"File mode: {name}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Final values - Name: '{name}', Path: '{path}', Type: '{type}'");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                {
                    // Remove existing item with same path and type (for archives, also check ArchiveEntryKey)
                    RecentItem? existing = null;
                    if (type == "Archive")
                    {
                        existing = _recentItems.FirstOrDefault(r => r.Path == path && r.Type == type && r.ArchiveEntryKey == archiveEntryKey);
                    }
                    else
                    {
                        existing = _recentItems.FirstOrDefault(r => r.Path == path && r.Type == type);
                    }

                    if (existing != null)
                    {
                        _recentItems.Remove(existing);
                        System.Diagnostics.Debug.WriteLine($"Removed existing recent item: {existing.Name}");
                    }

                    // Add new recent item
                    var recentItem = new RecentItem
                    {
                        Name = name,
                        Path = path,
                        Type = type,
                        ArchiveEntryKey = archiveEntryKey,
                        AccessedAt = DateTime.Now
                    };

                    _recentItems.Add(recentItem);
                    System.Diagnostics.Debug.WriteLine($"Added recent item: {recentItem.Name}");

                    // Keep only the most recent MaxRecentItems
                    while (_recentItems.Count > MaxRecentItems)
                    {
                        var oldest = _recentItems.OrderBy<RecentItem, DateTime>(r => r.AccessedAt).First();
                        _recentItems.Remove(oldest);
                        System.Diagnostics.Debug.WriteLine($"Removed oldest recent item: {oldest.Name}");
                    }

                    await SaveRecentItems();
                    UpdateRecentMenu();
                    System.Diagnostics.Debug.WriteLine("Recent item added and saved successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Cannot add recent item - missing name or path");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding to recent: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task RemoveRecentAsync(RecentItem recent)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Removing recent item: {recent.Name}");
                _recentItems.Remove(recent);
                await SaveRecentItems();
                UpdateRecentMenu();
                System.Diagnostics.Debug.WriteLine($"Recent item removed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing recent item: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task NavigateToRecentAsync(RecentItem recent)
        {
            try
            {
                switch (recent.Type)
                {
                    case "Folder":
                        if (Directory.Exists(recent.Path))
                        {
                            LoadExplorerFolder(recent.Path);
                        }
                        break;
                    case "File":
                        if (File.Exists(recent.Path))
                        {
                            var file = await StorageFile.GetFileFromPathAsync(recent.Path);
                            await LoadImageFromFileAsync(file);
                        }
                        break;
                    case "Archive":
                        if (File.Exists(recent.Path))
                        {
                            // First navigate to the archive file's folder
                            var archiveFolder = Path.GetDirectoryName(recent.Path);
                            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                            {
                                LoadExplorerFolder(archiveFolder);

                                // Then open the archive and navigate to specific entry
                                if (!string.IsNullOrEmpty(recent.ArchiveEntryKey))
                                {
                                    await LoadImagesFromArchiveAsync(recent.Path);
                                    var entryIndex = _imageEntries.FindIndex(e => e.ArchiveEntryKey == recent.ArchiveEntryKey);
                                    if (entryIndex >= 0)
                                    {
                                        _currentIndex = entryIndex;
                                        await DisplayCurrentImageAsync();
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to recent: {ex.Message}");
            }
        }

        #endregion
    }
}