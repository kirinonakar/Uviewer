using System.Text.Json.Serialization;

namespace Uviewer.Models
{
    public class TextSettings
    {
        public double FontSize { get; set; } = 18;
        public string FontFamily { get; set; } = "Yu Gothic";
        public int ThemeIndex { get; set; } = 0;
        public bool IsVerticalMode { get; set; } = false;
        public string? CustomBackgroundColor { get; set; }
        public string? CustomForegroundColor { get; set; }
        public string? Language { get; set; } // "ko-KR", "en-US", "ja-JP" or null for auto
        public string? UIFontFamily { get; set; }
    }

    [JsonSerializable(typeof(TextSettings))]
    public partial class TextSettingsContext : JsonSerializerContext
    {
    }
}
