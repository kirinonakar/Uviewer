using System;
using System.Collections.Generic;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextResumeService
    {
        public int GetSavedStartLine(
            IEnumerable<RecentItem> recentItems,
            IEnumerable<FavoriteItem> favorites,
            string name,
            string? path)
        {
            try
            {
                var recent = recentItems.OrderByDescending(r => r.AccessedAt)
                    .FirstOrDefault(r => IsSameDocument(r.Name, r.Path, name, path));

                if (recent != null)
                {
                    if (recent.SavedLine > 1) return recent.SavedLine;
                    if (recent.SavedPage > 0) return -recent.SavedPage;
                    return 1;
                }

                var favorite = favorites.FirstOrDefault(f => IsSameDocument(f.Name, f.Path, name, path));
                if (favorite != null)
                {
                    if (favorite.SavedLine > 1) return favorite.SavedLine;
                    if (favorite.SavedPage > 0) return -favorite.SavedPage;
                }
            }
            catch
            {
            }

            return 1;
        }

        private static bool IsSameDocument(string itemName, string itemPath, string name, string? path)
        {
            return (path != null && string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase)) ||
                   (path == null && string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
