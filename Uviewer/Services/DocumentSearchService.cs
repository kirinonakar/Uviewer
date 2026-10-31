using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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
        public int MatchIndex { get; init; } = -1;
        public int MatchStart { get; init; } = -1;
        public int MatchLength { get; init; }
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
        private PdfTextCache _pdfCache = new();

        private sealed record PdfPageText(string MappedText, string FallbackText);
        private sealed record PdfCacheKey(string Path, long Length, long ModifiedTicks, string PasswordHash);

        private sealed class PdfTextCache
        {
            public readonly SemaphoreSlim Gate = new(1);
            public readonly Dictionary<int, PdfPageText> Pages = new();
            public PdfCacheKey? Key;
            public int PageCount;
            public long Bytes;

            public void Add(int index, PdfPageText text)
            {
                long bytes = 128L + ((long)text.MappedText.Length + text.FallbackText.Length) * sizeof(char);
                // Keep a stable working set: LRU eviction would thrash on every full-document scan.
                if (Pages.Count >= 2048 || Bytes + bytes > 16 * 1024 * 1024) return;
                Pages.Add(index, text);
                Bytes += bytes;
            }
        }

        private string? _textCacheKey;
        private List<SearchableLine> _textCache = new();

        private string? _epubCacheKey;
        private List<SearchableLine> _epubCache = new();

        private string? _aozoraCacheKey;
        private List<SearchableLine> _aozoraCache = new();

        public IReadOnlyList<DocumentSearchMatch> SearchText(string cacheKey, string content, string query)
        {
            if (!string.Equals(_textCacheKey, cacheKey, StringComparison.Ordinal))
            {
                _textCacheKey = cacheKey;
                _textCache = BuildTextLines(content);
            }

            return FindMatches(_textCache, query);
        }

        public IReadOnlyList<DocumentSearchMatch> SearchAozoraBlocks(
            string cacheKey,
            IReadOnlyList<AozoraBindingModel> blocks,
            string query)
        {
            if (!string.Equals(_aozoraCacheKey, cacheKey, StringComparison.Ordinal))
            {
                _aozoraCacheKey = cacheKey;
                _aozoraCache = BuildAozoraBlockLines(blocks, DocumentSearchKind.Text, epubChapterIndex: -1);
            }

            return FindMatches(_aozoraCache, query);
        }

        public async Task<IReadOnlyList<DocumentSearchMatch>> SearchPdfAsync(
            string pdfPath,
            string? password,
            string query,
            CancellationToken token)
        {
            var cache = Volatile.Read(ref _pdfCache);
            return await Task.Run(() =>
            {
                cache.Gate.Wait(token);
                try { return FindPdfMatchesByPageMap(cache, pdfPath, password, query, token); }
                finally { cache.Gate.Release(); }
            }, token);
        }

        public async Task<IReadOnlyList<DocumentSearchMatch>> SearchEpubAsync(
            string cacheKey,
            EpubSession session,
            EpubDocumentService documentService,
            string query,
            CancellationToken token)
        {
            if (!string.Equals(_epubCacheKey, cacheKey, StringComparison.Ordinal))
            {
                _epubCacheKey = cacheKey;
                _epubCache = await BuildEpubLinesAsync(session, documentService, token);
            }

            return FindMatches(_epubCache, query);
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

        public void ClearPdfCache() => Interlocked.Exchange(ref _pdfCache, new PdfTextCache());

        public void Clear()
        {
            ClearPdfCache();
            _textCacheKey = null;
            _textCache.Clear();
            _epubCacheKey = null;
            _epubCache.Clear();
            _aozoraCacheKey = null;
            _aozoraCache.Clear();
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

        private static List<DocumentSearchMatch> FindPdfMatchesByPageMap(
            PdfTextCache cache,
            string pdfPath,
            string? password,
            string query,
            CancellationToken token)
        {
            var result = new List<DocumentSearchMatch>();
            if (string.IsNullOrWhiteSpace(query)) return result;

            token.ThrowIfCancellationRequested();
            var info = new FileInfo(pdfPath);
            var key = new PdfCacheKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty))));
            if (cache.Key != key)
            {
                cache.Pages.Clear();
                cache.Bytes = 0;
                cache.PageCount = 0;
                cache.Key = key;
            }

            PdfDocument? document = null;
            try
            {
                if (cache.PageCount == 0)
                {
                    document = PdfPigDocumentFactory.Open(pdfPath, password);
                    cache.PageCount = document.NumberOfPages;
                }
                string normalizedQuery = SearchHighlightService.CollapseWhitespace(query);
                string compactQuery = SearchHighlightService.RemoveWhitespace(normalizedQuery);
                for (int pageIndex = 0; pageIndex < cache.PageCount; pageIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!cache.Pages.TryGetValue(pageIndex, out var text))
                    {
                        document ??= PdfPigDocumentFactory.Open(pdfPath, password);
                        var page = document.GetPage(pageIndex + 1);
                        string mapped = SearchHighlightService.ExtractPdfMappedText(page);
                        text = new PdfPageText(mapped, SearchHighlightService.ExtractPdfPageText(page, mapped));
                        token.ThrowIfCancellationRequested();
                        cache.Add(pageIndex, text);
                    }
                    string mappedText = text.MappedText;
                    var ranges = SearchHighlightService.FindPdfTextRanges(mappedText, query);
                    if (ranges.Count > 0)
                    {
                        for (int matchIndex = 0; matchIndex < ranges.Count; matchIndex++)
                        {
                            token.ThrowIfCancellationRequested();
                            var range = ranges[matchIndex];
                            result.Add(new DocumentSearchMatch
                            {
                                Kind = DocumentSearchKind.Pdf,
                                PageIndex = pageIndex,
                                LineNumber = pageIndex + 1,
                                MatchIndex = matchIndex,
                                MatchStart = range.Start,
                                MatchLength = range.Length,
                                SortKey = pageIndex + 1,
                                Preview = CreatePreview(mappedText, range, query)
                            });
                        }

                        continue;
                    }

                    string fallbackText = text.FallbackText;
                    if (string.IsNullOrWhiteSpace(fallbackText))
                    {
                        continue;
                    }

                    if (!SearchHighlightService.ContainsSearchText(fallbackText, normalizedQuery, compactQuery, allowCompactFallback: true))
                    {
                        continue;
                    }

                    var fallbackRanges = SearchHighlightService.FindTextRanges(fallbackText, normalizedQuery, allowCompactFallback: true);
                    result.Add(new DocumentSearchMatch
                    {
                        Kind = DocumentSearchKind.Pdf,
                        PageIndex = pageIndex,
                        LineNumber = pageIndex + 1,
                        MatchIndex = 0,
                        MatchStart = fallbackRanges.Count == 0 ? -1 : fallbackRanges[0].Start,
                        MatchLength = fallbackRanges.Count == 0 ? 0 : fallbackRanges[0].Length,
                        SortKey = pageIndex + 1,
                        Preview = fallbackRanges.Count == 0
                            ? CreatePreview(fallbackText)
                            : CreatePreview(fallbackText, fallbackRanges[0], query)
                    });
                }
            }
            finally
            {
                document?.Dispose();
            }

            return result;
        }

        private static async Task<List<SearchableLine>> BuildEpubLinesAsync(
            EpubSession session,
            EpubDocumentService documentService,
            CancellationToken token)
        {
            var result = new List<SearchableLine>();

            for (int chapterIndex = 0; chapterIndex < session.Spine.Count; chapterIndex++)
            {
                token.ThrowIfCancellationRequested();

                string path = session.Spine[chapterIndex];
                string? html = await session.ReadEntryTextAsync(path, documentService);
                if (string.IsNullOrEmpty(html)) continue;

                var parseResult = documentService.ParseHtmlToAozoraBlocks(html, path, chapterIndex);
                foreach (var line in BuildAozoraBlockLines(parseResult.Blocks, DocumentSearchKind.Epub, chapterIndex))
                {
                    token.ThrowIfCancellationRequested();
                    result.Add(line);
                }
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
                foreach (var line in BuildAozoraBlockLines(parseResult.Blocks, DocumentSearchKind.Epub, chapterIndex))
                {
                    token.ThrowIfCancellationRequested();
                    result.Add(line);
                }
            }

            return result;
        }

        private static List<SearchableLine> BuildAozoraBlockLines(
            IReadOnlyList<AozoraBindingModel> blocks,
            DocumentSearchKind kind,
            int epubChapterIndex)
        {
            var result = new List<SearchableLine>(blocks.Count);

            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                var block = blocks[blockIndex];
                if (block.HasImage) continue;

                string text = GetBlockText(block);
                if (string.IsNullOrWhiteSpace(text)) continue;

                int lineNumber = Math.Max(1, block.SourceLineNumber);
                result.Add(new SearchableLine
                {
                    Kind = kind,
                    EpubChapterIndex = epubChapterIndex,
                    BlockIndex = blockIndex,
                    LineNumber = lineNumber,
                    SortKey = kind == DocumentSearchKind.Epub
                        ? CreateEpubSortKey(epubChapterIndex, lineNumber)
                        : lineNumber,
                    Text = text
                });
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
                var ranges = SearchHighlightService.FindTextRanges(line.Text, trimmedQuery, allowCompactFallback);
                if (ranges.Count == 0) continue;

                string previewSource = line.PreviewText ?? line.Text;
                IReadOnlyList<TextSearchRange> previewRanges = Array.Empty<TextSearchRange>();
                if (line.PreviewText != null)
                {
                    previewRanges = SearchHighlightService.FindTextRanges(previewSource, trimmedQuery, allowCompactFallback);
                }

                for (int matchIndex = 0; matchIndex < ranges.Count; matchIndex++)
                {
                    var range = ranges[matchIndex];
                    var previewRange = previewRanges.Count > matchIndex ? previewRanges[matchIndex] : range;

                    matches.Add(new DocumentSearchMatch
                    {
                        Kind = line.Kind,
                        LineNumber = line.LineNumber,
                        PageIndex = line.PageIndex,
                        EpubChapterIndex = line.EpubChapterIndex,
                        BlockIndex = line.BlockIndex,
                        MatchIndex = matchIndex,
                        MatchStart = range.Start,
                        MatchLength = range.Length,
                        SortKey = line.SortKey,
                        Preview = CreatePreview(previewSource, previewRange, trimmedQuery)
                    });
                }
            }

            return matches;
        }

        public static string GetBlockText(AozoraBindingModel block)
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
                    case AozoraStyledText styled:
                        sb.Append(styled.Text);
                        break;
                    case AozoraTCY tcy:
                        sb.Append(tcy.Text);
                        break;
                    case AozoraLineBreak:
                        sb.Append(' ');
                        break;
                    case AozoraImage when block.IsMarkdown:
                        sb.Append('\uFFFC'); // Keep text offsets aligned with native inline image objects.
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

        private static string CreatePreview(string text, TextSearchRange range, string? query)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            const int contextLength = 55;
            int textLength = text.Length;
            int matchStart = Math.Clamp(range.Start, 0, textLength);
            int matchEnd = Math.Clamp(range.Start + Math.Max(1, range.Length), matchStart, textLength);
            int previewStart = Math.Max(0, matchStart - contextLength);
            int previewEnd = Math.Min(textLength, matchEnd + contextLength);

            string preview = text.Substring(previewStart, previewEnd - previewStart);
            int localMatchStart = matchStart - previewStart;
            int localMatchEnd = Math.Min(preview.Length, matchEnd - previewStart);
            if (!string.IsNullOrWhiteSpace(query) && localMatchStart < localMatchEnd)
            {
                string prefix = preview.Substring(0, localMatchStart);
                string matchedText = preview.Substring(localMatchStart, localMatchEnd - localMatchStart);
                string suffix = preview.Substring(localMatchEnd);
                preview = prefix + NormalizePreviewMatch(matchedText, query) + suffix;
            }

            preview = SearchHighlightService.CollapseWhitespace(preview);
            if (previewStart > 0) preview = "..." + preview;
            if (previewEnd < textLength) preview += "...";
            return preview;
        }

        private static string NormalizePreviewMatch(string matchedText, string query)
        {
            string normalizedMatch = SearchHighlightService.CollapseWhitespace(matchedText);
            string normalizedQuery = SearchHighlightService.CollapseWhitespace(query);
            string compactMatch = SearchHighlightService.RemoveWhitespace(normalizedMatch);
            string compactQuery = SearchHighlightService.RemoveWhitespace(normalizedQuery);
            bool matchContainsInsertedWhitespace = compactMatch.Length != normalizedMatch.Length;

            return matchContainsInsertedWhitespace &&
                compactQuery.Length > 0 &&
                string.Equals(compactMatch, compactQuery, StringComparison.CurrentCultureIgnoreCase)
                    ? normalizedQuery
                    : matchedText;
        }
    }
}
