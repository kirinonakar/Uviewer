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
            bool isSharpenEnabled,
            float upscaleFactor,
            int currentIndex,
            int totalCount)
        {
            string fileName = FileExplorerService.GetFormattedDisplayName(
                entry.DisplayName,
                entry.IsArchiveEntry,
                archivePath,
                webDavPath);

            // 샤프닝 업스케일이 적용된 비트맵이 표시 중이면 "원래 크기 (업스케일 크기)" 형식으로 표시
            bool isUpscaled = isSharpenEnabled && !isPdfMode && upscaleFactor > 1.0f;

            string imageInfo = TryGetBitmapSize(bitmap, out var width, out var height)
                ? FormatImageSize(width, height, isUpscaled, upscaleFactor)
                : string.Empty;

            string imageIndex = isSideBySide && !isPdfMode
                ? $"{(currentIndex / 2) + 1} / {(totalCount + 1) / 2} (B)"
                : $"{currentIndex + 1} / {totalCount}";

            return new ImageStatusBarContent(fileName, imageInfo, imageIndex, string.Empty);
        }

        private static string FormatImageSize(double width, double height, bool isUpscaled, float upscaleFactor)
        {
            if (isUpscaled)
            {
                int originalWidth = (int)Math.Round(width / upscaleFactor);
                int originalHeight = (int)Math.Round(height / upscaleFactor);
                return $"{originalWidth} × {originalHeight} ({(int)width} × {(int)height})";
            }

            return $"{(int)width} × {(int)height}";
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
