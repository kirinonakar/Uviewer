namespace Uviewer.Models
{
    public readonly struct ReaderPageMargins
    {
        public ReaderPageMargins(float top, float bottom, float right, float left)
        {
            Top = top;
            Bottom = bottom;
            Right = right;
            Left = left;
        }

        public float Top { get; }
        public float Bottom { get; }
        public float Right { get; }
        public float Left { get; }
        public float Horizontal => Left + Right;
        public float Vertical => Top + Bottom;

        public static ReaderPageMargins HorizontalText => new(30f, 10f, 40f, 40f);
        public static ReaderPageMargins VerticalText => new(20f, 20f, 30f, 10f);
        public static ReaderPageMargins EpubVerticalText => new(20f, 20f, 30f, 25f);
    }

    public readonly struct ReaderPageLayout
    {
        public ReaderPageLayout(
            ReaderPageMargins margins,
            float availableWidth,
            float availableHeight,
            float maxWidth)
        {
            Margins = margins;
            AvailableWidth = availableWidth;
            AvailableHeight = availableHeight;
            MaxWidth = maxWidth;
        }

        public ReaderPageMargins Margins { get; }
        public float AvailableWidth { get; }
        public float AvailableHeight { get; }
        public float MaxWidth { get; }
    }

    public readonly struct ReaderViewportSize
    {
        public ReaderViewportSize(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }
        public float Height { get; }
    }
}
