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
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // [추가] 파일 이동 중 자동 저장을 막기 위한 플래그
        private bool _isNavigatingRecent = false;



        private const string TextSettingsFilePath = "text_settings.json";
        private string GetTextSettingsFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", TextSettingsFilePath);




        #region Favorites


        private void UpdateFavoritesMenu()
        {
            UpdateFavoritesPanel(FileFavoritesPanel, FolderFavoritesPanel);
            UpdateFavoritesPanel(SidebarFileFavoritesPanel, SidebarFolderFavoritesPanel);
        }

        private void UpdateFavoritesPanel(StackPanel filePanel, StackPanel folderPanel)
        {
            if (filePanel == null || folderPanel == null) return;

            filePanel.Children.Clear();
            folderPanel.Children.Clear();

            var fileFavorites = _favoritesService.Favorites.Where(f => f.Type != "Folder").OrderByDescending(f => f.IsPinned).ThenByDescending(f => f.CreatedAt).ToList();
            var folderFavorites = _favoritesService.Favorites.Where(f => f.Type == "Folder").OrderByDescending(f => f.IsPinned).ThenByDescending(f => f.CreatedAt).ToList();

            if (fileFavorites.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = Strings.NoFavorites,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
                {
                    try { emptyText.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                    catch { }
                }
                filePanel.Children.Add(emptyText);
            }
            else
            {
                foreach (var fav in fileFavorites)
                {
                    filePanel.Children.Add(CreateFavoriteItemControl(fav));
                }
            }

            if (folderFavorites.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = Strings.NoFavorites,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
                {
                    try { emptyText.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                    catch { }
                }
                folderPanel.Children.Add(emptyText);
            }
            else
            {
                foreach (var fav in folderFavorites)
                {
                    folderPanel.Children.Add(CreateFavoriteItemControl(fav));
                }
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
                MinHeight = favorite.Type != "Folder" ? 44 : 36
            };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool isImageFile = favorite.Type == "File" && !string.IsNullOrEmpty(favorite.Path) && 
                               FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(favorite.Path).ToLowerInvariant());

            string vMark = "";
            string posString = "";

            if (!isImageFile)
            {
                if (favorite.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                {
                    posString = $" (Ch.{favorite.ChapterIndex + 1} P.{favorite.SavedPage + 1} L.{favorite.SavedLine})";
                }
                else if (favorite.Type == "File" && !favorite.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    posString = $" (Line {favorite.SavedLine})";
                }
                else if (favorite.SavedPage > 0 || favorite.ChapterIndex > 0) 
                    posString = $" ({(favorite.ChapterIndex > 0 ? $"Ch.{favorite.ChapterIndex + 1} " : "")}P.{favorite.SavedPage + 1})";
            }

            // Create vertical container for text content + progress bar
            var contentPanel = new StackPanel 
            { 
                Orientation = Orientation.Vertical, 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 4, 8, 4)
            };

            // Create horizontal row for icon + name
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };

            if (favorite.IsWebDav)
            {
                var webIcon = new FontIcon
                {
                    Glyph = "\uE774", // Globe icon
                    FontSize = 12,
                    Margin = new Thickness(0, 1, 6, 0),
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue)
                };
                nameRow.Children.Add(webIcon);
            }

            // Create TextBlock for the favorite name
            var nameTextBlock = new TextBlock
            {
                Text = vMark + favorite.Name + posString,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxWidth = favorite.IsWebDav ? 270 : 300,
                FontSize = 13
            };

            // Set font family with validation to prevent 'Unknown' crash
            if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
            {
                try { nameTextBlock.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                catch { /* Fallback to default if invalid */ }
            }
            if (favorite.IsWebDav && !string.IsNullOrEmpty(_settingsManager.UIFontFamily))
            {
                // Ensure webIcon also uses UI font if possible, though it's likely FontIcon anyway
            }
            nameRow.Children.Add(nameTextBlock);
            contentPanel.Children.Add(nameRow);

            // Add progress bar for non-folder items (excluding single image files)
            if (favorite.Type != "Folder" && !isImageFile)
            {
                var progressRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Progress bar container (transparent background = clean look)
                var progressBarBg = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var progressBarFill = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MaxWidth = 280
                };

                // Use a Grid to overlay fill on background
                var progressBarGrid = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 280
                };
                progressBarGrid.Children.Add(progressBarBg);
                progressBarGrid.Children.Add(progressBarFill);

                // Set width based on progress
                double progress = Math.Min(Math.Max(favorite.Progress, 0), 100);
                progressBarFill.Loaded += (s, e) =>
                {
                    if (progressBarGrid.ActualWidth > 0)
                        progressBarFill.Width = progressBarGrid.ActualWidth * (progress / 100.0);
                    else
                        progressBarFill.Width = 280 * (progress / 100.0);
                };
                progressBarGrid.SizeChanged += (s, e) =>
                {
                    if (e.NewSize.Width > 0)
                        progressBarFill.Width = e.NewSize.Width * (progress / 100.0);
                };

                Grid.SetColumn(progressBarGrid, 0);
                progressRow.Children.Add(progressBarGrid);

                // Progress percentage text
                var progressText = new TextBlock
                {
                    Text = $"{progress:F0}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(6, -2, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(progressText, 1);
                progressRow.Children.Add(progressText);

                contentPanel.Children.Add(progressRow);
            }
            
            string tooltipText = favorite.Path + (string.IsNullOrEmpty(posString) ? "" : $"\n{posString.Trim(' ', '(', ')')}");
             if (favorite.IsWebDav && !string.IsNullOrEmpty(favorite.WebDavServerName))
            {
                tooltipText = $"[{favorite.WebDavServerName}] {tooltipText}";
            }
            if (favorite.Type != "Folder" && !isImageFile)
                tooltipText += $"\n{Strings.ProgressLabel}: {favorite.Progress:F1}%";
            ToolTipService.SetToolTip(contentPanel, tooltipText);

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
            nameContainer.Children.Add(contentPanel);
            nameContainer.Children.Add(nameButton);

            Grid.SetColumn(nameContainer, 0);
            itemGrid.Children.Add(nameContainer);

            // Create pin button
            var pinButton = new Button
            {
                Content = new FontIcon { 
                    Glyph = favorite.IsPinned ? "\uE840" : "\uE718", 
                    FontSize = 14,
                    Foreground = favorite.IsPinned 
                        ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] 
                        : new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.5 }
                },
                Width = 32,
                Height = 32,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 0)
            };
            ToolTipService.SetToolTip(pinButton, favorite.IsPinned ? Strings.UnpinFavorite : Strings.PinFavorite);
            pinButton.Click += async (s, e) =>
            {
                await _favoritesService.TogglePinAsync(favorite);
                UpdateFavoritesMenu();
            };
            Grid.SetColumn(pinButton, 1);
            itemGrid.Children.Add(pinButton);

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
            ToolTipService.SetToolTip(deleteButton, Strings.RemoveFavorite);
            deleteButton.Click += async (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Delete button clicked for: {currentFavorite.Name}");
                await RemoveFavoriteAsync(currentFavorite);
            };
            Grid.SetColumn(deleteButton, 2);
            itemGrid.Children.Add(deleteButton);

            return itemGrid;
        }

        // [수정] 수동 저장 여부를 구분하기 위해 isManualSave 파라미터 추가 (기본값 true)
        private async Task AddToFavoritesAsync(bool isManualSave = true)
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
                System.Diagnostics.Debug.WriteLine($"Current favorites count: {_favoritesService.Favorites.Count}");
                foreach (var fav in _favoritesService.Favorites)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {fav.Name} ({fav.Type}): {fav.Path}");
                }

                if (_isTextMode && !string.IsNullOrEmpty(_currentTextFilePath))
                {
                    name = Path.GetFileName(_currentTextFilePath);
                    path = _currentTextFilePath;
                    type = "File";
                    if (!_isWebDavMode) CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                }
                else if (_isEpubMode && !string.IsNullOrEmpty(_currentEpubFilePath))
                {
                    name = Path.GetFileName(_currentEpubFilePath);
                    path = _currentEpubFilePath;
                    type = "File";
                    if (!_isWebDavMode) CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                }
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentArchivePath))
                {
                    if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        var currentEntry = _imageEntries[_currentIndex];
                        name = $"{Path.GetFileName(_currentArchivePath)} - {currentEntry.DisplayName}";
                        path = _currentArchivePath;
                        type = "Archive";
                        archiveEntryKey = currentEntry.ArchiveEntryKey;
                        if (!_isWebDavMode) CheckAndAddFolderToFavorites(Path.GetDirectoryName(_currentArchivePath));
                    }
                }
                else if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count && 
                         (EmptyStatePanel == null || EmptyStatePanel.Visibility == Visibility.Collapsed))
                {
                    var currentEntry = _imageEntries[_currentIndex];
                    if (!string.IsNullOrEmpty(currentEntry.FilePath))
                    {
                        name = currentEntry.DisplayName;
                        path = currentEntry.FilePath;
                        type = "File";
                        if (!_isWebDavMode) CheckAndAddFolderToFavorites(Path.GetDirectoryName(path));
                    }
                }
                else if (!string.IsNullOrEmpty(_currentExplorerPath) || (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavPath)))
                {
                    if (!string.IsNullOrEmpty(_currentExplorerPath))
                    {
                        name = Path.GetFileName(_currentExplorerPath);
                        if (string.IsNullOrEmpty(name))
                            name = _currentExplorerPath;
                        path = _currentExplorerPath;
                        type = "Folder";
                    }
                    else
                    {
                        // WebDAV Folder - will be refined in the WebDAV block below
                        type = "Folder";
                        path = _currentWebDavPath!;
                        name = Path.GetFileName(path.TrimEnd('/')) ?? "";
                    }
                }

                string? webDavServerName = null;
                bool isWebDav = false;

                if (_isWebDavMode && _webDavService.CurrentServer != null)
                {
                    isWebDav = true;
                    webDavServerName = _webDavService.CurrentServer.ServerName;
                    
                    if (type == "Archive")
                    {
                        if (path.StartsWith("WebDAV:"))
                            path = path.Substring(7);
                        
                        // Use original filename for WebDAV archive
                        if (!string.IsNullOrEmpty(_currentWebDavItemPath))
                        {
                            // [수정] WebDAV 아카이브의 경우 path를 로컬 temp 경로가 아닌 원본 WebDAV 경로로 설정
                            path = _currentWebDavItemPath;

                            string origArchiveName = Path.GetFileName(_currentWebDavItemPath);
                            if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                            {
                                name = $"{origArchiveName} - {_imageEntries[_currentIndex].DisplayName}";
                            }
                        }
                        CheckAndAddFolderToFavorites(_currentWebDavPath);
                    }
                    else if (type == "File")
                    {
                        if (!string.IsNullOrEmpty(_currentWebDavItemPath))
                        {
                            path = _currentWebDavItemPath;
                            name = Path.GetFileName(_currentWebDavItemPath);
                        }
                        CheckAndAddFolderToFavorites(_currentWebDavPath);
                    }
                    else if (type == "Folder")
                    {
                        if (!string.IsNullOrEmpty(_currentWebDavPath))
                        {
                            path = _currentWebDavPath;
                            name = Path.GetFileName(path.TrimEnd('/'));
                            if (string.IsNullOrEmpty(name)) name = _webDavService.CurrentServer.ServerName;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Final values - Name: '{name}', Path: '{path}', Type: '{type}'");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                {


                    int savedPage = 0;
                    int savedLine = 1;
                    if (_isEpubMode)
                    {
                        savedPage = CurrentEpubPageIndex;
                        if (_isVerticalMode)
                        {
                            savedLine = _currentVerticalPageInfo.StartLine;
                            savedPage = 0; 
                        }
                        else if (_epubWin2DPages != null && CurrentEpubPageIndex >= 0 && CurrentEpubPageIndex < _epubWin2DPages.Count)
                        {
                            var page = _epubWin2DPages[CurrentEpubPageIndex];
                            if (!page.IsImagePage)
                            {
                                savedLine = page.StartLine;
                            }
                            else if (_aozoraBlocks != null)
                            {
                                var targetBlock = _aozoraBlocks.FirstOrDefault(b => b.Inlines.OfType<AozoraImage>().Any(img => img.Source == page.ImagePath));
                                if (targetBlock != null) savedLine = targetBlock.SourceLineNumber;
                                else savedLine = 1;
                            }
                        }
                    }
                    else if (_isTextMode)
                    {
                        if (_isVerticalMode)
                        {
                            savedLine = _currentVerticalPageInfo.StartLine;
                        }
                        else if (_isAozoraMode && _aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                        {
                            savedLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                        }
                        else if (TextScrollViewer != null)
                        {
                            savedLine = GetTopVisibleLineIndex();
                        }
                    }
                    else if (_currentPdfDocument != null || _currentArchive != null || _current7zArchive != null)
                    {
                        savedPage = _currentIndex;
                    }

                    double calcProgress = 0;
                    int chapterIndex = _isEpubMode ? CurrentEpubChapterIndex : 0;
                    if (_isEpubMode && _epubSpine.Count > 0)
                    {
                        int totalPages = _epubPages.Count > 0 ? _epubPages.Count : 1;
                        double chapterProg = (double)chapterIndex / _epubSpine.Count;
                        double pageProg = (double)savedPage / totalPages / _epubSpine.Count;
                        calcProgress = Math.Min((chapterProg + pageProg) * 100.0, 100);
                    }
                    else if (_isTextMode)
                    {
                        int totalLines = _textTotalLineCountInSource > 0 ? _textTotalLineCountInSource : _aozoraTotalLineCountInSource;
                        if (totalLines > 0)
                            calcProgress = Math.Min((double)savedLine / totalLines * 100.0, 100);
                    }
                    else if ((_currentArchive != null || _current7zArchive != null) && _imageEntries.Count > 0)
                    {
                        calcProgress = Math.Min((double)(_currentIndex + 1) / _imageEntries.Count * 100.0, 100);
                    }
                    else if (_imageEntries.Count > 0 && _currentIndex >= 0)
                    {
                        calcProgress = Math.Min((double)(_currentIndex + 1) / _imageEntries.Count * 100.0, 100);
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
                        ChapterIndex = chapterIndex,
                        IsWebDav = isWebDav,
                        WebDavServerName = webDavServerName,
                        IsVertical = _isVerticalMode,
                        Progress = Math.Max(calcProgress, 0),
                        IsPinned = false
                    };

                    await _favoritesService.AddOrUpdateFavoriteAsync(favorite, isManualSave);
                    System.Diagnostics.Debug.WriteLine($"Added favorite: {favorite.Name}");
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
                await _favoritesService.RemoveFavoriteAsync(favorite);
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


                if (favorite.IsWebDav && !string.IsNullOrEmpty(favorite.WebDavServerName))
                {
                     // WebDAV Bookmark handling
                     try
                     {
                         // 1. Connect to server if needed
                         if (!_isWebDavMode || _webDavService.CurrentServer?.ServerName != favorite.WebDavServerName)
                         {
                             await ConnectToWebDavServerAsync(favorite.WebDavServerName);
                             // Connection failed?
                             if (!_isWebDavMode) return;
                         }

                         // 2. Open file/folder
                         var fileItem = new FileItem
                         {
                             Name = favorite.Name, // This might differ from actual filename but ext check uses it
                             WebDavPath = favorite.Path,
                             IsWebDav = true,
                             IsDirectory = favorite.Type == "Folder",
                             IsArchive = favorite.Type == "Archive",
                             // We need to set flags for OpenWebDavFileAsync
                         };
                         // Re-derive flags based on path extension, strictly for opening logic
                         string ext = Path.GetExtension(favorite.Path).ToLowerInvariant();
                         fileItem.IsImage = FileExplorerService.SupportedImageExtensions.Contains(ext);
                         fileItem.IsText = FileExplorerService.SupportedTextExtensions.Contains(ext);
                         fileItem.IsEpub = FileExplorerService.SupportedEpubExtensions.Contains(ext);

                         // Load parent folder in explorer for files and archives
                         if (favorite.Type != "Folder")
                         {
                              var parentPath = Path.GetDirectoryName(favorite.Path)?.Replace("\\", "/");
                              if (!string.IsNullOrEmpty(parentPath))
                              {
                                   // Ensure it starts with / if not empty, though GetDirectoryName might strip it or handle it weirdly for unix paths on windows
                                   // WebDAV paths usually start with /
                                   if (!parentPath.StartsWith("/")) parentPath = "/" + parentPath;
                                   await LoadWebDavFolderAsync(parentPath);
                              }
                              else
                              {
                                   // Root
                                   await LoadWebDavFolderAsync("/");
                              }
                         }

                         if (favorite.Type == "Folder")
                         {
                             await LoadWebDavFolderAsync(favorite.Path);
                         }
                         else if (favorite.Type == "Archive")
                         {
                             await OpenWebDavArchiveAsync(fileItem);
                             
                             // Restore Archive Position
                             if (!string.IsNullOrEmpty(favorite.ArchiveEntryKey))
                             {
                                 var entryIndex = _imageEntries.FindIndex(e => e.ArchiveEntryKey == favorite.ArchiveEntryKey);
                                 if (entryIndex >= 0)
                                 {
                                     _currentIndex = entryIndex;
                                     await DisplayCurrentImageAsync();
                                     
                                     if (favorite.ScrollOffset.HasValue && TextScrollViewer != null)
                                     {
                                         await Task.Delay(100);
                                         TextScrollViewer.ChangeView(null, favorite.ScrollOffset.Value, null);
                                         UpdateTextStatusBar();
                                      }
                                 }
                             }
                         }
                         else // File
                         {
                             // Set pending values for restoration
                             if (fileItem.IsEpub)
                             {
                                  PendingEpubChapterIndex = favorite.ChapterIndex;
                                  PendingEpubPageIndex = favorite.SavedPage;
                                  // [추가] EPUB 모드에서도 저장된 정확한 줄 번호를 복구하도록 변수에 할당합니다.
                                  _aozoraPendingTargetLine = favorite.SavedLine > 1 ? favorite.SavedLine : 1;
                             }
                             else if (fileItem.IsText)
                             {
                                  _aozoraPendingTargetLine = favorite.SavedLine > 1 ? favorite.SavedLine : (favorite.SavedPage > 0 ? -favorite.SavedPage : 1);
                             }

                             await OpenWebDavFileAsync(fileItem);
                             
                             if (!fileItem.IsEpub && !fileItem.IsText && favorite.Type == "File" && favorite.ScrollOffset.HasValue && TextScrollViewer != null) 
                             {
                                  // For simple text viewing if IsText was somehow false but type is file? Unlikely.
                                  // Handled by LoadImageFromFileAsync logic generally.
                             }
                         }
                         return; // Exit after WebDAV handling
                     }
                     catch (Exception ex)
                     {
                         System.Diagnostics.Debug.WriteLine($"Error opening WebDAV favorite: {ex.Message}");
                         ShowNotification($"WebDAV 즐겨찾기 열기 실패: {ex.Message}");
                         return;
                     }
                 }

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
                            // Set pending values for EPUB
                            if (favorite.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                            {
                                PendingEpubChapterIndex = favorite.ChapterIndex;
                                PendingEpubPageIndex = favorite.SavedPage;
                            }
                            else if (favorite.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                _pendingPdfPageIndex = favorite.SavedPage;
                            }

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
                            
                            if (favorite.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                await LoadImagesFromPdfAsync(favorite.Path);
                            }
                            else
                            {
                                await LoadImageFromFileAsync(file);
                            }
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
                         // Handled via PendingEpubChapterIndex/PageIndex during load
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
            if (string.IsNullOrEmpty(folderPath)) return;

            string folderName = "";
            bool isWebDav = _isWebDavMode;
            string? webDavServerName = _webDavService.CurrentServer?.ServerName;

            if (isWebDav)
            {
                folderName = Path.GetFileName(folderPath.TrimEnd('/'));
                if (string.IsNullOrEmpty(folderName)) folderName = webDavServerName ?? "WebDAV";
            }
            else
            {
                if (!Directory.Exists(folderPath)) return;
                folderName = Path.GetFileName(folderPath);
                if (string.IsNullOrEmpty(folderName)) folderName = folderPath;
            }

            // Check if folder bookmark already exists
            if (!_favoritesService.AnyFolderFavoriteExists(folderPath, isWebDav, webDavServerName))
            {
                var folderFavorite = new FavoriteItem
                {
                    Name = folderName,
                    Path = folderPath,
                    Type = "Folder",
                    IsWebDav = isWebDav,
                    WebDavServerName = webDavServerName
                };
                _ = _favoritesService.AddOrUpdateFavoriteAsync(folderFavorite, true);
            }
        }

        private double GetCurrentProgress()
        {
            try
            {
                if (_isEpubMode)
                {
                    if (_epubSpine.Count > 0)
                    {
                        int currentPage = _currentEpubPageIndex + 1;
                        int totalPages = _epubPages.Count > 0 ? _epubPages.Count : 1;
                        double chapterProgress = (double)_currentEpubChapterIndex / _epubSpine.Count;
                        double pageProgressInChapter = (double)(currentPage - 1) / totalPages / _epubSpine.Count;
                        double progress = (chapterProgress + pageProgressInChapter) * 100.0;
                        return Math.Min(Math.Max(progress, 0), 100);
                    }
                }
                else if (_isTextMode)
                {
                    int totalLines = _textTotalLineCountInSource > 0 ? _textTotalLineCountInSource : _aozoraTotalLineCountInSource;
                    if (totalLines > 0)
                    {
                        int currentLine = 1;
                        if (_isVerticalMode)
                        {
                            currentLine = _currentVerticalPageInfo.StartLine;
                        }
                        else if (_isAozoraMode && _aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                        {
                            currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                        }
                        else if (TextScrollViewer != null)
                        {
                            currentLine = GetTopVisibleLineIndex();
                        }
                        double progress = (double)currentLine / totalLines * 100.0;
                        return Math.Min(Math.Max(progress, 0), 100);
                    }
                }
                else if ((_currentArchive != null || _current7zArchive != null) && _imageEntries.Count > 0)
                {
                    double progress = (double)(_currentIndex + 1) / _imageEntries.Count * 100.0;
                    return Math.Min(Math.Max(progress, 0), 100);
                }
                else if (_imageEntries.Count > 0 && _currentIndex >= 0)
                {
                    double progress = (double)(_currentIndex + 1) / _imageEntries.Count * 100.0;
                    return Math.Min(Math.Max(progress, 0), 100);
                }
            }
            catch { }
            return 0;
        }

        #endregion

        #region Recent Items


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

            if (_recentService.RecentItems.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = Strings.NoRecentFiles,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(12, 8, 12, 8),
                    FontSize = 13
                };
                if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
                {
                    try { emptyText.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                    catch { }
                }
                panel.Children.Add(emptyText);
                return;
            }

            foreach (var recent in _recentService.RecentItems.OrderByDescending<RecentItem, DateTime>(r => r.AccessedAt))
            {
                // Capture the recent item for lambda closures
                var currentRecent = recent;

                // Create a Grid to hold the name and delete button
                var itemGrid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    MinHeight = recent.Type != "Folder" ? 44 : 36
                };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                bool isImageFile = recent.Type == "File" && !string.IsNullOrEmpty(recent.Path) && 
                                   FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(recent.Path).ToLowerInvariant());

                string vMark = "";
                string posString = "";

                if (!isImageFile)
                {
                    if (recent.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                    {
                        posString = $" (Ch.{recent.ChapterIndex + 1} P.{recent.SavedPage + 1} L.{recent.SavedLine})";
                    }
                    else if (recent.Type == "File" && !recent.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        posString = $" (Line {recent.SavedLine})";
                    }
                    else if (recent.SavedPage > 0 || recent.ChapterIndex > 0) 
                        posString = $" ({(recent.ChapterIndex > 0 ? $"Ch.{recent.ChapterIndex + 1} " : "")}P.{recent.SavedPage + 1})";
                }
                
                // Create vertical container for name + progress bar
                var contentPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 4, 8, 4)
                };

                // Create horizontal row for icon + name
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal };

                if (recent.IsWebDav)
                {
                    var webIcon = new FontIcon
                    {
                        Glyph = "\uE774", // Globe icon
                        FontSize = 12,
                        Margin = new Thickness(0, 1, 6, 0),
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue)
                    };
                    nameRow.Children.Add(webIcon);
                }

                // Create TextBlock for the recent item name with left alignment and tooltip
                var nameTextBlock = new TextBlock
                {
                    Text = vMark + recent.Name + posString,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = recent.IsWebDav ? 310 : 340,
                    FontSize = 13
                };
                if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
                {
                    try { nameTextBlock.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                    catch { }
                }
                nameRow.Children.Add(nameTextBlock);
                contentPanel.Children.Add(nameRow);

                // Add progress bar for non-folder items (excluding single image files)
                if (recent.Type != "Folder" && !isImageFile)
                {
                    var progressRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                    progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Progress bar container (transparent background = clean look)
                    var progressBarBg = new Border
                    {
                        Height = 3,
                        CornerRadius = new CornerRadius(1.5),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var progressBarFill = new Border
                    {
                        Height = 3,
                        CornerRadius = new CornerRadius(1.5),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MaxWidth = 280
                    };

                    // Use a Grid to overlay fill on background
                    var progressBarGrid = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center,
                        MaxWidth = 280
                    };
                    progressBarGrid.Children.Add(progressBarBg);
                    progressBarGrid.Children.Add(progressBarFill);

                    // Set width based on progress
                    double progress = Math.Min(Math.Max(recent.Progress, 0), 100);
                    progressBarFill.Loaded += (s, e) =>
                    {
                        if (progressBarGrid.ActualWidth > 0)
                            progressBarFill.Width = progressBarGrid.ActualWidth * (progress / 100.0);
                        else
                            progressBarFill.Width = 280 * (progress / 100.0);
                    };
                    progressBarGrid.SizeChanged += (s, e) =>
                    {
                        if (e.NewSize.Width > 0)
                            progressBarFill.Width = e.NewSize.Width * (progress / 100.0);
                    };

                    Grid.SetColumn(progressBarGrid, 0);
                    progressRow.Children.Add(progressBarGrid);

                    // Progress percentage text
                    var progressText = new TextBlock
                    {
                        Text = $"{progress:F0}%",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                        Margin = new Thickness(6, -2, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(progressText, 1);
                    progressRow.Children.Add(progressText);

                    contentPanel.Children.Add(progressRow);
                }
                
                string tooltipText = recent.Path + (string.IsNullOrEmpty(posString) ? "" : $"\n{posString.Trim(' ', '(', ')')}");
                if (recent.IsWebDav && !string.IsNullOrEmpty(recent.WebDavServerName))
                {
                    tooltipText = $"[{recent.WebDavServerName}] {tooltipText}";
                }
                if (recent.Type != "Folder" && !isImageFile)
                    tooltipText += $"\n{Strings.ProgressLabel}: {recent.Progress:F1}%";
                ToolTipService.SetToolTip(contentPanel, tooltipText);

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
                nameContainer.Children.Add(contentPanel);
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
                // [추가] Recent 메뉴를 통해 이동 중일 때는 자동 저장을 차단하여 데이터 오염 방지
                if (_isNavigatingRecent)
                {
                    System.Diagnostics.Debug.WriteLine("Skipping AddToRecentAsync during navigation.");
                    return;
                }
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
                else if ((_currentArchive != null || _current7zArchive != null) && !string.IsNullOrEmpty(_currentArchivePath))
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
                else if (_isWebDavMode && !string.IsNullOrEmpty(_currentWebDavPath))
                {
                    path = _currentWebDavPath;
                    name = Path.GetFileName(path.TrimEnd('/'));
                    if (string.IsNullOrEmpty(name)) name = _webDavService.CurrentServer?.ServerName ?? "WebDAV";
                    type = "Folder";
                }

                if (string.IsNullOrEmpty(path)) return;

                string? webDavServerName = null;
                bool isWebDav = false;

                if (_isWebDavMode && _webDavService.CurrentServer != null)
                {
                    isWebDav = true;
                    webDavServerName = _webDavService.CurrentServer.ServerName;

                    if (type == "Archive")
                    {
                        if (path.StartsWith("WebDAV:"))
                            path = path.Substring(7);

                        if (!string.IsNullOrEmpty(_currentWebDavItemPath))
                        {
                            // [수정] WebDAV 아카이브의 경우 path를 로컬 temp 경로가 아닌 원본 WebDAV 경로로 설정
                            path = _currentWebDavItemPath;

                            string origArchiveName = Path.GetFileName(_currentWebDavItemPath);
                            if (_currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                            {
                                name = $"{origArchiveName} - {_imageEntries[_currentIndex].DisplayName}";
                            }
                        }
                    }
                    else if (type == "File")
                    {
                        if (!string.IsNullOrEmpty(_currentWebDavItemPath))
                        {
                            path = _currentWebDavItemPath;
                            name = Path.GetFileName(_currentWebDavItemPath);
                        }
                    }
                    else if (type == "Folder")
                    {
                        if (!string.IsNullOrEmpty(_currentWebDavPath))
                        {
                            path = _currentWebDavPath;
                            name = Path.GetFileName(path.TrimEnd('/'));
                            if (string.IsNullOrEmpty(name)) name = _webDavService.CurrentServer.ServerName;
                        }
                    }
                }

                // 2. 기존 기록이 있는지 확인
                RecentItem? existing = _recentService.RecentItems.FirstOrDefault(r =>
                    r.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

                // 3. 저장할 위치 값 결정 (기본값: 기존 기록 유지)
                double? targetOffset = existing?.ScrollOffset;
                int targetPage = existing?.SavedPage ?? 0;
                int targetChapter = existing?.ChapterIndex ?? 0;
                int targetLine = existing?.SavedLine ?? 1;
                double targetProgress = existing?.Progress ?? 0;
                string? targetArchiveKey = existing?.ArchiveEntryKey;

                // [핵심 수정] saveCurrentPosition이 true일 때 '현재 뷰어 상태'를 읽어오되,
                // 로딩 중 초기화(0)된 값으로 유의미한 기존 기록을 덮어쓰는 것을 방지
                if (saveCurrentPosition)
                {
                    targetProgress = GetCurrentProgress(); // 일단 현재 UI 상태로 진행률 계산

                    if (_isEpubMode)
                    {
                        // 페이지가 0이거나 챕터가 0인 초기화 상태에서, 
                        // 기존에 읽던 기록(페이지 > 0 OR 챕터 > 0)이 있다면 기존 값 유지
                        bool isResetState = CurrentEpubPageIndex == 0 && CurrentEpubChapterIndex == 0;
                        bool hasExistingProgress = existing != null && (existing.SavedPage > 0 || existing.ChapterIndex > 0);

                        if (isResetState && existing != null && (existing.SavedPage > 0 || existing.ChapterIndex > 0))
                        {
                            targetPage = existing.SavedPage;
                            targetChapter = existing.ChapterIndex;
                            targetLine = existing.SavedLine;
                            targetProgress = existing.Progress; // 진행률도 기존 값 복구
                            System.Diagnostics.Debug.WriteLine($"[SafeGuard] Epub reset state detected. Keeping previous position: Ch.{targetChapter} P.{targetPage}");
                        }
                        else
                        {
                            targetPage = CurrentEpubPageIndex;
                            targetChapter = CurrentEpubChapterIndex;
                            
                            if (_isVerticalMode)
                            {
                                targetLine = _currentVerticalPageInfo.StartLine;
                                targetPage = 0; // Use line for restoration in vertical mode
                            }
                            else if (_epubWin2DPages != null && targetPage >= 0 && targetPage < _epubWin2DPages.Count)
                            {
                                var page = _epubWin2DPages[targetPage];
                                if (!page.IsImagePage)
                                {
                                    targetLine = page.StartLine;
                                }
                                else if (_aozoraBlocks != null)
                                {
                                    var targetBlock = _aozoraBlocks.FirstOrDefault(b => b.Inlines.OfType<AozoraImage>().Any(img => img.Source == page.ImagePath));
                                    if (targetBlock != null) targetLine = targetBlock.SourceLineNumber;
                                    else targetLine = 1;
                                }
                            }
                        }
                    }
                    else if (_isTextMode)
                    {
                        // --- 텍스트 뷰어 상태 수집 (세로모드, 아오조라, 일반 텍스트 통합) ---
                        int currentLine = 1;
                        double? currentOffset = 0;
                        bool hasViewerContent = false;

                        if (_isVerticalMode)
                        {
                            currentLine = _currentVerticalPageInfo.StartLine;
                            currentOffset = 0;
                            hasViewerContent = _currentVerticalPageInfo.Blocks != null && _currentVerticalPageInfo.Blocks.Count > 0;
                        }
                        else if (_isAozoraMode)
                        {
                            if (_aozoraBlocks.Count > 0 && _currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                            {
                                currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                                hasViewerContent = true;
                            }
                            currentOffset = 0;
                        }
                        else if (TextScrollViewer != null)
                        {
                            currentOffset = TextScrollViewer.VerticalOffset;
                            currentLine = GetTopVisibleLineIndex();
                            hasViewerContent = _textLines.Count > 0;
                        }

                        // --- 세이프가드: 로딩 중 또는 초기화된 상태에서 기존 유효한 기록 덮어쓰기 방지 ---
                        // 1. 현재 뷰어가 줄번호 1 이하, 오프셋 0인 '초기 상태'인가?
                        // 2. 기존 DB 기록에는 의미 있는 진행 내역(줄 > 1 또는 오프셋 > 0)이 있는가?
                        bool isResetState = currentLine <= 1 && (currentOffset == 0 || currentOffset == null);
                        bool hasExistingProgress = existing != null && (existing.SavedLine > 1 || (existing.ScrollOffset ?? 0) > 0);

                        if (isResetState && hasExistingProgress)
                        {
                            // 로딩 중(내용 없음)이거나, 로딩은 되었지만 아직 스크롤 복원 전(_lastRecentSaveLine == -1)인 경우
                            // 기존 DB의 위치 값을 그대로 유지하여 데이터 오염을 방지합니다.
                            if (!hasViewerContent || _lastRecentSaveLine == -1)
                            {
                                targetLine = existing.SavedLine;
                                targetOffset = existing.ScrollOffset;
                                targetProgress = existing.Progress; // 진행률도 기존 값 복구
                                System.Diagnostics.Debug.WriteLine($"[SafeGuard] Text loading/restoring state. Preserving previous: Line {targetLine}");
                            }
                            else
                            {
                                // 사용자가 수동으로 맨 위로 스크롤한 경우라면 1로 저장 보류 없이 허용
                                targetLine = currentLine;
                                targetOffset = currentOffset;
                            }
                        }
                        else
                        {
                            // 그 외의 경우(정상 읽기 중 혹은 첫 감상) 정상 업데이트
                            targetLine = currentLine;
                            targetOffset = currentOffset;
                        }
                    }
                    else if (_currentPdfDocument != null)
                    {
                        targetPage = _currentIndex;
                    }
                    else if (type == "Archive" && _currentIndex >= 0 && _currentIndex < _imageEntries.Count)
                    {
                        // 아카이브의 경우 인덱스가 명확하므로 업데이트 (단, 0번 인덱스일 때 기존 키가 있다면 고민해볼 수 있으나 보통 의도된 이동임)
                        targetArchiveKey = _imageEntries[_currentIndex].ArchiveEntryKey;
                        name = $"{Path.GetFileName(_currentArchivePath)} - {_imageEntries[_currentIndex].DisplayName}";
                        targetPage = _currentIndex;
                    }
                }

                // 4. 목록 갱신 (기존 항목 제거 후 맨 앞에 추가)
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
                    SavedLine = targetLine,
                    IsWebDav = isWebDav,
                    WebDavServerName = webDavServerName,
                    IsVertical = _isVerticalMode,
                    Progress = saveCurrentPosition ? targetProgress : (existing?.Progress ?? 0)
                };

                await _recentService.AddToRecentAsync(newItem);
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
                await _recentService.RemoveRecentAsync(recent);
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
            // 1. 클릭한 시점의 위치/정보를 미리 캡처 (데이터 오염 방지)
            double? targetOffset = recent.ScrollOffset;
            int targetLine = recent.SavedLine;
            string targetType = recent.Type;
            string targetPath = recent.Path;
            int targetPage = recent.SavedPage;
            string? targetArchiveKey = recent.ArchiveEntryKey;
            string targetName = recent.Name;
            int targetChapter = recent.ChapterIndex;

            try
            {
                // 2. [중요] 새 파일을 로드하기 전에, 현재 보고 있던 '이전 파일'의 상태를 확실히 저장
                // 이때는 아직 _isNavigatingRecent가 false이므로 저장이 정상 동작함
                await AddToRecentAsync(true);

                // 3. [잠금] 이제부터 파일 로드가 완료될 때까지 자동 저장 기능을 차단
                _isNavigatingRecent = true;

                if (recent.IsWebDav && !string.IsNullOrEmpty(recent.WebDavServerName))
                {
                    try
                    {
                        if (!_isWebDavMode || _webDavService.CurrentServer?.ServerName != recent.WebDavServerName)
                        {
                            await ConnectToWebDavServerAsync(recent.WebDavServerName, false);
                            if (!_isWebDavMode) return;
                        }

                        var fileItem = new FileItem
                        {
                            Name = targetName,
                            WebDavPath = targetPath,
                            IsWebDav = true,
                            IsDirectory = targetType == "Folder",
                            IsArchive = targetType == "Archive",
                        };
                        string ext = Path.GetExtension(targetPath).ToLowerInvariant();
                        fileItem.IsImage = FileExplorerService.SupportedImageExtensions.Contains(ext);
                        fileItem.IsText = FileExplorerService.SupportedTextExtensions.Contains(ext);
                        fileItem.IsEpub = FileExplorerService.SupportedEpubExtensions.Contains(ext);
                        fileItem.IsPdf = FileExplorerService.SupportedPdfExtensions.Contains(ext);

                        if (targetType != "Folder")
                        {
                            var parentPath = Path.GetDirectoryName(targetPath)?.Replace("\\", "/");
                            if (!string.IsNullOrEmpty(parentPath))
                            {
                                if (!parentPath.StartsWith("/")) parentPath = "/" + parentPath;
                                // WebDAV는 폴더 구분자가 /로 끝나야 함 (서비스에서 처리하지만 여기서도 보정)
                                if (!parentPath.EndsWith("/")) parentPath += "/";
                                await LoadWebDavFolderAsync(parentPath);
                            }
                            else await LoadWebDavFolderAsync("/");
                        }

                        if (targetType == "Folder")
                        {
                            await LoadWebDavFolderAsync(targetPath);
                        }
                        else if (targetType == "Archive")
                        {
                            await OpenWebDavArchiveAsync(fileItem);
                            if (!string.IsNullOrEmpty(targetArchiveKey))
                            {
                                var entryIndex = _imageEntries.FindIndex(e => e.ArchiveEntryKey == targetArchiveKey);
                                if (entryIndex >= 0)
                                {
                                    _currentIndex = entryIndex;
                                    await DisplayCurrentImageAsync();
                                    if (targetOffset.HasValue && TextScrollViewer != null)
                                    {
                                        await Task.Delay(100);
                                        TextScrollViewer.ChangeView(null, targetOffset.Value, null);
                                        UpdateTextStatusBar();
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (fileItem.IsEpub)
                            {
                                PendingEpubChapterIndex = targetChapter;
                                PendingEpubPageIndex = targetPage;
                                // [추가] EPUB 모드에서도 저장된 정확한 줄 번호를 복구하도록 변수에 할당합니다.
                                _aozoraPendingTargetLine = targetLine > 1 ? targetLine : 1;
                            }
                            else if (fileItem.IsText)
                            {
                                _aozoraPendingTargetLine = targetLine > 1 ? targetLine : (targetPage > 0 ? -targetPage : 1);
                            }
                            else if (fileItem.IsPdf)
                            {
                                _pendingPdfPageIndex = targetPage;
                            }
                            await OpenWebDavFileAsync(fileItem);
                        }
                        return;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error opening WebDAV recent: {ex.Message}");
                        ShowNotification($"WebDAV 최근 항목 열기 실패: {ex.Message}");
                        return;
                    }
                }

                switch (targetType)
                {
                    case "Folder":
                        if (Directory.Exists(targetPath))
                        {
                            LoadExplorerFolder(targetPath);
                        }
                        break;

                    case "File":
                        if (File.Exists(targetPath))
                        {
                            // EPUB용 Pending 설정
                            if (targetPath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                            {
                                PendingEpubChapterIndex = targetChapter;
                                PendingEpubPageIndex = targetPage;
                            }
                            else if (targetPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                _pendingPdfPageIndex = targetPage;
                            }

                            // 텍스트/아오조라용 Pending 설정
                            _aozoraPendingTargetLine = targetLine > 1 ? targetLine : (targetPage > 0 ? -targetPage : 1);

                            // 탐색기 폴더 이동 및 선택
                            var parentDir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                            {
                                LoadExplorerFolder(parentDir);

                                var filename = Path.GetFileName(targetPath);
                                var item = _fileItems.FirstOrDefault(f => f.Name.Equals(filename, StringComparison.OrdinalIgnoreCase));
                                if (item != null)
                                {
                                    if (_isExplorerGrid) FileGridView.SelectedItem = item;
                                    else FileListView.SelectedItem = item;
                                }
                            }

                            var file = await StorageFile.GetFileFromPathAsync(targetPath);

                            // [로드] 파일 열기 (내부에서 AddToRecentAsync가 호출되지만, 위에서 건 플래그 때문에 무시됨)
                            if (targetPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                await LoadImagesFromPdfAsync(targetPath);
                            }
                            else
                            {
                                await LoadImageFromFileAsync(file);
                            }
                        }
                        break;

                    case "Archive":
                        if (File.Exists(targetPath))
                        {
                            var archiveFolder = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                            {
                                LoadExplorerFolder(archiveFolder);

                                if (!string.IsNullOrEmpty(targetArchiveKey))
                                {
                                    await LoadImagesFromArchiveAsync(targetPath);
                                    var entryIndex = _imageEntries.FindIndex(e => e.ArchiveEntryKey == targetArchiveKey);
                                    if (entryIndex >= 0)
                                    {
                                        _currentIndex = entryIndex;
                                        await DisplayCurrentImageAsync();

                                        // 아카이브 내 텍스트 파일인 경우 스크롤 복원
                                        if (targetOffset.HasValue && TextScrollViewer != null)
                                        {
                                            await Task.Delay(100);
                                            TextScrollViewer.ChangeView(null, targetOffset.Value, null);
                                            UpdateTextStatusBar();
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }

                // 4. [복원] 일반 텍스트 파일 위치 복원은 이제 LoadTextLinesProgressivelyAsync 내의 ScrollToLine에서 처리함
                // (이전 방식인 pixel offset 복원은 폰트 크기 변경 등에 취약하여 제거)
                /*
                if (targetType == "File")
                {
                    if (!targetPath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) && !_isAozoraMode)
                    {
                        if (targetOffset.HasValue && TextScrollViewer != null)
                        {
                            await Task.Delay(200);
                            TextScrollViewer.ChangeView(null, targetOffset.Value, null);
                            UpdateTextStatusBar();
                        }
                    }
                }
                */
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to recent: {ex.Message}");
            }
            finally
            {
                // 5. [해제] 로드 및 복원이 끝났으므로 잠금 해제
                _isNavigatingRecent = false;
            }

            // 6. [갱신] 이제 안전하게 현재 파일을 Recent 목록의 최상단으로 이동 (위치는 유지)
            await AddToRecentAsync(false);
        }

        #endregion
    }
}