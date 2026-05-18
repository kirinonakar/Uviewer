using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ImageDoublePageDecisionService
    {
        private readonly ImageCacheManager _imageCache;

        public ImageDoublePageDecisionService(ImageCacheManager imageCache)
        {
            _imageCache = imageCache;
        }

        public async Task<bool> ShouldUseSideBySideAsync(
            IReadOnlyList<ImageEntry> entries,
            int currentIndex,
            bool isSideBySideMode,
            bool autoDoublePageForArchive,
            bool hasArchive,
            bool isPdfMode,
            double zoomLevel,
            CanvasBitmap? currentBitmap,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> loadBitmapAsync,
            CancellationToken token)
        {
            bool canSideBySide = isSideBySideMode && !isPdfMode && entries.Count > 1;

            if (!autoDoublePageForArchive || !hasArchive || isPdfMode || entries.Count <= 1)
            {
                return canSideBySide;
            }

            var firstBitmap = await GetOrLoadBitmapAsync(entries[currentIndex], currentIndex, zoomLevel, currentBitmap, loadBitmapAsync, token);
            if (firstBitmap == null)
            {
                return canSideBySide;
            }

            if (firstBitmap.Size.Width >= firstBitmap.Size.Height * 1.2)
            {
                return false;
            }

            if (IsTallCandidate(firstBitmap.Size.Width, firstBitmap.Size.Height))
            {
                if (currentIndex + 1 >= entries.Count)
                {
                    return false;
                }

                var nextBitmap = await GetOrLoadBitmapAsync(entries[currentIndex + 1], currentIndex + 1, zoomLevel, currentBitmap, loadBitmapAsync, token);
                return nextBitmap != null && IsTallCandidate(nextBitmap.Size.Width, nextBitmap.Size.Height);
            }

            if (firstBitmap.Size.Height > firstBitmap.Size.Width * 3.0)
            {
                return false;
            }

            return canSideBySide;
        }

        public static bool IsTallCandidate(double width, double height)
        {
            if (width <= 0 || height <= 0) return false;
            return height >= width * 1.2 && height <= width * 3.0;
        }

        private async Task<CanvasBitmap?> GetOrLoadBitmapAsync(
            ImageEntry entry,
            int index,
            double zoomLevel,
            CanvasBitmap? currentBitmap,
            Func<ImageEntry, CancellationToken, Task<CanvasBitmap?>> loadBitmapAsync,
            CancellationToken token)
        {
            var bitmap = _imageCache.GetPreloadedImage(index, zoomLevel);
            if (bitmap != null)
            {
                return bitmap;
            }

            bitmap = await loadBitmapAsync(entry, token);
            if (bitmap != null)
            {
                _imageCache.UpdateCache(index, bitmap, false, zoomLevel, currentBitmap);
            }

            return bitmap;
        }
    }
}
