using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextSearchHighlightPresenterService
    {
        private readonly SearchHighlightService _searchHighlightService;

        public TextSearchHighlightPresenterService(SearchHighlightService searchHighlightService)
        {
            _searchHighlightService = searchHighlightService;
        }

        public void ApplyToTextBlock(
            TextBlock textBlock,
            string content,
            int lineNumber,
            DocumentSearchState state,
            int currentEpubChapterIndex)
        {
            textBlock.TextHighlighters.Clear();
            var ranges = _searchHighlightService.FindRanges(content, state.Query);
            if (ranges.Count == 0) return;

            int currentRangeIndex = state.GetCurrentRangeIndex(
                DocumentSearchKind.Text,
                lineNumber,
                blockIndex: -1,
                ranges,
                currentEpubChapterIndex);

            var highlighter = new TextHighlighter
            {
                Background = SearchHighlightService.CreateHighlightBrush()
            };

            for (int i = 0; i < ranges.Count; i++)
            {
                if (i == currentRangeIndex) continue;
                var range = ranges[i];
                highlighter.Ranges.Add(new TextRange
                {
                    StartIndex = range.Start,
                    Length = range.Length
                });
            }

            if (highlighter.Ranges.Count > 0)
            {
                textBlock.TextHighlighters.Add(highlighter);
            }

            if (currentRangeIndex >= 0 && currentRangeIndex < ranges.Count)
            {
                var currentRange = ranges[currentRangeIndex];
                var currentHighlighter = new TextHighlighter
                {
                    Background = new SolidColorBrush(SearchHighlightService.CurrentHighlightColor)
                };
                currentHighlighter.Ranges.Add(new TextRange
                {
                    StartIndex = currentRange.Start,
                    Length = currentRange.Length
                });
                textBlock.TextHighlighters.Add(currentHighlighter);
            }
        }
    }
}
