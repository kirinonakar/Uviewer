using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using System;
using Uviewer.Models;
using Windows.UI;

namespace Uviewer.Services;

internal static class MarkdownNativeStyle
{
    public static bool IsDark(Color foreground) => foreground.R * 0.2126 + foreground.G * 0.7152 + foreground.B * 0.0722 > 128;
    public static Color CodeBackground(Color foreground) => IsDark(foreground) ? Rgb(0x161b22) : Rgb(0xf6f8fa);
    public static Color InlineCodeBackground(Color foreground) => IsDark(foreground)
        ? ColorHelper.FromArgb(102, 110, 118, 129) : ColorHelper.FromArgb(51, 175, 184, 193);
    public static Color LinkColor(Color foreground) => IsDark(foreground) ? Rgb(0x58a6ff) : Rgb(0x0969da);

    public static float LineSpacing(AozoraBindingModel block, float fontSize) => block.IsMarkdown
        ? fontSize * (block.IsCodeBlock ? 1.45f : 1.5f)
        : fontSize * (block.IsTable || block.HeadingLevel > 0 ? 1.3f : 2.1f);
    public static float Baseline(AozoraBindingModel block, float fontSize) => block.IsMarkdown
        ? fontSize * 1.15f : fontSize * (block.IsTable || block.HeadingLevel > 0 ? 1.05f : 1.35f);

    public static void ApplyCode(CanvasTextLayout layout, int start, int length, float fontSize, bool isBlock)
    {
        if (length <= 0) return;
        layout.SetFontFamily(start, length, "Consolas");
        if (!isBlock) layout.SetFontSize(start, length, fontSize * 0.85f);
    }

    public static Color? SyntaxColor(string? scope, Color foreground)
    {
        if (string.IsNullOrEmpty(scope)) return null;
        string name = scope.ToLowerInvariant();
        bool dark = IsDark(foreground);
        if (name.Contains("comment")) return dark ? Rgb(0x8b949e) : Rgb(0x6e7781);
        if (name.Contains("string")) return dark ? Rgb(0xa5d6ff) : Rgb(0x0a3069);
        if (name.Contains("keyword") || name.Contains("preprocessor")) return dark ? Rgb(0xff7b72) : Rgb(0xcf222e);
        if (name.Contains("number") || name.Contains("literal") || name.Contains("attribute")) return dark ? Rgb(0x79c0ff) : Rgb(0x0550ae);
        if (name.Contains("class") || name.Contains("type") || name.Contains("function")) return dark ? Rgb(0xd2a8ff) : Rgb(0x8250df);
        if (name.Contains("tag")) return dark ? Rgb(0x7ee787) : Rgb(0x116329);
        return null;
    }

    private static Color Rgb(int rgb) => ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
