using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Numerics;
using Uviewer.Models;
using Uviewer.Renderers;
using Windows.Foundation;
using Windows.UI;

namespace Uviewer.Services;

internal sealed class MathInlineLayout : IDisposable
{
    private readonly List<MathObject> _objects = new();

    public void Apply(CanvasTextLayout layout, IEnumerable<(int start, int length, AozoraMath math)> ranges,
        float fontSize, float maxWidth, Color color, bool preserveNaturalLineSpacing = false)
    {
        float ascent = 0, descent = 0;
        foreach (var range in ranges)
        {
            if (range.length == 0) continue;
            var item = MathObject.Create(range.math, fontSize, maxWidth, color);
            if (item == null) continue; // Keep the readable fallback for unsupported TeX.
            _objects.Add(item);
            layout.SetInlineObject(range.start, range.length, item);
            ascent = Math.Max(ascent, item.Baseline);
            descent = Math.Max(descent, (float)item.Size.Height - item.Baseline);
        }

        if (_objects.Count > 0 && !preserveNaturalLineSpacing)
        {
            // Uniform spacing must reserve both sides of the common text/math baseline.
            // Keep the normal reading rhythm, but expand it for tall fractions and limits.
            float baseline = Math.Max(fontSize * 1.35f, ascent + fontSize * 0.15f);
            layout.LineSpacingBaseline = baseline;
            layout.LineSpacing = Math.Max(layout.LineSpacing, baseline + Math.Max(fontSize * 0.4f, descent) + fontSize * 0.15f);
            layout.LineSpacingMode = CanvasLineSpacingMode.Uniform;
        }
    }

    public void Draw(CanvasDrawingSession session, CanvasTextLayout layout, float x, float y, Color color)
    {
        foreach (var item in _objects) item.Session = session;
        try { session.DrawTextLayout(layout, x, y, color); }
        finally { foreach (var item in _objects) item.Session = null; }
    }

    public void Dispose() { foreach (var item in _objects) item.Session = null; }

    private sealed partial class MathObject : ICanvasTextInlineObject
    {
        private readonly MathPainter _painter;
        private readonly float _scale;
        private readonly float _padding;
        private readonly float _ascent;
        private readonly LatexBoxLayout _box;
        public CanvasDrawingSession? Session { get; set; }
        public Size Size { get; }
        public float Baseline { get; }
        public Rect DrawBounds => new(0, 0, Size.Width, Size.Height);
        public bool SupportsSideways => true;
        public CanvasLineBreakCondition BreakBefore => CanvasLineBreakCondition.CanBreak;
        public CanvasLineBreakCondition BreakAfter => CanvasLineBreakCondition.CanBreak;

        private MathObject(MathPainter painter, LatexBoxLayout box, float fontSize, float maxWidth)
        {
            _painter = painter;
            _box = box;
            var bounds = painter.Measure();
            _padding = fontSize * 0.12f;
            float width = bounds.Width + 2 * box.Padding;
            _scale = Math.Min(1, Math.Max(1, maxWidth - 2 * _padding) / Math.Max(1, width));
            _ascent = -bounds.Top + box.Padding;
            Baseline = _ascent * _scale;
            Size = new Size(width * _scale + 2 * _padding, Math.Max(1, (bounds.Height + 2 * box.Padding) * _scale));
        }

        public static MathObject? Create(AozoraMath math, float fontSize, float maxWidth, Color color)
        {
            var box = LatexBoxLayout.Parse(math.Text, fontSize);
            var painter = new MathPainter
            {
                FontSize = fontSize,
                LaTeX = math.IsBold ? @"\mathbf{" + box.Content + "}" : box.Content,
                // Readable full-size fractions and limits, including inside prose.
                LineStyle = CSharpMath.Atom.LineStyle.Display,
                TextColor = color,
                DisplayErrorInline = false
            };
            if (painter.ErrorMessage != null) return null;
            painter.Measure();
            if (painter.Display == null) return null;
            return new MathObject(painter, box, fontSize, maxWidth);
        }

        public void Draw(ICanvasTextRenderer textRenderer, Vector2 point, bool isSideways, bool isRightToLeft, object brush)
        {
            var session = Session;
            if (session == null) return;
            var original = session.Transform;
            try
            {
                // DirectWrite gives the inline object's top-left, after baseline alignment.
                var transform = Matrix3x2.CreateScale(_scale);
                if (isSideways) transform *= Matrix3x2.CreateRotation(MathF.PI / 2);
                session.Transform = transform * Matrix3x2.CreateTranslation(point) * original;
                float left = _padding / _scale;
                _painter.Draw(new MathCanvas(session), left + _box.Padding, _ascent);
                for (int i = 0; i < _box.BoxCount; i++)
                {
                    float inset = i * _box.Inset + _box.StrokeThickness / 2;
                    session.DrawRectangle(left + inset, inset,
                        (float)(Size.Width - 2 * _padding) / _scale - 2 * inset,
                        (float)Size.Height / _scale - 2 * inset,
                        _painter.TextColor, _box.StrokeThickness);
                }
            }
            finally { session.Transform = original; }
        }
    }
}
