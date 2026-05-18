using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed record SideBySideImagePair(
        ImageEntry LeftEntry,
        CanvasBitmap? LeftBitmap,
        ImageEntry RightEntry,
        CanvasBitmap? RightBitmap,
        bool NextImageOnRight)
    {
        public CanvasBitmap? PrimaryBitmap =>
            NextImageOnRight
                ? LeftBitmap ?? RightBitmap
                : RightBitmap ?? LeftBitmap;
    }

    public sealed class SideBySideImageLoadService
    {
        public async Task<SideBySideImagePair?> LoadAsync(
            IReadOnlyList<ImageEntry> entries,
            int currentIndex,
            bool nextImageOnRight,
            CanvasControl leftCanvas,
            CanvasControl rightCanvas,
            Func<ImageEntry, CanvasControl, CancellationToken, Task<CanvasBitmap?>> loadBitmapAsync,
            Action<CanvasBitmap?> releaseBitmap,
            CancellationToken token)
        {
            CanvasBitmap? leftBitmap = null;
            CanvasBitmap? rightBitmap = null;

            try
            {
                ImageEntry leftEntry;
                ImageEntry rightEntry;

                if (nextImageOnRight)
                {
                    leftEntry = entries[currentIndex];
                    leftBitmap = await loadBitmapAsync(leftEntry, leftCanvas, token);
                    if (ReleaseIfCanceled(token, releaseBitmap, leftBitmap)) return null;

                    if (currentIndex + 1 < entries.Count)
                    {
                        rightEntry = entries[currentIndex + 1];
                        rightBitmap = await loadBitmapAsync(rightEntry, rightCanvas, token);
                    }
                    else
                    {
                        rightEntry = leftEntry;
                    }
                }
                else
                {
                    if (currentIndex + 1 < entries.Count)
                    {
                        leftEntry = entries[currentIndex + 1];
                        leftBitmap = await loadBitmapAsync(leftEntry, leftCanvas, token);
                    }
                    else
                    {
                        leftEntry = entries[currentIndex];
                    }

                    if (ReleaseIfCanceled(token, releaseBitmap, leftBitmap)) return null;

                    rightEntry = entries[currentIndex];
                    rightBitmap = await loadBitmapAsync(rightEntry, rightCanvas, token);
                }

                if (token.IsCancellationRequested)
                {
                    releaseBitmap(leftBitmap);
                    releaseBitmap(rightBitmap);
                    return null;
                }

                return new SideBySideImagePair(leftEntry, leftBitmap, rightEntry, rightBitmap, nextImageOnRight);
            }
            catch
            {
                releaseBitmap(leftBitmap);
                releaseBitmap(rightBitmap);
                throw;
            }
        }

        private static bool ReleaseIfCanceled(
            CancellationToken token,
            Action<CanvasBitmap?> releaseBitmap,
            CanvasBitmap? bitmap)
        {
            if (!token.IsCancellationRequested)
            {
                return false;
            }

            releaseBitmap(bitmap);
            return true;
        }
    }
}
