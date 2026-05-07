using System;
using System.IO;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public static class BookmarkViewModelFactory
    {
        public static BookmarkViewModel Create(FavoriteItem favorite)
        {
            var displayPosition = CreateDisplayPosition(
                favorite.Type,
                favorite.Path,
                favorite.SavedPage,
                favorite.ChapterIndex,
                favorite.SavedLine);

            return BookmarkViewModel.FromFavorite(
                favorite,
                displayPosition,
                CreateTooltip(
                    favorite.Path,
                    favorite.Type,
                    favorite.Progress,
                    displayPosition,
                    favorite.IsWebDav,
                    favorite.WebDavServerName));
        }

        public static BookmarkViewModel Create(RecentItem recent)
        {
            var displayPosition = CreateDisplayPosition(
                recent.Type,
                recent.Path,
                recent.SavedPage,
                recent.ChapterIndex,
                recent.SavedLine);

            return BookmarkViewModel.FromRecent(
                recent,
                displayPosition,
                CreateTooltip(
                    recent.Path,
                    recent.Type,
                    recent.Progress,
                    displayPosition,
                    recent.IsWebDav,
                    recent.WebDavServerName));
        }

        private static string CreateDisplayPosition(
            string type,
            string path,
            int savedPage,
            int chapterIndex,
            int savedLine)
        {
            if (IsImageFile(type, path))
            {
                return "";
            }

            if (path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            {
                return $" (Ch.{chapterIndex + 1} P.{savedPage + 1} L.{savedLine})";
            }

            if (type == "File" && !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return $" (Line {savedLine})";
            }

            if (savedPage > 0 || chapterIndex > 0)
            {
                return $" ({(chapterIndex > 0 ? $"Ch.{chapterIndex + 1} " : "")}P.{savedPage + 1})";
            }

            return "";
        }

        private static string CreateTooltip(
            string path,
            string type,
            double progress,
            string displayPosition,
            bool isWebDav,
            string? webDavServerName)
        {
            var tooltip = path + (string.IsNullOrEmpty(displayPosition) ? "" : $"\n{displayPosition.Trim(' ', '(', ')')}");
            if (isWebDav && !string.IsNullOrEmpty(webDavServerName))
            {
                tooltip = $"[{webDavServerName}] {tooltip}";
            }

            if (type != "Folder" && !IsImageFile(type, path))
            {
                tooltip += $"\n{Strings.ProgressLabel}: {progress:F1}%";
            }

            return tooltip;
        }

        private static bool IsImageFile(string type, string path)
        {
            return type == "File" &&
                   !string.IsNullOrEmpty(path) &&
                   FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }
    }
}
