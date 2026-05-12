using System;
using System.Collections.Generic;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class EpubPageFlowService
    {
        public IReadOnlyList<int> GetPreloadChapterIndices(
            int currentIndex,
            int chapterCount,
            int nextCount = 3,
            int previousCount = 1)
        {
            var indices = new List<int>();

            for (int i = 1; i <= nextCount; i++)
            {
                int next = currentIndex + i;
                if (next < chapterCount) indices.Add(next);
            }

            for (int i = 1; i <= previousCount; i++)
            {
                int previous = currentIndex - i;
                if (previous >= 0) indices.Add(previous);
            }

            return indices;
        }

        public IReadOnlyList<int> GetPreloadKeysToRemove(
            IEnumerable<int> cachedChapterIndices,
            int currentIndex,
            int nextCount = 3,
            int previousCount = 1)
        {
            int min = currentIndex - previousCount;
            int max = currentIndex + nextCount;

            return cachedChapterIndices
                .Where(index => index < min || index > max)
                .ToList();
        }

        public int FindTargetPage(
            IReadOnlyList<EpubWin2DPage> pages,
            int targetBlockIndex,
            int targetLine,
            int targetPage,
            double? progress,
            bool fromEnd)
        {
            if (pages.Count == 0) return 0;

            if (targetBlockIndex >= 0)
                return FindPageByBlockIndex(pages, targetBlockIndex);

            if (targetLine > 1)
                return FindPageByLine(pages, targetLine);

            if (targetPage > 0)
                return Math.Min(targetPage, pages.Count - 1);

            if (progress.HasValue)
                return (int)(Math.Max(0, pages.Count - 1) * progress.Value);

            if (fromEnd)
                return pages.Count - 1;

            return 0;
        }

        public bool ShouldShowImageSideBySide(
            EpubWin2DPage currentPage,
            EpubWin2DPage? nextPage,
            bool sideBySideMode,
            bool autoDoublePageForArchive,
            Func<string, EpubImageSize?> getImageSize,
            Func<double, double, bool> isTallCandidate)
        {
            if (!currentPage.IsImagePage || nextPage == null || !nextPage.IsImagePage)
                return false;

            bool canSideBySide = sideBySideMode;

            if (autoDoublePageForArchive)
            {
                var currentSize = getImageSize(currentPage.ImagePath);
                if (currentSize.HasValue)
                {
                    if (isTallCandidate(currentSize.Value.Width, currentSize.Value.Height))
                    {
                        canSideBySide = true;
                    }
                    else if (currentSize.Value.Width >= currentSize.Value.Height * 1.2 ||
                             currentSize.Value.Height > currentSize.Value.Width * 3.0)
                    {
                        canSideBySide = false;
                    }
                    else
                    {
                        canSideBySide = sideBySideMode;
                    }
                }
            }

            if (!canSideBySide) return false;

            if (autoDoublePageForArchive)
            {
                var nextSize = getImageSize(nextPage.ImagePath);
                if (nextSize.HasValue && !isTallCandidate(nextSize.Value.Width, nextSize.Value.Height))
                    return false;
            }

            return true;
        }

        private static int FindPageByBlockIndex(IReadOnlyList<EpubWin2DPage> pages, int targetBlockIndex)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (page.Blocks == null || page.Blocks.Count == 0) continue;

                int nextStart = i + 1 < pages.Count ? pages[i + 1].StartBlockIndex : int.MaxValue;
                if (targetBlockIndex >= page.StartBlockIndex && targetBlockIndex < nextStart)
                    return i;
            }

            return 0;
        }

        private static int FindPageByLine(IReadOnlyList<EpubWin2DPage> pages, int targetLine)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (page.Blocks == null || page.Blocks.Count == 0) continue;

                int pageStartLine = page.Blocks.First().SourceLineNumber;
                int nextStartLine = i + 1 < pages.Count && pages[i + 1].Blocks != null && pages[i + 1].Blocks.Count > 0
                    ? pages[i + 1].Blocks.First().SourceLineNumber
                    : int.MaxValue;

                if (targetLine >= pageStartLine && targetLine < nextStartLine)
                    return i;
            }

            return 0;
        }
    }

    public readonly struct EpubImageSize
    {
        public EpubImageSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }
    }
}
