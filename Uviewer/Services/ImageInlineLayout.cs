using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using System;
using System.Collections.Generic;
using Uviewer.Models;
using Windows.Foundation;

namespace Uviewer.Services;

// Native DirectWrite inline objects keep images/badges in the surrounding prose.
internal sealed class ImageInlineLayout : IDisposable
{
    private readonly List<ImageObject> _objects = new();

    public void Apply(CanvasTextLayout layout, IEnumerable<(int Start, AozoraImage Image)> ranges,
        float maxWidth, Func<AozoraImage, CanvasBitmap?>? getImage)
    {
        foreach (var range in ranges)
        {
            var bitmap = getImage?.Invoke(range.Image);
            var item = new ImageObject(range.Image, bitmap, maxWidth);
            _objects.Add(item);
            layout.SetInlineObject(range.Start, 1, item);
        }
        // The caller creates image-bearing layouts with natural line spacing.
        // Changing the mode after constructing a uniform layout retains its fixed
        // line boxes in Win2D, even when the public mode property says Default.
    }

    public void Draw(CanvasDrawingSession session, Action drawText)
    {
        foreach (var item in _objects) item.Session = session;
        try { drawText(); }
        finally { foreach (var item in _objects) item.Session = null; }
    }

    public void Dispose() { foreach (var item in _objects) item.Session = null; }

    private sealed class ImageObject : ICanvasTextInlineObject
    {
        private readonly CanvasBitmap? _bitmap;
        private readonly string _alt;
        private readonly bool _isDiagram;
        public CanvasDrawingSession? Session { get; set; }
        public Size Size { get; }
        public float Baseline => (float)Size.Height;
        public Rect DrawBounds => new(0, 0, Size.Width, Size.Height);
        public bool SupportsSideways => false;
        public CanvasLineBreakCondition BreakBefore => CanvasLineBreakCondition.CanBreak;
        public CanvasLineBreakCondition BreakAfter => CanvasLineBreakCondition.CanBreak;

        public ImageObject(AozoraImage image, CanvasBitmap? bitmap, float maxWidth)
        {
            _bitmap = bitmap;
            _alt = image.AltText;
            _isDiagram = PeleDiagramSource.IsDiagram(image.Source);
            // Ordinary image pixels follow CSS sizing. Generated diagrams retain
            // their logical size while carrying extra pixels for HiDPI displays.
            bool useLogicalSize = bitmap is CanvasRenderTarget || _isDiagram;
            double naturalWidth = bitmap == null ? 180 : useLogicalSize ? bitmap.Size.Width : bitmap.SizeInPixels.Width;
            double naturalHeight = bitmap == null ? 120 : useLogicalSize ? bitmap.Size.Height : bitmap.SizeInPixels.Height;
            var size = MarkdownImageSizing.Resolve(image, naturalWidth, naturalHeight, maxWidth);
            Size = new Size(size.Width, size.Height);
        }

        public void Draw(ICanvasTextRenderer renderer, System.Numerics.Vector2 baselineOrigin, bool isSideways, bool isRightToLeft, object brush)
        {
            if (Session == null) return;
            // Win2D supplies the object's top-left after baseline alignment.
            var rect = new Rect(baselineOrigin.X, baselineOrigin.Y, Size.Width, Size.Height);
            if (_bitmap != null)
            {
                if (_isDiagram)
                    Session.DrawImage(_bitmap, rect, _bitmap.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
                else Session.DrawImage(_bitmap, rect);
            }
            else
            {
                Session.DrawRectangle(rect, Colors.Gray, 1);
                if (_alt.Length > 0)
                {
                    using var format = new CanvasTextFormat { FontSize = 12, WordWrapping = CanvasWordWrapping.Wrap };
                    Session.DrawText(_alt, rect, Colors.Gray, format);
                }
            }
        }
    }
}
