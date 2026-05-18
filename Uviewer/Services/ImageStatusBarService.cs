using Microsoft.Graphics.Canvas;
using System;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed record ImageStatusBarContent(
        string FileName,
        string ImageInfo,
        string ImageIndex,
        string TextProgress);

    public sealed class ImageStatusBarService
    {
        public ImageStatusBarContent Create(
            ImageEntry entry,
            CanvasBitmap bitmap,
            string? archivePath,
            string? webDavPath,
            bool isSideBySide,
            bool isPdfMode,
            int currentIndex,
            int totalCount)
        {
            string fileName = FileExplorerService.GetFormattedDisplayName(
                entry.DisplayName,
                entry.IsArchiveEntry,
                archivePath,
                webDavPath);

            string imageInfo = TryGetBitmapSize(bitmap, out var width, out var height)
                ? $"{(int)width} × {(int)height}"
                : string.Empty;

            string imageIndex = isSideBySide && !isPdfMode
                ? $"{(currentIndex / 2) + 1} / {(totalCount + 1) / 2} (B)"
                : $"{currentIndex + 1} / {totalCount}";

            return new ImageStatusBarContent(fileName, imageInfo, imageIndex, string.Empty);
        }

        private static bool TryGetBitmapSize(CanvasBitmap? bitmap, out double width, out double height)
        {
            width = 0;
            height = 0;

            if (bitmap == null) return false;

            try
            {
                if (bitmap.Device == null) return false;
                var size = bitmap.Size;
                if (size.Width <= 0 || size.Height <= 0) return false;
                width = size.Width;
                height = size.Height;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
