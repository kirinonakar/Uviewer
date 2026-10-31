using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Linq;
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
            Func<string, Windows.UI.Text.FontWeight> getFontWeight,
            Func<AozoraImage, CanvasBitmap?>? getImage = null)
        {
            bool hasInlineImages = block.HasImage || block.TableContentRows.SelectMany(row => row).SelectMany(cell => cell).Any(item => item is AozoraImage);
            var cacheKey = BlockMeasureCacheKey.Create(block, isVertical: false, availableWidth, fontSize, block.FontFamily ?? defaultFontFamily);
            if (!hasInlineImages && _cache.TryGetValue(cacheKey, out float cachedHeight))
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
                var columnWidths = TableColumnLayout.Measure(device, block, availableWidth - tableIndent - tableRightMargin,
                    fontSize, block.FontFamily ?? defaultFontFamily, getFontWeight(block.FontFamily ?? defaultFontFamily), getImage);

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
                for (int c = 0; c < colCount; c++)
                {
                    using var cell = new TableCellLayout(device, TableCellLayout.GetCell(block, c),
                        Math.Max(1, columnWidths[c] - 20), fontSize, tableFormat.FontFamily, tableFormat.FontWeight, Microsoft.UI.Colors.Black, getImage: getImage);

                    float h = (float)cell.Layout.LayoutBounds.Height;
                    if (h > maxCellHeight) maxCellHeight = h;
                }

                float rowHeight = maxCellHeight + 20f;
                if (block.TableRowIndex == block.TableRowCount - 1) rowHeight += 20f;
                if (block.IsMarkdown) rowHeight += (float)block.Margin.Top;

                _cache[cacheKey] = rowHeight;
                return rowHeight;
            }

            var textInfo = BuildHorizontalText(block);
            string text = string.IsNullOrEmpty(textInfo.Text) ? " " : textInfo.Text;

            float lineSpacing = MarkdownNativeStyle.LineSpacing(block, fontSize);

            using var format = new CanvasTextFormat
            {
                FontSize = fontSize,
                FontFamily = block.FontFamily ?? defaultFontFamily,
                FontWeight = getFontWeight(block.FontFamily ?? defaultFontFamily),
                Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                WordWrapping = block.IsTable && !block.IsCodeBlock ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap,
                LineSpacing = textInfo.ImageRanges.Count > 0 ? -1 : lineSpacing,
                LineSpacingBaseline = textInfo.ImageRanges.Count > 0 ? 0 : MarkdownNativeStyle.Baseline(block, fontSize),
                LineSpacingMode = textInfo.ImageRanges.Count > 0 ? CanvasLineSpacingMode.Default : CanvasLineSpacingMode.Uniform,
                VerticalAlignment = CanvasVerticalAlignment.Top
            };

            float baseFontSize = block.FontSizeScale > 0 ? fontSize / (float)block.FontSizeScale : fontSize;
            float blockIndentChars = (float)(block.BlockIndentChars > 0 ? block.BlockIndentChars : block.Margin.Left / baseFontSize);
            float indent = blockIndentChars * fontSize;
            float rightMarginChars = (float)(block.RightMarginChars > 0 ? block.RightMarginChars : block.Margin.Right / fontSize);
            float rightMargin = rightMarginChars * fontSize;
            if (block.IsMarkdown) { indent = (float)block.Margin.Left; rightMargin = (float)block.Margin.Right; }
            float actualAvailableWidth = availableWidth - indent - rightMargin;
            if (actualAvailableWidth < 100) actualAvailableWidth = 100;

            HorizontalHeadingLayout.Configure(format, block);
            using var layout = new CanvasTextLayout(device, text, format, actualAvailableWidth, 0.0f);
            layout.Options = CanvasDrawTextOptions.EnableColorFont;

            if (block.IsBold) layout.SetFontWeight(0, text.Length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.BoldRanges) layout.SetFontWeight(r.start, r.length, Microsoft.UI.Text.FontWeights.Bold);
            foreach (var r in textInfo.ItalicRanges) layout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);
            foreach (var r in textInfo.StyledRanges) InlineTextFormatting.Apply(layout, r.start, r.length, r.style);
            if (block.IsMarkdown)
                foreach (var r in textInfo.CodeRanges) MarkdownNativeStyle.ApplyCode(layout, r.start, r.length, fontSize, block.IsCodeBlock);

            using var mathLayout = new MathInlineLayout();
            mathLayout.Apply(layout, textInfo.MathRanges, fontSize, actualAvailableWidth, Microsoft.UI.Colors.Black, textInfo.ImageRanges.Count > 0);
            using var imageLayout = new ImageInlineLayout();
            imageLayout.Apply(layout, textInfo.ImageRanges, actualAvailableWidth, getImage);
            float result = HorizontalHeadingLayout.Measure(layout, block).height + (float)block.Margin.Bottom;
            if (block.IsMarkdown) result += (float)block.Margin.Top;
            if (block.IsCodeBlock) result += (float)(block.Padding.Top + block.Padding.Bottom);
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
            foreach (var r in textInfo.StyledRanges) InlineTextFormatting.Apply(layout, r.start, r.length, r.style);

            using var mathLayout = new MathInlineLayout();
            mathLayout.Apply(layout, textInfo.MathRanges, fontSize, actualHeight, Microsoft.UI.Colors.Black);
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
            var mathRanges = new List<(int start, int length, AozoraMath math)>();
            var styledRanges = new List<(int start, int length, AozoraStyledText style)>();
            var imageRanges = new List<(int Start, AozoraImage Image)>();
            var codeRanges = new List<(int start, int length)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(s);
                else if (inline is AozoraStyledText styled)
                {
                    sb.Append(styled.Text);
                    styledRanges.Add((start, styled.Text.Length, styled));
                    if (styled.Code) codeRanges.Add((start, styled.Text.Length));
                }
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
                else if (inline is AozoraCode code) { sb.Append(code.Text); codeRanges.Add((start, code.Text.Length)); }
                else if (inline is AozoraImage image && block.IsMarkdown) { sb.Append('\uFFFC'); imageRanges.Add((start, image)); }
                else if (inline is AozoraHighlight highlight) sb.Append(highlight.Text);
                else if (inline is AozoraMath math)
                {
                    var rendered = KatexStandaloneRenderer.RenderToText(math.Text);
                    sb.Append(rendered);
                    mathRanges.Add((start, rendered.Length, math));
                    if (math.IsBold) boldRanges.Add((start, rendered.Length));
                }
                else if (inline is AozoraTCY tcy)
                {
                    sb.Append(tcy.Text);
                    if (tcy.IsBold) boldRanges.Add((start, tcy.Text.Length));
                }
                else if (inline is AozoraLineBreak) sb.Append("\n");
            }

            return new TextMeasureInput(sb.ToString(), boldRanges, italicRanges, mathRanges, styledRanges, imageRanges, codeRanges);
        }

        private static TextMeasureInput BuildVerticalText(AozoraBindingModel block)
        {
            var sb = new StringBuilder();
            var boldRanges = new List<(int start, int length)>();
            var italicRanges = new List<(int start, int length)>();
            var mathRanges = new List<(int start, int length, AozoraMath math)>();
            var styledRanges = new List<(int start, int length, AozoraStyledText style)>();

            foreach (var inline in block.Inlines)
            {
                int start = sb.Length;
                if (inline is string s) sb.Append(VerticalRenderer.NormalizeVerticalText(s));
                else if (inline is AozoraStyledText styled)
                {
                    var text = VerticalRenderer.NormalizeVerticalText(styled.Text);
                    sb.Append(text);
                    styledRanges.Add((start, text.Length, styled));
                }
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
                    mathRanges.Add((start, normText.Length, math));
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

            return new TextMeasureInput(sb.ToString(), boldRanges, italicRanges, mathRanges, styledRanges, new(), new());
        }

        private readonly record struct TextMeasureInput(
            string Text,
            List<(int start, int length)> BoldRanges,
            List<(int start, int length)> ItalicRanges,
            List<(int start, int length, AozoraMath math)> MathRanges,
            List<(int start, int length, AozoraStyledText style)> StyledRanges,
            List<(int Start, AozoraImage Image)> ImageRanges,
            List<(int start, int length)> CodeRanges);

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
                var content = new HashCode();
                foreach (var inline in block.Inlines) content.Add(inline);
                content.Add(block.TableRowIndex);
                content.Add(block.TableContentRows);
                content.Add(block.IsBold);
                content.Add(block.IsCodeBlock);
                content.Add(block.IsMarkdown);
                content.Add(block.FontSizeScale);
                content.Add(block.HeadingLevel);
                content.Add(block.Margin);
                content.Add(block.Padding);
                content.Add(block.BlockIndentChars);
                content.Add(block.RightMarginChars);
                int contentHash = content.ToHashCode();
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
