using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        #region Favorites


        private void UpdateFavoritesMenu()
            => _bookmarkInteractionController.UpdateFavoritesMenu(_fileFavoriteItems, _folderFavoriteItems);


        private async Task AddToFavoritesAsync(bool isManualSave = true)
            => await _bookmarkInteractionController.AddCurrentFavoriteAsync(isManualSave);

        #endregion

        #region Recent Items


        private void UpdateRecentMenu()
            => _bookmarkInteractionController.UpdateRecentMenu(_recentItemsList);


        private async Task AddToRecentAsync(bool saveCurrentPosition = false)
            => await _bookmarkInteractionController.AddCurrentRecentAsync(saveCurrentPosition);

        #endregion
    }
}
