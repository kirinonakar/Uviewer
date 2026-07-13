using System;
using System.Collections.Generic;
using System.Text;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed record TextBlockParsePreview(TextBlockDocument Document, bool IsPartial);

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

        public TextBlockParsePreview ParsePreview(
            string content,
            bool isMarkdown,
            double baseFontSize,
            int targetLine,
            int lineCount = 400,
            int contextLines = 40)
        {
            int safeTargetLine = Math.Max(1, targetLine);
            int startLine = Math.Max(1, safeTargetLine - Math.Max(0, contextLines));
            var window = ExtractLineWindow(content, startLine, Math.Max(1, lineCount));

            (List<AozoraBindingModel> Blocks, int SourceLineCount) result;
            if (isMarkdown)
            {
                result = AozoraParserService.ParseMarkdownContent(window.Text);
                int sourceOffset = window.StartLine - 1;
                foreach (var block in result.Blocks)
                {
                    block.SourceLineNumber += sourceOffset;
                }
            }
            else
            {
                string prepared = AozoraParserService.PreprocessAozoraBold(window.Text);
                var lines = prepared.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                result = (AozoraParserService.ParseAozoraLines(lines, window.StartLine, baseFontSize), lines.Length);
            }

            int visibleSourceLineCount = window.StartLine + Math.Max(0, result.SourceLineCount - 1);
            var document = new TextBlockDocument(result.Blocks, visibleSourceLineCount);
            return new TextBlockParsePreview(document, window.StartLine > 1 || !window.ReachedEnd);
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
            {
                int candidate = Math.Clamp(targetBlockIndex, 0, blocks.Count - 1);
                // A bookmark can outlive edits to the document. Trust the exact block
                // only while it still points at the saved source line; otherwise the
                // source line is the safer stable anchor.
                if (targetLine < 1 || blocks[candidate].SourceLineNumber == targetLine)
                    return candidate;
            }

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

        private static (string Text, int StartLine, bool ReachedEnd) ExtractLineWindow(
            string content,
            int startLine,
            int lineCount)
        {
            if (string.IsNullOrEmpty(content))
                return (string.Empty, 1, true);

            int currentLine = 1;
            int index = 0;
            while (index < content.Length && currentLine < startLine)
            {
                char c = content[index++];
                if (c == '\r')
                {
                    if (index < content.Length && content[index] == '\n') index++;
                    currentLine++;
                }
                else if (c == '\n')
                {
                    currentLine++;
                }
            }

            int actualStartLine = currentLine;
            int remainingLines = lineCount;
            var builder = new StringBuilder(Math.Min(content.Length - index, 32 * 1024));
            while (index < content.Length && remainingLines > 0)
            {
                char c = content[index++];
                if (c == '\r')
                {
                    if (index < content.Length && content[index] == '\n') index++;
                    remainingLines--;
                    if (remainingLines > 0) builder.Append('\n');
                }
                else if (c == '\n')
                {
                    remainingLines--;
                    if (remainingLines > 0) builder.Append('\n');
                }
                else
                {
                    builder.Append(c);
                }
            }

            return (builder.ToString(), actualStartLine, index >= content.Length);
        }

        private static int EstimateBlockIndexFromLegacyPage(int blockCount, int targetPage, int estimatedBlocksPerPage)
        {
            int page = Math.Max(1, targetPage);
            int blockIndex = (page - 1) * Math.Max(1, estimatedBlocksPerPage);
            return Math.Min(blockIndex, Math.Max(0, blockCount - 1));
        }
    }
}
