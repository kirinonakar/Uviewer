using System.Collections.Generic;

namespace Uviewer.Models
{
    public class EpubWin2DPage
    {
        public List<AozoraBindingModel> Blocks { get; set; } = new();
        public int StartBlockIndex { get; set; }
        public int StartLine { get; set; }
        public int LineCount { get; set; }
        public int TotalLinesInChapter { get; set; }
        public bool IsImagePage { get; set; }
        public string ImagePath { get; set; } = string.Empty;
    }
}
