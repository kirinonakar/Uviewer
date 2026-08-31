using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextLinePresenterService
    {
        private readonly TextLineLayoutService _layoutService;

        public TextLinePresenterService(TextLineLayoutService layoutService)
        {
            _layoutService = layoutService;
        }

        public void ApplyToTextBlock(
            TextBlock textBlock,
            TextLine line,
            int lineNumber,
            Action<TextBlock, string, int> applySearchHighlights)
        {
            ApplyStyle(textBlock, line);
            ApplyContent(textBlock, line.Content);
            applySearchHighlights(textBlock, line.Content, lineNumber);
        }

        public void RefreshRealizedElements(ItemsRepeater repeater, IReadOnlyList<TextLine> lines)
        {
            // Preserve the repeater's layout/scroll anchor and progressive ItemsSource.
            // Unrealized lines receive their updated style in ElementPrepared.
            int childCount = VisualTreeHelper.GetChildrenCount(repeater);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(repeater, i) is not TextBlock textBlock) continue;

                int index = repeater.GetElementIndex(textBlock);
                if (index >= 0 && index < lines.Count)
                {
                    ApplyStyle(textBlock, lines[index]);
                }
            }
        }

        private void ApplyStyle(TextBlock textBlock, TextLine line)
        {
            textBlock.FontSize = line.FontSize;
            if (textBlock.FontFamily.Source != line.FontFamily)
            {
                textBlock.FontFamily = new FontFamily(line.FontFamily);
            }
            textBlock.FontWeight = _layoutService.GetFontWeightForFamily(line.FontFamily);
            textBlock.Foreground = line.Foreground;
            textBlock.MaxWidth = line.MaxWidth;
            textBlock.TextAlignment = line.TextAlignment;
            textBlock.LineHeight = Math.Ceiling(line.FontSize * 1.8);
            textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            textBlock.Margin = line.Margin;
            textBlock.Padding = line.Padding;

            if (string.IsNullOrEmpty(line.Content))
            {
                textBlock.MinHeight = textBlock.LineHeight;
            }
            else
            {
                textBlock.ClearValue(FrameworkElement.MinHeightProperty);
            }
        }

        private static void ApplyContent(TextBlock textBlock, string content)
        {
            if (!content.Contains("**"))
            {
                textBlock.Inlines.Clear();
                textBlock.Text = content;
                return;
            }

            textBlock.Text = "";
            textBlock.Inlines.Clear();
            var parts = Regex.Split(content, @"(\*\*.*?\*\*)");

            foreach (var part in parts)
            {
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4)
                {
                    string boldText = part.Substring(2, part.Length - 4);
                    textBlock.Inlines.Add(new Run { Text = boldText, FontWeight = FontWeights.Bold });
                }
                else if (!string.IsNullOrEmpty(part))
                {
                    textBlock.Inlines.Add(new Run { Text = part });
                }
            }
        }
    }
}
