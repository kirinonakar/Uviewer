using HtmlAgilityPack;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Uviewer.Models;

namespace Uviewer.Services;

internal static class MarkdownDocumentParser
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UseMathematics()
            // A closing math delimiter is punctuation; allow **$...$**입니다.
            .UseCjkFriendlyEmphasis();
        // Accept TeX delimiters adjacent to Korean prose as well as whitespace.
        builder.Extensions.Add(new ReaderMathExtension());
        return builder.Build();
    }

    private sealed class ReaderMathExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder builder)
        {
            // One owner for inline math: rejected currency must stay literal,
            // rather than being reconsidered by the standard dollar parser.
            builder.InlineParsers.TryRemove<MathInlineParser>();
            builder.InlineParsers.Insert(0, new ReaderMathParser());
        }
        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
    }

    public static (List<AozoraBindingModel> Blocks, int SourceLineCount) Parse(string source)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var document = Markdown.Parse(NormalizeDisplayDelimiters(source), Pipeline);
        // Render each top-level node with its source anchor. Raw HTML containers may
        // span several nodes, so parse the resulting HTML as one document.
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        foreach (var block in document)
        {
            renderer.Write($"<!--uviewer-source:{block.Line + 1}-->\n");
            renderer.Render(block);
        }
        var html = new HtmlDocument();
        html.LoadHtml(writer.ToString());
        var converter = new Converter();
        converter.Read(html.DocumentNode);
        foreach (var block in converter.Blocks) block.IsMarkdown = true;
        // Preserve Markdown paragraphs and inline runs. Aozora's pagination chunks
        // would become forced line breaks in the continuous-scrolling renderer.
        return (converter.Blocks, source.Count(c => c == '\n') + 1);
    }

    private static string NormalizeDisplayDelimiters(string source)
    {
        var lines = source.Split('\n');
        char fence = '\0';
        int fenceLength = 0;
        bool display = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            int run = line.TakeWhile(c => c == '`' || c == '~').Count();
            if (run >= 3 && (fence == '\0' || line[0] == fence))
            {
                if (fence == '\0') { fence = line[0]; fenceLength = run; }
                else if (run >= fenceLength) fence = '\0';
                continue;
            }
            if (fence != '\0' || lines[i].StartsWith("    ") || lines[i].StartsWith('\t')) continue;
            if (!display && line.StartsWith(@"\["))
            {
                lines[i] = lines[i].Replace(@"\[", "$$");
                display = true;
            }
            if (display && lines[i].Contains(@"\]"))
            {
                lines[i] = lines[i].Replace(@"\]", "$$");
                display = false;
            }
        }
        return string.Join('\n', lines);
    }

    private sealed class ReaderMathParser : InlineParser
    {
        public ReaderMathParser() => OpeningCharacters = ['$', '\\'];
        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            int start = slice.Start;
            string text = slice.Text;
            bool parenthesis = text[start] == '\\' && start + 1 <= slice.End && text[start + 1] == '(';
            if (!parenthesis && text[start] != '$') return false;
            int length = parenthesis ? 2 : (start + 1 <= slice.End && text[start + 1] == '$' ? 2 : 1);
            bool singleDollar = !parenthesis && length == 1;
            if (singleDollar && (start == slice.End || char.IsWhiteSpace(text[start + 1]))) return false;
            string closer = parenthesis ? @"\)" : new string('$', length);
            int end = start + length;
            while (end <= slice.End)
            {
                if (text[end] is '\n' or '\r') return false;
                if (text.AsSpan(end, slice.End - end + 1).StartsWith(closer))
                {
                    // The first next dollar is either a valid closer or evidence
                    // that this opener is literal. Do not span later real formulas.
                    if (singleDollar && (char.IsWhiteSpace(text[end - 1]) ||
                        (end < slice.End && (char.IsDigit(text[end + 1]) || text[end + 1] == '$')))) return false;
                    string tex = text.Substring(start + length, end - start - length);
                    if (string.IsNullOrWhiteSpace(tex)) return false;
                    string tag = !parenthesis && length == 2 ? "div" : "span";
                    processor.Inline = new HtmlInline($"<{tag} class=\"uviewer-math\">" + WebUtility.HtmlEncode(tex) + $"</{tag}>");
                    slice.Start = end + length;
                    return true;
                }
                if (text[end] == '\\' && !parenthesis) end++;
                end++;
            }
            return false;
        }
    }

    private sealed class Converter
    {
        public List<AozoraBindingModel> Blocks { get; } = new();
        private AozoraBindingModel _current = new();
        private int _sourceLine = 1;
        private bool _bold, _italic, _highlight, _code, _underline, _strike, _link;
        private int _script;
        private int _codeGroup;

        private void Flush()
        {
            if (_current.Inlines.Count == 0) return;
            if (_current.Inlines.All(x => x is string s && string.IsNullOrWhiteSpace(s))) { _current.Inlines.Clear(); return; }
            Blocks.Add(_current);
            _current = AozoraParserService.CloneBlockProperties(_current);
            _current.IsParagraphContinuation = false;
        }

        public void Read(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Comment)
            {
                var match = Regex.Match(node.InnerHtml, @"uviewer-source:(\d+)");
                if (match.Success)
                {
                    Flush();
                    _sourceLine = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    _current.SourceLineNumber = _sourceLine;
                }
                return;
            }
            if (node.NodeType == HtmlNodeType.Text)
            {
                AddText(WebUtility.HtmlDecode(node.InnerText));
                return;
            }

            string name = node.Name.ToLowerInvariant();
            if (name is "script" or "style" or "head" or "iframe" or "object" ||
                node.Attributes["hidden"] != null || Regex.IsMatch(node.GetAttributeValue("style", ""), @"display\s*:\s*none", RegexOptions.IgnoreCase)) return;
            if (name == "img")
            {
                string src = WebUtility.HtmlDecode(node.GetAttributeValue("src", ""));
                if (src.Length > 0)
                {
                    var width = ReadImageLength(node, "width");
                    var height = ReadImageLength(node, "height");
                    var maxWidth = ReadImageLength(node, "max-width");
                    _current.Inlines.Add(new AozoraImage
                    {
                        Source = src, AltText = WebUtility.HtmlDecode(node.GetAttributeValue("alt", "")),
                        Width = width.Value, WidthIsPercent = width.Percent,
                        Height = height.Value, HeightIsPercent = height.Percent,
                        MaxWidth = maxWidth.Value, MaxWidthIsPercent = maxWidth.Percent
                    });
                }
                else AddText(WebUtility.HtmlDecode(node.GetAttributeValue("alt", "")));
                return;
            }
            if (name == "br") { _current.Inlines.Add(new AozoraLineBreak()); return; }
            if (name == "input")
            {
                if (node.GetAttributeValue("type", "").Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                    AddText(node.Attributes["checked"] != null ? "☑ " : "☐ ");
                return;
            }
            if (name == "table") { Flush(); ReadTable(node); return; }
            if (name == "pre") { Flush(); ReadCodeBlock(node); return; }
            if (name == "ruby")
            {
                var rt = node.ChildNodes.FirstOrDefault(n => n.Name == "rt");
                string body = string.Concat(node.ChildNodes.Where(n => n.Name is not ("rt" or "rp")).Select(n => n.InnerText));
                _current.Inlines.Add(new AozoraRuby { BaseText = WebUtility.HtmlDecode(body), RubyText = WebUtility.HtmlDecode(rt?.InnerText ?? ""), IsBold = _bold });
                return;
            }
            var classes = node.GetAttributeValue("class", "").Split(' ');
            if (classes.Contains("math") || classes.Contains("uviewer-math"))
            {
                bool display = name == "div";
                if (display) Flush();
                string tex = WebUtility.HtmlDecode(node.InnerText).Trim();
                if ((tex.StartsWith(@"\(") && tex.EndsWith(@"\)")) || (tex.StartsWith(@"\[") && tex.EndsWith(@"\]"))) tex = tex[2..^2].Trim();
                if (display) { _current.Alignment = TextAlignment.Center; _current.Margin = new Thickness(0, 8, 0, 12); }
                _current.Inlines.Add(new AozoraMath { Text = tex, DisplayMode = display, IsBold = _bold });
                if (display) { Flush(); _current = new() { SourceLineNumber = _sourceLine }; }
                return;
            }
            bool listParagraph = name == "p" && node.ParentNode?.Name == "li";
            if (listParagraph && node.ParentNode!.ChildNodes.TakeWhile(n => n != node).Any(n => n.Name == "p"))
                _current.Inlines.Add(new AozoraLineBreak());
            bool block = !listParagraph && name is ("p" or "div" or "center" or "section" or "article" or "blockquote" or "li" or "ul" or "ol" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "hr" or "details" or "summary");
            var parent = _current;
            var style = (_bold, _italic, _highlight, _code, _underline, _strike, _script, _link);
            if (block)
            {
                Flush();
                parent = _current;
                _current = AozoraParserService.CloneBlockProperties(parent);
                _current.SourceLineNumber = _sourceLine;
                _current.Margin = new Thickness(parent.Margin.Left, 0, parent.Margin.Right, name == "p" ? 16 : 0);
                if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]))
                {
                    int level = name[1] - '0';
                    _current.HeadingLevel = level;
                    _current.HeadingText = WebUtility.HtmlDecode(node.InnerText);
                    _current.FontSizeScale = level switch { 1 => 2, 2 => 1.5, 3 => 1.25, 4 => 1, 5 => 0.875, _ => 0.85 };
                    _current.IsBold = true;
                    _current.Margin = new Thickness(parent.Margin.Left, Blocks.Count > 0 ? 24 : 0, parent.Margin.Right, 16);
                    if (level <= 2) { _current.BorderColor = Colors.Gray; _current.BorderThickness = new Thickness(0, 0, 0, 1); }
                }
                if (name == "blockquote") { _current.BorderColor = Colors.Gray; _current.BorderThickness = new Thickness(4, 0, 0, 0); _current.Margin = new Thickness(parent.Margin.Left + 20, 0, 0, 0); }
                if (name == "li")
                {
                    _current.Margin = new Thickness(parent.Margin.Left + 20, 0, 0, 0);
                    bool ordered = node.ParentNode?.Name == "ol";
                    int number = (node.ParentNode?.GetAttributeValue("start", 1) ?? 1) +
                        (node.ParentNode?.ChildNodes.TakeWhile(n => n != node).Count(n => n.Name == "li") ?? 0);
                    AddText(ordered ? $"{number}. " : "• ");
                }
                if (name == "hr") { _current.Inlines.Add(" "); _current.BorderColor = Colors.Gray; _current.BorderThickness = new Thickness(0, 0, 0, 1); }
                string align = node.GetAttributeValue("align", "");
                var alignment = Regex.Match(node.GetAttributeValue("style", ""), @"text-align\s*:\s*(center|right|left)", RegexOptions.IgnoreCase);
                if (alignment.Success) align = alignment.Groups[1].Value;
                if (name == "center" || align.Equals("center", StringComparison.OrdinalIgnoreCase)) _current.Alignment = TextAlignment.Center;
                else if (align.Equals("right", StringComparison.OrdinalIgnoreCase)) _current.Alignment = TextAlignment.Right;
            }
            _bold |= name is "strong" or "b";
            _italic |= name is "em" or "i";
            _highlight |= name == "mark";
            _code |= name is "code" or "kbd" or "samp";
            _underline |= name is "u" or "ins";
            _strike |= name is "s" or "strike" or "del";
            _link |= name == "a" && !string.IsNullOrEmpty(node.GetAttributeValue("href", ""));
            _bold |= name == "summary";
            if (name == "sup") _script = 1;
            else if (name == "sub") _script = -1;
            string css = node.GetAttributeValue("style", "").ToLowerInvariant();
            _bold |= Regex.IsMatch(css, @"font-weight\s*:\s*(bold|[6-9]00)");
            _italic |= Regex.IsMatch(css, @"font-style\s*:\s*(italic|oblique)");
            _underline |= Regex.IsMatch(css, @"text-decoration(?:-line)?\s*:[^;]*underline");
            _strike |= Regex.IsMatch(css, @"text-decoration(?:-line)?\s*:[^;]*line-through");
            foreach (var child in node.ChildNodes) Read(child);
            if (block)
            {
                Flush();
                _current = AozoraParserService.CloneBlockProperties(parent);
                _current.SourceLineNumber = _sourceLine;
            }
            (_bold, _italic, _highlight, _code, _underline, _strike, _script, _link) = style;
            if (node.NodeType == HtmlNodeType.Document) Flush();
        }

        private void AddText(string text)
        {
            if (!_code) text = Regex.Replace(text, @"\s+", " ");
            if (text.Length == 0 || (_current.Inlines.Count == 0 && string.IsNullOrWhiteSpace(text))) return;
            if (_link || _underline || _strike || _script != 0 || (_bold && _italic) || (_highlight && (_bold || _italic || _code)))
                _current.Inlines.Add(new AozoraStyledText { Text = text, Bold = _bold, Italic = _italic, Underline = _underline,
                    Strikethrough = _strike, Script = _script, Highlight = _highlight, Code = _code, IsLink = _link });
            else if (_code) _current.Inlines.Add(new AozoraCode { Text = text });
            else if (_bold) _current.Inlines.Add(new AozoraBold { Text = text });
            else if (_italic) _current.Inlines.Add(new AozoraItalic { Text = text });
            else if (_highlight) _current.Inlines.Add(new AozoraHighlight { Text = text });
            else _current.Inlines.Add(text);
        }

        private static (double? Value, bool Percent) ReadImageLength(HtmlNode node, string property)
        {
            string value = node.GetAttributeValue(property, "");
            var css = Regex.Match(node.GetAttributeValue("style", ""), @"(?:^|;)\s*" + Regex.Escape(property) + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (css.Success) value = css.Groups[1].Value.Trim();
            var length = Regex.Match(value, @"^\s*(\d+(?:\.\d+)?)\s*(px|%)?\s*$", RegexOptions.IgnoreCase);
            if (!length.Success || !double.TryParse(length.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0)
                return (null, false);
            return (Math.Min(number, 100_000), length.Groups[2].Value == "%");
        }

        private void ReadTable(HtmlNode node)
        {
            var rows = node.Descendants("tr").Where(n => n.Ancestors("table").FirstOrDefault() == node).ToList();
            int count = rows.Count;
            int columns = rows.Select(r => r.ChildNodes.Count(n => n.Name is "td" or "th")).DefaultIfEmpty(0).Max();
            var contentRows = new List<List<List<object>>>();
            foreach (var row in rows)
            {
                var cells = new List<List<object>>();
                foreach (var cell in row.ChildNodes.Where(n => n.Name is "td" or "th"))
                {
                    var converter = new Converter();
                    foreach (var child in cell.ChildNodes) converter.Read(child);
                    converter.Flush();
                    cells.Add(converter.Blocks.SelectMany(b => b.Inlines).ToList());
                }
                while (cells.Count < columns) cells.Add(new());
                contentRows.Add(cells);
            }
            for (int i = 0; i < count; i++)
            {
                var cells = contentRows[i].Select(cell => string.Concat(cell.Select(TableCellLayout.InlineText))).ToList();
                if (cells.Count == 0) continue;
                Blocks.Add(new AozoraBindingModel { IsTable = true, TableRows = new() { cells }, TableContentRows = contentRows, TableRowIndex = i, TableRowCount = count, SourceLineNumber = _sourceLine, Inlines = { " " } });
            }
        }

        private void ReadCodeBlock(HtmlNode node)
        {
            string code = WebUtility.HtmlDecode(node.InnerText).Replace("\r\n", "\n");
            if (code.EndsWith('\n')) code = code[..^1];
            var codeNode = node.ChildNodes.FirstOrDefault(n => n.Name == "code");
            var languages = (codeNode?.GetAttributeValue("class", "") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (languages.Any(language => language.Equals("language-mermaid", StringComparison.OrdinalIgnoreCase) ||
                                          language.Equals("language-pele", StringComparison.OrdinalIgnoreCase)))
            {
                var image = AozoraParserService.CloneBlockProperties(_current);
                image.SourceLineNumber = _sourceLine;
                image.Inlines.Add(new AozoraImage { Source = PeleDiagramSource.Create(code) });
                Blocks.Add(image);
                return;
            }
            string language = languages.FirstOrDefault(s => s.StartsWith("language-", StringComparison.OrdinalIgnoreCase))?[9..] ?? "";
            var lines = MarkdownCodeHighlighter.HighlightLines(code, language);
            int group = ++_codeGroup;
            for (int i = 0; i < lines.Count; i++)
            {
                Blocks.Add(new AozoraBindingModel
                {
                    IsCodeBlock = true, CodeBlockGroup = group, IsTable = true,
                    CodeBlockLineIndex = i, CodeBlockLineCount = lines.Count,
                    IsParagraphContinuation = i > 0, FontFamily = "Consolas",
                    FontSizeScale = 0.85,
                    BackgroundColor = ColorHelper.FromArgb(30, 128, 128, 128),
                    Padding = new Thickness(16, i == 0 ? 16 : 0, 16, i == lines.Count - 1 ? 16 : 0),
                    Margin = new Thickness(16, 0, 16, i == lines.Count - 1 ? 16 : 0),
                    SourceLineNumber = _sourceLine + i,
                    Inlines = lines[i]
                });
            }
        }

    }
}
