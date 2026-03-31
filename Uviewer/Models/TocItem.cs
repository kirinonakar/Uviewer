using Microsoft.UI.Xaml;

namespace Uviewer.Models
{
    public class TocItem
    {
        public string HeadingText { get; set; } = "";
        public int HeadingLevel { get; set; }
        public int SourceLineNumber { get; set; } = -1;
        public object? Tag { get; set; }

        public Thickness Margin => new Thickness((HeadingLevel > 0 ? HeadingLevel - 1 : 0) * 16, 0, 0, 0);

        // For EPUB 
        public string EpubLink { get; set; } = "";
    }

    public class EpubTocItem
    {
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public int Level { get; set; } = 1;
    }
}
