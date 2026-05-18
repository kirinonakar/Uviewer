using System.IO;

namespace Uviewer.Services
{
    public sealed record TextDisplayPreparation(
        string Content,
        bool IsMarkdownRenderMode,
        bool CanUseVerticalMode);

    public sealed class TextDisplayPreparationService
    {
        public TextDisplayPreparation Prepare(string content, string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            bool isMarkdown = extension == ".md" || extension == ".markdown";

            if (extension == ".html" || extension == ".htm")
            {
                content = AozoraParserService.ParseHtml(content);
            }

            return new TextDisplayPreparation(
                content,
                isMarkdown,
                !isMarkdown);
        }
    }
}
