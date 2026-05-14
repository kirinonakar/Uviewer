using System;
using System.Collections.Generic;
using Uviewer.Models;

namespace Uviewer.Services
{
    public enum NegativeLineTargetBehavior
    {
        EstimateByPage,
        NearEnd
    }

    public sealed class TextBlockDocumentService
    {
        public TextBlockDocument Parse(string content, bool isMarkdown, double baseFontSize)
        {
            var result = isMarkdown
                ? AozoraParserService.ParseMarkdownContent(content)
                : AozoraParserService.ParseAozoraContent(content, baseFontSize);

            return new TextBlockDocument(result.Blocks, result.SourceLineCount);
        }

        public int FindStartBlockIndex(
            IReadOnlyList<AozoraBindingModel> blocks,
            int targetLine,
            int targetBlockIndex = -1,
            NegativeLineTargetBehavior negativeLineBehavior = NegativeLineTargetBehavior.EstimateByPage,
            int estimatedBlocksPerPage = 30,
            int nearEndBacktrackBlocks = 15)
        {
            if (blocks.Count == 0) return 0;

            if (targetBlockIndex >= 0)
                return Math.Clamp(targetBlockIndex, 0, blocks.Count - 1);

            if (targetLine < 0)
            {
                return negativeLineBehavior == NegativeLineTargetBehavior.NearEnd
                    ? Math.Max(0, blocks.Count - nearEndBacktrackBlocks)
                    : EstimateBlockIndexFromLegacyPage(blocks.Count, -targetLine, estimatedBlocksPerPage);
            }

            if (targetLine <= 1) return 0;

            return FindFirstBlockAtOrBeforeLine(blocks, targetLine);
        }

        public int FindFirstBlockAtOrBeforeLine(IReadOnlyList<AozoraBindingModel> blocks, int targetLine)
        {
            if (blocks.Count == 0) return 0;

            int left = 0;
            int right = blocks.Count - 1;
            int candidate = 0;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                int line = blocks[mid].SourceLineNumber;

                if (line < targetLine)
                {
                    candidate = mid;
                    left = mid + 1;
                }
                else
                {
                    if (line == targetLine) candidate = mid;
                    right = mid - 1;
                }
            }

            return Math.Clamp(candidate, 0, blocks.Count - 1);
        }

        public int CountNormalizedLines(string content)
        {
            if (string.IsNullOrEmpty(content)) return 1;

            int count = 1;
            foreach (char c in content)
            {
                if (c == '\n') count++;
            }

            return count;
        }

        private static int EstimateBlockIndexFromLegacyPage(int blockCount, int targetPage, int estimatedBlocksPerPage)
        {
            int page = Math.Max(1, targetPage);
            int blockIndex = (page - 1) * Math.Max(1, estimatedBlocksPerPage);
            return Math.Min(blockIndex, Math.Max(0, blockCount - 1));
        }
    }
}
