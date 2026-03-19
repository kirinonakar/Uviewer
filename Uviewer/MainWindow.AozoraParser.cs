using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        public class AozoraBindingModel
        {
            public List<object> Inlines { get; set; } = new(); // String (for text), AozoraBold (for bold), AozoraRuby (for ruby)
            public double FontSizeScale { get; set; } = 1.0;
            public TextAlignment Alignment { get; set; } = TextAlignment.Left;
            public Thickness Margin { get; set; } = new Thickness(0);
            public Thickness Padding { get; set; } = new Thickness(0);
            public Windows.UI.Color? BorderColor { get; set; } = null;
            public Thickness BorderThickness { get; set; } = new Thickness(0);
            public Windows.UI.Color? BackgroundColor { get; set; } = null;
            public string? FontFamily { get; set; } = null; // Override font family (e.g. for code)
            public bool IsTable { get; set; } = false;
            public List<List<string>> TableRows { get; set; } = new();
            public int SourceLineNumber { get; set; } = 0; // Original line number in source text
            public int HeadingLevel { get; set; } = 0; // 0=None, 1=Large/H1, 2=Medium/H2, 3=Small/H3...
            public string HeadingText { get; set; } = "";
            public bool HasImage => Inlines.Any(i => i is AozoraImage);
            public int EpubChapterIndex { get; set; } = -1;
            public bool IsPageBreak { get; set; } = false;
            public bool IsBold { get; set; } = false;
            public double BlockIndent { get; set; } = 0;
            public bool IsBlankLine { get; set; } = false;
            public bool IsParagraphContinuation { get; set; } = false;
        }



        public class AozoraBold { public string Text { get; set; } = ""; }
        public class AozoraItalic { public string Text { get; set; } = ""; }
        public class AozoraCode { public string Text { get; set; } = ""; }
        public class AozoraLineBreak { }
        public class AozoraRuby { public string BaseText { get; set; } = ""; public string RubyText { get; set; } = ""; public bool IsBold { get; set; } = false; }
        public class AozoraTCY { public string Text { get; set; } = ""; public bool IsBold { get; set; } = false; }
        public class AozoraImage { public string Source { get; set; } = ""; }


        private string PreprocessAozoraBold(string text)
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

        private List<AozoraBindingModel> ParseAozoraContent(string text)
        {
            text = PreprocessAozoraBold(text);
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            _aozoraTotalLineCountInSource = lines.Length;
            return ParseAozoraLines(lines, 1);
        }

        private List<AozoraBindingModel> ParseAozoraLines(string[] lines, int startLineOffset)
        {
            var blocks = new List<AozoraBindingModel>();
            bool lastWasEmpty = false;

            // Flags for multi-line tags
            bool currentBold = false;
            double currentIndentEm = 0;
            bool inKeigakomi = false;
            int smallTextLevel = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Basic clean
                var content = line.Replace('\u3000', ' ').TrimEnd();

                if (string.IsNullOrEmpty(content))
                {
                    if (lastWasEmpty) continue; // Collapse consecutive empty lines

                    var blankModel = new AozoraBindingModel
                    {
                        Inlines = { "" },
                        Margin = new Thickness(0),
                        SourceLineNumber = startLineOffset + i,
                        BlockIndent = currentIndentEm * _textFontSize,
                        IsBlankLine = true
                    };

                    // [핵심 추가] 빈 줄이라도 罫囲み(테두리) 내부에 있다면 테두리 속성 상속
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

                // --- State Updates and Tag Support ---
                // 1. Page Break
                bool isPageBreak = false;
                if (Regex.IsMatch(content, @"［＃(?:改ページ|改頁)］"))
                {
                    isPageBreak = true;
                    content = Regex.Replace(content, @"［＃(?:改ページ|改頁)］", "");
                }

                // 2. Multi-line Bold (Markers handled by tokenizer)


                // 3. Indents
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

                // 4. Keigakomi
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

                // 5. Small text
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

                // --- Specific Tag Support (Bouten, TCY, BoldSpecific) ---
                // Bouten
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

                // TCY
                var tcyMatches = Regex.Matches(content, @"［＃「(.+?)」は縦中横］");
                foreach (Match m in tcyMatches)
                {
                    string targetWord = m.Groups[1].Value;
                    string fullTag = m.Value;
                    string targetPattern = Regex.Escape(targetWord) + Regex.Escape(fullTag);
                    content = Regex.Replace(content, targetPattern, $"{{{{TCY|{targetWord}}}}}");
                }

                // Bold Specific
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
                    BlockIndent = currentIndentEm * _textFontSize
                };
                model.Margin = new Thickness(0);

                if (inKeigakomi)
                {
                    model.BorderColor = Colors.Gray;
                    model.BorderThickness = new Thickness(1);
                    model.Padding = new Thickness(10);
                }
                if (smallTextLevel == 2) model.FontSizeScale = 0.85;

                // --- Aozora Tag Parsing ---
                // Headers
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

                // Alignments
                if (content.Contains("［＃センター］"))
                {
                    model.Alignment = TextAlignment.Center;
                    content = content.Replace("［＃センター］", "");
                }
                else if (content.Contains("［＃地から３字上げ］"))
                {
                    model.Alignment = TextAlignment.Right;
                    model.Margin = new Thickness(0, 0, 60, 0);
                    content = content.Replace("［＃地から３字上げ］", "");
                }

                // 4. Image tags: <img src="file.jpg"> or ［＃挿絵（img/file.jpg）入る］
                // IMPORTANT: Parse images BEFORE cleaning up all ［＃...］ tags
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                // Cleanup other tags
                content = Regex.Replace(content, @"［＃[^］]+］", "");

                // --- Inline Parsing (Ruby & Bold) ---
                // 1. Aozora Ruby with pipe: ｜漢字《かんじ》
                content = Regex.Replace(content, @"｜(.+?)《(.+?)》", (m) =>
                {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });

                // 2. Aozora Ruby for emphasis dots (any character + 《'》): 한자가 아닌 경우에도 방점으로 인식
                content = Regex.Replace(content, @"(.)《'》", "{{RUBY|$1|・}}");
                content = Regex.Replace(content, @"(.)《’》", "{{RUBY|$1|・}}");

                // 3. Aozora Ruby without pipe (Kanji + Ruby): 漢字《かんじ》
                content = Regex.Replace(content, @"([\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF々]+)《(.+?)》", (m) =>
                {
                    string b = m.Groups[1].Value;
                    string r = m.Groups[2].Value;
                    if (r == "'" || r == "’") r = "・";
                    return $"{{{{RUBY|{b}|{r}}}}}";
                });

                // Markdown Bold **...** 
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "@@BOLD_START@@$2@@BOLD_END@@");

                // Tokenize
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
                        var inner = part.Trim('{', '}'); // RUBY|Base|Ruby
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
                        else model.Inlines.Add(part); // String
                    }
                }

                currentBold = inlineBold;
                blocks.Add(model);

                // 💡 [추가] 罫囲み(박스)가 끝난 직후 여백을 위해 빈 줄을 하나 삽입
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

                    // 다음 줄이 원래 빈 줄이더라도 중복으로 너무 넓어지지 않도록 플래그 처리
                    lastWasEmpty = true;
                }
            }

            // ===== [추가된 부분] 문단이 긴 경우 문장 단위로 블록 분리 =====
            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                splitBlocks.AddRange(SplitBlockBySentences(block));
            }

            return splitBlocks;
        }

        private List<AozoraBindingModel> SplitBlockBySentences(AozoraBindingModel originalBlock)
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

            // [추가] 기호들을 여는 괄호, 닫는 괄호, 일반 종결자로 세분화
            char[] openBrackets = { '「', '『', '(', '<', '《', '〈', '【', '［', '“', '‘' };
            char[] closeBrackets = { '」', '』', ')', '>', '》', '〉', '】', '］', '”', '’' };
            char[] terminators = { '。', '！', '？', '.', '!', '?', '、', ',', ' ', '　' };

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

                        // 1. 여는 괄호: 앞에 누적된 글자가 있다면 괄호 "직전"에서 자른다.
                        if (isOpen && j > start)
                        {
                            splitPos = j; 
                        }
                        // 2. 닫는 괄호: 닫는 괄호를 포함시키고 괄호 "직후"에서 자른다. (연속된 닫는 괄호도 모두 포함)
                        else if (isClose)
                        {
                            while (j + 1 < text.Length && Array.IndexOf(closeBrackets, text[j + 1]) >= 0) j++;
                            splitPos = j + 1;
                        }
                        // 3. 종결자 (마침표, 쉼표 등): 기호를 포함시키고 뒤에 닫는 괄호가 오면 같이 묶어서 자른다.
                        else if (isTerminator)
                        {
                            if (c == '.' && j > 0 && j < text.Length - 1 && char.IsDigit(text[j - 1]) && char.IsDigit(text[j + 1]))
                            {
                                // 소수점은 무시
                            }
                            else
                            {
                                while (j + 1 < text.Length && Array.IndexOf(closeBrackets, text[j + 1]) >= 0) j++;
                                splitPos = j + 1;
                            }
                        }
                        // 4. 길이 초과 (15자 이상): 다음 문자가 닫는 괄호나 종결자면 자르기를 보류하고 같이 묶는다.
                        else if (j - start + 1 >= 15)
                        {
                            if (j + 1 < text.Length)
                            {
                                char nextC = text[j + 1];
                                if (Array.IndexOf(closeBrackets, nextC) < 0 && Array.IndexOf(terminators, nextC) < 0)
                                {
                                    splitPos = j + 1;
                                }
                            }
                            else
                            {
                                splitPos = j + 1;
                            }
                        }

                        // 분리 지점이 정해졌을 때 조각을 저장
                        if (splitPos != -1)
                        {
                            bool isLastInText = (splitPos == text.Length);
                            bool isLastInline = (i == originalBlock.Inlines.Count - 1);

                            // 마지막 조각은 루프 끝에서 처리하므로 패스
                            if (!(isLastInText && isLastInline))
                            {
                                string part = text.Substring(start, splitPos - start);
                                if (currentBlock.Inlines.Count == 0) part = part.TrimStart();
                                if (!string.IsNullOrEmpty(part)) currentBlock.Inlines.Add(part);

                                if (currentBlock.Inlines.Count > 0)
                                {
                                    if (!isFirst) currentBlock.IsParagraphContinuation = true;
                                    result.Add(currentBlock);
                                    isFirst = false;
                                }

                                currentBlock = CloneBlockProperties(originalBlock);
                                currentBlock.BlockIndent = 0;
                                currentBlock.Margin = new Thickness(0);

                                start = splitPos;
                                j = splitPos - 1; // for 루프에서 j++가 되므로 위치 보정
                            }
                        }
                    }

                    // 남은 텍스트 추가
                    if (start < text.Length)
                    {
                        string left = text.Substring(start);
                        if (currentBlock.Inlines.Count == 0) left = left.TrimStart();
                        if (!string.IsNullOrEmpty(left)) currentBlock.Inlines.Add(left);
                    }
                }
                else
                {
                    // 루비 등 특수 요소
                    currentBlock.Inlines.Add(inline);
                    
                    if (currentBlock.Inlines.Count > 0 && i < originalBlock.Inlines.Count - 1)
                    {
                        if (!isFirst) currentBlock.IsParagraphContinuation = true;
                        result.Add(currentBlock);
                        isFirst = false;
                        
                        currentBlock = CloneBlockProperties(originalBlock);
                        currentBlock.BlockIndent = 0;
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

        private List<AozoraBindingModel> ParseMarkdownContent(string text)
        {
            var blocks = new List<AozoraBindingModel>();
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            bool inCodeBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                string content = line;
                int sourceLine = i + 1;

                // Code Block Handling
                if (content.Trim().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue; // Skip the fence line
                }

                if (inCodeBlock)
                {
                    var model = new AozoraBindingModel();
                    model.FontFamily = "Consolas, Courier New, Monospace";
                    model.Inlines.Add(content);
                    model.BackgroundColor = Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128);
                    model.Padding = new Thickness(4, 0, 4, 0);
                    model.Margin = new Thickness(20, 0, 20, 0);
                    model.SourceLineNumber = sourceLine;
                    blocks.Add(model);
                    continue;
                }

                // Table Parsing
                if (content.Trim().StartsWith("|"))
                {
                    // Lookahead: consecutive lines starting with |
                    var tableLines = new List<string>();
                    int k = i;
                    while (k < lines.Length && lines[k].Trim().StartsWith("|"))
                    {
                        tableLines.Add(lines[k].Trim());
                        k++;
                    }

                    if (tableLines.Count >= 2) // Header + Separator at minimum
                    {
                        var tableModel = new AozoraBindingModel { IsTable = true };
                        bool isValidTable = false;

                        foreach (var tLine in tableLines)
                        {
                            // Check separator line |---|---|
                            var trimLine = tLine.Trim('|');
                            // If it contains only -, :, | and whitespace, treat as separator
                            if (Regex.IsMatch(trimLine, @"^[\-:\|\s]+$") && trimLine.Contains("-"))
                            {
                                isValidTable = true;
                                continue; // Skip separator row
                            }

                            // Parse cells
                            // naive split by |
                            // | A | B | -> ["", "A", "B", ""]
                            var cells = tLine.Split('|').Select(c => c.Trim()).ToList();

                            // Remove empty first/last if empty
                            if (cells.Count > 0 && string.IsNullOrEmpty(cells[0])) cells.RemoveAt(0);
                            if (cells.Count > 0 && string.IsNullOrEmpty(cells[cells.Count - 1])) cells.RemoveAt(cells.Count - 1);

                            tableModel.TableRows.Add(cells);
                        }

                        if (isValidTable || tableLines.Count > 1)
                        {
                            tableModel.SourceLineNumber = sourceLine;
                            blocks.Add(tableModel);
                            i = k - 1; // Advance loop
                            continue;
                        }
                    }
                }

                content = content.TrimEnd();

                if (string.IsNullOrEmpty(content))
                {
                    // Spacer
                    blocks.Add(new AozoraBindingModel { Inlines = { "" }, Margin = new Thickness(0), IsBlankLine = true });
                    continue;
                }

                var blockModel = new AozoraBindingModel();
                blockModel.Margin = new Thickness(0);

                // --- Markdown Block Parsing ---

                // Headers
                if (content.StartsWith("#"))
                {
                    int level = 0;
                    while (level < content.Length && content[level] == '#') level++;

                    if (level > 0 && level <= 6)
                    {
                        if (level == 1) blockModel.FontSizeScale = 2.0;
                        else if (level == 2) blockModel.FontSizeScale = 1.5;
                        else if (level == 3) blockModel.FontSizeScale = 1.25;
                        else blockModel.FontSizeScale = 1.1;

                        content = content.Substring(level).TrimStart();
                        blockModel.HeadingLevel = level;
                        blockModel.HeadingText = Regex.Replace(content, @"[#\[\]]", "").Trim();

                        if (level == 1 || level == 2)
                        {
                            blockModel.BorderColor = Colors.LightGray;
                            blockModel.BorderThickness = new Thickness(0, 0, 0, 1); // Bottom border for H1/H2
                        }
                    }
                }
                // Quote
                else if (content.StartsWith(">"))
                {
                    content = content.TrimStart('>', ' ');
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                    blockModel.BorderColor = Colors.Gray;
                    blockModel.BorderThickness = new Thickness(4, 0, 0, 0); // Left border
                    blockModel.Padding = new Thickness(10, 0, 0, 0);
                    blockModel.Inlines.Add(new AozoraItalic { Text = "" }); // Force italic style logic if we had it, but for now just indent
                }
                // List (Unordered)
                else if (Regex.IsMatch(content, @"^[\*\-]\s"))
                {
                    content = "• " + content.Substring(2);
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                }
                // List (Ordered)
                else if (Regex.IsMatch(content, @"^\d+\.\s"))
                {
                    // Keep number
                    blockModel.Margin = new Thickness(20, 0, 0, 0);
                }
                // HR
                else if (Regex.IsMatch(content, @"^(\*{3,}|-{3,})$"))
                {
                    blockModel.Inlines.Add("");
                    blockModel.BorderColor = Colors.Gray;
                    blockModel.BorderThickness = new Thickness(0, 1, 0, 0);
                    blockModel.Margin = new Thickness(0, 10, 0, 10);
                    blocks.Add(blockModel);
                    continue;
                }

                // --- Inline Parsing ---
                // 1. Code `...` -> {{CODE|...}}
                content = Regex.Replace(content, @"`(.+?)`", "{{CODE|$1}}");

                // 1.5 <br> -> {{BR}} (Case insensitive, supports <br>, <br/>, <br />)
                content = Regex.Replace(content, @"<br\s*/?>", "{{BR}}", RegexOptions.IgnoreCase);

                // 1.6 Image <img src="..."> -> {{IMG|...}}
                content = Regex.Replace(content, @"<img\s+src=[""'](.+?)[""']\s*/?>", "{{IMG|$1}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"［＃挿絵\s*[（\(\[［]\s*([^）\)\]］]+?)\s*[）\)\]］].*?］", "{{IMG|$1}}");

                // 2. Bold **...** or __...__ (Support spaces inside: ** text **)
                content = Regex.Replace(content, @"(\*\*|__)(.*?)\1", "{{BOLD|$2}}");

                // 3. Italic *...* or _..._
                content = Regex.Replace(content, @"(\*|_)(.*?)\1", "{{ITALIC|$2}}");

                // Tokenize
                string pattern = @"(\{\{CODE\|.*?\}\}|\{\{BOLD\|.*?\}\}|\{\{ITALIC\|.*?\}\}|\{\{BR\}\}|\{\{IMG\|.*?\}\})";
                var parts = Regex.Split(content, pattern);

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part.StartsWith("{{CODE|"))
                    {
                        var inner = part.Substring(7, part.Length - 9);
                        blockModel.Inlines.Add(new AozoraCode { Text = inner });
                    }
                    else if (part.StartsWith("{{IMG|"))
                    {
                        var src = part.Substring(6, part.Length - 8);
                        blockModel.Inlines.Add(new AozoraImage { Source = src });
                    }
                    else if (part == "{{BR}}")
                    {
                        blockModel.Inlines.Add(new AozoraLineBreak());
                    }
                    else if (part.StartsWith("{{BOLD|"))
                    {
                        var inner = part.Substring(7, part.Length - 9);
                        blockModel.Inlines.Add(new AozoraBold { Text = inner });
                    }
                    else if (part.StartsWith("{{ITALIC|"))
                    {
                        var inner = part.Substring(9, part.Length - 11);
                        blockModel.Inlines.Add(new AozoraItalic { Text = inner });
                    }
                    else
                    {
                        blockModel.Inlines.Add(part);
                    }
                }

                blocks.Add(blockModel);
            }

            return blocks;
        }

        private double ConvertFullWidthToDouble(string input)
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

        private AozoraBindingModel CloneBlockProperties(AozoraBindingModel source, bool copyInlines = false)
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
                HeadingLevel = source.HeadingLevel,
                HeadingText = source.HeadingText,
                IsTable = source.IsTable,
                IsBlankLine = source.IsBlankLine,
                IsPageBreak = source.IsPageBreak,
                IsParagraphContinuation = source.IsParagraphContinuation,

                // [버그 수정 1] EPUB 챕터 인덱스 복사 누락 해결 (이것이 챕터 1로 순환되던 원인입니다)
                EpubChapterIndex = source.EpubChapterIndex
            };

            // [버그 수정 2] 테이블 데이터가 있을 경우 참조 오류를 막기 위해 깊은 복사(Deep Copy) 처리
            if (source.IsTable && source.TableRows != null)
            {
                clone.TableRows = source.TableRows.Select(row => new List<string>(row)).ToList();
            }

            if (copyInlines) clone.Inlines = new List<object>(source.Inlines);
            return clone;
        }


        // TOC Handlers

        public class TocItem
        {
            public string HeadingText { get; set; } = "";
            public int SourceLineNumber { get; set; }
            public Thickness Margin => new Thickness((HeadingLevel - 1) * 16, 0, 0, 0);
            public int HeadingLevel { get; set; }
            public object? Tag { get; set; }
        }

        private async void TocButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTextMode && !_isEpubMode) return;

            // Ensure TOC Title
            if (TocFlyout.Content is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock tb)
            {
                tb.Text = Strings.TocTitle;
            }

            List<TocItem> items = new();

            if (_currentPdfDocument != null && _pdfToc.Count > 0)
            {
                items = _pdfToc.ToList();
            }
            else if ((_isAozoraMode || _isVerticalMode) && _aozoraBlocks.Count > 0)
            {
                items = _aozoraBlocks
                    .Where(b => b.HeadingLevel > 0)
                    .Select(b => new TocItem
                    {
                        HeadingText = b.HeadingText,
                        SourceLineNumber = b.SourceLineNumber,
                        HeadingLevel = b.HeadingLevel
                    })
                    .ToList();
            }

            else if (_isEpubMode)
            {
                // EPUB Mode
                if (_epubToc != null && _epubToc.Count > 0)
                {
                    items = _epubToc.Select(t => new TocItem
                    {
                        HeadingText = t.Title,
                        HeadingLevel = 1, // Simplify level for now
                        SourceLineNumber = -1,
                        Tag = t
                    }).ToList();
                }
            }
            else if (!string.IsNullOrEmpty(_currentTextContent))
            {
                // Scan raw text on demand for Normal Mode or if blocks are empty
                items = await Task.Run(() =>
                {
                    var list = new List<TocItem>();
                    var lines = _textLines.Count > 0 ? _textLines : SplitTextToLines(_currentTextContent); // Prefer split lines if available

                    // In standard mode, finding exact source line number might be tricky if _textLines is wrapped.
                    // But _textLines usually stores 1:1 if no wrap? No, MainWindow.text.cs implementation of SplitTextToLines:
                    // Wait, SplitTextToLines splits by wrapping width? 
                    // Let's check SplitTextToLines implementation in MainWindow.text.cs.
                    // If SplitTextToLines wraps connected lines, SourceLineNumber logic is complex.
                    // A safe bet is scanning the raw source lines.
                    var rawLines = _currentTextContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                    for (int i = 0; i < rawLines.Length; i++)
                    {
                        var line = rawLines[i].Trim();
                        int level = 0;
                        string text = "";

                        // Check Aozora
                        if (line.Contains("［＃大見出し］") || line.StartsWith("# ")) { level = 1; text = line.Replace("［＃大見出し］", "").TrimStart('#', ' '); }
                        else if (line.Contains("［＃中見出し］") || line.StartsWith("## ")) { level = 2; text = line.Replace("［＃中見出し］", "").TrimStart('#', ' '); }

                        if (level > 0)
                        {
                            // Clean tags
                            text = Regex.Replace(text, @"［＃[^］]+］|\[.*?\]|[#]", "").Trim();
                            list.Add(new TocItem { HeadingText = text, SourceLineNumber = i + 1, HeadingLevel = level });
                        }
                    }
                    return list;
                });
            }

            // Highlight current item and scroll
            int currentIndex = -1;

            if (_isEpubMode)
            {
                if (_currentEpubChapterIndex >= 0 && _currentEpubChapterIndex < _epubSpine.Count)
                {
                    string currentSpinePath = _epubSpine[_currentEpubChapterIndex];
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Tag is EpubTocItem epi)
                        {
                            string linkPath = epi.Link;
                            int hashIndex = linkPath.IndexOf('#');
                            if (hashIndex >= 0) linkPath = linkPath.Substring(0, hashIndex);

                            if (string.Equals(linkPath, currentSpinePath, StringComparison.OrdinalIgnoreCase))
                            {
                                currentIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
            else if (_currentPdfDocument != null && items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= _currentIndex)
                        currentIndex = i;
                    else
                        break;
                }
            }
            else
            {
                // Text / Aozora Mode
                int currentLine = 1;
                if (_isVerticalMode)
                {
                    currentLine = _currentVerticalPageInfo.StartLine;
                }
                else if (_isAozoraMode)
                {
                    if (_currentAozoraStartBlockIndex >= 0 && _currentAozoraStartBlockIndex < _aozoraBlocks.Count)
                        currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                }
                else
                {
                    currentLine = GetTopVisibleLineIndex();
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].SourceLineNumber <= currentLine)
                        currentIndex = i;
                    else
                        break;
                }
            }

            if (currentIndex >= 0 && currentIndex < items.Count)
            {
                items[currentIndex].HeadingText = "⮕ " + items[currentIndex].HeadingText;
            }

            if (items.Count == 0)
            {
                items.Add(new TocItem { HeadingText = Strings.NoTocContent, SourceLineNumber = -1 });
            }

            TocListView.ItemsSource = items;

            if (currentIndex >= 0)
            {
                // Ensure layout updated before scrolling
                this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        TocListView.ScrollIntoView(items[currentIndex], ScrollIntoViewAlignment.Leading);
                    }
                    catch { }
                });
            }
        }

        private void TocListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TocItem item)
            {
                TocFlyout.Hide();

                if (_isEpubMode)
                {
                    if (item.Tag is EpubTocItem epubItem)
                    {
                        JumpToEpubTocItem(epubItem);
                    }
                }
                else if (item.Tag?.ToString() == "PDF")
                {
                    if (item.SourceLineNumber >= 0 && item.SourceLineNumber < _imageEntries.Count)
                    {
                        _currentIndex = item.SourceLineNumber;
                        _ = DisplayCurrentImageAsync();
                    }
                }
                else if (item.SourceLineNumber > 0)
                {
                    if (_isVerticalMode)
                    {
                        _ = PrepareVerticalTextAsync(item.SourceLineNumber);
                    }
                    else if (_isAozoraMode)
                    {
                        JumpToAozoraLine(item.SourceLineNumber);
                    }
                    else
                    {
                        ScrollToLine(item.SourceLineNumber);
                    }
                }
            }
        }
    }
}