using System;
using System.Collections.Generic;
using System.Threading;

namespace Uviewer.Models
{
    public sealed class TextReaderState : IDisposable
    {
        public List<TextLine> Lines { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public int TotalLineCountInSource { get; set; }
        public bool LinesFullyLoaded { get; set; }
        public string? FilePath { get; set; }
        public string? ArchiveEntryKey { get; set; }
        public int LastRecentSaveLine { get; set; } = -1;

        public double CalculatedTotalHeight { get; set; }
        public bool IsPageCalculationCompleted { get; set; }
        public int[]? LinePages { get; set; }
        public int TotalPages { get; set; }

        public CancellationTokenSource? GlobalCts { get; private set; }
        public CancellationTokenSource? PageCalcCts { get; private set; }

        public bool HasContent => !string.IsNullOrEmpty(Content);
        public bool IsArchiveEntry => ArchiveEntryKey != null;

        public void SetLocalSource(string filePath)
        {
            FilePath = filePath;
            ArchiveEntryKey = null;
        }

        public void SetArchiveSource(string? archiveEntryKey)
        {
            FilePath = null;
            ArchiveEntryKey = archiveEntryKey;
        }

        public CancellationToken RestartGlobalLoad()
        {
            GlobalCts?.Cancel();
            GlobalCts = new CancellationTokenSource();
            return GlobalCts.Token;
        }

        public void CancelGlobalLoad()
        {
            GlobalCts?.Cancel();
        }

        public CancellationToken RestartPageCalculation()
        {
            PageCalcCts?.Cancel();
            PageCalcCts = new CancellationTokenSource();
            ResetPageCalculation();
            return PageCalcCts.Token;
        }

        public void CancelPageCalculation()
        {
            PageCalcCts?.Cancel();
        }

        public void ResetPageCalculation()
        {
            CalculatedTotalHeight = 0;
            IsPageCalculationCompleted = false;
            TotalPages = 0;
            LinePages = null;
        }

        public void CompletePageCalculation(PaginationResult result)
        {
            LinePages = result.Pages;
            TotalPages = result.TotalPages;
            CalculatedTotalHeight = result.TotalHeight;
            IsPageCalculationCompleted = true;
        }

        public void CompletePageCalculationFallback(double totalHeight)
        {
            CalculatedTotalHeight = totalHeight;
            IsPageCalculationCompleted = true;
        }

        public void ResetRecentSaveLine()
        {
            LastRecentSaveLine = -1;
        }

        public void ClearDocument()
        {
            FilePath = null;
            ArchiveEntryKey = null;
            Content = string.Empty;
            Lines.Clear();
            TotalLineCountInSource = 0;
            LinesFullyLoaded = false;
            ResetRecentSaveLine();
            ResetPageCalculation();
        }

        public void Dispose()
        {
            GlobalCts?.Cancel();
            GlobalCts?.Dispose();
            PageCalcCts?.Cancel();
            PageCalcCts?.Dispose();
        }
    }
}
