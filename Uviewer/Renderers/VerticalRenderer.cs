using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer.Renderers
{
    public static class VerticalRenderer
    {
        public static string NormalizeVerticalText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 세로 모드 전용: !!, ??, ?!, !? 를 하나의 TCY(세로중짜) 유니코드 문자로 치환하여
            // 세로 쓰기 레이아웃에서 나란히 바르게 서도록 처리합니다. (전각/반각 모두 지원)
            text = text.Replace("!!", "‼").Replace("！！", "‼");
            text = text.Replace("??", "⁇").Replace("？？", "⁇");
            text = text.Replace("?!", "⁈").Replace("？！", "⁈");
            text = text.Replace("!?", "⁉").Replace("！？", "⁉");

            return text;
        }

        public static void ApplyVerticalBracketSpacing(ICanvasResourceCreator resourceCreator, CanvasTextFormat format, CanvasTextLayout layout, string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return;

            string brackets = "()[]{}<>（）「」『』【】〈〉《》";
            float baseReduction = -fontSize * 0.4f;

            for (int i = 0; i < text.Length; i++)
            {
                if (brackets.Contains(text[i]))
                {
                    using var tmpFormat = new CanvasTextFormat
                    {
                        FontFamily = format.FontFamily,
                        FontSize = fontSize,
                        Direction = format.Direction,
                        VerticalAlignment = CanvasVerticalAlignment.Top 
                    };
                    
                    using var tmpLayout = new CanvasTextLayout(resourceCreator, text[i].ToString(), tmpFormat, fontSize * 2, fontSize * 2);
                    var drawBounds = tmpLayout.DrawBounds;
                    var layoutBounds = tmpLayout.LayoutBounds;

                    float slotBottom = (float)(layoutBounds.Y + layoutBounds.Height);
                    float inkBottom = (float)(drawBounds.Y + drawBounds.Height);
                    float gapBelow = slotBottom - inkBottom;

                    if (gapBelow > fontSize * 0.15f)
                    {
                        float actualReduction = Math.Max(baseReduction, -gapBelow * 0.85f);
                        layout.SetCharacterSpacing(i, 1, 0, actualReduction, 0);
                    }
                }
            }
        }

        public class VerticalRubyRenderInfo
        {
            public required CanvasTextLayout Layout { get; set; }
            public float IdealY { get; set; }
            public float Height { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
        }

        public static void ResolveVerticalRubyOverlaps(List<VerticalRubyRenderInfo> rubies)
        {
            if (rubies.Count == 0) return;

            int startIndex = 0;
            while (startIndex < rubies.Count)
            {
                int endIndex = startIndex;
                float currentX = rubies[startIndex].X;

                while (endIndex + 1 < rubies.Count && Math.Abs(rubies[endIndex + 1].X - currentX) < 2.0f)
                {
                    endIndex++;
                }

                ResolveVerticalRubyOverlapsInColumn(rubies, startIndex, endIndex);
                startIndex = endIndex + 1;
            }
        }

        private static void ResolveVerticalRubyOverlapsInColumn(List<VerticalRubyRenderInfo> rubies, int start, int end)
        {
            float prevBottom = -10000f; 

            int i = start;
            while (i <= end)
            {
                float clusterSumCenter = rubies[i].IdealY + rubies[i].Height / 2.0f;
                float clusterTotalHeight = rubies[i].Height;
                int clusterCount = 1;
                int clusterEnd = i;

                while (clusterEnd + 1 <= end)
                {
                    var next = rubies[clusterEnd + 1];
                    float currentHypotheticalTop = (clusterSumCenter / clusterCount) - (clusterTotalHeight / 2.0f);
                    float currentHypotheticalBottom = currentHypotheticalTop + clusterTotalHeight;

                    if (currentHypotheticalBottom > next.IdealY)
                    {
                        clusterEnd++;
                        clusterSumCenter += (next.IdealY + next.Height / 2.0f);
                        clusterTotalHeight += next.Height;
                        clusterCount++;
                    }
                    else break;
                }

                float finalTop = (clusterSumCenter / clusterCount) - (clusterTotalHeight / 2.0f);

                if (finalTop < prevBottom) finalTop = prevBottom;

                for (int k = i; k <= clusterEnd; k++)
                {
                    rubies[k].Y = finalTop;
                    finalTop += rubies[k].Height;
                }

                prevBottom = finalTop;
                i = clusterEnd + 1;
            }
        }

        /// <summary>
        /// Aozora 블록 리스트를 세로 모드로 캔버스에 렌더링합니다.
        /// </summary>
        public static void RenderBlocks(
            CanvasDrawingSession ds,
            List<AozoraBindingModel> blocks,
            Color textColor,
            Size canvasSize,
            float marginTop,
            float marginBottom,
            float marginRight,
            float marginLeft,
            double baseFontSize,
            string defaultFontFamily,
            Func<string, FontWeight> getFontWeight,
            string? searchQuery = null)
        {
            float currentX = (float)canvasSize.Width - marginRight; 
            float startY = marginTop;
            float drawHeight = (float)canvasSize.Height - (marginTop + marginBottom);

            bool isBoxing = false;
            float boxRight = 0f;
            float boxLeft = float.MaxValue;
            float boxTop = float.MaxValue;
            float boxBottom = float.MinValue;
            Color boxColor = Colors.Gray;
            float boxPad = 20f; 

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                float fontSize = (float)(baseFontSize * block.FontSizeScale);
                float rubyFontSize = fontSize * 0.5f;
                float measureWidth = fontSize * 2f;

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

                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var tcyRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();
                var highlightRanges = new List<(int start, int length)>();
                var mathRanges = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(NormalizeVerticalText(s));
                    else if (inline is AozoraRuby ruby)
                    {
                        var normBase = NormalizeVerticalText(ruby.BaseText);
                        sb.Append(normBase);
                        rubyRanges.Add((start, normBase.Length, ruby.RubyText));
                        if (ruby.IsBold) boldRanges.Add((start, normBase.Length));
                    }
                    else if (inline is AozoraBold bold)
                    {
                        var normText = NormalizeVerticalText(bold.Text);
                        sb.Append(normText);
                        boldRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraItalic italic)
                    {
                        var normText = NormalizeVerticalText(italic.Text);
                        sb.Append(normText);
                        italicRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraCode code) sb.Append(NormalizeVerticalText(code.Text));
                    else if (inline is AozoraHighlight highlight)
                    {
                        var normText = NormalizeVerticalText(highlight.Text);
                        sb.Append(normText);
                        highlightRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraMath math)
                    {
                        var rendered = NormalizeVerticalText(KatexStandaloneRenderer.RenderToText(math.Text));
                        sb.Append(rendered);
                        mathRanges.Add((start, rendered.Length));
                        if (math.IsBold) boldRanges.Add((start, rendered.Length));
                    }
                    else if (inline is AozoraTCY tcy)
                    {
                        var normText = NormalizeVerticalText(tcy.Text);
                        sb.Append(normText);
                        tcyRanges.Add((start, normText.Length));
                        if (tcy.IsBold) boldRanges.Add((start, normText.Length));
                    }
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }

                if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
                {
                    foreach (var row in block.TableRows) sb.AppendLine(string.Join(" | ", row));
                }

                string blockText = sb.ToString();

                float indentY = (float)(block.BlockIndentChars * fontSize);
                float actualDrawHeight = Math.Max(fontSize, drawHeight - indentY);
                using var textLayout = new CanvasTextLayout(ds, blockText, format, measureWidth, actualDrawHeight);
                ApplyVerticalBracketSpacing(ds, format, textLayout, blockText, fontSize);
                
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, FontWeights.Bold);
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);
                foreach (var r in mathRanges)
                {
                    textLayout.SetFontFamily(r.start, r.length, "Cambria Math");
                    textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);
                }

                var bounds = textLayout.LayoutBounds;
                float currentLineThickness = (float)bounds.Width;
                if (block.IsBlankLine) currentLineThickness *= 0.5f;

                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                
                float drawY = startY + (float)block.Margin.Top + indentY;
                if (block.Alignment == TextAlignment.Center) drawY = (float)((canvasSize.Height - (float)bounds.Height) / 2);
                else if (block.Alignment == TextAlignment.Right) drawY = (float)(canvasSize.Height - (float)bounds.Height - marginBottom);

                float currentH = (float)bounds.Height;
                if (block.IsBlankLine && currentH < fontSize) currentH = fontSize;

                if (isKeigakomi)
                {
                    if (!isBoxing)
                    {
                        currentX -= boxPad;
                        isBoxing = true;
                        boxRight = currentX;
                        boxLeft = currentX - currentLineThickness;
                        boxTop = drawY + (float)bounds.Y;
                        boxBottom = drawY + (float)bounds.Y + currentH;
                        boxColor = block.BorderColor ?? Colors.Gray;
                    }
                    else
                    {
                        boxLeft = currentX - currentLineThickness;
                        boxTop = Math.Min(boxTop, drawY + (float)bounds.Y);
                        boxBottom = Math.Max(boxBottom, drawY + (float)bounds.Y + currentH);
                    }
                }
                else if (!isKeigakomi && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                    currentX -= boxPad;
                }

                float drawX = currentX - (float)(bounds.X + bounds.Width);

                DrawRangeBackgrounds(ds, textLayout, highlightRanges, drawX, drawY, ColorHelper.FromArgb(120, 255, 230, 96), fontSize);
                DrawRangeBackgrounds(
                    ds,
                    textLayout,
                    SearchHighlightService.FindTextRanges(blockText, searchQuery)
                        .Select(range => (start: range.Start, length: range.Length))
                        .ToList(),
                    drawX,
                    drawY,
                    SearchHighlightService.HighlightColor,
                    fontSize);
                ds.DrawTextLayout(textLayout, drawX, drawY, textColor);

                using var rubyFormat = new CanvasTextFormat
                {
                    FontSize = rubyFontSize,
                    FontFamily = defaultFontFamily,
                    FontWeight = getFontWeight(defaultFontFamily),
                    Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.NoWrap
                };

                var rubyRenderInfos = new List<VerticalRubyRenderInfo>();
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;
                        float rubyX = drawX + (float)charBounds.Left + (float)charBounds.Width + (rubyFontSize * 2.2f);
                        float rubyY = drawY + (float)charBounds.Top; 

                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, rubyFontSize * 1.5f);
                        if (block.IsBold || boldRanges.Any(br => ruby.start >= br.start && ruby.start < br.start + br.length))
                        {
                            rubyLayout.SetFontWeight(0, ruby.rubyText.Length, FontWeights.Bold);
                        }

                        float rubyHeight = (float)rubyLayout.LayoutBounds.Height;
                        float charHeight = (float)charBounds.Height;
                        float idealTop = rubyY + (charHeight - rubyHeight) / 2;

                        rubyRenderInfos.Add(new VerticalRubyRenderInfo
                        {
                            Layout = rubyLayout,
                            IdealY = idealTop,
                            Height = rubyHeight,
                            X = rubyX,
                            Y = idealTop 
                        });
                    }
                }

                ResolveVerticalRubyOverlaps(rubyRenderInfos);

                foreach (var info in rubyRenderInfos)
                {
                    ds.DrawTextLayout(info.Layout, info.X, info.Y, textColor);
                    info.Layout.Dispose(); 
                }

                float spacing = fontSize * (block.IsBlankLine ? 0.2f : 0.6f) + (float)block.Margin.Bottom; 
                currentX -= (currentLineThickness + spacing);

                if (i == blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }

        private static void DrawRangeBackgrounds(
            CanvasDrawingSession ds,
            CanvasTextLayout layout,
            List<(int start, int length)> ranges,
            float drawX,
            float drawY,
            Color color,
            float fontSize)
        {
            foreach (var range in ranges)
            {
                if (range.length <= 0) continue;
                var regions = layout.GetCharacterRegions(range.start, range.length);
                foreach (var region in regions)
                {
                    var bounds = region.LayoutBounds;
                    float x = drawX + (float)bounds.Left + fontSize * 0.15f;
                    float y = drawY + (float)bounds.Top - 2f;
                    float width = Math.Max(fontSize * 0.7f, (float)bounds.Width * 0.72f);
                    float height = Math.Max(2f, (float)bounds.Height + 4f);
                    ds.FillRectangle(x, y, width, height, color);
                }
            }
        }
    }
}
