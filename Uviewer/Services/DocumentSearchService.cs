using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Uviewer.Models;

namespace Uviewer.Services
{
    public enum DocumentSearchKind
    {
        Text,
        Epub,
        Pdf
    }

    public sealed class DocumentSearchMatch
    {
        public DocumentSearchKind Kind { get; init; }
        public int LineNumber { get; init; }
        public int PageIndex { get; init; } = -1;
        public int EpubChapterIndex { get; init; } = -1;
        public int BlockIndex { get; init; } = -1;
        public long SortKey { get; init; }
        public string Preview { get; init; } = string.Empty;
    }

    internal sealed class SearchableLine
    {
        public DocumentSearchKind Kind { get; init; }
        public int LineNumber { get; init; }
        public int PageIndex { get; init; } = -1;
        public int EpubChapterIndex { get; init; } = -1;
        public int BlockIndex { get; init; } = -1;
        public long SortKey { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    public sealed class DocumentSearchService
    {
        private const long EpubChapterSortStride = 1_000_000L;

        private readonly CompareInfo _compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        private const CompareOptions SearchOptions =
            CompareOptions.IgnoreCase |
            CompareOptions.IgnoreNonSpace |
            CompareOptions.IgnoreKanaType |
            CompareOptions.IgnoreWidth;

        private string? _textCacheKey;
        private List<SearchableLine> _textCache = new();

        private string? _pdfCacheKey;
        private List<SearchableLine> _pdfCache = new();

        private string? _epubCacheKey;
        private List<SearchableLine> _epubCache = new();

        public IReadOnlyList<DocumentSearchMatch> SearchText(string cacheKey, string content, string query)
        {
            if (!string.Equals(_textCacheKey, cacheKey, StringComparison.Ordinal))
            {
                _textCacheKey = cacheKey;
                _textCache = BuildTextLines(content);
            }

            return FindMatches(_textCache, query);
        }

        public async Task<IReadOnlyList<DocumentSearchMatch>> SearchPdfAsync(string pdfPath, string query, CancellationToken token)
        {
            if (!string.Equals(_pdfCacheKey, pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                _pdfCacheKey = pdfPath;
                _pdfCache = await Task.Run(() => BuildPdfLines(pdfPath, token), token);
            }

            return FindMatches(_pdfCache, query);
        }

        public async Task<IReadOnlyList<DocumentSearchMatch>> SearchEpubAsync(
            string cacheKey,
            ZipArchive archive,
            IReadOnlyList<string> spine,
            SemaphoreSlim archiveLock,
            EpubDocumentService documentService,
            string query,
            CancellationToken token)
        {
            if (!string.Equals(_epubCacheKey, cacheKey, StringComparison.Ordinal))
            {
                _epubCacheKey = cacheKey;
                _epubCache = await BuildEpubLinesAsync(archive, spine, archiveLock, documentService, token);
            }

            return FindMatches(_epubCache, query);
        }

        public void Clear()
        {
            _textCacheKey = null;
            _textCache.Clear();
            _pdfCacheKey = null;
            _pdfCache.Clear();
            _epubCacheKey = null;
            _epubCache.Clear();
        }

        public static long CreateEpubSortKey(int chapterIndex, int lineNumber)
            => ((long)chapterIndex * EpubChapterSortStride) + Math.Max(1, lineNumber);

        private static List<SearchableLine> BuildTextLines(string content)
        {
            var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var result = new List<SearchableLine>(lines.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                result.Add(new SearchableLine
                {
                    Kind = DocumentSearchKind.Text,
                    LineNumber = i + 1,
                    SortKey = i + 1,
                    Text = lines[i]
                });
            }

            return result;
        }

        private static List<SearchableLine> BuildPdfLines(string pdfPath, CancellationToken token)
        {
            var result = new List<SearchableLine>();

            using var document = PdfDocument.Open(pdfPath);
            int pageIndex = 0;
            foreach (var page in document.GetPages())
            {
                token.ThrowIfCancellationRequested();
                string text = ExtractPdfPageText(page);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(new SearchableLine
                    {
                        Kind = DocumentSearchKind.Pdf,
                        PageIndex = pageIndex,
                        LineNumber = pageIndex + 1,
                        SortKey = pageIndex + 1,
                        Text = CollapseWhitespace(text)
                    });
                }

                pageIndex++;
            }

            return result;
        }

        private static async Task<List<SearchableLine>> BuildEpubLinesAsync(
            ZipArchive archive,
            IReadOnlyList<string> spine,
            SemaphoreSlim archiveLock,
            EpubDocumentService documentService,
            CancellationToken token)
        {
            var result = new List<SearchableLine>();

            for (int chapterIndex = 0; chapterIndex < spine.Count; chapterIndex++)
            {
                token.ThrowIfCancellationRequested();

                string path = spine[chapterIndex];
                string? html = await documentService.ReadEntryTextAsync(archive, path, archiveLock);
                if (string.IsNullOrEmpty(html)) continue;

                var parseResult = documentService.ParseHtmlToAozoraBlocks(html, path, chapterIndex);
                for (int blockIndex = 0; blockIndex < parseResult.Blocks.Count; blockIndex++)
                {
                    token.ThrowIfCancellationRequested();

                    var block = parseResult.Blocks[blockIndex];
                    if (block.HasImage) continue;

                    string text = GetBlockText(block);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    result.Add(new SearchableLine
                    {
                        Kind = DocumentSearchKind.Epub,
                        EpubChapterIndex = chapterIndex,
                        BlockIndex = blockIndex,
                        LineNumber = Math.Max(1, block.SourceLineNumber),
                        SortKey = CreateEpubSortKey(chapterIndex, block.SourceLineNumber),
                        Text = text
                    });
                }
            }

            return result;
        }

        private IReadOnlyList<DocumentSearchMatch> FindMatches(IReadOnlyList<SearchableLine> lines, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<DocumentSearchMatch>();

            string trimmedQuery = CollapseWhitespace(query);
            string compactQuery = RemoveWhitespace(trimmedQuery);
            bool queryContainsWhitespace = compactQuery.Length > 0 && compactQuery.Length != trimmedQuery.Length;
            var matches = new List<DocumentSearchMatch>();

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line.Text)) continue;
                bool allowCompactFallback = queryContainsWhitespace || line.Kind == DocumentSearchKind.Pdf;
                if (!ContainsSearchText(line.Text, trimmedQuery, compactQuery, allowCompactFallback)) continue;

                matches.Add(new DocumentSearchMatch
                {
                    Kind = line.Kind,
                    LineNumber = line.LineNumber,
                    PageIndex = line.PageIndex,
                    EpubChapterIndex = line.EpubChapterIndex,
                    BlockIndex = line.BlockIndex,
                    SortKey = line.SortKey,
                    Preview = CreatePreview(line.Text)
                });
            }

            return matches;
        }

        private bool ContainsSearchText(string text, string query, string compactQuery, bool allowCompactFallback)
        {
            if (_compareInfo.IndexOf(text, query, SearchOptions) >= 0) return true;
            if (!allowCompactFallback) return false;

            string compactText = RemoveWhitespace(text);
            return compactText.Length > 0 && _compareInfo.IndexOf(compactText, compactQuery, SearchOptions) >= 0;
        }

        private static string ExtractPdfPageText(UglyToad.PdfPig.Content.Page page)
        {
            var candidates = new List<string>();

            try
            {
                var words = page.GetWords();
                if (words != null)
                {
                    var wordText = string.Join(" ", words
                        .Select(w => (string?)w.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));

                    if (!string.IsNullOrWhiteSpace(wordText))
                    {
                        candidates.Add(CollapseWhitespace(wordText));
                    }
                }
            }
            catch
            {
            }

            string letterText = ExtractPdfLettersText(page);
            if (!string.IsNullOrWhiteSpace(letterText))
            {
                candidates.Add(letterText);
            }

            string rawText = CollapseWhitespace(page.Text ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                candidates.Add(rawText);
            }

            if (candidates.Count == 0) return string.Empty;

            return string.Join(" ", candidates
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(text => text.Length));
        }

        private static string ExtractPdfLettersText(UglyToad.PdfPig.Content.Page page)
        {
            try
            {
                var letters = page.Letters
                    .Where(letter => !string.IsNullOrWhiteSpace(letter.Value))
                    .OrderByDescending(letter => letter.StartBaseLine.Y)
                    .ThenBy(letter => letter.StartBaseLine.X)
                    .ToList();

                if (letters.Count == 0) return string.Empty;

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

                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    var ordered = line
                        .OrderBy(letter => letter.StartBaseLine.X)
                        .ToList();

                    Letter? previous = null;
                    foreach (var letter in ordered)
                    {
                        if (previous != null && ShouldInsertPdfSpace(previous, letter))
                        {
                            sb.Append(' ');
                        }

                        sb.Append(letter.Value);
                        previous = letter;
                    }

                    sb.Append(' ');
                }

                return CollapseWhitespace(sb.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ShouldInsertPdfSpace(Letter previous, Letter current)
        {
            double gap = current.StartBaseLine.X - previous.EndBaseLine.X;
            if (gap <= 0) return false;

            double previousWidth = Math.Max(0.1, previous.Width);
            double threshold = Math.Max(1.5, Math.Max(previousWidth * 0.45, previous.FontSize * 0.18));
            return gap > threshold;
        }

        private static string GetBlockText(AozoraBindingModel block)
        {
            var sb = new StringBuilder();

            foreach (var inline in block.Inlines)
            {
                switch (inline)
                {
                    case string text:
                        sb.Append(text);
                        break;
                    case AozoraRuby ruby:
                        sb.Append(ruby.BaseText);
                        break;
                    case AozoraBold bold:
                        sb.Append(bold.Text);
                        break;
                    case AozoraItalic italic:
                        sb.Append(italic.Text);
                        break;
                    case AozoraCode code:
                        sb.Append(code.Text);
                        break;
                    case AozoraHighlight highlight:
                        sb.Append(highlight.Text);
                        break;
                    case AozoraMath math:
                        sb.Append(KatexStandaloneRenderer.RenderToText(math.Text));
                        break;
                    case AozoraTCY tcy:
                        sb.Append(tcy.Text);
                        break;
                    case AozoraLineBreak:
                        sb.Append(' ');
                        break;
                }
            }

            if (block.IsTable && block.TableRows.Count > 0)
            {
                foreach (var row in block.TableRows)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.AppendJoin(' ', row);
                }
            }

            return CollapseWhitespace(sb.ToString());
        }

        private static string CreatePreview(string text)
        {
            text = CollapseWhitespace(text);
            const int maxLength = 120;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "...";
        }

        private static string CollapseWhitespace(string text)
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

        private static string RemoveWhitespace(string text)
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
    }
}
