using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace Uviewer.Models
{
    public class AozoraBindingModel
    {
        public List<object> Inlines { get; set; } = new(); 
        public double FontSizeScale { get; set; } = 1.0;
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public Thickness Margin { get; set; } = new Thickness(0);
        public Thickness Padding { get; set; } = new Thickness(0);
        public Windows.UI.Color? BorderColor { get; set; } = null;
        public Thickness BorderThickness { get; set; } = new Thickness(0);
        public Windows.UI.Color? BackgroundColor { get; set; } = null;
        public string? FontFamily { get; set; } = null; 
        public bool IsTable { get; set; } = false;
        public bool IsCodeBlock { get; set; }
        public bool IsMarkdown { get; set; }
        public int CodeBlockGroup { get; set; }
        public int CodeBlockLineIndex { get; set; }
        public int CodeBlockLineCount { get; set; }
        public List<List<string>> TableRows { get; set; } = new();
        // Shared by every row of one table, so widths stay stable across pages.
        public List<List<List<object>>> TableContentRows { get; set; } = new();
        public int SourceLineNumber { get; set; } = 0; 
        public int HeadingLevel { get; set; } = 0; 
        public string HeadingText { get; set; } = "";
        public bool HasImage => Inlines.Any(i => i is AozoraImage);
        public int EpubChapterIndex { get; set; } = -1;
        public bool IsPageBreak { get; set; } = false;
        public bool IsBold { get; set; } = false;
        public double BlockIndent { get; set; } = 0;
        public double BlockIndentChars { get; set; } = 0;
        public double RightMarginChars { get; set; } = 0;
        public bool IsBlankLine { get; set; } = false;
        public bool IsParagraphContinuation { get; set; } = false;
        public int TableRowIndex { get; set; } = -1;
        public int TableRowCount { get; set; } = 0;
        public int OriginalBlockIndex { get; set; } = -1;
        public int HorizontalColumnIndex { get; set; }
        public List<AozoraSearchSegment> SearchSegments { get; set; } = new();
    }

    public class AozoraSearchSegment
    {
        public int BlockIndex { get; set; } = -1;
        public int Start { get; set; }
        public int Length { get; set; }
    }

    public class AozoraBold { public string Text { get; set; } = ""; }
    public class AozoraStyledText
    {
        public string Text { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool Highlight { get; set; }
        public bool Code { get; set; }
        public int Script { get; set; }
        public string? SyntaxScope { get; set; }
        public bool IsLink { get; set; }
    }
    public class AozoraItalic { public string Text { get; set; } = ""; }
    public class AozoraCode { public string Text { get; set; } = ""; }
    public class AozoraHighlight { public string Text { get; set; } = ""; }
    public class AozoraMath { public string Text { get; set; } = ""; public bool DisplayMode { get; set; } = false; public bool IsBold { get; set; } = false; }
    public class AozoraLineBreak { }
    public class AozoraRuby { public string BaseText { get; set; } = ""; public string RubyText { get; set; } = ""; public bool IsBold { get; set; } = false; }
    public class AozoraTCY { public string Text { get; set; } = ""; public bool IsBold { get; set; } = false; }
    public class AozoraImage
    {
        public string Source { get; set; } = "";
        public string AltText { get; set; } = "";
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool WidthIsPercent { get; set; }
        public bool HeightIsPercent { get; set; }
        public double? MaxWidth { get; set; }
        public bool MaxWidthIsPercent { get; set; }
    }
}
