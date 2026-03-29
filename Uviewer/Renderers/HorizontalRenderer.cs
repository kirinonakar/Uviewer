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
        public static void RenderBlocks(
            CanvasDrawingSession ds,
            List<AozoraBindingModel> blocks,
            Color textColor,
            float marginLeft,
            float marginTop,
            float maxWidth,
            double baseFontSize,
            string defaultFontFamily,
            Func<string, FontWeight> getFontWeight)
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

                // 1. 테이블 렌더링
                if (block.IsTable && block.TableRows != null && block.TableRows.Count > 0)
                {
                    var row = block.TableRows[0];
                    int colCount = row.Count;
                    int r = block.TableRowIndex;
                    bool isHeader = (r == 0);
                    bool isFirstOnPage = (i == 0) || !blocks[i - 1].IsTable;

                    float tableIndent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                    float tableMaxWidth = maxWidth - tableIndent;
                    float tableDrawX = marginLeft + tableIndent;
                    float colWidth = tableMaxWidth / colCount;

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
                    var cellLayouts = new List<CanvasTextLayout>();

                    foreach (var cellText in row)
                    {
                        var parsed = ParseTableInline(cellText);
                        var cellLayout = new CanvasTextLayout(ds, parsed.text, tableFormat, Math.Max(10, colWidth - 20), 0.0f);
                        cellLayout.Options = CanvasDrawTextOptions.EnableColorFont;
                        foreach (var br in parsed.boldRanges)
                            cellLayout.SetFontWeight(br.start, br.length, FontWeights.Bold);
                            
                        cellLayouts.Add(cellLayout);
                        float h = (float)cellLayout.LayoutBounds.Height;
                        if (h > maxCellHeight) maxCellHeight = h;
                    }

                    float rowHeight = maxCellHeight + 20f;

                    if (isHeader)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, ColorHelper.FromArgb(30, 128, 128, 128));
                    else if (r % 2 == 1)
                        ds.FillRectangle(tableDrawX, currentY, tableMaxWidth, rowHeight, ColorHelper.FromArgb(10, 128, 128, 128));

                    for (int c = 0; c < colCount; c++)
                    {
                        float cellX = tableDrawX + (c * colWidth);
                        ds.DrawTextLayout(cellLayouts[c], cellX + 10, currentY + 10, textColor);
                        cellLayouts[c].Dispose();
                        ds.DrawLine(cellX, currentY, cellX, currentY + rowHeight, Colors.Gray, 1f); 
                    }
                    
                    ds.DrawLine(tableDrawX + tableMaxWidth, currentY, tableDrawX + tableMaxWidth, currentY + rowHeight, Colors.Gray, 1f);

                    currentY += rowHeight;
                    ds.DrawLine(tableDrawX, currentY, tableDrawX + tableMaxWidth, currentY, Colors.Gray, isHeader ? 2f : 1f);

                    if (r == block.TableRowCount - 1)
                        currentY += 20f; 

                    continue; 
                }

                // 2. 일반 텍스트 블록 렌더링
                float lineSpacing = (block.IsTable || block.HeadingLevel > 0) ? fontSize * 1.3f : fontSize * 2.1f;

                StringBuilder sb = new StringBuilder();
                var rubyRanges = new List<(int start, int length, string rubyText)>();
                var boldRanges = new List<(int start, int length)>();
                var italicRanges = new List<(int start, int length)>();

                foreach (var inline in block.Inlines)
                {
                    int start = sb.Length;
                    if (inline is string s) sb.Append(s);
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
                    else if (inline is AozoraCode code) sb.Append(code.Text);
                    else if (inline is AozoraTCY tcy)
                    {
                        sb.Append(tcy.Text);
                        if (tcy.IsBold) boldRanges.Add((start, tcy.Text.Length));
                    }
                    else if (inline is AozoraLineBreak) sb.Append("\n");
                }

                string blockText = sb.ToString();
                if (string.IsNullOrEmpty(blockText)) blockText = " "; // 빈 블록 방어

                float indent = (float)(block.BlockIndent > 0 ? block.BlockIndent : block.Margin.Left);
                float actualMaxWidth = maxWidth - indent - (float)block.Margin.Right;
                if (actualMaxWidth < 100) actualMaxWidth = 100;

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

                using var textLayout = new CanvasTextLayout(ds, blockText, format, actualMaxWidth, 0.0f);
                textLayout.Options = CanvasDrawTextOptions.EnableColorFont; 
                if (block.IsBold) textLayout.SetFontWeight(0, blockText.Length, FontWeights.Bold);
                foreach (var r in boldRanges) textLayout.SetFontWeight(r.start, r.length, FontWeights.Bold);
                foreach (var r in italicRanges) textLayout.SetFontStyle(r.start, r.length, Windows.UI.Text.FontStyle.Italic);

                int lineCount = textLayout.LineCount;
                float currentBlockHeight = block.IsBlankLine ? lineSpacing * 0.3f : lineCount * lineSpacing;

                var bounds = textLayout.LayoutBounds;
                float drawX = marginLeft + indent;
                if (block.Alignment == TextAlignment.Center) drawX = marginLeft + (maxWidth - (float)bounds.Width) / 2;
                else if (block.Alignment == TextAlignment.Right) drawX = marginLeft + maxWidth - (float)bounds.Width - (float)block.Margin.Right;

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

                    ds.FillRectangle(drawX - padLeft, bgTop, currentW + padLeft + padRight, bgHeight, block.BackgroundColor.Value);
                }

                // 5. 텍스트 본문 그리기
                ds.DrawTextLayout(textLayout, drawX, currentY, textColor);

                // 6. 밑줄(헤딩) 및 좌측 선(인용구) 그리기
                if (!isKeigakomi && block.BorderColor != null)
                {
                    var drawBounds = textLayout.DrawBounds;
                    float borderBottomY = currentY + (float)drawBounds.Bottom + 4f;
                    
                    if (block.BorderThickness.Bottom > 0)
                    {
                        ds.DrawLine(drawX, borderBottomY, drawX + currentW, borderBottomY, block.BorderColor.Value, (float)block.BorderThickness.Bottom);
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
                foreach (var ruby in rubyRanges)
                {
                    var regions = textLayout.GetCharacterRegions(ruby.start, ruby.length);
                    if (regions.Length > 0)
                    {
                        var charBounds = regions[0].LayoutBounds;
                        float lineBoxTop = currentY + (float)charBounds.Top;
                        float rubyY = lineBoxTop - (rubyFontSize * 3f);
                        float charCenter = drawX + (float)charBounds.Left + (float)charBounds.Width / 2.0f;

                        var rubyLayout = new CanvasTextLayout(ds, ruby.rubyText, rubyFormat, 0.0f, 0.0f);
                        rubyLayout.Options = CanvasDrawTextOptions.EnableColorFont; 
                        
                        if (block.IsBold || boldRanges.Any(br => ruby.start >= br.start && ruby.start < br.start + br.length))
                        {
                            rubyLayout.SetFontWeight(0, ruby.rubyText.Length, FontWeights.Bold);
                        }

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

                currentY += currentBlockHeight + (float)block.Margin.Bottom;

                if (i == blocks.Count - 1 && isBoxing)
                {
                    ds.DrawRectangle(boxLeft - boxPad, boxTop - boxPad, boxRight - boxLeft + boxPad * 2, boxBottom - boxTop + boxPad * 2, boxColor, 1.5f);
                    isBoxing = false;
                }
            }
        }
    }
}
