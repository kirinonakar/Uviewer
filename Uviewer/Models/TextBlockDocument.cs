using System.Collections.Generic;

namespace Uviewer.Models
{
    public sealed class TextBlockDocument
    {
        public TextBlockDocument(List<AozoraBindingModel> blocks, int sourceLineCount)
        {
            Blocks = blocks;
            SourceLineCount = sourceLineCount;
        }

        public List<AozoraBindingModel> Blocks { get; }
        public int SourceLineCount { get; }
        public int BlockCount => Blocks.Count;
    }
}
