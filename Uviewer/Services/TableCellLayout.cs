using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Uviewer.Models;
using Uviewer.Renderers;
using Windows.UI;
using Windows.UI.Text;

namespace Uviewer.Services;

internal sealed class TableCellLayout : IDisposable
{
    public CanvasTextLayout Layout { get; }
    private readonly MathInlineLayout _math = new();
    private readonly ImageInlineLayout _images = new();
    private readonly List<(int start, int length)> _code = new();
    private readonly float _fontSize;

    public TableCellLayout(ICanvasResourceCreator creator, IEnumerable<object> inlines,
        float width, float fontSize, string font, FontWeight weight, Color color, bool wrap = true,
        Func<AozoraImage, CanvasBitmap?>? getImage = null)
    {
        _fontSize = fontSize;
        var text = new StringBuilder();
        var bold = new List<(int, int)>();
        var italic = new List<(int, int)>();
        var math = new List<(int start, int length, AozoraMath math)>();
        var styled = new List<(int start, int length, AozoraStyledText style)>();
        var images = new List<(int Start, AozoraImage Image)>();
        foreach (var item in inlines)
        {
            int start = text.Length;
            text.Append(InlineText(item));
            int length = text.Length - start;
            if (item is AozoraBold || item is AozoraMath { IsBold: true } || item is AozoraRuby { IsBold: true }) bold.Add((start, length));
            if (item is AozoraItalic) italic.Add((start, length));
            if (item is AozoraMath formula) math.Add((start, length, formula));
            if (item is AozoraStyledText style) styled.Add((start, length, style));
            if (item is AozoraImage image) images.Add((start, image));
            if (item is AozoraCode || item is AozoraStyledText { Code: true }) _code.Add((start, length));
        }
        using var format = new CanvasTextFormat
        {
            FontFamily = font, FontSize = fontSize, FontWeight = weight,
            LineSpacing = images.Count > 0 ? -1 : fontSize * 1.3f, VerticalAlignment = CanvasVerticalAlignment.Top,
            LineSpacingBaseline = images.Count > 0 ? 0 : fontSize * 1.05f,
            LineSpacingMode = images.Count > 0 ? CanvasLineSpacingMode.Default : CanvasLineSpacingMode.Uniform,
            WordWrapping = wrap ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap
        };
        Layout = new CanvasTextLayout(creator, text.Length == 0 ? " " : text.ToString(), format, Math.Max(1, width), 0);
        Layout.Options = CanvasDrawTextOptions.EnableColorFont;
        foreach (var (start, length) in bold) Layout.SetFontWeight(start, length, Microsoft.UI.Text.FontWeights.Bold);
        foreach (var (start, length) in italic) Layout.SetFontStyle(start, length, FontStyle.Italic);
        foreach (var r in styled) InlineTextFormatting.Apply(Layout, r.start, r.length, r.style);
        foreach (var r in _code) MarkdownNativeStyle.ApplyCode(Layout, r.start, r.length, fontSize, false);
        foreach (var r in styled.Where(r => r.style.IsLink && r.length > 0)) Layout.SetColor(r.start, r.length, MarkdownNativeStyle.LinkColor(color));
        _math.Apply(Layout, math, fontSize, width, color, images.Count > 0);
        _images.Apply(Layout, images, width, getImage);
    }

    public void Draw(CanvasDrawingSession session, float x, float y, Color color)
    {
        HorizontalRenderer.DrawRangeBackgrounds(session, Layout, _code, x, y, MarkdownNativeStyle.InlineCodeBackground(color), _fontSize, rounded: true);
        _images.Draw(session, () => _math.Draw(session, Layout, x, y, color));
    }
    public void Dispose() { _images.Dispose(); _math.Dispose(); Layout.Dispose(); }

    public static string InlineText(object item) => item switch
    {
        string text => text, AozoraBold x => x.Text, AozoraItalic x => x.Text, AozoraStyledText x => x.Text,
        AozoraCode x => x.Text, AozoraHighlight x => x.Text, AozoraRuby x => x.BaseText,
        AozoraTCY x => x.Text, AozoraMath x => KatexStandaloneRenderer.RenderToText(x.Text),
        AozoraLineBreak => "\n", AozoraImage => "\uFFFC", _ => ""
    };

    public static IEnumerable<object> GetCell(AozoraBindingModel block, int column)
    {
        if (block.TableRowIndex >= 0 && block.TableRowIndex < block.TableContentRows.Count &&
            column < block.TableContentRows[block.TableRowIndex].Count)
            return block.TableContentRows[block.TableRowIndex][column];
        var parsed = HorizontalRenderer.ParseTableInline(block.TableRows[0][column]);
        // Legacy Aozora/EPUB rows can still carry Markdown bold markers.
        var result = new List<object>();
        int start = 0;
        foreach (var bold in parsed.boldRanges.OrderBy(r => r.start))
        {
            if (bold.start > start) result.Add(parsed.text[start..bold.start]);
            result.Add(new AozoraBold { Text = parsed.text.Substring(bold.start, bold.length) });
            start = bold.start + bold.length;
        }
        if (start < parsed.text.Length) result.Add(parsed.text[start..]);
        return result;
    }
}
