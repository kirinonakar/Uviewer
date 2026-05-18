using System;
using System.Collections.Generic;
using System.Threading;
using Uviewer.Services;

namespace Uviewer.Models
{
    public sealed class DocumentSearchState : IDisposable
    {
        public string? Query { get; private set; }
        public DocumentSearchMatch? ActiveMatch { get; private set; }
        public IReadOnlyList<PdfSearchHighlight> PdfHighlights { get; private set; } = Array.Empty<PdfSearchHighlight>();
        public int PdfPageIndex { get; private set; } = -1;
        public int PdfMatchIndex { get; private set; } = -1;
        public CancellationTokenSource? PdfHighlightCts { get; private set; }

        public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

        public void SetQuery(string? query)
        {
            Query = string.IsNullOrWhiteSpace(query) ? null : query;
            ActiveMatch = null;
            ClearPdfHighlights();
        }

        public void SetActiveMatch(DocumentSearchMatch match)
        {
            ActiveMatch = match;
        }

        public void SetPdfMatchIndex(int matchIndex)
        {
            PdfMatchIndex = matchIndex;
        }

        public void SetPdfHighlights(
            IReadOnlyList<PdfSearchHighlight> highlights,
            int pageIndex,
            int matchIndex)
        {
            PdfHighlights = highlights;
            PdfPageIndex = pageIndex;
            PdfMatchIndex = matchIndex;
        }

        public CancellationToken RestartPdfHighlightSearch()
        {
            PdfHighlightCts?.Cancel();
            PdfHighlightCts?.Dispose();
            PdfHighlightCts = new CancellationTokenSource();
            return PdfHighlightCts.Token;
        }

        public void ClearPdfHighlights()
        {
            PdfHighlights = Array.Empty<PdfSearchHighlight>();
            PdfPageIndex = -1;
            PdfMatchIndex = -1;
        }

        public DocumentSearchMatch? GetActiveMatchFor(
            DocumentSearchKind kind,
            int currentEpubChapterIndex)
        {
            if (ActiveMatch == null || ActiveMatch.Kind != kind)
            {
                return null;
            }

            if (kind == DocumentSearchKind.Epub &&
                ActiveMatch.EpubChapterIndex != currentEpubChapterIndex)
            {
                return null;
            }

            return ActiveMatch;
        }

        public int GetCurrentRangeIndex(
            DocumentSearchKind kind,
            int lineNumber,
            int blockIndex,
            IReadOnlyList<TextSearchRange> ranges,
            int currentEpubChapterIndex)
        {
            var match = GetActiveMatchFor(kind, currentEpubChapterIndex);
            if (match == null || ranges.Count == 0) return -1;

            if (blockIndex >= 0 && match.BlockIndex >= 0)
            {
                if (blockIndex != match.BlockIndex) return -1;
            }
            else if (match.LineNumber != lineNumber)
            {
                return -1;
            }

            if (match.MatchIndex >= 0 && match.MatchIndex < ranges.Count)
            {
                return match.MatchIndex;
            }

            if (match.MatchStart >= 0)
            {
                for (int i = 0; i < ranges.Count; i++)
                {
                    if (ranges[i].Start == match.MatchStart && ranges[i].Length == match.MatchLength)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        public void Dispose()
        {
            PdfHighlightCts?.Cancel();
            PdfHighlightCts?.Dispose();
        }
    }
}
