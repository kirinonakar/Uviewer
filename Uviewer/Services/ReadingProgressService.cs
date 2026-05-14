using System;

namespace Uviewer.Services
{
    public sealed class ReadingProgressService
    {
        public double CalculateLineProgress(int currentLine, int totalLines, bool isAtEnd = false)
        {
            if (isAtEnd) return 100.0;

            int safeTotal = Math.Max(1, totalLines);
            int safeCurrent = Math.Clamp(currentLine, 1, safeTotal);

            if (safeTotal <= 1) return 100.0;

            return ClampPercent((double)(safeCurrent - 1) / (safeTotal - 1) * 100.0);
        }

        public double CalculateEpubProgress(int chapterIndex, int chapterCount, int currentPageOneBased, int totalPages)
        {
            if (chapterCount <= 0) return 0.0;

            int safeTotalPages = Math.Max(1, totalPages);
            int safeChapter = Math.Clamp(chapterIndex, 0, chapterCount - 1);
            int safeCurrentPage = Math.Clamp(currentPageOneBased, 1, safeTotalPages);

            double chapterProgress = (double)safeChapter / chapterCount;
            double pageProgressInChapter = (double)(safeCurrentPage - 1) / safeTotalPages / chapterCount;
            return ClampPercent((chapterProgress + pageProgressInChapter) * 100.0);
        }

        public int ClampPage(int page, int totalPages)
        {
            if (totalPages < 1) return 1;
            return Math.Clamp(page, 1, totalPages);
        }

        public string FormatPercent(double value)
        {
            return $"{ClampPercent(value):F1}%";
        }

        private static double ClampPercent(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }
}
