using Microsoft.UI.Xaml.Controls;
using System;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed record TextStatusBarContent(
        string? FileName,
        string LineInfo,
        string ProgressText,
        string PageInfo,
        int CurrentLine);

    public sealed class TextStatusBarService
    {
        private readonly ReadingProgressService _readingProgressService;

        public TextStatusBarService(ReadingProgressService readingProgressService)
        {
            _readingProgressService = readingProgressService;
        }

        public TextStatusBarContent Create(
            string? fileName,
            bool isArchiveEntry,
            int? totalLines,
            TextReaderState state,
            ScrollViewer scrollViewer,
            int currentLine)
        {
            string? formattedFileName = fileName != null
                ? FileExplorerService.GetFormattedDisplayName(fileName, isArchiveEntry)
                : null;

            int total = totalLines ?? state.Lines.Count;
            if (total == 0) total = 1;

            bool isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 10.0;
            if (isAtBottom) currentLine = total;
            if (currentLine > total) currentLine = total;
            if (currentLine < 1) currentLine = 1;

            double progress = _readingProgressService.CalculateLineProgress(currentLine, total, isAtBottom);

            return new TextStatusBarContent(
                formattedFileName,
                Strings.LineInfo(currentLine, total),
                _readingProgressService.FormatPercent(progress),
                CreatePageInfo(state, currentLine),
                currentLine);
        }

        public TextStatusBarContent CreatePagedReader(
            ReaderPageState pageState,
            int currentLine,
            int totalLines)
        {
            int total = Math.Max(1, totalLines);
            int safeCurrentLine = Math.Clamp(currentLine, 1, total);
            double progress = _readingProgressService.CalculateLineProgress(safeCurrentLine, total);

            return new TextStatusBarContent(
                null,
                Strings.LineInfo(safeCurrentLine, total),
                _readingProgressService.FormatPercent(progress),
                CreateReaderPageInfo(pageState),
                safeCurrentLine);
        }

        public TextStatusBarContent CreateEpub(
            int chapterIndex,
            int chapterCount,
            int currentPageIndex,
            int totalPages,
            EpubWin2DPage? page)
        {
            int safeTotalPages = Math.Max(1, totalPages);
            int currentPageOneBased = Math.Clamp(currentPageIndex + 1, 1, safeTotalPages);
            int currentLine = Math.Max(1, page?.StartLine ?? 1);
            int totalLines = Math.Max(1, page?.TotalLinesInChapter ?? 1);

            double progress = _readingProgressService.CalculateEpubProgress(
                chapterIndex,
                chapterCount,
                currentPageOneBased,
                safeTotalPages);

            return new TextStatusBarContent(
                null,
                Strings.LineInfo(currentLine, totalLines),
                _readingProgressService.FormatPercent(progress),
                $"{currentPageOneBased} / {safeTotalPages} (Ch.{chapterIndex + 1})",
                currentLine);
        }

        private string CreatePageInfo(TextReaderState state, int currentLine)
        {
            if (state.IsPageCalculationCompleted && state.LinePages != null && state.TotalPages > 0)
            {
                int lineIndex = Math.Clamp(currentLine - 1, 0, state.LinePages.Length - 1);
                int totalPages = Math.Max(1, state.TotalPages);
                int currentPage = _readingProgressService.ClampPage(state.LinePages[lineIndex], totalPages);
                return $"{currentPage} / {totalPages}";
            }

            if (!state.IsPageCalculationCompleted)
            {
                return Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
            }

            return string.Empty;
        }

        private string CreateReaderPageInfo(ReaderPageState pageState)
        {
            if (!pageState.IsPageCalculationCompleted)
            {
                return Strings.CalculatingPages.Trim().Replace("(", "").Replace(")", "");
            }

            pageState.SyncCalculatedCurrentPageFromMap();
            int totalPages = Math.Max(1, pageState.TotalPages);
            int currentPage = _readingProgressService.ClampPage(pageState.CalculatedCurrentPage, totalPages);
            return $"{currentPage} / {totalPages}";
        }
    }
}
