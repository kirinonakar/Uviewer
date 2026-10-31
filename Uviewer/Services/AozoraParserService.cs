using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class AozoraParserService
    {
        public static string PreprocessAozoraBold(string text)
        {
            string boldStartTag = @"［＃(?:ここから太字)］";
            string boldEndTag = @"［＃(?:ここで太字終わり)］";
            return Regex.Replace(text, $"{boldStartTag}(.*?){boldEndTag}", (m) =>
            {
                string inner = m.Groups[1].Value;
                var startRegex = new Regex(boldStartTag);
                var parts = startRegex.Split(inner);
                if (parts.Length <= 1) return $"@@BOLD_START@@{inner}@@BOLD_END@@";
                string prefix = string.Join("", parts.Take(parts.Length - 1));
                string boldContent = parts.Last();
                return $"{prefix}@@BOLD_START@@{boldContent}@@BOLD_END@@";
            }, RegexOptions.Singleline);
        }

        public static (List<AozoraBindingModel> Blocks, int SourceLineCount) ParseAozoraContent(string text, double baseFontSize)
        {
            text = PreprocessAozoraBold(text);
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var blocks = ParseAozoraLines(lines, 1, baseFontSize);
            return (blocks, lines.Length);
        }

        public static List<AozoraBindingModel> ParseAozoraLines(string[] lines, int startLineOffset, double baseFontSize)
        {
            var blocks = new List<AozoraBindingModel>();
            bool lastWasEmpty = false;
            bool currentBold = false;
            double currentIndentEm = 0;
            bool inKeigakomi = false;
            int smallTextLevel = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var content = line.Replace("\t", "    ").TrimEnd();

                if (string.IsNullOrEmpty(content))
                {
                    if (lastWasEmpty) continue;

                    var blankModel = new AozoraBindingModel
                    {
                        Inlines = { "" },
                        Margin = new Thickness(0),
                        SourceLineNumber = startLineOffset + i,
                        BlockIndent = currentIndentEm * baseFontSize,
                        BlockIndentChars = currentIndentEm,
                        IsBlankLine = true
                    };

                    if (inKeigakomi)
                    {
                        blankModel.BorderColor = Colors.Gray;
                        blankModel.BorderThickness = new Thickness(1);
                        blankModel.Padding = new Thickness(10);
                    }

                    blocks.Add(blankModel);
                    lastWasEmpty = true;
                    continue;
                }
                lastWasEmpty = false;

                bool isPageBreak = false;
                if (Regex.IsMatch(content, @"［＃(?:改ページ|改頁)］"))
                {
                    isPageBreak = true;
                    content = Regex.Replace(content, @"［＃(?:改ページ|改頁)］", "");
                }

                var indentMatch = Regex.Match(content, @"［＃ここから(?:(\d+)|([０-９]+))字下げ］");
                if (indentMatch.Success)
                {
                    string val = indentMatch.Groups[1].Value;
                    if (string.IsNullOrEmpty(val)) val = indentMatch.Groups[2].Value;
                    currentIndentEm = ConvertFullWidthToDouble(val);
                    content = content.Replace(indentMatch.Value, "");
                }
                if (content.Contains("［＃ここで字下げ終わり］"))
                {
                    currentIndentEm = 0;
                    content = content.Replace("［＃ここで字下げ終わり］", "");
                }

                if (content.Contains("［＃ここから罫囲み］"))
                {
                    inKeigakomi = true;
                    content = content.Replace("［＃ここから罫囲み］", "");
                }
                bool justExitedKeigakomi = false;
                if (content.Contains("［＃ここで罫囲み終わり］"))
                {
                    inKeigakomi = false;
                    justExitedKeigakomi = true;
                    content = content.Replace("［＃ここで罫囲み終わり］", "");
                }

                if (content.Contains("［＃ここから２段階小さな文字］"))
                {
                    smallTextLevel = 2;
                    content = content.Replace("［＃ここから２段階小さな文字］", "");
                }
                if (content.Contains("［＃ここで小さな文字終わり］"))
                {
                    smallTextLevel = 0;
                    content = content.Replace("［＃ここで小さな文字終わり］", "");
                }

                var boutenMatches = Regex.Matches(content, @"［＃「(.+?)」に傍点］");
                foreach (Match m in boutenMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(targetWord)) targetWord = m.Groups[2].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, (match) =>
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (char c in targetWord) sb.Append($"{{{{RUBY|{c}|﹅}}}}");
                        return sb.ToString();
                    });
                }

                var tcyMatches = Regex.Matches(content, @"［＃「(.+?)」は縦中横］");
                foreach (Match m in tcyMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, $"{{{{TCY|{targetWord}}}}}");
                }

                var boldSpecificMatches = Regex.Matches(content, @"［＃「(.+?)」[は]太字］");
                foreach (Match m in boldSpecificMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, $"@@BOLD_START@@{targetWord}@@BOLD_END@@");
                }

                var model = new AozoraBindingModel
                {
                    SourceLineNumber = startLineOffset + i,
                    IsPageBreak = isPageBreak,
                    BlockIndent = currentIndentEm * baseFontSize,
                    BlockIndentChars = currentIndentEm
                };
                model.Margin = new Thickness(0);

                if (inKeigakomi)
                {
                    model.BorderColor = Colors.Gray;
                    model.BorderThickness = new Thickness(1);
                    model.Padding = new Thickness(10);
                }
                if (smallTextLevel == 2) model.FontSizeScale = 0.85;

                if (content.Contains("［＃大見出し］") || content.StartsWith("# "))
                {
                    model.FontSizeScale = 1.5;
                    content = content.Replace("［＃大見出し］", "").TrimStart('#', ' ');
                    model.HeadingLevel = 1;
                    model.HeadingText = Regex.Replace(content, @"［＃[^］]+］|\[.*?\]", "").Trim();
                }
                else if (content.Contains("［＃中見出し］") || content.StartsWith("## "))
                {
                    model.FontSizeScale = 1.25;
                    content = content.Replace("［＃中見出し］", "").TrimStart('#', ' ');
                    model.HeadingLevel = 2;
                    model.HeadingText = Regex.Replace(content, @"［＃[^］]+］|\[.*?\]", "").Trim();
                }
                else if (content.Contains("［＃小見出し］") || content.StartsWith("### "))
                {
                    model.FontSizeScale = 1.1;
                    content = content.Replace("［＃小見出し］", "").TrimStart('#', ' ');
                    model.HeadingLevel = 3;
                    model.HeadingText = Regex.Replace(content, @"［＃[^］]+］|\[.*?\]", "").Trim();
                }

                if (content.Contains("［＃センター］"))
                {
                    model.Alignment = TextAlignment.Center;
                    content = content.Replace("［＃センター］", "");
                }
                else if (content.Contains("［＃地から３字上げ］"))
                {
                    model.Alignment = TextAlignment.Right;
                    model.Margin = new Thickness(0, 0, 3 * baseFontSize, 0);
                    model.RightMarginChars = 3;
                    content = content.Replace("［＃地から３字上げ］", "");
                }

                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");
                content = Regex.Replace(content, @"［＃[^］]+］", "");

                content = Regex.Replace(content, @"｜(.+?)《(.+?)》", (m) =>
                {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });
                content = Regex.Replace(content, @"(.)《'》", "{{RUBY|$1|・}}");
                content = Regex.Replace(content, @"(.)《’》", "{{RUBY|$1|・}}");
                content = Regex.Replace(content, @"([\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF々]+)《(.+?)》", (m) =>
                {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "@@BOLD_START@@$2@@BOLD_END@@");

                string pattern = @"(\{\{RUBY\|.*?\|.*?\}\}|\{\{IMG\|.*?\}\}|\{\{TCY\|.*?\}\}|@@BOLD_START@@|@@BOLD_END@@)";
                var parts = Regex.Split(content, pattern);
                bool inlineBold = currentBold;

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part == "@@BOLD_START@@") { inlineBold = true; continue; }
                    if (part == "@@BOLD_END@@") { inlineBold = false; continue; }

                    if (part.StartsWith("{{RUBY|"))
                    {
                        var inner = part.Trim('{', '}'); 
                        var p = inner.Split('|');
                        if (p.Length >= 3)
                        {
                            model.Inlines.Add(new AozoraRuby { BaseText = p[1], RubyText = p[2], IsBold = inlineBold });
                        }
                    }
                    else if (part.StartsWith("{{IMG|"))
                    {
                        var src = part.Substring(6, part.Length - 8);
                        int commaIdx = src.IndexOfAny(new[] { ',', '，', '、' });
                        if (commaIdx >= 0) src = src.Substring(0, commaIdx).Trim();

                        model.Inlines.Add(new AozoraImage { Source = src });
                    }
                    else if (part.StartsWith("{{TCY|"))
                    {
                        var textStr = part.Substring(6, part.Length - 8);
                        model.Inlines.Add(new AozoraTCY { Text = textStr, IsBold = inlineBold });
                    }
                    else
                    {
                        if (inlineBold) model.Inlines.Add(new AozoraBold { Text = part });
                        else model.Inlines.Add(part);
                    }
                }

                currentBold = inlineBold;
                blocks.Add(model);

                if (justExitedKeigakomi)
                {
                    blocks.Add(new AozoraBindingModel
                    {
                        Inlines = { "" },
                        Margin = new Thickness(0),
                        SourceLineNumber = startLineOffset + i,
                        BlockIndent = 0,
                        IsBlankLine = true
                    });
                    lastWasEmpty = true;
                }
            }

            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(SplitBlockBySentences(block));
            }

            return splitBlocks;
        }

        public static List<AozoraBindingModel> SplitBlockBySentences(AozoraBindingModel originalBlock)
        {
            bool isKeigakomi = originalBlock.BorderColor != null ||
                               originalBlock.BorderThickness.Top > 0 ||
                               originalBlock.BorderThickness.Left > 0 ||
                               originalBlock.BorderThickness.Bottom > 0 ||
                               originalBlock.BorderThickness.Right > 0;

            if (originalBlock.HeadingLevel > 0 || originalBlock.HasImage || originalBlock.IsTable || originalBlock.IsPageBreak || originalBlock.IsBlankLine || isKeigakomi)
            {
                return new List<AozoraBindingModel> { originalBlock };
            }

            char[] openBrackets = { '「', '『', '(', '<', '《', '〈', '【', '［', '“', '‘' };
            char[] closeBrackets = { '」', '』', ')', '>', '》', '〉', '】', '］', '”', '’' };
            char[] terminators = { '。', '！', '？', '.', '!', '?', '、', ',' };

            var result = new List<AozoraBindingModel>();
            var currentBlock = CloneBlockProperties(originalBlock);
            bool isFirst = true;

            for (int i = 0; i < originalBlock.Inlines.Count; i++)
            {
                var inline = originalBlock.Inlines[i];

                if (inline is string text)
                {
                    int start = 0;
                    for (int j = 0; j < text.Length; j++)
                    {
                        char c = text[j];
                        bool isOpen = Array.IndexOf(openBrackets, c) >= 0;
                        bool isClose = Array.IndexOf(closeBrackets, c) >= 0;
                        bool isTerminator = Array.IndexOf(terminators, c) >= 0;

                        int splitPos = -1;

                        if (isOpen && j > start) splitPos = j; 
                        else if (isClose)
                        {
                            while (j + 1 < text.Length && Array.IndexOf(closeBrackets, text[j + 1]) >= 0) j++;
                            splitPos = j + 1;
                        }
                        else if (isTerminator)
                        {
                            if (c == '.' && j > 0 && j < text.Length - 1 && char.IsDigit(text[j - 1]) && char.IsDigit(text[j + 1])) { /* 무시 */ }
                            else
                            {
                                // [수정] 연속된 !, ?, ！, ？ 기호는 분리하지 않고 한 덩어리로 묶음 (NormalizeVerticalText에서 !! 등을 처리하기 위함)
                                while (j + 1 < text.Length && (text[j + 1] == '!' || text[j + 1] == '?' || text[j + 1] == '！' || text[j + 1] == '？'))
                                {
                                    j++;
                                }

                                while (j + 1 < text.Length && Array.IndexOf(closeBrackets, text[j + 1]) >= 0) j++;
                                splitPos = j + 1;
                            }
                        }
                        else if (j - start + 1 >= 20)
                        {
                            if (j + 1 < text.Length)
                            {
                                char nextC = text[j + 1];
                                if (Array.IndexOf(closeBrackets, nextC) < 0 && Array.IndexOf(terminators, nextC) < 0) splitPos = j + 1;
                            }
                            else splitPos = j + 1;
                        }

                        if (splitPos != -1)
                        {
                            bool isLastInText = (splitPos == text.Length);
                            bool isLastInline = (i == originalBlock.Inlines.Count - 1);

                            if (!(isLastInText && isLastInline))
                            {
                                string part = text.Substring(start, splitPos - start);
                                if (!string.IsNullOrEmpty(part)) currentBlock.Inlines.Add(part);

                                if (currentBlock.Inlines.Count > 0)
                                {
                                    if (!isFirst) currentBlock.IsParagraphContinuation = true;
                                    result.Add(currentBlock);
                                    isFirst = false;
                                }

                                currentBlock = CloneBlockProperties(originalBlock);
                                currentBlock.BlockIndent = 0;
                                currentBlock.BlockIndentChars = 0;
                                currentBlock.Margin = new Thickness(0);

                                start = splitPos;
                                j = splitPos - 1; 
                            }
                        }
                    }

                    if (start < text.Length)
                    {
                        string left = text.Substring(start);
                        if (!string.IsNullOrEmpty(left)) currentBlock.Inlines.Add(left);
                    }
                }
                else
                {
                    currentBlock.Inlines.Add(inline);
                    if (currentBlock.Inlines.Count > 0 && i < originalBlock.Inlines.Count - 1)
                    {
                        if (!isFirst) currentBlock.IsParagraphContinuation = true;
                        result.Add(currentBlock);
                        isFirst = false;
                        
                        currentBlock = CloneBlockProperties(originalBlock);
                        currentBlock.BlockIndent = 0;
                        currentBlock.BlockIndentChars = 0;
                        currentBlock.Margin = new Thickness(0);
                    }
                }
            }

            if (currentBlock.Inlines.Count > 0)
            {
                if (!isFirst) currentBlock.IsParagraphContinuation = true;
                result.Add(currentBlock);
            }

            return result;
        }

        public static (List<AozoraBindingModel> Blocks, int SourceLineCount) ParseMarkdownContent(string text)
            => MarkdownDocumentParser.Parse(text);

        private static double ConvertFullWidthToDouble(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
                else sb.Append(c);
            }
            double.TryParse(sb.ToString(), out double result);
            return result;
        }

        // MainWindow.aozora.cs 에서도 범용적으로 사용하기 위해 Public Static 메서드로 둡니다.
        public static AozoraBindingModel CloneBlockProperties(AozoraBindingModel source, bool copyInlines = false)
        {
            var clone = new AozoraBindingModel
            {
                FontSizeScale = source.FontSizeScale,
                Alignment = source.Alignment,
                Margin = source.Margin,
                Padding = source.Padding,
                BorderColor = source.BorderColor,
                BorderThickness = source.BorderThickness,
                BackgroundColor = source.BackgroundColor,
                FontFamily = source.FontFamily,
                SourceLineNumber = source.SourceLineNumber,
                IsBold = source.IsBold,
                BlockIndent = source.BlockIndent,
                BlockIndentChars = source.BlockIndentChars,
                RightMarginChars = source.RightMarginChars,
                HeadingLevel = source.HeadingLevel,
                HeadingText = source.HeadingText,
                IsTable = source.IsTable,
                IsCodeBlock = source.IsCodeBlock,
                IsMarkdown = source.IsMarkdown,
                CodeBlockGroup = source.CodeBlockGroup,
                CodeBlockLineIndex = source.CodeBlockLineIndex,
                CodeBlockLineCount = source.CodeBlockLineCount,
                IsBlankLine = source.IsBlankLine,
                IsPageBreak = source.IsPageBreak,
                IsParagraphContinuation = source.IsParagraphContinuation,
                TableRowIndex = source.TableRowIndex,
                TableRowCount = source.TableRowCount,
                TableContentRows = source.TableContentRows,
                EpubChapterIndex = source.EpubChapterIndex,
                OriginalBlockIndex = source.OriginalBlockIndex,
                SearchSegments = source.SearchSegments
                    .Select(segment => new AozoraSearchSegment
                    {
                        BlockIndex = segment.BlockIndex,
                        Start = segment.Start,
                        Length = segment.Length
                    })
                    .ToList()
            };

            if (source.IsTable && source.TableRows != null)
            {
                clone.TableRows = source.TableRows.Select(row => new List<string>(row)).ToList();
            }

            if (copyInlines) clone.Inlines = new List<object>(source.Inlines);
            return clone;
        }

        public static void ApplySimpleAozoraStyling(Uviewer.Models.TextLine line, double baseFontSize)
        {
            string content = line.Content;

            if (content.Contains("［＃大見出し］"))
            {
                line.FontSize = baseFontSize * 1.5;
                content = content.Replace("［＃大見出し］", "");
            }
            if (content.Contains("［＃中見出し］"))
            {
                line.FontSize = baseFontSize * 1.25;
                content = content.Replace("［＃中見出し］", "");
            }
            if (content.Contains("［＃小見出し］"))
            {
                line.FontSize = baseFontSize * 1.1;
                content = content.Replace("［＃小見出し］", "");
            }
            if (content.Contains("［＃センター］"))
            {
                line.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center;
                content = content.Replace("［＃センター］", "");
            }
            if (content.Contains("［＃地から３字上げ］"))
            {
                line.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right;
                line.Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 60, 0); 
                content = content.Replace("［＃地から３字上げ］", "");
            }
            if (content.Contains("［＃ここから２字下げ］"))
            {
                line.Margin = new Microsoft.UI.Xaml.Thickness(40, 0, 0, 0);
                content = content.Replace("［＃ここから２字下げ］", "");
            }
            if (content.Contains("［＃ここから罫囲み］"))
            {
                line.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                line.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                line.Padding = new Microsoft.UI.Xaml.Thickness(10);
                content = content.Replace("［＃ここから罫囲み］", "");
            }
            if (content.Contains("［＃ここから２段階小さな文字］"))
            {
                line.FontSize = Math.Max(8, baseFontSize * 0.7);
                content = content.Replace("［＃ここから２段階小さな文字］", "");
            }

            content = Regex.Replace(content, @"［＃[^］]+］", ""); 
            line.Content = content;
        }

        public static string ParseHtml(string html)
        {
            string noScript = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            string noStyle = Regex.Replace(noScript, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            string textOnly = Regex.Replace(noStyle, @"<[^>]+>", "\n");
            textOnly = System.Net.WebUtility.HtmlDecode(textOnly);
            return Regex.Replace(textOnly, @"\n\s+\n", "\n\n");
        }
    }
}
