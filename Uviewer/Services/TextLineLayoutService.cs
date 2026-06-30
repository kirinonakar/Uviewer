using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextLineStyle
    {
        public TextLineStyle(double fontSize, string fontFamily, Brush foreground, double maxWidth)
        {
            FontSize = fontSize;
            FontFamily = fontFamily;
            Foreground = foreground;
            MaxWidth = maxWidth;
        }

        public double FontSize { get; }
        public string FontFamily { get; }
        public Brush Foreground { get; }
        public double MaxWidth { get; }
    }

    public sealed class TextLineLayoutService
    {
        public const int PlainTextLockedMaxLineLength = 2048;

        public static string[] SplitNormalizedLines(string content, int maxLineLength = 0)
        {
            if (maxLineLength <= 0)
            {
                return content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            }

            var lines = new List<string>();
            int lineStart = 0;
            int index = 0;

            while (index < content.Length)
            {
                char current = content[index];
                if (current == '\r' || current == '\n')
                {
                    AddWrappedSegments(lines, content, lineStart, index - lineStart, maxLineLength);

                    if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                    {
                        index++;
                    }

                    index++;
                    lineStart = index;
                    continue;
                }

                index++;
            }

            AddWrappedSegments(lines, content, lineStart, content.Length - lineStart, maxLineLength);
            return lines.ToArray();
        }

        public List<TextLine> CreatePlainLines(IEnumerable<string> lines, TextLineStyle style)
        {
            var result = new List<TextLine>();

            foreach (var line in lines)
            {
                result.Add(CreatePlainLine(line, style));
            }

            return result;
        }

        public List<TextLine> CreatePlainLines(IReadOnlyCollection<string> lines, TextLineStyle style)
        {
            var result = new List<TextLine>(lines.Count);

            foreach (var line in lines)
            {
                result.Add(CreatePlainLine(line, style));
            }

            return result;
        }

        public TextLine CreateStyledLine(string content, TextLineStyle style)
        {
            var line = CreatePlainLine(content, style);
            AozoraParserService.ApplySimpleAozoraStyling(line, style.FontSize);
            return line;
        }

        public async Task UpdateLinesAsync(List<TextLine> lines, TextLineStyle style)
        {
            if (lines.Count > 1000)
            {
                await Task.Run(() => UpdateLines(lines, style));
                return;
            }

            UpdateLines(lines, style);
        }

        public double CalculateReadableMaxWidth(double textAreaWidth, double fontSize)
        {
            double containerWidth = textAreaWidth > 0
                ? textAreaWidth - 80
                : 800;

            double limitedWidth = 42 * fontSize;
            return Math.Max(100, Math.Min(containerWidth, limitedWidth));
        }

        public Windows.UI.Text.FontWeight GetFontWeightForFamily(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily)) return FontWeights.Normal;
            if (fontFamily.Contains("Yu Gothic", StringComparison.OrdinalIgnoreCase) ||
                fontFamily.Contains("游ゴシック", StringComparison.OrdinalIgnoreCase))
            {
                return FontWeights.Medium;
            }

            return FontWeights.Normal;
        }

        private static TextLine CreatePlainLine(string content, TextLineStyle style)
        {
            return new TextLine
            {
                Content = content,
                FontSize = style.FontSize,
                FontFamily = style.FontFamily,
                Foreground = style.Foreground,
                MaxWidth = style.MaxWidth
            };
        }

        private static void UpdateLines(List<TextLine> lines, TextLineStyle style)
        {
            foreach (var line in lines)
            {
                line.FontSize = style.FontSize;
                line.FontFamily = style.FontFamily;
                line.Foreground = style.Foreground;
                line.MaxWidth = style.MaxWidth;
            }
        }

        private static void AddWrappedSegments(List<string> lines, string content, int start, int length, int maxLineLength)
        {
            if (length <= maxLineLength)
            {
                lines.Add(content.Substring(start, length));
                return;
            }

            int remainingStart = start;
            int remainingEnd = start + length;

            while (remainingEnd - remainingStart > maxLineLength)
            {
                int hardEnd = remainingStart + maxLineLength;
                int splitEnd = FindReadableSplitEnd(content, remainingStart, hardEnd, maxLineLength);
                lines.Add(content.Substring(remainingStart, splitEnd - remainingStart));
                remainingStart = splitEnd;
            }

            lines.Add(content.Substring(remainingStart, remainingEnd - remainingStart));
        }

        private static int FindReadableSplitEnd(string content, int start, int hardEnd, int maxLineLength)
        {
            int lowerBound = Math.Max(start + 1, hardEnd - (maxLineLength / 2));

            for (int i = hardEnd - 1; i >= lowerBound; i--)
            {
                char c = content[i];
                if (char.IsWhiteSpace(c) ||
                    c == ',' ||
                    c == ';' ||
                    c == '{' ||
                    c == '}' ||
                    c == '[' ||
                    c == ']')
                {
                    return i + 1;
                }
            }

            return hardEnd;
        }
    }
}
