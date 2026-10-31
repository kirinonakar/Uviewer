using ColorCode;
using ColorCode.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services;

// Produces native styled runs; no HTML view or script execution is involved.
internal sealed class MarkdownCodeHighlighter : CodeColorizerBase
{
    private readonly List<AozoraStyledText> _runs = new();
    private MarkdownCodeHighlighter() : base(null, null) { }

    public static List<List<object>> HighlightLines(string code, string language)
    {
        string id = language.ToLowerInvariant() switch
        {
            "cs" or "c#" or "csharp" => "csharp", "js" or "jsx" => "javascript",
            "ts" or "tsx" => "typescript", "py" => "python", "c" or "c++" or "h" => "cpp",
            "ps1" or "pwsh" => "powershell", "htm" => "html", "yml" => "yaml", _ => language.ToLowerInvariant()
        };
        var formatter = new MarkdownCodeHighlighter();
        var grammar = id switch
        {
            "csharp" => Languages.CSharp, "javascript" => Languages.JavaScript,
            "typescript" => Languages.Typescript, "python" => Languages.Python,
            "cpp" => Languages.Cpp, "powershell" => Languages.PowerShell,
            "html" => Languages.Html, "css" => Languages.Css, "xml" => Languages.Xml,
            _ => Languages.FindById(id)
        };
        try
        {
            // Keep oversized/unknown code readable, without expensive tokenization.
            if (grammar != null && code.Length <= 100_000)
                formatter.languageParser.Parse(code, grammar, formatter.Write);
            else formatter._runs.Add(new AozoraStyledText { Text = code, Code = true });
        }
        catch
        {
            formatter._runs.Clear();
            formatter._runs.Add(new AozoraStyledText { Text = code, Code = true });
        }
        var lines = new List<List<object>> { new() };
        foreach (var run in formatter._runs)
        {
            var pieces = run.Text.Split('\n');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) lines.Add(new());
                if (pieces[i].Length > 0)
                    lines[^1].Add(new AozoraStyledText { Text = pieces[i], Code = true, SyntaxScope = run.SyntaxScope });
            }
        }
        return lines;
    }

    protected override void Write(string text, IList<Scope> scopes)
    {
        var flattened = new List<(int Start, int End, string Name, int Depth)>();
        void Visit(Scope scope, int depth)
        {
            flattened.Add((Math.Clamp(scope.Index, 0, text.Length), Math.Clamp(scope.Index + scope.Length, 0, text.Length), scope.Name, depth));
            foreach (var child in scope.Children) Visit(child, depth + 1);
        }
        foreach (var scope in scopes) Visit(scope, 0);
        if (flattened.Count > 4096)
        {
            _runs.Add(new AozoraStyledText { Text = text, Code = true });
            return;
        }
        var boundaries = flattened.SelectMany(s => new[] { s.Start, s.End }).Append(0).Append(text.Length).Distinct().Order().ToArray();
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int start = boundaries[i], end = boundaries[i + 1];
            if (end <= start) continue;
            string? name = flattened.Where(s => s.Start <= start && s.End >= end).OrderByDescending(s => s.Depth).Select(s => s.Name).FirstOrDefault();
            _runs.Add(new AozoraStyledText { Text = text[start..end], Code = true, SyntaxScope = name });
        }
    }
}
