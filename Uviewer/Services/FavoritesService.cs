using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
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

        public async Task AddOrUpdateFavoriteAsync(FavoriteItem favorite, bool isManualSave)
        {
            FavoriteItem? existing = _favorites.FirstOrDefault(f => f.Path == favorite.Path && f.Type == favorite.Type);
            bool wasPinned = false;

            if (existing != null)
            {
                wasPinned = existing.IsPinned;

                if (!isManualSave)
                {
                    return; 
                }

                _favorites.Remove(existing);
            }

            favorite.IsPinned = wasPinned;
            _favorites.Add(favorite);

            await SaveFavoritesAsync();
            FavoritesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task RemoveFavoriteAsync(FavoriteItem favorite)
        {
            if (_favorites.Remove(favorite))
            {
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
            return _favorites.Any(f => f.Path == folderPath && f.Type == "Folder" && f.IsWebDav == isWebDav && f.WebDavServerName == webDavServerName);
        }
    }
}
