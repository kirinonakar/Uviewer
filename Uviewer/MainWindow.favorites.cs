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
        private const int MaxRecentItems = 10;

        public class FavoriteItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Type { get; set; } = ""; // "Folder", "File", "Archive"
            public string? ArchiveEntryKey { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public double? ScrollOffset { get; set; }
            public int SavedPage { get; set; } = 0;
            public int ChapterIndex { get; set; } = 0;
            public int SavedLine { get; set; } = 1;
        }

        public class RecentItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Type { get; set; } = ""; // "Folder", "File", "Archive"
            public string? ArchiveEntryKey { get; set; }
            public DateTime AccessedAt { get; set; } = DateTime.Now;
            public double? ScrollOffset { get; set; }
            public int SavedPage { get; set; } = 0;
            public int ChapterIndex { get; set; } = 0;
            public int SavedLine { get; set; } = 1;
        }

        public class TextSettings
        {
             public double FontSize { get; set; } = 18;
             public string FontFamily { get; set; } = "Yu Gothic Medium";
             public int ThemeIndex { get; set; } = 0;
        }

        private const string TextSettingsFilePath = "text_settings.json";
        private string GetTextSettingsFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", TextSettingsFilePath);

        [System.Text.Json.Serialization.JsonSerializable(typeof(TextSettings))]
        public partial class TextSettingsContext : System.Text.Json.Serialization.JsonSerializerContext;


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
            UpdateFavoritesPanel(FavoritesPanel);
            UpdateFavoritesPanel(SidebarFavoritesPanel);
        }

        private void UpdateFavoritesPanel(StackPanel panel)
        {
            if (panel == null) return;

            // Remove all dynamic items (keep only the first 2: Add button and separator)
            while (panel.Children.Count > 2)
            {
                panel.Children.RemoveAt(panel.Children.Count - 1);
            }

            if (_favorites.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = Strings.NoFavorites,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                panel.Children.Add(emptyText);
                return;
            }

            var fileFavorites = _favorites.Where(f => f.Type != "Folder").OrderByDescending(f => f.CreatedAt).ToList();
            var folderFavorites = _favorites.Where(f => f.Type == "Folder").OrderByDescending(f => f.CreatedAt).ToList();

            foreach (var fav in fileFavorites)
            {
                panel.Children.Add(CreateFavoriteItemControl(fav));
            }

            if (fileFavorites.Count > 0 && folderFavorites.Count > 0)
            {
                // Add Separator
                var separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.3 },
                    Margin = new Thickness(12, 4, 12, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                panel.Children.Add(separator);
            }

            foreach (var fav in folderFavorites)
            {
                panel.Children.Add(CreateFavoriteItemControl(fav));
            }
        }

        private Grid CreateFavoriteItemControl(FavoriteItem favorite)
        {
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

            string posString = "";
            if (favorite.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            {
                posString = $" (Ch.{favorite.ChapterIndex + 1} P.{favorite.SavedPage + 1} L.{favorite.SavedLine})";
            }
            else if (favorite.SavedLine > 1) posString = $" (Line {favorite.SavedLine})";
            else if (favorite.SavedPage > 0 || favorite.ChapterIndex > 0) 
                posString = $" ({(favorite.ChapterIndex > 0 ? $"Ch.{favorite.ChapterIndex + 1} " : "")}Line {favorite.SavedPage + 1})";

            // Create TextBlock for the favorite name with left alignment and tooltip
            var nameTextBlock = new TextBlock
            {
                Text = favorite.Name + posString,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxWidth = 340, // Reserve space for delete button (400 - 12 left margin - 8 right margin - 40 for button)
                Margin = new Thickness(12, 0, 8, 0),
                FontSize = 13
            };
            
            string tooltipText = favorite.Path + (string.IsNullOrEmpty(posString) ? "" : $"\n{posString.Trim(' ', '(', ')')}");
            ToolTipService.SetToolTip(nameTextBlock, tooltipText);

            // Create a transparent button overlay for clicking
            var nameButton = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            ToolTipService.SetToolTip(nameButton, tooltipText); // Set tooltip on button too
            
            nameButton.Click += async (s, e) =>
            {
                FavoritesFlyout?.Hide();
                SidebarFavoritesFlyout?.Hide();
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

            return itemGrid;
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

                if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath))
                {
                    // Text File mode
                    name = Path.GetFileName(_currentTextFilePath);
                    path = _currentTextFilePath;
                    type = "File";
                    CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                }
                else if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath))
                {
                    // Epub Mode
                    name = Path.GetFileName(_currentEpubFilePath);
                    path = _currentEpubFilePath;
                    type = "File";
                    CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                }
                else if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    // Archive mode - add current image position
                    if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        var currentEntry = _imageEntries[_currentIndex];
                        name = $"{Path.GetFileName(_currentArchivePath)} - {currentEntry.DisplayName}";
                        path = _currentArchivePath;
                        type = "Archive";
                        archiveEntryKey = currentEntry.ArchiveEntryKey;

                        // Also add the archive folder as a separate bookmark
                        CheckAndAddFolderToFavorites(Path.GetDirectoryName(_currentArchivePath));
                    }
                }
                else if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    // File mode - add current file (Prioritize File over Folder)
                    // Note: This often covers Image files in the list
                    var currentEntry = _imageEntries[_currentIndex];
                    if (!string.IsNullOrEmpty(currentEntry.FilePath))
                    {
                        name = currentEntry.DisplayName;
                        path = currentEntry.FilePath;
                        type = "File";
                        CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                    }
                }
                else if (!string.IsNullOrEmpty(_currentExplorerPath))
                {
                    // Folder mode - add current folder (Only if no file is open)
                    name = Path.GetFileName(_currentExplorerPath);
                    if (string.IsNullOrEmpty(name))
                        name = _currentExplorerPath;
                    path = _currentExplorerPath;
                    type = "Folder";
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
                        System.Diagnostics.Debug.WriteLine($"Favorite already exists: {existing.Name}. Updating position.");
                        _favorites.Remove(existing);
                    }

                    int savedPage = 0;
                    int savedLine = 1;
                    if (_isEpubMode)
                    {
                        savedPage = CurrentEpubPageIndex;
                        if (EpubFlipView?.SelectedItem is Grid g && g.Tag is EpubPageInfoTag tag)
                        {
                            savedLine = tag.StartLine;
                        }
                    }
                    else if (_isAozoraMode)
                    {
                        savedPage = 0; 
                        if (_aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                            savedLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                    }
                    else if (_isTextMode && TextScrollViewer != null)
                    {
                        savedLine = GetTopVisibleLineIndex();
                    }

                    var favorite = new FavoriteItem
                    {
                        Name = name,
                        Path = path,
                        Type = type,
                        ArchiveEntryKey = archiveEntryKey,
                        ScrollOffset = (_isTextMode && TextScrollViewer != null) ? TextScrollViewer.VerticalOffset : null,
                        SavedPage = savedPage,
                        SavedLine = savedLine,
                        ChapterIndex = _isEpubMode ? CurrentEpubChapterIndex : 0
                    };

                    _favorites.Add(favorite);
                    System.Diagnostics.Debug.WriteLine($"Added favorite: {favorite.Name}");
                    await SaveFavorites();
                    UpdateFavoritesMenu();
                    System.Diagnostics.Debug.WriteLine("Favorite added and saved successfully");
                    ShowNotification(Strings.AddedToFavoritesNotification);
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
                            // Set pending target line BEFORE loading triggers
                            // Unified navigation: Set pending target line for both modes
                            // DisplayLoadedText will handle the actual scrolling/rendering
                            _aozoraPendingTargetLine = favorite.SavedLine > 1 ? favorite.SavedLine : (favorite.SavedPage > 0 ? -favorite.SavedPage : 1);

                            // Load explorer parent folder
                            var parentDir = Path.GetDirectoryName(favorite.Path);
                            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                            {
                                 LoadExplorerFolder(parentDir);
                            }
                            
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
                                        
                                        // Restore scroll if text
                                        if (favorite.ScrollOffset.HasValue && TextScrollViewer != null) 
                                        {
                                             // Wait a bit for layout
                                             await Task.Delay(100);
                                             TextScrollViewer.ChangeView(null, favorite.ScrollOffset.Value, null);
                                             UpdateTextStatusBar();
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }
                
                // For File Types
                if (favorite.Type == "File")
                {
                    if (favorite.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                    {
                         // Restore Epub
                         await RestoreEpubStateAsync(favorite.ChapterIndex, favorite.SavedPage);
                    }
                    else 
                    {
                        if (_isAozoraMode)
                        {
                            // Already handled via pending target line
                        }
                        else if (favorite.ScrollOffset.HasValue && TextScrollViewer != null && favorite.SavedLine <= 1)
                        {
                             // Fallback to offset if no line saved
                             TextScrollViewer.ChangeView(null, favorite.ScrollOffset.Value, null);
                             UpdateTextStatusBar();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to favorite: {ex.Message}");
            }
        }

        private void CheckAndAddFolderToFavorites(string? folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            var folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

            // Check if folder bookmark already exists
            var existingFolder = _favorites.FirstOrDefault(f => f.Path == folderPath && f.Type == "Folder");
            if (existingFolder == null)
            {
                var folderFavorite = new FavoriteItem
                {
                    Name = folderName,
                    Path = folderPath,
                    Type = "Folder"
                };
                _favorites.Add(folderFavorite);
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
            UpdateRecentPanel(RecentPanel);
            UpdateRecentPanel(SidebarRecentPanel);
        }

        private void UpdateRecentPanel(StackPanel panel)
        {
            if (panel == null) return;

            // Remove all existing items
            panel.Children.Clear();

            if (_recentItems.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = Strings.NoRecentFiles,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                panel.Children.Add(emptyText);
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

                string posString = "";
                if (recent.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                {
                    posString = $" (Ch.{recent.ChapterIndex + 1} P.{recent.SavedPage + 1} L.{recent.SavedLine})";
                }
                else if (recent.SavedLine > 1) posString = $" (Line {recent.SavedLine})";
                else if (recent.SavedPage > 0 || recent.ChapterIndex > 0) 
                    posString = $" ({(recent.ChapterIndex > 0 ? $"Ch.{recent.ChapterIndex + 1} " : "")}Line {recent.SavedPage + 1})";
                
                // Create TextBlock for the recent item name with left alignment and tooltip
                var nameTextBlock = new TextBlock
                {
                    Text = recent.Name + posString,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = 340, 
                    Margin = new Thickness(12, 0, 8, 0),
                    FontSize = 13
                };
                
                string tooltipText = recent.Path + (string.IsNullOrEmpty(posString) ? "" : $"\n{posString.Trim(' ', '(', ')')}");
                ToolTipService.SetToolTip(nameTextBlock, tooltipText);

                // Create a transparent button overlay for clicking
                var nameButton = new Button
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                ToolTipService.SetToolTip(nameButton, tooltipText); // Tooltip on button
                
                nameButton.Click += async (s, e) =>
                {
                    RecentFlyout?.Hide();
                    SidebarRecentFlyout?.Hide();
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

                panel.Children.Add(itemGrid);
            }
        }

        /// <summary>
        /// 최근 항목에 추가하거나 갱신합니다.
        /// </summary>
        /// <param name="saveCurrentPosition">
        /// true: 현재 뷰어의 스크롤/페이지 위치를 저장합니다. (파일 닫기, 앱 종료 시)
        /// false: 기존 저장된 위치를 유지하고 날짜만 갱신합니다. (파일 열 때)
        /// </param>
        private async Task AddToRecentAsync(bool saveCurrentPosition = false)
        {
            try
            {
                string name = "";
                string path = "";
                string type = "";

                // 1. 현재 열려있는 파일 정보 파악
                if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath))
                {
                    name = Path.GetFileName(_currentTextFilePath);
                    path = _currentTextFilePath;
                    type = "File";
                }
                else if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath))
                {
                    name = Path.GetFileName(_currentEpubFilePath);
                    path = _currentEpubFilePath;
                    type = "File";
                }
                else if (_currentArchive != null && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    path = _currentArchivePath;
                    type = "Archive";
                    if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                        name = $"{Path.GetFileName(_currentArchivePath)} - {_imageEntries[_currentIndex].DisplayName}";
                    else
                        name = Path.GetFileName(_currentArchivePath);
                }
                else if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                {
                    var currentEntry = _imageEntries[_currentIndex];
                    if (!string.IsNullOrEmpty(currentEntry.FilePath))
                    {
                        name = currentEntry.DisplayName;
                        path = currentEntry.FilePath;
                        type = "File";
                    }
                }
                else if (!string.IsNullOrEmpty(_currentExplorerPath))
                {
                    name = Path.GetFileName(_currentExplorerPath);
                    if (string.IsNullOrEmpty(name)) name = _currentExplorerPath;
                    path = _currentExplorerPath;
                    type = "Folder";
                }

                if (string.IsNullOrEmpty(path)) return;

                // 2. 기존 기록이 있는지 확인
                RecentItem? existing = _recentItems.FirstOrDefault(r =>
                    r.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

                // 3. 저장할 위치 값 결정 (기본값: 기존 기록 유지)
                double? targetOffset = existing?.ScrollOffset;
                int targetPage = existing?.SavedPage ?? 0;
                int targetChapter = existing?.ChapterIndex ?? 0;
                int targetLine = existing?.SavedLine ?? 1;
                string? targetArchiveKey = existing?.ArchiveEntryKey;

                // [핵심 수정] saveCurrentPosition이 true일 때 '현재 뷰어 상태'를 읽어오되,
                // 로딩 중 초기화(0)된 값으로 유의미한 기존 기록을 덮어쓰는 것을 방지
                if (saveCurrentPosition)
                {
                    if (_isEpubMode)
                    {
                        // 페이지가 0이거나 챕터가 0인 초기화 상태에서, 
                        // 기존에 읽던 기록(페이지 > 0 OR 챕터 > 0)이 있다면 기존 값 유지
                        bool isResetState = CurrentEpubPageIndex == 0 && CurrentEpubChapterIndex == 0;
                        bool hasExistingProgress = existing != null && (existing.SavedPage > 0 || existing.ChapterIndex > 0);

                        if (isResetState && hasExistingProgress)
                        {
                            targetPage = existing.SavedPage;
                            targetChapter = existing.ChapterIndex;
                            targetLine = existing.SavedLine;
                            System.Diagnostics.Debug.WriteLine($"[SafeGuard] Epub reset state detected. Keeping previous position: Ch.{targetChapter} P.{targetPage}");
                        }
                        else
                        {
                            targetPage = CurrentEpubPageIndex;
                            targetChapter = CurrentEpubChapterIndex;
                            if (EpubFlipView?.SelectedItem is Grid g && g.Tag is EpubPageInfoTag tag)
                                targetLine = tag.StartLine;
                        }
                    }
                    else if (_isTextMode && TextScrollViewer != null)
                    {
                        double currentOffset = TextScrollViewer.VerticalOffset;
                        
                        // 오프셋이 0인데 기존에 읽던 기록(>0)이 있다면, 기존 값 유지
                        if (currentOffset == 0 && existing != null && existing.ScrollOffset > 0)
                        {
                            targetOffset = existing.ScrollOffset;
                            targetLine = existing.SavedLine;
                            System.Diagnostics.Debug.WriteLine($"[SafeGuard] Text offset 0 detected. Keeping previous offset: {targetOffset}");
                        }
                        else
                        {
                            targetOffset = currentOffset;
                            double lineH = _textFontSize * 1.8; // 폰트 사이즈가 0이면 Infinity가 될 수 있음 주의
                            if (lineH > 0)
                            {
                                targetLine = (int)(currentOffset / lineH) + 1;
                            }
                            
                            // 아오조라 모드 보정
                            if (_isAozoraMode && _aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0)
                                targetLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                        }
                    }
                    else if (type == "Archive" && _currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        // 아카이브의 경우 인덱스가 명확하므로 업데이트 (단, 0번 인덱스일 때 기존 키가 있다면 고민해볼 수 있으나 보통 의도된 이동임)
                        targetArchiveKey = _imageEntries[_currentIndex].ArchiveEntryKey;
                        name = $"{Path.GetFileName(_currentArchivePath)} - {_imageEntries[_currentIndex].DisplayName}";
                    }
                }

                // 4. 목록 갱신 (기존 항목 제거 후 맨 앞에 추가)
                if (existing != null) _recentItems.Remove(existing);

                var newItem = new RecentItem
                {
                    Name = name,
                    Path = path,
                    Type = type,
                    ArchiveEntryKey = targetArchiveKey,
                    AccessedAt = DateTime.Now,
                    ScrollOffset = targetOffset,
                    SavedPage = targetPage,
                    ChapterIndex = targetChapter,
                    SavedLine = targetLine
                };

                _recentItems.Insert(0, newItem);

                // 최대 개수 제한
                while (_recentItems.Count > MaxRecentItems)
                {
                    _recentItems.RemoveAt(_recentItems.Count - 1);
                }

                await SaveRecentItems();
                UpdateRecentMenu();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding to recent: {ex.Message}");
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
                            // Set pending target line BEFORE loading triggers
                            _aozoraPendingTargetLine = recent.SavedLine > 1 ? recent.SavedLine : (recent.SavedPage > 0 ? -recent.SavedPage : 1);

                            // Load explorer parent folder
                            var parentDir = Path.GetDirectoryName(recent.Path);
                            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                            {
                                 LoadExplorerFolder(parentDir);
                                 
                                 // Select text file in explorer
                                 var filename = Path.GetFileName(recent.Path);
                                 var item = _fileItems.FirstOrDefault(f => f.Name.Equals(filename, StringComparison.OrdinalIgnoreCase));
                                 if (item != null)
                                 {
                                     if (_isExplorerGrid)
                                     {
                                         FileGridView.SelectedItem = item;
                                         FileGridView.ScrollIntoView(item);
                                     }
                                     else
                                     {
                                         FileListView.SelectedItem = item;
                                         FileListView.ScrollIntoView(item);
                                     }
                                 }
                            }

                            var file = await StorageFile.GetFileFromPathAsync(recent.Path);
                            await LoadImageFromFileAsync(file);
                            
                            // Restore text position
                             if (recent.ScrollOffset.HasValue && _isTextMode && TextScrollViewer != null)
                            {
                                await Task.Delay(300);
                                TextScrollViewer.ChangeView(null, recent.ScrollOffset.Value, null);
                                UpdateTextStatusBar();
                            }
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
                                        
                                        if (recent.ScrollOffset.HasValue && TextScrollViewer != null)
                                        {
                                            await Task.Delay(100);
                                            TextScrollViewer.ChangeView(null, recent.ScrollOffset.Value, null);
                                            UpdateTextStatusBar();
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }

                if (recent.Type == "File")
                {
                    if (recent.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                    {
                        await RestoreEpubStateAsync(recent.ChapterIndex, recent.SavedPage);
                    }
                    else if (!_isAozoraMode && recent.ScrollOffset.HasValue && TextScrollViewer != null)
                    {
                        await Task.Delay(100);
                        TextScrollViewer.ChangeView(null, recent.ScrollOffset.Value, null);
                        UpdateTextStatusBar();
                    }
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