using System.Collections.Generic;

namespace Uviewer.Models
{
    public struct ReaderPageInfo
    {
        public List<AozoraBindingModel> Blocks;
        public int StartLine;

        public static ReaderPageInfo Empty => new()
        {
            Blocks = new List<AozoraBindingModel>(),
            StartLine = 1
        };

        public static ReaderPageInfo FromBlocks(List<AozoraBindingModel> blocks)
        {
            return new ReaderPageInfo
            {
                Blocks = blocks,
                StartLine = blocks.Count > 0 ? blocks[0].SourceLineNumber : 1
            };
        }
    }
}
