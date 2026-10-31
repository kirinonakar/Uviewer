using System;
using System.Text;

namespace Uviewer.Services;

// Keep document parsing independent of the JS engine, SVG parser and rasterizer.
// Source travels with the document, so closing it does not leave a global registry.
internal static class PeleDiagramSource
{
    private const string Prefix = "uviewer-pele:";

    public static string Create(string source) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
    public static bool IsDiagram(string source) => source.StartsWith(Prefix, StringComparison.Ordinal);
    public static string Decode(string source) => Encoding.UTF8.GetString(Convert.FromBase64String(source[Prefix.Length..]));
}
