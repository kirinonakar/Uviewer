using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer.Renderers
{
    public static class HorizontalRenderer
    {
        // 텍스트 내 볼드체 및 줄바꿈 파싱 (높이 측정과 렌더링 양쪽에서 사용)
        public static (string text, List<(int start, int length)> boldRanges) ParseTableInline(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return (" ", new List<(int, int)>());
            var boldRanges = new List<(int, int)>();
            string text = rawText;
            
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            var match = Regex.Match(text, @"(\*\*|__)(.*?)\1");
            while (match.Success)
            {
                int start = match.Index;
                string inner = match.Groups[2].Value;
                text = text.Remove(match.Index, match.Length).Insert(match.Index, inner);
                boldRanges.Add((start, inner.Length));
                match = Regex.Match(text, @"(\*\*|__)(.*?)\1");
            }
            
            return (text, boldRanges);
        }

        public class HorizontalRubyRenderInfo
        {
            public required CanvasTextLayout Layout { get; set; }
            public float IdealX { get; set; }
            public float Width { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
        }

        public static void ResolveHorizontalRubyOverlaps(List<HorizontalRubyRenderInfo> rubies)
        {
            if (rubies.Count == 0) return;

            int startIndex = 0;
            while (startIndex < rubies.Count)
            {
                int endIndex = startIndex;
                float currentY = rubies[startIndex].Y;

                while (endIndex + 1 < rubies.Count && Math.Abs(rubies[endIndex + 1].Y - currentY) < 2.0f)
                {
                    endIndex++;
                }

                ResolveHorizontalRubyOverlapsInRow(rubies, startIndex, endIndex);
                startIndex = endIndex + 1;
            }
        }

        private static void ResolveHorizontalRubyOverlapsInRow(List<HorizontalRubyRenderInfo> rubies, int start, int end)
        {
            float prevRight = -10000f; 

            int i = start;
            while (i <= end)
            {
                float clusterSumCenter = rubies[i].IdealX + rubies[i].Width / 2.0f;
                float clusterTotalWidth = rubies[i].Width;
                int clusterCount = 1;
                int clusterEnd = i;

                while (clusterEnd + 1 <= end)
                {
                    var next = rubies[clusterEnd + 1];
                    float currentHypotheticalLeft = (clusterSumCenter / clusterCount) - (clusterTotalWidth / 2.0f);
                    float currentHypotheticalRight = currentHypotheticalLeft + clusterTotalWidth;

                    if (currentHypotheticalRight > next.IdealX)
                    {
                        clusterEnd++;
                        clusterSumCenter += (next.IdealX + next.Width / 2.0f);
                        clusterTotalWidth += next.Width;
                        clusterCount++;
                    }
                    else break;
                }

                float finalLeft = (clusterSumCenter / clusterCount) - (clusterTotalWidth / 2.0f);

                if (finalLeft < prevRight) finalLeft = prevRight;

                for (int k = i; k <= clusterEnd; k++)
                {
                    rubies[k].X = finalLeft;
                    finalLeft += rubies[k].Width;
                }

                prevRight = finalLeft;
                i = clusterEnd + 1;
            }
        }

        /// <summary>
        /// Aozora 블록 리스트를 가로 모드로 캔버스에 렌더링합니다.
        /// </summary>
        internal static void RenderBlocks(
            CanvasDrawingSession ds,
            List<AozoraBindingModel> blocks,
            Color textColor,
            float marginLeft,
            float marginTop,
            float maxWidth,
            double baseFontSize,
            string defaultFontFamily,
            Func<string, FontWeight> getFontWeight,
            string? searchQuery = null,
            DocumentSearchMatch? currentSearchMatch = null,
            DocumentSearchKind renderedSearchKind = DocumentSearchKind.Text,
            int firstBlockIndex = -1,
            CanvasTextGeometry? selectionGeometry = null,
            IReadOnlyList<CanvasTextRange>? selectionRanges = null,
            int selectionBlockOffset = 0,
            Func<AozoraImage, CanvasBitmap?>? getImage = null)
        {
            float currentY = marginTop;
            bool isBoxing = false;
            float boxLeft = float.MaxValue;
            float boxRight = float.MinValue;
            float boxTop = 0f;
            float boxBottom = float.MaxValue;
            Color boxColor = Colors.Gray;
            float boxPad = 20f;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                float fontSize = (float)(baseFontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;
                if (block.IsMarkdown) currentY += (float)block.Margin.Top;

                // 1. 테이블 렌더링
                if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
                {
                    var row = block.TableRows[0];
                    int colCount = row.Count;
                    int r = block.TableRowIndex;
                    bool isHeader = (r == 0);
                    bool isFirstOnPage = (i == 0) || !blocks[i - 1].IsTable;

                    float tableIndentChars = (float)(block.BlockIndentChars > 0 ? block.BlockIndentChars : (block.Margin.Left / baseFontSize));
                    float tableIndent = (float)(tableIndentChars * fontSize);
                    float tableRightMargin = (float)(block.RightMarginChars * fontSize);
                    
                    float tableMaxWidth = maxWidth - tableIndent - tableRightMargin;
                    float tableDrawX = marginLeft + tableIndent;
                    var columnWidths = TableColumnLayout.Measure(ds, block, tableMaxWidth, fontSize,
                        block.FontFamily ?? defaultFontFamily, getFontWeight(block.FontFamily ?? defaultFontFamily), getImage);
                    tableMaxWidth = columnWidths.Sum();

                    using var tableFormat = new CanvasTextFormat
                    {
                        FontSize = fontSize,
                        FontFamily = block.FontFamily ?? defaultFontFamily,
                        WordWrapping = CanvasWordWrapping.Wrap,
                        FontWeight = isHeader ? FontWeights.Bold : getFontWeight(block.FontFamily ?? defaultFontFamily)
                    };

                    if (isHeader || isFirstOnPage)
                        ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, 1.5f);

                    float maxCellHeight = 0;
                    var cellLayouts = new List<TableCellLayout>();

                    for (int c = 0; c < colCount; c++)
                    {
                        var cellLayout = new TableCellLayout(ds, TableCellLayout.GetCell(block, c),
                            Math.Max(1, columnWidths[c] - 20), fontSize, tableFormat.FontFamily, tableFormat.FontWeight, textColor, getImage: getImage);
                            
                        cellLayouts.Add(cellLayout);
                        float h = (float)cellLayout.Layout.LayoutBounds.Height;
                        if (h > maxCellHeight) maxCellHeight = h;
                    }

                    float rowHeight = maxCellHeight + 20f;

                    if (isHeader)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, ColorHelper.FromArgb(30, 128, 128, 128));
                    else if (r % 2 == 1)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, ColorHelper.FromArgb(10, 128, 128, 128));

                    float cellX = tableDrawX;
                    for (int c = 0; c < colCount; c++)
                    {
                        float cellOffsetY = (maxCellHeight - (float)cellLayouts[c].Layout.LayoutBounds.Height) / 2;
                        cellLayouts[c].Draw(ds, cellX + 10, currentY + 10 + cellOffsetY, textColor);
                        cellLayouts[c].Dispose();
                        ds.DrawLine(cellX, currentY, cellX, currentY + rowHeight, Colors.Gray, 1f); 
                        cellX += columnWidths[c];
                    }
                    
                    ds.DrawLine(tableDrawX + tableMaxWidth, currentY, tableDrawX + tableMaxWidth, currentY + rowHeight, Colors.Gray, 1f);

                    currentY += rowHeight;
                    ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, isHeader ? 2f : 1f);

                    if (r == block.TableRowCount - 1)
                        currentY += 20f; 

                    continue; 
                }

                // 2. 일반 텍스트 블록 렌더링
                if (block.IsCodeBlock) currentY += (float)block.Padding.Top;
                float lineSpacing = MarkdownNativeStyle.LineSpacing(block, fontSize);

                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();
                var highlightRanges = new List<(int start, int length)>();
                var mathRanges = new List<(int start, int length, AozoraMath math)>();
                var styledRanges = new List<(int start, int length, AozoraStyledText style)>();
                var codeRanges = new List<(int start, int length)>();
                var imageRanges = new List<(int Start, AozoraImage Image)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(s);
                    else if (inline is AozoraStyledText styled)
                    {
                        sb.Append(styled.Text);
                        styledRanges.Add((start, styled.Text.Length, styled));
                        if (styled.Code) codeRanges.Add((start, styled.Text.Length));
                        if (styled.Highlight) highlightRanges.Add((start, styled.Text.Length));
                    }
                    else if (inline is AozoraRuby ruby)
                    {
                        sb.Append(ruby.BaseText);
                        rubyRanges.Add((start, ruby.BaseText.Length, ruby.RubyText));
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
                    else if (inline is AozoraHighlight highlight)
                    {
                        sb.Append(highlight.Text);
                        highlightRanges.Add((start, highlight.Text.Length));
                    }
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

                string blockText = sb.ToString();
                if (string.IsNullOrEmpty(blockText)) blockText = " "; // 빈 블록 방어

                float blockIndentChars = (float)(block.BlockIndentChars > 0 ? block.BlockIndentChars : (block.Margin.Left / baseFontSize));
                float indent = (float)(blockIndentChars * fontSize);
                float rightMargin = (float)(block.RightMarginChars > 0 ? block.RightMarginChars * fontSize : block.Margin.Right);
                if (block.IsMarkdown) { indent = (float)block.Margin.Left; rightMargin = (float)block.Margin.Right; }
                float actualMaxWidth = maxWidth - indent - rightMargin;
                if (actualMaxWidth < 100) actualMaxWidth = 100;

                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = block.FontFamily ?? defaultFontFamily,
                    FontWeight = getFontWeight(block.FontFamily ?? defaultFontFamily),
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    WordWrapping = block.IsTable && !block.IsCodeBlock ? CanvasWordWrapping.NoWrap : CanvasWordWrapping.Wrap,
                    LineSpacing = imageRanges.Count > 0 ? -1 : lineSpacing,
                    LineSpacingBaseline = imageRanges.Count > 0 ? 0 : MarkdownNativeStyle.Baseline(block, fontSize),
                    LineSpacingMode = imageRanges.Count > 0 ? CanvasLineSpacingMode.Default : CanvasLineSpacingMode.Uniform,
                    VerticalAlignment = CanvasVerticalAlignment.Top
                };

                HorizontalHeadingLayout.Configure(format, block);
                var textLayout = new CanvasTextLayout(ds, blockText, format, actualMaxWidth, 0.0f);
                bool layoutRetained = false;
                textLayout.Options = CanvasDrawTextOptions.EnableColorFont; 
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, FontWeights.Bold);
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);
                foreach (var r in styledRanges)
                {
                    InlineTextFormatting.Apply(textLayout, r.start, r.length, r.style);
                    if (r.length > 0 && r.style.IsLink) textLayout.SetColor(r.start, r.length, MarkdownNativeStyle.LinkColor(textColor));
                    var syntaxColor = MarkdownNativeStyle.SyntaxColor(r.style.SyntaxScope, textColor);
                    if (r.length > 0 && syntaxColor.HasValue) textLayout.SetColor(r.start, r.length, syntaxColor.Value);
                }
                if (block.IsMarkdown)
                    foreach (var r in codeRanges) MarkdownNativeStyle.ApplyCode(textLayout, r.start, r.length, fontSize, block.IsCodeBlock);
                using var mathLayout = new MathInlineLayout();
                mathLayout.Apply(textLayout, mathRanges, fontSize, actualMaxWidth, textColor, imageRanges.Count > 0);
                using var imageLayout = new ImageInlineLayout();
                imageLayout.Apply(textLayout, imageRanges, actualMaxWidth, getImage);

                var extent = HorizontalHeadingLayout.Measure(textLayout, block);
                currentY += extent.topInset;
                float currentBlockHeight = block.IsBlankLine ? lineSpacing * 0.3f : extent.height - extent.topInset;

                var bounds = textLayout.LayoutBounds;
                float drawX = marginLeft + indent;
                if (block.Alignment == TextAlignment.Center) drawX = marginLeft + (maxWidth - (float)bounds.Width) / 2;
                else if (block.Alignment == TextAlignment.Right) drawX = marginLeft + maxWidth - (float)bounds.Width - rightMargin;

                bool isKeigakomi = block.BorderThickness.Top > 0 && block.BorderThickness.Bottom > 0 && block.BorderThickness.Left > 0 && block.BorderThickness.Right > 0;
                float currentW = (float)bounds.Width;
                if (block.IsBlankLine && currentW < fontSize) currentW = fontSize;

                // 3. 罫囲み (박스 테두리) 계산
                if (isKeigakomi)
                {
                    if (!isBoxing)
                    {
                        currentY += boxPad;
                        isBoxing = true;
                        boxTop = currentY;
                        boxBottom = currentY + currentBlockHeight;
                        boxLeft = drawX + (float)bounds.X;
                        boxRight = drawX + (float)bounds.X + currentW;
                        boxColor = block.BorderColor ?? Colors.Gray;
                    }
                    else
                    {
                        boxTop = Math.Min(boxTop, currentY + (float)bounds.Y);
                        boxBottom = Math.Max(boxBottom, currentY + (float)bounds.Y + currentBlockHeight);
                        boxLeft = Math.Min(boxLeft, drawX + (float)bounds.X);
                        boxRight = Math.Max(boxRight, drawX + (float)bounds.X + currentW);
                    }
                }
                else if (!isKeigakomi && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                    currentY += boxPad + lineSpacing;
                }

                // 4. 배경색 그리기 (Padding 지원)
                if (block.BackgroundColor != null)
                {
                    var drawBounds = textLayout.DrawBounds;
                    float dbTop = (float)drawBounds.Top;
                    float dbHeight = (float)drawBounds.Height;

                    if (dbHeight < fontSize) dbHeight = fontSize;
                    
                    float padTop = (float)block.Padding.Top;
                    float padBottom = (float)block.Padding.Bottom;
                    float padLeft = (float)block.Padding.Left;
                    float padRight = (float)block.Padding.Right;

                    float bgTop = currentY + dbTop - padTop;
                    float bgHeight = dbHeight + padTop + padBottom;
                    if (block.IsCodeBlock)
                    {
                        bgTop = currentY - padTop;
                        bgHeight = currentBlockHeight + padTop + padBottom;
                    }

                    var backgroundRect = new Windows.Foundation.Rect(drawX - padLeft, bgTop,
                        (block.IsCodeBlock ? actualMaxWidth : currentW) + padLeft + padRight, bgHeight);
                    if (block.IsMarkdown && block.IsCodeBlock)
                        FillMarkdownCodeBackground(ds, backgroundRect, MarkdownNativeStyle.CodeBackground(textColor),
                            block.CodeBlockLineIndex == 0, block.CodeBlockLineCount <= 1 || block.CodeBlockLineIndex == block.CodeBlockLineCount - 1);
                    else ds.FillRectangle(backgroundRect, block.BackgroundColor.Value);
                }

                // 5. 인라인 배경 및 텍스트 본문 그리기
                if (block.IsMarkdown && !block.IsCodeBlock)
                    DrawRangeBackgrounds(ds, textLayout, codeRanges, drawX, currentY,
                        MarkdownNativeStyle.InlineCodeBackground(textColor), fontSize, rounded: true);
                DrawRangeBackgrounds(ds, textLayout, highlightRanges, drawX, currentY, ColorHelper.FromArgb(120, 255, 230, 96), fontSize);
                var searchHighlights = SearchHighlightService.CreateAozoraHighlightRanges(
                    block,
                    firstBlockIndex,
                    i,
                    currentSearchMatch,
                    renderedSearchKind,
                    blockText,
                    searchQuery);

                DrawRangeBackgrounds(
                    ds,
                    textLayout,
                    searchHighlights.OtherRanges,
                    drawX,
                    currentY,
                    SearchHighlightService.HighlightColor,
                    fontSize);
                if (searchHighlights.CurrentRange.HasValue)
                {
                    var currentRange = searchHighlights.CurrentRange.Value;
                    DrawRangeBackgrounds(
                        ds,
                        textLayout,
                        new List<(int start, int length)> { currentRange },
                        drawX,
                        currentY,
                        SearchHighlightService.CurrentHighlightColor,
                        fontSize);
                }

                if (selectionRanges != null && selectionRanges.Count > 0)
                {
                    var blockSelectionRanges = new List<(int start, int length)>();
                    foreach (var range in selectionRanges)
                    {
                        if (range.BlockIndex == selectionBlockOffset + i && range.Length > 0)
                        {
                            blockSelectionRanges.Add((range.Start, range.Length));
                        }
                    }

                    if (blockSelectionRanges.Count > 0)
                    {
                        DrawRangeBackgrounds(
                            ds,
                            textLayout,
                            blockSelectionRanges,
                            drawX,
                            currentY,
                            TextSelectionVisuals.SelectionColor,
                            fontSize);
                    }
                }

                imageLayout.Draw(ds, () => mathLayout.Draw(ds, textLayout, drawX, currentY, textColor));
                if (selectionGeometry != null)
                {
                    selectionGeometry.AddBlock(selectionBlockOffset + i, blockText, drawX, currentY, fontSize, textLayout);
                    layoutRetained = true;
                }

                // 6. 밑줄(헤딩) 및 좌측 선(인용구) 그리기
                if (!isKeigakomi && block.BorderColor != null)
                {
                    var drawBounds = textLayout.DrawBounds;
                    float borderBottomY = currentY + (float)drawBounds.Bottom + 4f;
                    
                    if (block.BorderThickness.Bottom > 0)
                    {
                        ds.DrawLine(drawX, borderBottomY, drawX + (block.IsMarkdown ? actualMaxWidth : currentW), borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Bottom);
                    }
                    if (block.BorderThickness.Left > 0)
                    {
                        float quoteLeft = drawX - 15;
                        float actualTextTop = (float)Math.Min(drawBounds.Top, 0);
                        ds.DrawLine(quoteLeft, currentY + actualTextTop, quoteLeft, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Left);
                    }
                }

                // 7. 루비(Ruby) 그리기
                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = defaultFontFamily,
                    FontWeight = getFontWeight(defaultFontFamily),
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };

                var rubyRenderInfos = new List<HorizontalRubyRenderInfo>();
                var rubyLineBounds = rubyRanges.Count > 0 ? BuildLineBounds(textLayout) : new List<LineBounds>();
                using var rubyBaseFormat = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = format.FontFamily,
                    FontWeight = format.FontWeight,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;
                        float charCenter = drawX + (float)charBounds.Left + (float)charBounds.Width / 2.0f;

                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, 0.0f);
                        rubyLayout.Options = CanvasDrawTextOptions.EnableColorFont; 
                        using var baseLayout = new CanvasTextLayout(ds, blockText.Substring(ruby.start, ruby.length), rubyBaseFormat, 0.0f, 0.0f);
                        
                        if (block.IsBold || boldRanges.Any(br => ruby.start >= br.start && ruby.start < br.start + br.length))
                        {
                            rubyLayout.SetFontWeight(0, ruby.rubyText.Length, FontWeights.Bold);
                            baseLayout.SetFontWeight(0, ruby.length, FontWeights.Bold);
                        }

                        // Character regions include line spacing, not just visible glyphs.
                        // Anchor to the actual baseline so uniform spacing, wrapping and
                        // tall inline formulas cannot push ruby away from its base text.
                        var line = rubyLineBounds[FindLineIndex(rubyLineBounds, ruby.start, 0)];
                        float baseInkTop = currentY + line.Top + line.Baseline
                            + (float)baseLayout.DrawBounds.Top - baseLayout.LineMetrics[0].Baseline;
                        float rubyY = baseInkTop - Math.Max(3f, fontSize * 0.08f) - (float)rubyLayout.DrawBounds.Bottom;
                        float rubyWidth = (float)rubyLayout.LayoutBounds.Width;
                        float idealLeft = charCenter - (rubyWidth / 2.0f);

                        rubyRenderInfos.Add(new HorizontalRubyRenderInfo
                        {
                            Layout = rubyLayout,
                            IdealX = idealLeft,
                            Width = rubyWidth,
                            X = idealLeft,
                            Y = rubyY
                        });
                    }
                }

                ResolveHorizontalRubyOverlaps(rubyRenderInfos);

                foreach (var info in rubyRenderInfos)
                {
                    ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor);
                    info.Layout.Dispose();
                }

                if (!layoutRetained)
                {
                    textLayout.Dispose();
                }

                currentY += currentBlockHeight + (float)block.Margin.Bottom;
                if (block.IsCodeBlock) currentY += (float)block.Padding.Bottom;

                if (i == blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }

        private static void FillMarkdownCodeBackground(CanvasDrawingSession ds, Windows.Foundation.Rect rect, Color color, bool first, bool last)
        {
            if (!first && !last) { ds.FillRectangle(rect, color); return; }
            ds.FillRoundedRectangle(rect, 6, 6, color);
            if (!last) ds.FillRectangle(new Windows.Foundation.Rect(rect.X, rect.Y + rect.Height / 2, rect.Width, rect.Height / 2), color);
            if (!first) ds.FillRectangle(new Windows.Foundation.Rect(rect.X, rect.Y, rect.Width, rect.Height / 2), color);
        }

        internal static void DrawRangeBackgrounds(
            CanvasDrawingSession ds,
            CanvasTextLayout layout,
            List<(int start, int length)> ranges,
            float drawX,
            float drawY,
            Color color,
            float fontSize,
            bool rounded = false)
        {
            var lineBounds = BuildLineBounds(layout);

            foreach (var range in ranges)
            {
                if (range.length <= 0) continue;
                var regions = layout.GetCharacterRegions(range.start, range.length);
                int lineIndex = FindLineIndex(lineBounds, range.start, 0);

                foreach (var region in regions)
                {
                    var bounds = region.LayoutBounds;
                    var line = lineBounds.Count > 0 ? lineBounds[Math.Min(lineIndex, lineBounds.Count - 1)] : default;
                    var highlightBounds = GetHighlightVerticalBounds(line, bounds, fontSize);

                    float x = drawX + (float)bounds.Left - 2f;
                    float y = drawY + highlightBounds.y;
                    float width = Math.Max(2f, (float)bounds.Width + 4f);
                    float height = highlightBounds.height;
                    if (rounded) ds.FillRoundedRectangle(x, y, width, height, 4, 4, color);
                    else ds.FillRectangle(x, y, width, height, color);

                    if (lineBounds.Count > 0 && lineIndex < lineBounds.Count - 1)
                        lineIndex++;
                }
            }
        }

        private readonly struct LineBounds
        {
            public LineBounds(int start, int end, float top, float height, float baseline)
            {
                Start = start;
                End = end;
                Top = top;
                Height = height;
                Baseline = baseline;
            }

            public int Start { get; }
            public int End { get; }
            public float Top { get; }
            public float Height { get; }
            public float Baseline { get; }
        }

        private static List<LineBounds> BuildLineBounds(CanvasTextLayout layout)
        {
            var result = new List<LineBounds>();
            int start = 0;
            float top = 0f;

            foreach (var metric in layout.LineMetrics)
            {
                int charCount = Math.Max(0, metric.CharacterCount);
                int end = start + charCount;
                result.Add(new LineBounds(start, end, top, metric.Height, metric.Baseline));
                start = end;
                top += metric.Height;
            }

            return result;
        }

        private static int FindLineIndex(List<LineBounds> lines, int characterIndex, int minIndex)
        {
            if (lines.Count == 0) return 0;

            int start = Math.Max(0, Math.Min(minIndex, lines.Count - 1));
            for (int i = start; i < lines.Count; i++)
            {
                if (characterIndex < lines[i].End || i == lines.Count - 1)
                    return i;
            }

            return lines.Count - 1;
        }

        private static (float y, float height) GetHighlightVerticalBounds(LineBounds line, Windows.Foundation.Rect characterBounds, float fontSize)
        {
            if (line.Height <= 0)
            {
                float fallbackHeight = Math.Max(fontSize * 0.7f, (float)characterBounds.Height * 0.72f);
                return ((float)characterBounds.Top + fontSize * 0.18f, fallbackHeight);
            }

            float height = fontSize * 1.05f;
            float y = line.Top + line.Baseline - (fontSize * 0.9f);
            return (y, height);
        }
    }
}
