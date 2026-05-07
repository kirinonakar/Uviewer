using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Uviewer.Models
{
    public sealed class BookmarkPanelState
    {
        public bool IsNavigatingRecent { get; set; }
        public ObservableCollection<BookmarkViewModel> FileFavoriteItems { get; } = new();
        public ObservableCollection<BookmarkViewModel> FolderFavoriteItems { get; } = new();
        public ObservableCollection<BookmarkViewModel> RecentItems { get; } = new();

        public void ReplaceFileFavorites(IEnumerable<BookmarkViewModel> items)
        {
            Replace(FileFavoriteItems, items);
        }

        public void ReplaceFolderFavorites(IEnumerable<BookmarkViewModel> items)
        {
            Replace(FolderFavoriteItems, items);
        }

        public void ReplaceRecentItems(IEnumerable<BookmarkViewModel> items)
        {
            Replace(RecentItems, items);
        }

        private static void Replace(
            ObservableCollection<BookmarkViewModel> target,
            IEnumerable<BookmarkViewModel> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }
    }
}
