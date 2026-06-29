using Windows.System;

namespace Uviewer.Services
{
    internal readonly record struct ShortcutInput(VirtualKey Key, bool CtrlPressed);

    internal sealed class ShortcutContext
    {
        public bool IsColorPickerOpen { get; init; }
        public bool IsFullscreen { get; init; }
        public bool IsEpubMode { get; init; }
        public bool IsTextMode { get; init; }
        public bool IsAozoraMode { get; init; }
        public bool IsVerticalMode { get; init; }
        public bool ShouldInvertControls { get; init; }
        public int CurrentEpubChapterIndex { get; init; }
        public int EpubSpineCount { get; init; }
        public int CurrentImageIndex { get; init; }
        public int ImageEntriesCount { get; init; }
        public bool HasPdfDocument { get; init; }
        public bool IsAboutDialogActive { get; init; }
        public bool IsSearchOverlayOpen { get; init; }
        public bool CanSearchCurrentDocument { get; init; }
    }
}
