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
        {
            var blocks = new List<AozoraBindingModel>();
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            bool inCodeBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                string content = line;
                int sourceLine = i + 1;

                if (content.Trim().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue; 
                }

                if (inCodeBlock)
                {
                    var model = new AozoraBindingModel();
                    model.IsTable = true; 
                    model.FontFamily = "Consolas, Courier New, Monospace";
                    model.Inlines.Add(content);
                    model.BackgroundColor = Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128);
                    model.Padding = new Thickness(4, 0, 4, 0);
                    model.Margin = new Thickness(20, 0, 20, 0);
                    model.SourceLineNumber = sourceLine;
                    blocks.Add(model);
                    continue;
                }

                if (TryParseMarkdownDisplayMath(lines, ref i, sourceLine, out var mathModel))
                {
                    blocks.Add(mathModel);
                    continue;
                }

                if (content.Trim().StartsWith("|"))
                {
                    var tableLines = new List<string>();
                    int k = i;
                    while (k < lines.Length && lines[k].Trim().StartsWith("|"))
                    {
                        tableLines.Add(lines[k].Trim());
                        k++;
                    }

                    if (tableLines.Count >= 2) 
                    {
                        var allRows = new List<List<string>>();

                        foreach (var tLine in tableLines)
                        {
                            var trimLine = tLine.Trim('|');
                            if (Regex.IsMatch(trimLine, @"^[\-:\|\s]+$") && trimLine.Contains("-")) continue; 

                            var cells = tLine.Split('|').Select(c => c.Trim()).ToList();
                            if (cells.Count > 0 && string.IsNullOrEmpty(cells[0])) cells.RemoveAt(0);
                            if (cells.Count > 0 && string.IsNullOrEmpty(cells[cells.Count - 1])) cells.RemoveAt(cells.Count - 1);
                            allRows.Add(cells);
                        }

                        if (allRows.Count > 0)
                        {
                            int colCount = allRows.Max(r => r.Count);
                            for (int r = 0; r < allRows.Count; r++) {
                                while (allRows[r].Count < colCount) allRows[r].Add(""); 

                                var tableModel = new AozoraBindingModel 
                                { 
                                    IsTable = true,
                                    TableRows = new List<List<string>> { allRows[r] }, 
                                    TableRowIndex = r, 
                                    TableRowCount = allRows.Count, 
                                    Margin = new Thickness(0),
                                    SourceLineNumber = sourceLine + r
                                };

                                tableModel.Inlines.Add(" "); 
                                blocks.Add(tableModel);
                            }
                            i = k - 1; 
                            continue;
                        }
                    }
                }

                content = content.TrimEnd();

                if (string.IsNullOrEmpty(content))
                {
                    blocks.Add(new AozoraBindingModel { Inlines = { "" }, Margin = new Thickness(0), IsBlankLine = true, SourceLineNumber = sourceLine });
                    continue;
                }

                var blockModel = new AozoraBindingModel();
                blockModel.SourceLineNumber = sourceLine;
                blockModel.Margin = new Thickness(0);

                int leadingIndent = 0;
                int spacesConsumed = 0;
                while (spacesConsumed < content.Length && (content[spacesConsumed] == ' ' || content[spacesConsumed] == '\t'))
                {
                    if (content[spacesConsumed] == '\t') leadingIndent += 4; 
                    else leadingIndent += 1;
                    spacesConsumed++;
                }
                
                string currentContent = content.TrimStart();
                blockModel.Margin = new Thickness(leadingIndent * 8, 0, 0, 0); 

                if (currentContent.StartsWith("#"))
                {
                    int level = 0;
                    while (level < currentContent.Length && currentContent[level] == '#') level++;

                    if (level > 0 && level <= 6)
                    {
                        if (level == 1) blockModel.FontSizeScale = 2.0;
                        else if (level == 2) blockModel.FontSizeScale = 1.5;
                        else if (level == 3) blockModel.FontSizeScale = 1.25;
                        else blockModel.FontSizeScale = 1.1;

                        content = currentContent.Substring(level).TrimStart();
                        blockModel.HeadingLevel = level;
                        blockModel.HeadingText = Regex.Replace(content, @"[#\[\]]", "").Trim();

                        if (level == 1)
                        {
                            blockModel.BorderColor = Colors.LightGray;
                            blockModel.BorderThickness = new Thickness(0, 0, 0, 1);
                            blockModel.Margin = new Thickness(blockModel.Margin.Left, 0, 0, 8); 
                        }
                        else if (level == 2)
                        {
                            blockModel.BorderColor = Colors.LightGray;
                            blockModel.BorderThickness = new Thickness(0, 0, 0, 1);
                            blockModel.Margin = new Thickness(blockModel.Margin.Left, 0, 0, 10); 
                        }
                        else if (level >= 3)
                        {
                            blockModel.Margin = new Thickness(blockModel.Margin.Left, 0, 0, 10); 
                        }
                    }
                }
                else if (currentContent.StartsWith(">"))
                {
                    content = currentContent.TrimStart('>', ' ');
                    blockModel.Margin = new Thickness(20 + leadingIndent * 8, 0, 0, 0);
                    blockModel.BorderColor = Colors.Gray;
                    blockModel.BorderThickness = new Thickness(4, 0, 0, 0); 
                    blockModel.Padding = new Thickness(10, 0, 0, 0);
                    blockModel.Inlines.Add(new AozoraItalic { Text = "" });
                }
                else if (Regex.IsMatch(currentContent, @"^[\*\-]\s"))
                {
                    content = "• " + currentContent.Substring(2);
                    blockModel.Margin = new Thickness(20 + leadingIndent * 8, 0, 0, 0);
                }
                else if (Regex.IsMatch(currentContent, @"^\d+\.\s"))
                {
                    content = currentContent; 
                    blockModel.Margin = new Thickness(20 + leadingIndent * 8, 0, 0, 0);
                }
                else if (Regex.IsMatch(currentContent, @"^(\*{3,}|-{3,})$"))
                {
                    blockModel.Inlines.Add("");
                    blockModel.BorderColor = Colors.Gray;
                    blockModel.BorderThickness = new Thickness(0, 1, 0, 0);
                    blockModel.Margin = new Thickness(0, 10, 0, 10);
                    blocks.Add(blockModel);
                    continue;
                }
                else
                {
                    content = currentContent;
                }

                content = Regex.Replace(content, @"<br\s*/?>", "{{BR}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                foreach (var inline in ParseMarkdownInlines(content))
                {
                    blockModel.Inlines.Add(inline);
                }

                if (blockModel.Inlines.Count == 0) blockModel.Inlines.Add("");

                blocks.Add(blockModel);
            }

            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(SplitBlockBySentences(block));
            }

            return (splitBlocks, lines.Length);
        }

        private static bool TryParseMarkdownDisplayMath(string[] lines, ref int index, int sourceLine, out AozoraBindingModel model)
        {
            model = new AozoraBindingModel();
            string content = lines[index];
            string trimmed = content.Trim();
            bool bracketMath = trimmed.StartsWith(@"\[");

            if (!trimmed.StartsWith("$$") && !bracketMath) return false;

            string opener = bracketMath ? @"\[" : "$$";
            string closer = bracketMath ? @"\]" : "$$";
            string math = trimmed.Substring(opener.Length);
            bool closed = false;

            int sameLineClose = FindUnescaped(math, closer, 0);
            if (sameLineClose >= 0)
            {
                math = math.Substring(0, sameLineClose);
                closed = true;
            }
            else
            {
                var sb = new StringBuilder(math);
                int k = index + 1;
                while (k < lines.Length)
                {
                    string line = lines[k];
                    int close = FindUnescaped(line, closer, 0);
                    if (close >= 0)
                    {
                        sb.Append('\n');
                        sb.Append(line.Substring(0, close));
                        closed = true;
                        break;
                    }

                    sb.Append('\n');
                    sb.Append(line);
                    k++;
                }

                if (closed) index = k;
                math = sb.ToString();
            }

            if (!closed) return false;

            model = new AozoraBindingModel
            {
                SourceLineNumber = sourceLine,
                Alignment = TextAlignment.Center,
                FontSizeScale = 1.15,
                Margin = new Thickness(0, 8, 0, 12)
            };
            model.Inlines.Add(new AozoraMath { Text = math.Trim(), DisplayMode = true });
            return true;
        }

        private static List<object> ParseMarkdownInlines(string content)
        {
            var result = new List<object>();
            var text = new StringBuilder();

            void Flush()
            {
                if (text.Length == 0) return;
                result.Add(text.ToString());
                text.Clear();
            }

            for (int i = 0; i < content.Length; i++)
            {
                if (content.AsSpan(i).StartsWith("{{BR}}".AsSpan()))
                {
                    Flush();
                    result.Add(new AozoraLineBreak());
                    i += "{{BR}}".Length - 1;
                    continue;
                }

                if (content.AsSpan(i).StartsWith("{{IMG|".AsSpan()))
                {
                    int close = content.IndexOf("}}", i + 6, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        Flush();
                        result.Add(new AozoraImage { Source = content.Substring(i + 6, close - i - 6) });
                        i = close + 1;
                        continue;
                    }
                }

                if (content[i] == '`')
                {
                    int close = FindUnescaped(content, "`", i + 1);
                    if (close > i)
                    {
                        Flush();
                        result.Add(new AozoraCode { Text = content.Substring(i + 1, close - i - 1) });
                        i = close;
                        continue;
                    }
                }

                if (content.AsSpan(i).StartsWith("==".AsSpan()))
                {
                    int close = FindUnescaped(content, "==", i + 2);
                    if (close > i + 1)
                    {
                        Flush();
                        result.Add(new AozoraHighlight { Text = content.Substring(i + 2, close - i - 2) });
                        i = close + 1;
                        continue;
                    }
                }

                if (content.AsSpan(i).StartsWith(@"\(".AsSpan()))
                {
                    int close = FindUnescaped(content, @"\)", i + 2);
                    if (close > i)
                    {
                        Flush();
                        result.Add(new AozoraMath { Text = content.Substring(i + 2, close - i - 2) });
                        i = close + 1;
                        continue;
                    }
                }

                if (content[i] == '$' && (i + 1 >= content.Length || content[i + 1] != '$') && !IsEscaped(content, i))
                {
                    int close = FindClosingInlineDollar(content, i + 1);
                    if (close > i)
                    {
                        Flush();
                        result.Add(new AozoraMath { Text = content.Substring(i + 1, close - i - 1) });
                        i = close;
                        continue;
                    }
                }

                if (content.AsSpan(i).StartsWith("**".AsSpan()) || content.AsSpan(i).StartsWith("__".AsSpan()))
                {
                    string marker = content.Substring(i, 2);
                    int close = FindUnescaped(content, marker, i + 2);
                    if (close > i + 1)
                    {
                        Flush();
                        result.Add(new AozoraBold { Text = content.Substring(i + 2, close - i - 2) });
                        i = close + 1;
                        continue;
                    }
                }

                if ((content[i] == '*' || content[i] == '_') && !IsEscaped(content, i))
                {
                    string marker = content[i].ToString();
                    int close = FindUnescaped(content, marker, i + 1);
                    if (close > i)
                    {
                        Flush();
                        result.Add(new AozoraItalic { Text = content.Substring(i + 1, close - i - 1) });
                        i = close;
                        continue;
                    }
                }

                text.Append(content[i]);
            }

            Flush();
            return result;
        }

        private static int FindClosingInlineDollar(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] != '$' || IsEscaped(text, i)) continue;
                if (i + 1 < text.Length && text[i + 1] == '$') continue;
                return i;
            }
            return -1;
        }

        private static int FindUnescaped(string text, string value, int start)
        {
            int index = text.IndexOf(value, start, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (!IsEscaped(text, index)) return index;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }
            return -1;
        }

        private static bool IsEscaped(string text, int index)
        {
            int slashCount = 0;
            for (int i = index - 1; i >= 0 && text[i] == '\\'; i--) slashCount++;
            return slashCount % 2 == 1;
        }

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
                IsBlankLine = source.IsBlankLine,
                IsPageBreak = source.IsPageBreak,
                IsParagraphContinuation = source.IsParagraphContinuation,
                TableRowIndex = source.TableRowIndex,
                TableRowCount = source.TableRowCount,
                EpubChapterIndex = source.EpubChapterIndex
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
