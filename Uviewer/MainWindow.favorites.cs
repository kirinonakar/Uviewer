using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
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
        private const string TextSettingsFilePath = "text_settings.json";
        private string GetTextSettingsFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", TextSettingsFilePath);




        #region Favorites


        private void UpdateFavoritesMenu()
        {
            _bookmarkPanelController.RefreshFavorites();

            // Bind to controls
            FileFavoritesList.ItemsSource = _fileFavoriteItems;
            FileFavoritesList.EmptyMessage = Strings.NoFavorites;
            
            FolderFavoritesList.ItemsSource = _folderFavoriteItems;
            FolderFavoritesList.EmptyMessage = Strings.NoFavorites;

            SidebarFileFavoritesList.ItemsSource = _fileFavoriteItems;
            SidebarFileFavoritesList.EmptyMessage = Strings.NoFavorites;
            
            SidebarFolderFavoritesList.ItemsSource = _folderFavoriteItems;
            SidebarFolderFavoritesList.EmptyMessage = Strings.NoFavorites;
        }

        // Event Handlers for BookmarkListControl
        private async void BookmarkList_ItemClicked(object? sender, BookmarkViewModel e)
        {
            try
            {
                if (e.OriginalItem is FavoriteItem fav)
                {
                    FavoritesFlyout?.Hide();
                    SidebarFavoritesFlyout?.Hide();
                    await NavigateToFavoriteAsync(fav);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in BookmarkList_ItemClicked: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void BookmarkList_RemoveClicked(object? sender, BookmarkViewModel e)
        {
            try
            {
                if (e.OriginalItem is FavoriteItem fav)
                {
                    await RemoveFavoriteAsync(fav);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in BookmarkList_RemoveClicked: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void BookmarkList_PinClicked(object? sender, BookmarkViewModel e)
        {
            try
            {
                if (e.OriginalItem is FavoriteItem fav)
                {
                    await _favoritesService.TogglePinAsync(fav);
                    UpdateFavoritesMenu();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in BookmarkList_PinClicked: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void RecentList_ItemClicked(object? sender, BookmarkViewModel e)
        {
            try
            {
                if (e.OriginalItem is RecentItem recent)
                {
                    RecentFlyout?.Hide();
                    SidebarRecentFlyout?.Hide();
                    await NavigateToRecentAsync(recent);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RecentList_ItemClicked: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        private async void RecentList_RemoveClicked(object? sender, BookmarkViewModel e)
        {
            try
            {
                if (e.OriginalItem is RecentItem recent)
                {
                    await RemoveRecentAsync(recent);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RecentList_RemoveClicked: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
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
                    int savedBlockIndex = -1;
                    if (_isEpubMode)
                    {
                        savedPage = CurrentEpubPageIndex;
                        savedBlockIndex = CurrentEpubWin2DPage?.StartBlockIndex ?? -1;
                        if (_isVerticalMode)
                        {
                            // [수정] EPUB 세로 모드일 때는 텍스트 뷰어(CurrentVerticalPageInfo)가 아닌 EPUB 페이지를 참조해야 함
                            savedLine = CurrentEpubWin2DPage?.StartLine ?? 1;
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
                        SavedBlockIndex = savedBlockIndex,
                        ChapterIndex = chapterIndex,
                        IsWebDav = isWebDav,
                        WebDavServerName = webDavServerName,
                        IsVertical = _isVerticalMode,
                        Progress = Math.Max(calcProgress, 0),
                        IsPinned = false
                    };

                    bool wasAdded = await _favoritesService.AddOrUpdateFavoriteAsync(favorite, isManualSave);
                    System.Diagnostics.Debug.WriteLine(wasAdded ? $"Added favorite: {favorite.Name}" : $"Updated favorite: {favorite.Name}");
                    UpdateFavoritesMenu();
                    System.Diagnostics.Debug.WriteLine("Favorite saved successfully");
                    if (wasAdded)
                    {
                        ShowNotification(Strings.AddedToFavoritesNotification, "\uE735", "Gold");
                    }
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
                             // [수정] WebDAV에서도 .7z의 경우 OpenWebDavFileAsync를 통해 다운로드 후 로컬처럼 추출하도록 수정합니다.
                             if (ext == ".7z")
                             {
                                 await OpenWebDavFileAsync(fileItem);
                             }
                             else
                             {
                                 await OpenWebDavArchiveAsync(fileItem);
                             }
                             
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
                                 _pendingEpubStartBlockIndex = favorite.SavedBlockIndex;
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
                        else
                        {
                            ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
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
                                _pendingEpubStartBlockIndex = favorite.SavedBlockIndex;
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
                        else
                        {
                            ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
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
            _bookmarkPanelController.RefreshRecent();

            RecentList.ItemsSource = _recentItemsList;
            RecentList.EmptyMessage = Strings.NoRecentFiles;

            SidebarRecentList.ItemsSource = _recentItemsList;
            SidebarRecentList.EmptyMessage = Strings.NoRecentFiles;
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
                int targetBlockIndex = existing?.SavedBlockIndex ?? -1;
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
                        // 기존에 읽던 기록(페이지, 챕터, 혹은 블록, 라인 등)이 있다면 기존 값 유지
                        bool isResetState = CurrentEpubPageIndex == 0 && CurrentEpubChapterIndex == 0 && (CurrentEpubWin2DPage?.StartBlockIndex ?? 0) == 0;
                        bool hasExistingProgress = existing != null && (existing.SavedPage > 0 || existing.ChapterIndex > 0 || existing.SavedBlockIndex > 0 || existing.SavedLine > 1);

                        if (isResetState && existing != null && hasExistingProgress)
                        {
                            targetPage = existing.SavedPage;
                            targetChapter = existing.ChapterIndex;
                            targetLine = existing.SavedLine;
                            targetBlockIndex = existing.SavedBlockIndex;
                            targetProgress = existing.Progress; // 진행률도 기존 값 복구
                            System.Diagnostics.Debug.WriteLine($"[SafeGuard] Epub reset state detected. Keeping previous position: Ch.{targetChapter} P.{targetPage} Block.{targetBlockIndex}");
                        }
                        else
                        {
                            targetPage = CurrentEpubPageIndex;
                            targetChapter = CurrentEpubChapterIndex;
                            targetBlockIndex = CurrentEpubWin2DPage?.StartBlockIndex ?? -1;
                            
                            if (_isVerticalMode)
                            {
                                // [수정] EPUB 세로 모드일 때는 텍스트 뷰어(CurrentVerticalPageInfo)가 아닌 EPUB 페이지를 참조해야 함
                                targetLine = CurrentEpubWin2DPage?.StartLine ?? 1;
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
                        if (isResetState && existing != null && (existing.SavedLine > 1 || (existing.ScrollOffset ?? 0) > 0))
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
                    SavedBlockIndex = targetBlockIndex,
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
            // [수정] targetBlockIndex도 누락 없이 캡처하여 기존 위치 값을 안전하게 보존합니다.
            double? targetOffset = recent.ScrollOffset;
            int targetLine = recent.SavedLine;
            string targetType = recent.Type;
            string targetPath = recent.Path;
            int targetPage = recent.SavedPage;
            int targetBlockIndex = recent.SavedBlockIndex; 
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
                            // [수정] WebDAV에서도 .7z의 경우 OpenWebDavFileAsync를 통해 다운로드 후 로컬처럼 추출하도록 수정합니다.
                            if (ext == ".7z")
                            {
                                await OpenWebDavFileAsync(fileItem);
                            }
                            else
                            {
                                await OpenWebDavArchiveAsync(fileItem);
                            }
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
                                _pendingEpubStartBlockIndex = targetBlockIndex; // [수정] 캡처된 변수 사용
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
                        else
                        {
                            ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
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
                                _pendingEpubStartBlockIndex = targetBlockIndex; // [수정] 캡처된 블록 인덱스 보장
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
                        else
                        {
                            ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
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
