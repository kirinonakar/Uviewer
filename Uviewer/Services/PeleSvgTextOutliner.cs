using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using Windows.UI.Text;

namespace Uviewer.Services;

// Adapts the text/tspan and inline styles emitted by the bundled Pele renderer
// to Direct2D's SVG subset. Pele already emits arrowheads as ordinary paths.
internal static class PeleSvgTextOutliner
{
    public static void Convert(ICanvasResourceCreator device, XElement root) =>
        Visit(device, root, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["font-family"] = "Segoe UI", ["font-size"] = "16", ["text-anchor"] = "start"
        }, null);

    private static void Visit(ICanvasResourceCreator device, XElement element,
        Dictionary<string, string> inherited, TextCursor? cursor)
    {
        var properties = new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes())
            if (IsTextProperty(attribute.Name.LocalName)) properties[attribute.Name.LocalName] = attribute.Value;
        foreach (string declaration in ((string?)element.Attribute("style") ?? "").Split(';'))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            string name = declaration[..colon].Trim(), value = declaration[(colon + 1)..].Trim();
            if (IsTextProperty(name)) properties[name] = value;
            // Pele uses CSS geometry properties for rounded corners. Direct2D
            // accepts rx/ry as XML attributes, rather than presentation styles.
            if (name is "rx" or "ry") element.SetAttributeValue(name, value);
        }

        float parentSize = ParseNumber(inherited["font-size"], 16);
        string size = properties["font-size"];
        float fontSize = size switch
        {
            "smaller" => parentSize * 0.8f,
            "larger" => parentSize * 1.2f,
            _ => ParseNumber(size, parentSize)
        };
        properties["font-size"] = Math.Clamp(fontSize, 1, 1024).ToString(CultureInfo.InvariantCulture);
        // Baseline shifts are relative to the parent line, including nested spans.
        float baseline = ParseNumber(inherited.GetValueOrDefault("baseline-offset", "0"), 0);
        string? shift = (string?)element.Attribute("baseline-shift");
        if (shift != null)
            baseline += shift switch { "sub" => parentSize * 0.2f, "super" => -parentSize * 0.35f, _ => -ParseNumber(shift, 0) };
        properties["baseline-offset"] = baseline.ToString(CultureInfo.InvariantCulture);

        bool textRoot = element.Name.LocalName == "text";
        bool text = textRoot || element.Name.LocalName == "tspan";
        if (text)
        {
            if (textRoot) cursor = new TextCursor();
            cursor ??= new TextCursor();
            if (textRoot || element.Attribute("x") != null || element.Attribute("y") != null)
            {
                cursor.FinishChunk();
                cursor.Anchor = properties["text-anchor"];
                cursor.Position = new Vector2(Number(element.Attribute("x"), cursor.Position.X),
                    Number(element.Attribute("y"), cursor.Position.Y));
            }
            cursor.Position += new Vector2(Number(element.Attribute("dx"), 0), Number(element.Attribute("dy"), 0));
            element.Name = element.Name.Namespace + "g";
            foreach (string name in new[] { "x", "y", "dx", "dy", "text-anchor", "textLength", "lengthAdjust" })
                element.Attribute(name)?.Remove();
        }

        foreach (var node in element.Nodes().ToArray())
        {
            if (node is XElement child)
            {
                Visit(device, child, properties, text ? cursor : null);
            }
            else if (text && node is XText content)
            {
                var (path, advance) = Outline(device, content.Value, properties, cursor!.Position + new Vector2(0, baseline), element.Name.Namespace);
                content.ReplaceWith(path);
                cursor.Position += new Vector2(advance, 0);
                cursor.Width += advance;
                cursor.Paths.Add(path);
            }
        }
        if (textRoot) cursor!.FinishChunk();
    }

    private static bool IsTextProperty(string name) => name is
        "font-family" or "font-size" or "font-weight" or "font-style" or "text-anchor" or "text-decoration";

    private static float Number(XAttribute? attribute, float fallback) =>
        attribute == null ? fallback : ParseNumber(attribute.Value, fallback);

    private static float ParseNumber(string value, float fallback)
    {
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) &&
            float.IsFinite(result) ? result : fallback;
    }

    private static (XElement Path, float Advance) Outline(ICanvasResourceCreator device, string text,
        Dictionary<string, string> properties, Vector2 origin, XNamespace ns)
    {
        float fontSize = Math.Clamp(ParseNumber(properties.GetValueOrDefault("font-size", "16"), 16), 1, 1024);
        string weight = properties.GetValueOrDefault("font-weight", "normal");
        ushort numericWeight = weight == "bold" ? (ushort)700 :
            ushort.TryParse(weight, out ushort number) ? (ushort)Math.Clamp((int)number, 1, 999) : (ushort)400;
        using var format = new CanvasTextFormat
        {
            FontFamily = properties["font-family"].Split(',')[0].Trim().Trim('\'', '"') switch
            {
                "monospace" => "Consolas", "sans-serif" => "Segoe UI", "serif" => "Times New Roman", var family => family
            },
            FontSize = fontSize,
            FontWeight = new FontWeight { Weight = numericWeight },
            FontStyle = properties.GetValueOrDefault("font-style") switch
            {
                "italic" => FontStyle.Italic, "oblique" => FontStyle.Oblique, _ => FontStyle.Normal
            },
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        using var layout = new CanvasTextLayout(device, text, format, 0, 0);
        string decoration = properties.GetValueOrDefault("text-decoration", "");
        if (decoration.Contains("underline", StringComparison.Ordinal)) layout.SetUnderline(0, text.Length, true);
        if (decoration.Contains("line-through", StringComparison.Ordinal)) layout.SetStrikethrough(0, text.Length, true);
        float advance = (float)layout.LayoutBoundsIncludingTrailingWhitespace.Width;
        // SVG y is a baseline; CanvasTextLayout's origin is the top of its line box.
        if (layout.LineMetrics.Length > 0) origin.Y -= layout.LineMetrics[0].Baseline;
        using var geometry = CanvasGeometry.CreateText(layout);
        using var positioned = geometry.Simplify(CanvasGeometrySimplification.CubicsAndLines,
            Matrix3x2.CreateTranslation(origin), CanvasGeometry.DefaultFlatteningTolerance);
        var writer = new SvgPathWriter();
        positioned.SendPathTo(writer);
        return (new XElement(ns + "path", new XAttribute("d", writer.Data.ToString()),
            new XAttribute("fill-rule", writer.FillRule)), advance);
    }

    private sealed class TextCursor
    {
        public Vector2 Position;
        public float Width;
        public string Anchor = "start";
        public List<XElement> Paths { get; } = new();

        public void FinishChunk()
        {
            // SVG anchors apply to a complete positioned chunk, including all
            // following styled tspans, rather than centering each run separately.
            float offset = Anchor switch { "middle" => Width / 2, "end" => Width, _ => 0 };
            if (offset != 0)
                foreach (var path in Paths)
                    path.SetAttributeValue("transform", FormattableString.Invariant($"translate({-offset},0)"));
            Paths.Clear();
            Width = 0;
        }
    }

    private sealed class SvgPathWriter : ICanvasPathReceiver
    {
        public StringBuilder Data { get; } = new();
        public string FillRule { get; private set; } = "nonzero";
        private void Point(Vector2 point) => Data.Append(CultureInfo.InvariantCulture, $"{point.X},{point.Y} ");
        public void BeginFigure(Vector2 startPoint, CanvasFigureFill figureFill) { Data.Append('M'); Point(startPoint); }
        public void AddLine(Vector2 endPoint) { Data.Append('L'); Point(endPoint); }
        public void AddCubicBezier(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 endPoint)
        { Data.Append('C'); Point(controlPoint1); Point(controlPoint2); Point(endPoint); }
        public void AddQuadraticBezier(Vector2 controlPoint, Vector2 endPoint)
        { Data.Append('Q'); Point(controlPoint); Point(endPoint); }
        public void AddArc(Vector2 endPoint, float radiusX, float radiusY, float rotationAngle,
            CanvasSweepDirection sweepDirection, CanvasArcSize arcSize) =>
            throw new InvalidOperationException("Simplified text outlines must not contain arcs.");
        public void EndFigure(CanvasFigureLoop figureLoop) { if (figureLoop == CanvasFigureLoop.Closed) Data.Append('Z'); }
        public void SetFilledRegionDetermination(CanvasFilledRegionDetermination value) =>
            FillRule = value == CanvasFilledRegionDetermination.Alternate ? "evenodd" : "nonzero";
        public void SetSegmentOptions(CanvasFigureSegmentOptions options) { }
    }
}
