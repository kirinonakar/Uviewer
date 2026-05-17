using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
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
        public string? PreviewText { get; init; }
    }

    public sealed class DocumentSearchService
    {
        private const long EpubChapterSortStride = 1_000_000L;

        private string? _textCacheKey;
        private List<SearchableLine> _textCache = new();

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
            return await Task.Run(() => FindPdfMatchesByPageMap(pdfPath, query, token), token);
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

        private static List<DocumentSearchMatch> FindPdfMatchesByPageMap(string pdfPath, string query, CancellationToken token)
        {
            var result = new List<DocumentSearchMatch>();

            using var document = PdfDocument.Open(pdfPath);
            int pageIndex = 0;
            foreach (var page in document.GetPages())
            {
                token.ThrowIfCancellationRequested();
                int matchCount = SearchHighlightService.CountPdfPageSearchMatches(page, query);
                if (matchCount <= 0)
                {
                    pageIndex++;
                    continue;
                }

                string previewText = SearchHighlightService.ExtractPdfPreviewText(page);
                if (string.IsNullOrWhiteSpace(previewText))
                {
                    previewText = SearchHighlightService.ExtractPdfPageText(page);
                }

                for (int matchIndex = 0; matchIndex < matchCount; matchIndex++)
                {
                    result.Add(new DocumentSearchMatch
                    {
                        Kind = DocumentSearchKind.Pdf,
                        PageIndex = pageIndex,
                        LineNumber = pageIndex + 1,
                        SortKey = pageIndex + 1,
                        Preview = CreatePreview(previewText)
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

            string trimmedQuery = SearchHighlightService.CollapseWhitespace(query);
            string compactQuery = SearchHighlightService.RemoveWhitespace(trimmedQuery);
            bool queryContainsWhitespace = compactQuery.Length > 0 && compactQuery.Length != trimmedQuery.Length;
            var matches = new List<DocumentSearchMatch>();

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line.Text)) continue;
                bool allowCompactFallback = queryContainsWhitespace || line.Kind == DocumentSearchKind.Pdf;
                if (!SearchHighlightService.ContainsSearchText(line.Text, trimmedQuery, compactQuery, allowCompactFallback)) continue;

                matches.Add(new DocumentSearchMatch
                {
                    Kind = line.Kind,
                    LineNumber = line.LineNumber,
                    PageIndex = line.PageIndex,
                    EpubChapterIndex = line.EpubChapterIndex,
                    BlockIndex = line.BlockIndex,
                    SortKey = line.SortKey,
                    Preview = CreatePreview(line.PreviewText ?? line.Text)
                });
            }

            return matches;
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

            return SearchHighlightService.CollapseWhitespace(sb.ToString());
        }

        private static string CreatePreview(string text)
        {
            text = SearchHighlightService.CollapseWhitespace(text);
            const int maxLength = 120;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "...";
        }
    }
}
