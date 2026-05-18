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
        public static string[] SplitNormalizedLines(string content)
        {
            return content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
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
    }
}
