using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class EpubDocumentService
    {
        private static readonly Regex RxFullPath = new("full-path=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxItem = new("<item\\s+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxId = new("id=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHref = new("href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxNavProp = new("properties=[\"'][^\"']*nav[^\"']*[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxItemRef = new("<itemref[^>]*idref=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxSpineToc = new("<spine[^>]*toc=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxImgTag = new("(<(?:img|image)\\b[^>]*>)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxIsImg = new("^<(?:img|image)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxAnyTag = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex RxSrc = new("(?:src|xlink:href)=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxScript = new(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxStyle = new(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxRuby = new(@"<ruby[^>]*>(.*?)</ruby>\s*", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxRp = new(@"<rp[^>]*>.*?</rp>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxRt = new(@"<rt[^>]*>(.*?)</rt>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxRubySplit = new(@"(\{\{RUBY\|.*?\}\})", RegexOptions.Compiled);
        private static readonly Regex RxHeading = new(@"<(h[1-6])[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        public async Task<EpubPackageInfo> LoadPackageInfoAsync(ZipArchive archive, SemaphoreSlim archiveLock, CancellationToken token = default)
        {
            string rootPath = await ParseContainerPathAsync(archive, archiveLock, token);
            if (string.IsNullOrEmpty(rootPath))
            {
                return new EpubPackageInfo(string.Empty, new List<string>(), null);
            }

            return await ParseOpfAsync(archive, archiveLock, rootPath, token);
        }

        public async Task<string> ParseContainerPathAsync(ZipArchive archive, SemaphoreSlim archiveLock, CancellationToken token = default)
        {
            string? content = await ReadEntryTextAsync(archive, "META-INF/container.xml", archiveLock, token);
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var match = RxFullPath.Match(content);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public async Task<string?> ReadEntryTextAsync(ZipArchive archive, string path, SemaphoreSlim archiveLock, CancellationToken token = default)
        {
            var entry = archive.GetEntry(path);
            if (entry == null) return null;

            await archiveLock.WaitAsync(token);
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(token);
            }
            finally
            {
                archiveLock.Release();
            }
        }

        public EpubHtmlParseResult ParseHtmlToAozoraBlocks(
            string html,
            string currentPath,
            int chapterIndex,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var blocks = new List<AozoraBindingModel>();

            html = RxScript.Replace(html, string.Empty);
            html = RxStyle.Replace(html, string.Empty);
            html = RxBr.Replace(html, "\n");

            var segments = RxImgTag.Split(html);
            int lineNum = 1;
            bool hasImages = html.Contains("<img", StringComparison.OrdinalIgnoreCase) || html.Contains("<image", StringComparison.OrdinalIgnoreCase);
            bool isFirstContent = true;

            foreach (var segment in segments)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(segment)) continue;

                if (RxIsImg.IsMatch(segment))
                {
                    var match = RxSrc.Match(segment);
                    if (match.Success)
                    {
                        string src = match.Groups[1].Value;
                        string fullPath = ResolveRelativePath(currentPath, src);

                        var block = new AozoraBindingModel { SourceLineNumber = lineNum++, EpubChapterIndex = chapterIndex };
                        block.Inlines.Add(new AozoraImage { Source = fullPath });
                        blocks.Add(block);
                        isFirstContent = false;
                    }
                }
                else
                {
                    var textBlocks = ParseHtmlToAozoraTextBlocks(segment, ref lineNum, chapterIndex);

                    if (isFirstContent && hasImages)
                    {
                        string plainText = RxAnyTag.Replace(segment, string.Empty);
                        plainText = WebUtility.HtmlDecode(plainText).Trim();
                        if (plainText.Length > 0 && plainText.Length < 150)
                        {
                            isFirstContent = false;
                            continue;
                        }
                    }

                    if (textBlocks.Count > 0)
                    {
                        blocks.AddRange(textBlocks);
                        isFirstContent = false;
                    }
                }
            }

            var splitBlocks = new List<AozoraBindingModel>();
            foreach (var block in blocks)
            {
                token.ThrowIfCancellationRequested();
                splitBlocks.AddRange(AozoraParserService.SplitBlockBySentences(block));
            }

            return new EpubHtmlParseResult(splitBlocks, lineNum - 1);
        }

        public (EpubHtmlParseResult Result, bool IsPartial) ParseHtmlPreview(
            string html,
            string currentPath,
            int chapterIndex,
            int targetLine,
            int targetBlockIndex,
            int initialCharacterCount = 64 * 1024,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (html.Length <= initialCharacterCount)
                return (ParseHtmlToAozoraBlocks(html, currentPath, chapterIndex, token), false);

            int characterCount = Math.Max(4096, initialCharacterCount);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int end = Math.Min(html.Length, characterCount);
                if (end < html.Length)
                {
                    int tagEnd = html.IndexOf('>', end);
                    if (tagEnd >= 0) end = tagEnd + 1;
                }

                var result = ParseHtmlToAozoraBlocks(html.Substring(0, end), currentPath, chapterIndex, token);
                bool hasTarget = targetBlockIndex >= 0
                    ? result.Blocks.Count > targetBlockIndex + 32
                    : targetLine <= 1 || result.TotalLineCount > targetLine + 32;

                if (hasTarget || end >= html.Length)
                    return (result, end < html.Length);

                characterCount = Math.Min(html.Length, characterCount * 2);
            }
        }

        public static string ResolveRelativePath(string baseXhtmlPath, string relativePath)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath)) return string.Empty;

                relativePath = Uri.UnescapeDataString(relativePath);

                if (relativePath.StartsWith("/"))
                {
                    return relativePath.TrimStart('/');
                }

                string baseDir = Path.GetDirectoryName(baseXhtmlPath)?.Replace("\\", "/") ?? string.Empty;
                string combined = string.IsNullOrEmpty(baseDir)
                    ? relativePath
                    : baseDir + "/" + relativePath;

                var parts = combined.Replace("\\", "/").Split('/');
                var stack = new Stack<string>();

                foreach (var part in parts)
                {
                    if (part == "." || string.IsNullOrEmpty(part)) continue;
                    if (part == "..")
                    {
                        if (stack.Count > 0) stack.Pop();
                    }
                    else
                    {
                        stack.Push(part);
                    }
                }

                return string.Join("/", stack.Reverse());
            }
            catch
            {
                return relativePath;
            }
        }

        private async Task<EpubPackageInfo> ParseOpfAsync(
            ZipArchive archive,
            SemaphoreSlim archiveLock,
            string opfPath,
            CancellationToken token)
        {
            string? content = await ReadEntryTextAsync(archive, opfPath, archiveLock, token);
            if (string.IsNullOrEmpty(content))
            {
                return new EpubPackageInfo(opfPath, new List<string>(), null);
            }

            var manifest = new Dictionary<string, string>();
            string opfDir = Path.GetDirectoryName(opfPath)?.Replace("\\", "/") ?? string.Empty;
            string? tocPath = null;

            var itemMatches = RxItem.Matches(content);
            foreach (Match m in itemMatches)
            {
                token.ThrowIfCancellationRequested();
                string tagContent = m.Value;
                var idMatch = RxId.Match(tagContent);
                var hrefMatch = RxHref.Match(tagContent);

                if (idMatch.Success && hrefMatch.Success)
                {
                    string id = idMatch.Groups[1].Value;
                    string href = hrefMatch.Groups[1].Value;
                    manifest[id] = href;

                    if (RxNavProp.IsMatch(tagContent))
                    {
                        tocPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    }
                }
            }

            var spine = new List<string>();
            var itemRefMatches = RxItemRef.Matches(content);
            foreach (Match m in itemRefMatches)
            {
                token.ThrowIfCancellationRequested();
                string id = m.Groups[1].Value;
                if (manifest.TryGetValue(id, out string? href))
                {
                    string fullPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    spine.Add(fullPath);
                }
            }

            if (string.IsNullOrEmpty(tocPath))
            {
                var spineMatch = RxSpineToc.Match(content);
                if (spineMatch.Success)
                {
                    string tocId = spineMatch.Groups[1].Value;
                    if (manifest.TryGetValue(tocId, out string? href))
                    {
                        tocPath = string.IsNullOrEmpty(opfDir) ? href : opfDir + "/" + href;
                    }
                }
            }

            return new EpubPackageInfo(opfPath, spine, tocPath);
        }

        private static List<AozoraBindingModel> ParseHtmlToAozoraTextBlocks(string html, ref int lineNum, int chapterIndex)
        {
            var blocks = new List<AozoraBindingModel>();

            html = RxRuby.Replace(html, m =>
            {
                string rubyContent = m.Groups[1].Value;
                rubyContent = RxRp.Replace(rubyContent, string.Empty);

                var sb = new StringBuilder();
                var rtMatches = RxRt.Matches(rubyContent);

                int lastIndex = 0;
                foreach (Match rtMatch in rtMatches)
                {
                    string basePart = rubyContent.Substring(lastIndex, rtMatch.Index - lastIndex);
                    string rtPart = rtMatch.Groups[1].Value;

                    string baseText = RxAnyTag.Replace(basePart, string.Empty).Trim();
                    string rtText = RxAnyTag.Replace(rtPart, string.Empty).Trim();

                    if (!string.IsNullOrEmpty(baseText) || !string.IsNullOrEmpty(rtText))
                    {
                        sb.Append($"{{{{RUBY|{baseText}|~|{rtText}}}}}");
                    }

                    lastIndex = rtMatch.Index + rtMatch.Length;
                }

                if (lastIndex < rubyContent.Length)
                {
                    string tailText = RxAnyTag.Replace(rubyContent.Substring(lastIndex), string.Empty).Trim();
                    if (!string.IsNullOrEmpty(tailText)) sb.Append(tailText);
                }

                return sb.ToString();
            });

            html = RxHeading.Replace(html, m =>
            {
                string level = m.Groups[1].Value.Substring(1);
                string content = m.Groups[2].Value;
                return $"\n\n@@HEADING_{level}@@{content}@@HEADING_END@@\n\n";
            });

            html = Regex.Replace(html, @"</?(?:p|div|li|blockquote|tr|table|ul|ol)[^>]*>", "\n\n", RegexOptions.IgnoreCase);
            html = RxAnyTag.Replace(html, string.Empty);
            html = WebUtility.HtmlDecode(html);

            html = html.Replace("\r\n", "\n").Replace("\r", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");

            var lines = html.Split('\n');
            bool isNewParagraph = true;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    isNewParagraph = true;
                    continue;
                }

                string cleanLine = line.Replace('\u00A0', ' ').TrimEnd('\r', '\n', ' ');
                if (isNewParagraph) cleanLine = cleanLine.TrimStart();
                if (string.IsNullOrWhiteSpace(cleanLine)) continue;

                var headingMatch = Regex.Match(cleanLine, @"@@HEADING_(\d)@@(.*?)@@HEADING_END@@", RegexOptions.Singleline);
                if (headingMatch.Success)
                {
                    int level = int.Parse(headingMatch.Groups[1].Value);
                    string headingText = headingMatch.Groups[2].Value;
                    headingText = RxAnyTag.Replace(headingText, string.Empty);
                    headingText = WebUtility.HtmlDecode(headingText).Trim();

                    if (string.IsNullOrEmpty(headingText)) continue;

                    var headingBlock = new AozoraBindingModel
                    {
                        SourceLineNumber = lineNum++,
                        EpubChapterIndex = chapterIndex,
                        HeadingLevel = level,
                        IsBold = true,
                        Alignment = TextAlignment.Center
                    };

                    if (level == 1)
                    {
                        headingBlock.FontSizeScale = 1.7;
                        headingBlock.Margin = new Thickness(0, 0, 0, 40);
                    }
                    else if (level == 2)
                    {
                        headingBlock.FontSizeScale = 1.5;
                        headingBlock.Margin = new Thickness(0, 0, 0, 30);
                    }
                    else
                    {
                        headingBlock.FontSizeScale = 1.3;
                        headingBlock.Margin = new Thickness(0, 0, 0, 20);
                    }

                    headingBlock.Inlines.Add(headingText);
                    blocks.Add(headingBlock);
                    isNewParagraph = true;
                    continue;
                }

                var tokens = RxRubySplit.Split(cleanLine);

                var currentBlock = new AozoraBindingModel
                {
                    SourceLineNumber = lineNum++,
                    EpubChapterIndex = chapterIndex,
                    IsParagraphContinuation = !isNewParagraph
                };

                foreach (var token in tokens)
                {
                    if (token.StartsWith("{{RUBY|"))
                    {
                        var content = token.Substring(7, token.Length - 9);
                        var parts = content.Split(new[] { "|~|" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            currentBlock.Inlines.Add(new AozoraRuby { BaseText = parts[0], RubyText = parts[1] });
                        }
                    }
                    else if (!string.IsNullOrEmpty(token))
                    {
                        currentBlock.Inlines.Add(token);
                    }
                }

                if (currentBlock.Inlines.Count > 0)
                {
                    blocks.Add(currentBlock);
                }

                isNewParagraph = false;
            }

            return blocks;
        }
    }

    public sealed class EpubPackageInfo
    {
        public EpubPackageInfo(string rootPath, List<string> spine, string? tocPath)
        {
            RootPath = rootPath;
            Spine = spine;
            TocPath = tocPath;
        }

        public string RootPath { get; }
        public List<string> Spine { get; }
        public string? TocPath { get; }
    }

    public sealed class EpubHtmlParseResult
    {
        public EpubHtmlParseResult(List<AozoraBindingModel> blocks, int totalLineCount)
        {
            Blocks = blocks;
            TotalLineCount = totalLineCount;
        }

        public List<AozoraBindingModel> Blocks { get; }
        public int TotalLineCount { get; }
    }
}
