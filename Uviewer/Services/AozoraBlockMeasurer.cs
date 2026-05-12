using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Uviewer.Models;
using Uviewer.Renderers;

namespace Uviewer.Services
{
    public sealed class AozoraBlockMeasurer
    {
        private readonly ConcurrentDictionary<BlockMeasureCacheKey, float> _cache = new();

        public void Clear()
        {
            _cache.Clear();
        }

        public float MeasureHorizontalBlockHeight(
            CanvasDevice? device,
            AozoraBindingModel block,
            float availableWidth,
            float fontSize,
            string defaultFontFamily,
            Func<string, Windows.UI.Text.FontWeight> getFontWeight)
        {
            var cacheKey = BlockMeasureCacheKey.Create(block, isVertical: false, availableWidth, fontSize, block.FontFamily ?? defaultFontFamily);
            if (_cache.TryGetValue(cacheKey, out float cachedHeight))
                return cachedHeight;

            if (device == null) return fontSize * 2.0f;

            if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
            {
                var row = block.TableRows[0];
                int colCount = row.Count;
                if (colCount == 0) return fontSize * 2.0f;

                float tableIndentChars = (float)(block.BlockIndentChars > 0 ? block.BlockIndentChars : block.Margin.Left / fontSize);
                float tableIndent = tableIndentChars * fontSize;
                float tableRightMarginChars = (float)block.RightMarginChars;
                float tableRightMargin = tableRightMarginChars * fontSize;
                float colWidth = (availableWidth - tableIndent - tableRightMargin) / colCount;

                using var tableFormat = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? defaultFontFamily,
                    WordWrapping = CanvasWordWrapping.Wrap,
                    FontWeight = block.TableRowIndex == 0
                        ? Microsoft.UI.Text.FontWeights.Bold
                        : getFontWeight(block.FontFamily ?? defaultFontFamily)
                };

                float maxCellHeight = 0;
                foreach (var cellText in row)
                {
                    var parsed = HorizontalRenderer.ParseTableInline(cellText);
                    using var cellLayout = new CanvasTextLayout(device, parsed.text, tableFormat, Math.Max(10, colWidth - 20), 0.0f);
                    foreach (var r in parsed.boldRanges)
                        cellLayout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);

                    float h = (float)cellLayout.LayoutBounds.Height;
                    if (h > maxCellHeight) maxCellHeight = h;
                }

                float rowHeight = maxCellHeight + 20f;
                if (block.TableRowIndex == block.TableRowCount - 1) rowHeight += 20f;

                _cache[cacheKey] = rowHeight;
                return rowHeight;
            }

            var textInfo = BuildHorizontalText(block);
            string text = string.IsNullOrEmpty(textInfo.Text) ? " " : textInfo.Text;

            float lineSpacing = (block.IsTable || block.HeadingLevel > 0) ? fontSize * 1.3f : fontSize * 2.1f;

            using var format = new CanvasTextFormat
            {
                FontSize = fontSize,
                FontFamily = block.FontFamily ?? defaultFontFamily,
                FontWeight = getFontWeight(block.FontFamily ?? defaultFontFamily),
                Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                WordWrapping = block.IsTable ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap,
                LineSpacing = lineSpacing,
                VerticalAlignment = CanvasVerticalAlignment.Top
            };

            float blockIndentChars = (float)(block.BlockIndentChars > 0 ? block.BlockIndentChars : block.Margin.Left / fontSize);
            float indent = blockIndentChars * fontSize;
            float rightMarginChars = (float)(block.RightMarginChars > 0 ? block.RightMarginChars : block.Margin.Right / fontSize);
            float rightMargin = rightMarginChars * fontSize;
            float actualAvailableWidth = availableWidth - indent - rightMargin;
            if (actualAvailableWidth < 100) actualAvailableWidth = 100;

            using var layout = new CanvasTextLayout(device, text, format, actualAvailableWidth, 0.0f);
            layout.Options = CanvasDrawTextOptions.EnableColorFont;

            if (block.IsBold) layout.SetFontWeight(0, text.Length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.BoldRanges) layout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.ItalicRanges) layout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

            float result = layout.LineCount * lineSpacing + (float)block.Margin.Bottom;
            if (block.IsBlankLine) result = lineSpacing * 0.3f;

            _cache[cacheKey] = result;
            return result;
        }

        public float MeasureVerticalBlockWidth(
            CanvasDevice? device,
            AozoraBindingModel block,
            float availableHeight,
            float fontSize,
            string defaultFontFamily,
            Func<string, Windows.UI.Text.FontWeight> getFontWeight)
        {
            var cacheKey = BlockMeasureCacheKey.Create(block, isVertical: true, availableHeight, fontSize, block.FontFamily ?? defaultFontFamily);
            if (_cache.TryGetValue(cacheKey, out float cachedWidth))
                return cachedWidth;

            if (device == null) return fontSize * 2.0f;

            var textInfo = BuildVerticalText(block);
            string text = string.IsNullOrEmpty(textInfo.Text) ? " " : textInfo.Text;

            using var format = new CanvasTextFormat
            {
                FontSize = fontSize,
                FontFamily = block.FontFamily ?? defaultFontFamily,
                FontWeight = getFontWeight(block.FontFamily ?? defaultFontFamily),
                Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                WordWrapping = CanvasWordWrapping.EmergencyBreak,
                LineSpacing = fontSize * 1.8f,
                VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };

            float indentY = (float)(block.BlockIndentChars * fontSize);
            float actualHeight = Math.Max(fontSize, availableHeight - indentY);

            using var layout = new CanvasTextLayout(device, text, format, fontSize * 2.0f, actualHeight);

            if (block.IsBold) layout.SetFontWeight(0, text.Length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.BoldRanges) layout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.ItalicRanges) layout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

            float boundsWidth = (float)layout.LayoutBounds.Width;
            float spacing = fontSize * (block.IsBlankLine ? 0.2f : 0.6f);

            float result = boundsWidth + spacing;
            if (block.IsBlankLine) result = boundsWidth * 0.5f + spacing;

            _cache[cacheKey] = result;
            return result;
        }

        private static TextMeasureInput BuildHorizontalText(AozoraBindingModel block)
        {
            var sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(s);
                else if (inline is AozoraRuby ruby)
                {
                    sb.Append(ruby.BaseText);
                    if (ruby.IsBold) boldRanges.Add((start, ruby.BaseText.Length));
                }
                else if (inline is AozoraBold bold)
                {
                    sb.Append(bold.Text);
                    boldRanges.Add((start, bold.Text.Length));
                }
                else if (inline is AozoraItalic italic)
                {
                    sb.Append(italic.Text);
                    italicRanges.Add((start, italic.Text.Length));
                }
                else if (inline is AozoraCode code) sb.Append(code.Text);
                else if (inline is AozoraHighlight highlight) sb.Append(highlight.Text);
                else if (inline is AozoraMath math)
                {
                    var rendered = KatexStandaloneRenderer.RenderToText(math.Text);
                    sb.Append(rendered);
                    if (math.IsBold) boldRanges.Add((start, rendered.Length));
                }
                else if (inline is AozoraTCY tcy)
                {
                    sb.Append(tcy.Text);
                    if (tcy.IsBold) boldRanges.Add((start, tcy.Text.Length));
                }
                else if (inline is AozoraLineBreak) sb.Append("\n");
            }

            return new TextMeasureInput(sb.ToString(), boldRanges, italicRanges);
        }

        private static TextMeasureInput BuildVerticalText(AozoraBindingModel block)
        {
            var sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(VerticalRenderer.NormalizeVerticalText(s));
                else if (inline is AozoraRuby ruby)
                {
                    var normBase = VerticalRenderer.NormalizeVerticalText(ruby.BaseText);
                    sb.Append(normBase);
                    if (ruby.IsBold) boldRanges.Add((start, normBase.Length));
                }
                else if (inline is AozoraBold bold)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(bold.Text);
                    sb.Append(normText);
                    boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraItalic italic)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(italic.Text);
                    sb.Append(normText);
                    italicRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraCode code) sb.Append(VerticalRenderer.NormalizeVerticalText(code.Text));
                else if (inline is AozoraHighlight highlight) sb.Append(VerticalRenderer.NormalizeVerticalText(highlight.Text));
                else if (inline is AozoraMath math)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(KatexStandaloneRenderer.RenderToText(math.Text));
                    sb.Append(normText);
                    if (math.IsBold) boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraTCY tcy)
                {
                    var normText = VerticalRenderer.NormalizeVerticalText(tcy.Text);
                    sb.Append(normText);
                    if (tcy.IsBold) boldRanges.Add((start, normText.Length));
                }
                else if (inline is AozoraLineBreak) sb.Append("\n");
            }

            if (block.IsTable && block.TableRows.Count > 0)
            {
                foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
            }

            return new TextMeasureInput(sb.ToString(), boldRanges, italicRanges);
        }

        private readonly record struct TextMeasureInput(
            string Text,
            List<(int start, int length)> BoldRanges,
            List<(int start, int length)> ItalicRanges);

        private readonly struct BlockMeasureCacheKey : IEquatable<BlockMeasureCacheKey>
        {
            private readonly int _sourceLine;
            private readonly int _inlineCount;
            private readonly int _contentHash;
            private readonly bool _isVertical;
            private readonly int _extentBucket;
            private readonly int _fontSizeBucket;
            private readonly string _fontFamily;

            private BlockMeasureCacheKey(
                int sourceLine,
                int inlineCount,
                int contentHash,
                bool isVertical,
                int extentBucket,
                int fontSizeBucket,
                string fontFamily)
            {
                _sourceLine = sourceLine;
                _inlineCount = inlineCount;
                _contentHash = contentHash;
                _isVertical = isVertical;
                _extentBucket = extentBucket;
                _fontSizeBucket = fontSizeBucket;
                _fontFamily = fontFamily;
            }

            public static BlockMeasureCacheKey Create(
                AozoraBindingModel block,
                bool isVertical,
                float extent,
                float fontSize,
                string fontFamily)
            {
                int contentHash = block.Inlines.Count > 0 ? block.Inlines[0].GetHashCode() : 0;
                return new BlockMeasureCacheKey(
                    block.SourceLineNumber,
                    block.Inlines.Count,
                    contentHash,
                    isVertical,
                    (int)Math.Round(extent),
                    (int)Math.Round(fontSize * 100),
                    fontFamily);
            }

            public bool Equals(BlockMeasureCacheKey other) =>
                _sourceLine == other._sourceLine &&
                _inlineCount == other._inlineCount &&
                _contentHash == other._contentHash &&
                _isVertical == other._isVertical &&
                _extentBucket == other._extentBucket &&
                _fontSizeBucket == other._fontSizeBucket &&
                string.Equals(_fontFamily, other._fontFamily, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is BlockMeasureCacheKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(_sourceLine, _inlineCount, _contentHash, _isVertical, _extentBucket, _fontSizeBucket, _fontFamily);
        }
    }
}
