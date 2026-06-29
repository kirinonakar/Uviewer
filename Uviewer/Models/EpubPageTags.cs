namespace Uviewer.Models
{
    public sealed class EpubPageInfoTag
    {
        public int StartLine { get; set; }
        public int LineCount { get; set; }
        public int TotalLinesInChapter { get; set; }
    }

    public sealed class EpubImageTag
    {
        public string FullPath { get; set; } = string.Empty;
    }
}
