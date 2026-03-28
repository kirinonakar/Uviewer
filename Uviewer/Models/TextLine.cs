using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Uviewer.Models
{
    public class TextLine
    {
        public string Content { get; set; } = "";
        public double FontSize { get; set; }
        public string FontFamily { get; set; } = "Yu Gothic";
        public Brush? Foreground { get; set; }
        public double MaxWidth { get; set; }

        // Styling properties for Aozora
        public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
        public Thickness Margin { get; set; } = new Thickness(0);
        public Thickness Padding { get; set; } = new Thickness(0);
        public Brush? BorderBrush { get; set; } = null;
        public Thickness BorderThickness { get; set; } = new Thickness(0);
    }
}
