using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Windows.UI;

namespace Uviewer.Services
{
    public readonly record struct TextSearchRange(int Start, int Length);

    public readonly record struct PdfSearchHighlight(
        double Left,
        double Bottom,
        double Right,
        double Top,
        double PageWidth,
        double PageHeight);

    public sealed class SearchHighlightService
    {
        public static readonly Color HighlightColor = ColorHelper.FromArgb(125, 255, 224, 92);
        public static SolidColorBrush CreateHighlightBrush() => new(HighlightColor);

        private const char PdfLineSearchSeparator = '\uE000';
        private static readonly CompareInfo CompareInfo = CultureInfo.CurrentCulture.CompareInfo;
        private const CompareOptions SearchOptions =
            CompareOptions.IgnoreCase |
            CompareOptions.IgnoreNonSpace |
            CompareOptions.IgnoreKanaType |
            CompareOptions.IgnoreWidth;

        public IReadOnlyList<TextSearchRange> FindRanges(string text, string? query, bool allowCompactFallback = false)
            => FindTextRanges(text, query, allowCompactFallback);

        public async Task<IReadOnlyList<PdfSearchHighlight>> FindPdfHighlightsAsync(
            string pdfPath,
            int pageIndex,
            string? query,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0 || string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<PdfSearchHighlight>();
            }

            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                using var document = PdfDocument.Open(pdfPath);
                var page = document.GetPage(pageIndex + 1);
                var lineMaps = BuildPdfLineTextMaps(page);
                if (lineMaps.Count == 0) return (IReadOnlyList<PdfSearchHighlight>)Array.Empty<PdfSearchHighlight>();

                var highlights = new List<PdfSearchHighlight>();
                double pageWidth = Math.Max(1.0, page.Width);
                double pageHeight = Math.Max(1.0, page.Height);

                foreach (var lineMap in lineMaps)
                {
                    token.ThrowIfCancellationRequested();

                    var ranges = FindTextRanges(lineMap.Text, query, allowCompactFallback: true);
                    foreach (var range in ranges)
                    {
                        token.ThrowIfCancellationRequested();

                        var letters = new List<Letter>();
                        int end = Math.Min(lineMap.Letters.Count, range.Start + range.Length);
                        for (int i = range.Start; i < end; i++)
                        {
                            if (lineMap.Letters[i] != null)
                            {
                                letters.Add(lineMap.Letters[i]!);
                            }
                        }

                        if (letters.Count == 0) continue;

                        double left = letters.Min(letter => letter.GlyphRectangleLoose.Left);
                        double right = letters.Max(letter => letter.GlyphRectangleLoose.Right);
                        double bottom = letters.Min(letter => letter.GlyphRectangleLoose.Bottom);
                        double top = letters.Max(letter => letter.GlyphRectangleLoose.Top);

                        if (right <= left || top <= bottom) continue;
                        highlights.Add(new PdfSearchHighlight(left, bottom, right, top, pageWidth, pageHeight));
                    }
                }

                return highlights;
            }, token);
        }

        public static IReadOnlyList<TextSearchRange> FindTextRanges(
            string text,
            string? query,
            bool allowCompactFallback = false)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(query)) return Array.Empty<TextSearchRange>();

            string normalizedQuery = CollapseWhitespace(query);
            if (normalizedQuery.Length == 0) return Array.Empty<TextSearchRange>();

            var directRanges = FindDirectRanges(text, normalizedQuery);
            if (directRanges.Count > 0 || !allowCompactFallback) return directRanges;

            string compactQuery = RemoveWhitespace(normalizedQuery);
            if (compactQuery.Length == 0) return Array.Empty<TextSearchRange>();

            var compact = BuildCompactTextMap(text);
            if (compact.Text.Length == 0) return Array.Empty<TextSearchRange>();

            var result = new List<TextSearchRange>();
            int index = 0;
            while (index < compact.Text.Length)
            {
                int found = CompareInfo.IndexOf(compact.Text, compactQuery, index, SearchOptions);
                if (found < 0) break;

                int end = Math.Min(compact.OriginalIndexes.Count - 1, found + compactQuery.Length - 1);
                int originalStart = compact.OriginalIndexes[found];
                int originalEnd = compact.OriginalIndexes[end] + 1;
                result.Add(new TextSearchRange(originalStart, Math.Max(1, originalEnd - originalStart)));
                index = found + Math.Max(1, compactQuery.Length);
            }

            return result;
        }

        public static bool ContainsSearchText(string text, string query, string compactQuery, bool allowCompactFallback)
        {
            if (CompareInfo.IndexOf(text, query, SearchOptions) >= 0) return true;
            if (!allowCompactFallback || string.IsNullOrEmpty(compactQuery)) return false;

            string compactText = RemoveWhitespace(text);
            return compactText.Length > 0 && CompareInfo.IndexOf(compactText, compactQuery, SearchOptions) >= 0;
        }

        public static string ExtractPdfPageText(Page page)
        {
            var candidates = new List<string>();

            TryAddCandidate(candidates, ExtractPdfMappedText(page));
            TryAddCandidate(candidates, ExtractContentOrderText(page));
            TryAddCandidate(candidates, ExtractPdfWordsText(page));
            TryAddCandidate(candidates, page.Text ?? string.Empty);

            if (candidates.Count == 0) return string.Empty;

            return string.Join(" ", candidates
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(text => text.Length));
        }

        public static string ExtractPdfPreviewText(Page page)
        {
            var candidates = new List<string>();

            TryAddCandidate(candidates, ExtractPdfWordsText(page));
            TryAddCandidate(candidates, ExtractContentOrderText(page));
            TryAddCandidate(candidates, page.Text ?? string.Empty);

            return candidates.Count == 0
                ? string.Empty
                : candidates
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(CountWhitespace)
                    .ThenByDescending(text => text.Length)
                    .First();
        }

        public static string ExtractPdfMappedText(Page page)
        {
            try
            {
                var textMap = BuildPdfTextMap(page);
                return textMap.Text;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string ExtractPdfSearchText(Page page)
        {
            try
            {
                var lineMaps = BuildPdfLineTextMaps(page);
                return lineMaps.Count == 0
                    ? string.Empty
                    : string.Join(PdfLineSearchSeparator.ToString(), lineMaps.Select(line => line.Text));
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool PdfPageContainsSearchText(Page page, string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            string mappedText = ExtractPdfSearchText(page);
            if (!string.IsNullOrWhiteSpace(mappedText) &&
                FindTextRanges(mappedText, query, allowCompactFallback: true).Count > 0)
            {
                return true;
            }

            string fallbackText = ExtractPdfPageText(page);
            if (string.IsNullOrWhiteSpace(fallbackText)) return false;

            string normalizedQuery = CollapseWhitespace(query);
            string compactQuery = RemoveWhitespace(normalizedQuery);
            return ContainsSearchText(fallbackText, normalizedQuery, compactQuery, allowCompactFallback: true);
        }

        public static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            bool previousWhitespace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                    {
                        sb.Append(' ');
                        previousWhitespace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    previousWhitespace = false;
                }
            }

            return sb.ToString().Trim();
        }

        public static string RemoveWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static List<TextSearchRange> FindDirectRanges(string text, string query)
        {
            var result = new List<TextSearchRange>();
            int index = 0;
            while (index < text.Length)
            {
                int found = CompareInfo.IndexOf(text, query, index, SearchOptions);
                if (found < 0) break;

                result.Add(new TextSearchRange(found, Math.Min(query.Length, text.Length - found)));
                index = found + Math.Max(1, query.Length);
            }

            return result;
        }

        private static (string Text, List<int> OriginalIndexes) BuildCompactTextMap(string text)
        {
            var sb = new StringBuilder(text.Length);
            var map = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) continue;
                sb.Append(text[i]);
                map.Add(i);
            }

            return (sb.ToString(), map);
        }

        private static string ExtractContentOrderText(Page page)
        {
            try
            {
                var options = new ContentOrderTextExtractor.Options
                {
                    ReplaceWhitespaceWithSpace = true,
                    NegativeGapAsWhitespace = true,
                    SeparateParagraphsWithDoubleNewline = false
                };
                return ContentOrderTextExtractor.GetText(page, options);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractPdfWordsText(Page page)
        {
            try
            {
                return string.Join(" ", page.GetWords()
                    .Select(word => word.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractPdfLettersText(Page page)
        {
            try
            {
                return ExtractPdfMappedText(page);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static (string Text, List<Letter?> Letters) BuildPdfTextMap(Page page)
        {
            var lineMaps = BuildPdfLineTextMaps(page);
            if (lineMaps.Count == 0) return (string.Empty, new List<Letter?>());

            var sb = new StringBuilder();
            var map = new List<Letter?>();

            foreach (var lineMap in lineMaps)
            {
                if (lineMap.Text.Length == 0) continue;
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                    map.Add(null);
                }

                sb.Append(lineMap.Text);
                map.AddRange(lineMap.Letters);
            }

            return (sb.ToString(), map);
        }

        private static List<(string Text, List<Letter?> Letters)> BuildPdfLineTextMaps(Page page)
        {
            var lines = BuildPdfLetterLines(page);
            if (lines.Count == 0) return new List<(string Text, List<Letter?> Letters)>();

            var result = new List<(string Text, List<Letter?> Letters)>();

            foreach (var line in lines)
            {
                var sb = new StringBuilder();
                var map = new List<Letter?>();
                Letter? previous = null;

                foreach (var letter in line.OrderBy(letter => letter.StartBaseLine.X))
                {
                    if (previous != null && ShouldInsertPdfSpace(previous, letter))
                    {
                        sb.Append(' ');
                        map.Add(null);
                    }

                    foreach (char c in letter.Value)
                    {
                        sb.Append(c);
                        map.Add(letter);
                    }

                    previous = letter;
                }

                string text = CollapseWhitespaceWithMap(sb, map);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add((text, map));
                }
            }

            return result;
        }

        private static List<List<Letter>> BuildPdfLetterLines(Page page)
        {
            var letters = page.Letters
                .Where(letter => !string.IsNullOrWhiteSpace(letter.Value))
                .OrderByDescending(letter => letter.StartBaseLine.Y)
                .ThenBy(letter => letter.StartBaseLine.X)
                .ToList();

            if (letters.Count == 0) return new List<List<Letter>>();

            double averageFontSize = letters
                .Select(letter => Math.Max(1.0, letter.FontSize))
                .DefaultIfEmpty(12.0)
                .Average();

            double lineTolerance = Math.Max(2.0, averageFontSize * 0.55);
            var lines = new List<List<Letter>>();

            foreach (var letter in letters)
            {
                var line = lines.FirstOrDefault(existing =>
                    Math.Abs(existing[0].StartBaseLine.Y - letter.StartBaseLine.Y) <= lineTolerance);

                if (line == null)
                {
                    line = new List<Letter>();
                    lines.Add(line);
                }

                line.Add(letter);
            }

            return lines;
        }

        private static IReadOnlyList<IReadOnlyList<Letter>> GroupLettersByLine(IReadOnlyList<Letter> letters)
        {
            if (letters.Count == 0) return Array.Empty<IReadOnlyList<Letter>>();

            double averageFontSize = letters.Select(letter => Math.Max(1.0, letter.FontSize)).DefaultIfEmpty(12.0).Average();
            double lineTolerance = Math.Max(2.0, averageFontSize * 0.55);
            var lines = new List<List<Letter>>();

            foreach (var letter in letters.OrderByDescending(letter => letter.StartBaseLine.Y))
            {
                var line = lines.FirstOrDefault(existing =>
                    Math.Abs(existing[0].StartBaseLine.Y - letter.StartBaseLine.Y) <= lineTolerance);

                if (line == null)
                {
                    line = new List<Letter>();
                    lines.Add(line);
                }

                line.Add(letter);
            }

            return lines;
        }

        private static string CollapseWhitespaceWithMap(StringBuilder source, List<Letter?> map)
        {
            var output = new StringBuilder(source.Length);
            int write = 0;
            bool previousWhitespace = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c))
                {
                    if (previousWhitespace || output.Length == 0) continue;
                    output.Append(' ');
                    map[write++] = null;
                    previousWhitespace = true;
                }
                else
                {
                    output.Append(c);
                    map[write++] = map[i];
                    previousWhitespace = false;
                }
            }

            if (output.Length > 0 && output[^1] == ' ')
            {
                output.Length--;
                write--;
            }

            if (write < map.Count)
            {
                map.RemoveRange(write, map.Count - write);
            }

            return output.ToString();
        }

        private static bool ShouldInsertPdfSpace(Letter previous, Letter current)
        {
            double gap = current.StartBaseLine.X - previous.EndBaseLine.X;
            if (gap <= 0) return false;

            double previousWidth = Math.Max(0.1, previous.Width);
            double threshold = Math.Max(1.5, Math.Max(previousWidth * 0.45, previous.FontSize * 0.18));
            return gap > threshold;
        }

        private static void TryAddCandidate(List<string> candidates, string text)
        {
            string collapsed = CollapseWhitespace(text);
            if (!string.IsNullOrWhiteSpace(collapsed))
            {
                candidates.Add(collapsed);
            }
        }

        private static int CountWhitespace(string text)
        {
            int count = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) count++;
            }

            return count;
        }
    }
}
