using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class BookmarkInteractionHandlers
    {
        public Action HideFavoritesFlyouts { get; init; } = null!;
        public Action HideRecentFlyouts { get; init; } = null!;
        public Action<ObservableCollection<BookmarkViewModel>, ObservableCollection<BookmarkViewModel>, string> SetFavoriteSources { get; init; } = null!;
        public Action<ObservableCollection<BookmarkViewModel>, string> SetFileFavoriteSidebarSource { get; init; } = null!;
        public Action<ObservableCollection<BookmarkViewModel>, string> SetFolderFavoriteSidebarSource { get; init; } = null!;
        public Action<ObservableCollection<BookmarkViewModel>, string> SetRecentSources { get; init; } = null!;
        public Action<ObservableCollection<BookmarkViewModel>, string> SetRecentSidebarSource { get; init; } = null!;
        public Action<string, string, string> ShowNotification { get; init; } = null!;
    }

    internal sealed class BookmarkInteractionController
    {
        private readonly FavoritesController _favoritesController;
        private readonly RecentController _recentController;
        private readonly IBookmarkNavigationHost _navigationHost;
        private readonly Func<FavoriteCaptureContext> _captureFavoriteContext;
        private readonly Func<RecentCaptureContext> _captureRecentContext;
        private readonly BookmarkInteractionHandlers _handlers;

        public BookmarkInteractionController(
            FavoritesController favoritesController,
            RecentController recentController,
            IBookmarkNavigationHost navigationHost,
            Func<FavoriteCaptureContext> captureFavoriteContext,
            Func<RecentCaptureContext> captureRecentContext,
            BookmarkInteractionHandlers handlers)
        {
            _favoritesController = favoritesController;
            _recentController = recentController;
            _navigationHost = navigationHost;
            _captureFavoriteContext = captureFavoriteContext;
            _captureRecentContext = captureRecentContext;
            _handlers = handlers;
        }

        public void UpdateFavoritesMenu(
            ObservableCollection<BookmarkViewModel> fileFavorites,
            ObservableCollection<BookmarkViewModel> folderFavorites)
        {
            _favoritesController.RefreshFavorites();

            _handlers.SetFavoriteSources(fileFavorites, folderFavorites, Strings.NoFavorites);
            _handlers.SetFileFavoriteSidebarSource(fileFavorites, Strings.NoFavorites);
            _handlers.SetFolderFavoriteSidebarSource(folderFavorites, Strings.NoFavorites);
        }

        public void UpdateRecentMenu(ObservableCollection<BookmarkViewModel> recentItems)
        {
            _recentController.RefreshRecent();

            _handlers.SetRecentSources(recentItems, Strings.NoRecentFiles);
            _handlers.SetRecentSidebarSource(recentItems, Strings.NoRecentFiles);
        }

        public async Task AddCurrentFavoriteAsync(bool isManualSave = true)
        {
            try
            {
                var saveResult = await _favoritesController.AddCurrentAsync(
                    _captureFavoriteContext(),
                    isManualSave);
                if (saveResult == FavoriteSaveResult.Added)
                {
                    _handlers.ShowNotification(Strings.AddedToFavoritesNotification, "\uE735", "Gold");
                }
                else if (saveResult == FavoriteSaveResult.PositionUpdated)
                {
                    _handlers.ShowNotification(Strings.FavoritePositionUpdatedNotification, "\uE735", "Gold");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding to favorites: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task HandleFavoriteClickedAsync(BookmarkViewModel model)
        {
            try
            {
                if (model.OriginalItem is FavoriteItem favorite)
                {
                    _handlers.HideFavoritesFlyouts();
                    await NavigateFavoriteAsync(favorite);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in BookmarkList_ItemClicked: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task HandleFavoriteRemoveClickedAsync(BookmarkViewModel model)
        {
            try
            {
                if (model.OriginalItem is FavoriteItem favorite)
                {
                    await RemoveFavoriteAsync(favorite);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in BookmarkList_RemoveClicked: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task HandleFavoritePinClickedAsync(BookmarkViewModel model)
        {
            try
            {
                if (model.OriginalItem is FavoriteItem favorite)
                {
                    await _favoritesController.TogglePinAsync(favorite);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in BookmarkList_PinClicked: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task HandleRecentClickedAsync(BookmarkViewModel model)
        {
            try
            {
                if (model.OriginalItem is RecentItem recent)
                {
                    _handlers.HideRecentFlyouts();
                    await _recentController.NavigateAsync(recent, _navigationHost, _captureRecentContext);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RecentList_ItemClicked: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task HandleRecentRemoveClickedAsync(BookmarkViewModel model)
        {
            try
            {
                if (model.OriginalItem is RecentItem recent)
                {
                    await _recentController.RemoveAsync(recent);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RecentList_RemoveClicked: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public Task AddCurrentRecentAsync(bool saveCurrentPosition = false)
        {
            return _recentController.AddCurrentAsync(_captureRecentContext(), saveCurrentPosition);
        }

        private async Task RemoveFavoriteAsync(FavoriteItem favorite)
        {
            try
            {
                await _favoritesController.RemoveAsync(favorite);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing favorite: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task NavigateFavoriteAsync(FavoriteItem favorite)
        {
            try
            {
                await _favoritesController.NavigateAsync(favorite, _navigationHost);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to favorite: {ex.Message}");
            }
        }
    }
}
