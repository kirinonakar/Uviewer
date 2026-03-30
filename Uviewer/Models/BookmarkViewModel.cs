using System;
using Uviewer.Models;

namespace Uviewer.Models
{
    public class BookmarkViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = ""; // "Folder", "File", "Archive"
        public double Progress { get; set; } = 0;
        public string DisplayPosition { get; set; } = "";
        
        // UI helper properties
        public bool IsNotFolder => Type != "Folder" && !string.IsNullOrEmpty(Path) && !IsImageFileExtension(Path);
        public bool IsFavorite => OriginalItem is FavoriteItem;
        public string ProgressString => $"{Progress:F0}%";
        public string PinIcon => IsPinned ? "\uE840" : "\uE718";
        public Microsoft.UI.Xaml.Media.Brush PinColor => IsPinned 
            ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"] 
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.5 };

        public bool IsPinned { get; set; } = false;
        public bool IsWebDav { get; set; } = false;
        public string? WebDavServerName { get; set; }
        public string Tooltip { get; set; } = "";
        
        // Reference to original item for navigation/actions
        public object? OriginalItem { get; set; }

        private static bool IsImageFileExtension(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".webp";
        }

        public static BookmarkViewModel FromFavorite(FavoriteItem favorite, string posString, string tooltip)
        {
            return new BookmarkViewModel
            {
                Name = favorite.Name,
                Path = favorite.Path,
                Type = favorite.Type,
                Progress = favorite.Progress,
                DisplayPosition = posString,
                IsPinned = favorite.IsPinned,
                IsWebDav = favorite.IsWebDav,
                WebDavServerName = favorite.WebDavServerName,
                Tooltip = tooltip,
                OriginalItem = favorite
            };
        }

        public static BookmarkViewModel FromRecent(RecentItem recent, string posString, string tooltip)
        {
            return new BookmarkViewModel
            {
                Name = recent.Name,
                Path = recent.Path,
                Type = recent.Type,
                Progress = recent.Progress,
                DisplayPosition = posString,
                IsPinned = false,
                IsWebDav = recent.IsWebDav,
                WebDavServerName = recent.WebDavServerName,
                Tooltip = tooltip,
                OriginalItem = recent
            };
        }
    }
}
