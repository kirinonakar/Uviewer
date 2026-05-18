using System.Collections.Generic;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextDocumentSearchService
    {
        private readonly DocumentSearchService _documentSearchService;

        public TextDocumentSearchService(DocumentSearchService documentSearchService)
        {
            _documentSearchService = documentSearchService;
        }

        public IReadOnlyList<DocumentSearchMatch> Search(
            TextReaderState state,
            bool isAozoraMode,
            IReadOnlyList<AozoraBindingModel> aozoraBlocks,
            bool isMarkdownRenderMode,
            string encodingName,
            string query)
        {
            if (isAozoraMode && aozoraBlocks.Count > 0)
            {
                string aozoraCacheKey = CreateAozoraCacheKey(
                    state,
                    aozoraBlocks.Count,
                    isMarkdownRenderMode);

                return _documentSearchService.SearchAozoraBlocks(
                    aozoraCacheKey,
                    aozoraBlocks,
                    query);
            }

            string cacheKey = CreateTextCacheKey(state, encodingName);
            return _documentSearchService.SearchText(cacheKey, state.Content, query);
        }

        private static string CreateAozoraCacheKey(
            TextReaderState state,
            int blockCount,
            bool isMarkdownRenderMode)
        {
            return $"aozora:{GetSourceKey(state)}:{state.Content.Length}:{blockCount}:{isMarkdownRenderMode}";
        }

        private static string CreateTextCacheKey(TextReaderState state, string encodingName)
        {
            return $"text:{GetSourceKey(state)}:{encodingName}:{state.Content.Length}";
        }

        private static string GetSourceKey(TextReaderState state)
        {
            return state.FilePath ?? state.ArchiveEntryKey ?? string.Empty;
        }
    }
}
