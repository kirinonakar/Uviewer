using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class BookmarkPanelController
    {
        private readonly BookmarkPanelState _state;
        private readonly FavoritesService _favoritesService;
        private readonly RecentService _recentService;

        public BookmarkPanelController(
            BookmarkPanelState state,
            FavoritesService favoritesService,
            RecentService recentService)
        {
            _state = state;
            _favoritesService = favoritesService;
            _recentService = recentService;
        }

        public void RefreshFavorites()
        {
            var fileFavorites = _favoritesService.Favorites
                .Where(f => f.Type != "Folder")
                .OrderByDescending(f => f.IsPinned)
                .ThenByDescending(f => f.CreatedAt)
                .Select(BookmarkViewModelFactory.Create);

            var folderFavorites = _favoritesService.Favorites
                .Where(f => f.Type == "Folder")
                .OrderByDescending(f => f.IsPinned)
                .ThenByDescending(f => f.CreatedAt)
                .Select(BookmarkViewModelFactory.Create);

            _state.ReplaceFileFavorites(fileFavorites);
            _state.ReplaceFolderFavorites(folderFavorites);
        }

        public void RefreshRecent()
        {
            var recentItems = _recentService.RecentItems
                .OrderByDescending(r => r.AccessedAt)
                .Select(BookmarkViewModelFactory.Create);

            _state.ReplaceRecentItems(recentItems);
        }
    }
}
