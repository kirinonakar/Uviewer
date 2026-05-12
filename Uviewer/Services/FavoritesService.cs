using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public enum FavoriteSaveResult
    {
        Unchanged,
        Added,
        Updated,
        PositionUpdated
    }

    public class FavoritesService
    {
        private ObservableCollection<FavoriteItem> _favorites = new();
        public ReadOnlyObservableCollection<FavoriteItem> Favorites { get; }

        public event EventHandler? FavoritesUpdated;

        private const string FavoritesFilePath = "favorites.json";
        private string GetFavoritesFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", FavoritesFilePath);

        public FavoritesService()
        {
            Favorites = new ReadOnlyObservableCollection<FavoriteItem>(_favorites);
        }

        public async Task LoadFavoritesAsync()
        {
            try
            {
                var favoritesFile = GetFavoritesFilePath();
                var favoritesDir = Path.GetDirectoryName(favoritesFile);

                if (!string.IsNullOrEmpty(favoritesDir) && !Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

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
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading favorites: {ex.Message}");
            }
        }

        public async Task SaveFavoritesAsync()
        {
            try
            {
                var favoritesFile = GetFavoritesFilePath();
                var favoritesDir = Path.GetDirectoryName(favoritesFile);

                if (!string.IsNullOrEmpty(favoritesDir) && !Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(_favorites.ToList(), typeof(List<FavoriteItem>), FavoritesContext.Default);
                await File.WriteAllTextAsync(favoritesFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving favorites: {ex.Message}");
            }
        }

        public async Task<FavoriteSaveResult> AddOrUpdateFavoriteAsync(FavoriteItem favorite, bool isManualSave)
        {
            string normPath = favorite.Path.Replace('\\', '/').TrimEnd('/');
            FavoriteItem? existing = _favorites.FirstOrDefault(f => 
                f.Path.Replace('\\', '/').TrimEnd('/').Equals(normPath, StringComparison.OrdinalIgnoreCase) && 
                f.Type == favorite.Type &&
                f.IsWebDav == favorite.IsWebDav &&
                (f.WebDavServerName == null && favorite.WebDavServerName == null || 
                 f.WebDavServerName != null && f.WebDavServerName.Equals(favorite.WebDavServerName, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (!isManualSave)
                {
                    return FavoriteSaveResult.Unchanged; 
                }

                bool positionChanged = HasPositionChanged(existing, favorite);

                // Update properties in-place instead of removing and re-adding
                existing.Name = favorite.Name;
                existing.Path = favorite.Path;
                existing.ArchiveEntryKey = favorite.ArchiveEntryKey;
                existing.ScrollOffset = favorite.ScrollOffset;
                existing.SavedPage = favorite.SavedPage;
                existing.ChapterIndex = favorite.ChapterIndex;
                existing.SavedLine = favorite.SavedLine;
                existing.SavedBlockIndex = favorite.SavedBlockIndex;
                existing.IsVertical = favorite.IsVertical;
                existing.Progress = favorite.Progress;
                // IsPinned and CreatedAt remain unchanged

                await SaveFavoritesAsync();
                FavoritesUpdated?.Invoke(this, EventArgs.Empty);
                return positionChanged ? FavoriteSaveResult.PositionUpdated : FavoriteSaveResult.Updated;
            }
            else
            {
                _favorites.Add(favorite);
                await SaveFavoritesAsync();
                FavoritesUpdated?.Invoke(this, EventArgs.Empty);
                return FavoriteSaveResult.Added;
            }
        }

        private static bool HasPositionChanged(FavoriteItem existing, FavoriteItem favorite)
        {
            return !string.Equals(existing.ArchiveEntryKey, favorite.ArchiveEntryKey, StringComparison.Ordinal) ||
                   !NullableDoubleEquals(existing.ScrollOffset, favorite.ScrollOffset) ||
                   existing.SavedPage != favorite.SavedPage ||
                   existing.ChapterIndex != favorite.ChapterIndex ||
                   existing.SavedLine != favorite.SavedLine ||
                   existing.SavedBlockIndex != favorite.SavedBlockIndex ||
                   existing.IsVertical != favorite.IsVertical ||
                   !DoubleEquals(existing.Progress, favorite.Progress);
        }

        private static bool NullableDoubleEquals(double? left, double? right)
        {
            if (!left.HasValue || !right.HasValue)
            {
                return left.HasValue == right.HasValue;
            }

            return DoubleEquals(left.Value, right.Value);
        }

        private static bool DoubleEquals(double left, double right)
        {
            return Math.Abs(left - right) < 0.001;
        }

        public async Task RemoveFavoriteAsync(FavoriteItem favorite)
        {
            string normPath = favorite.Path.Replace('\\', '/').TrimEnd('/');
            var existing = _favorites.FirstOrDefault(f => 
                f.Path.Replace('\\', '/').TrimEnd('/').Equals(normPath, StringComparison.OrdinalIgnoreCase) && 
                f.Type == favorite.Type &&
                f.IsWebDav == favorite.IsWebDav &&
                (f.WebDavServerName == null && favorite.WebDavServerName == null || 
                 f.WebDavServerName != null && f.WebDavServerName.Equals(favorite.WebDavServerName, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                _favorites.Remove(existing);
                await SaveFavoritesAsync();
                FavoritesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task TogglePinAsync(FavoriteItem favorite)
        {
            favorite.IsPinned = !favorite.IsPinned;
            await SaveFavoritesAsync();
            FavoritesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool AnyFolderFavoriteExists(string folderPath, bool isWebDav, string? webDavServerName)
        {
            string normPath = folderPath.Replace('\\', '/').TrimEnd('/');
            return _favorites.Any(f => 
                f.Path.Replace('\\', '/').TrimEnd('/').Equals(normPath, StringComparison.OrdinalIgnoreCase) && 
                f.Type == "Folder" && 
                f.IsWebDav == isWebDav && 
                (f.WebDavServerName == null && webDavServerName == null || 
                 f.WebDavServerName != null && f.WebDavServerName.Equals(webDavServerName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
