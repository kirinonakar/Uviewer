using System;

namespace Uviewer.Models;

internal enum ReaderContentKind { PlainText, Aozora, Markdown, Epub }
internal enum ReaderOrientation { Horizontal, Vertical }

// A snapshot of the effective display mode; persisted format preferences stay separate.
internal readonly record struct ReaderMode(ReaderContentKind ContentKind, ReaderOrientation Orientation)
{
    internal ReaderSurfaces Surfaces => (ContentKind, Orientation) switch
    {
        (ReaderContentKind.Epub, _) => new(false, false, false, false, true),
        (ReaderContentKind.Markdown, _) => new(false, true, false, true, false),
        (_, ReaderOrientation.Vertical) => new(false, false, true, true, false),
        (ReaderContentKind.PlainText, ReaderOrientation.Horizontal) => new(true, false, false, true, false),
        (ReaderContentKind.Aozora, ReaderOrientation.Horizontal) => new(false, true, false, true, false),
        _ => throw new ArgumentOutOfRangeException()
    };
}

internal readonly record struct ReaderSurfaces(
    bool TextScroll, bool AozoraCanvas, bool VerticalCanvas, bool TextArea, bool EpubArea);
