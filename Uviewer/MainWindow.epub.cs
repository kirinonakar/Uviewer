using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Windows.Storage;
using Windows.Storage.Streams;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Uviewer
{
    public class PageInfo
    {
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public double DocumentHeight { get; set; }
        public double ViewportHeight { get; set; }
    }

    [JsonSerializable(typeof(PageInfo))]
    public partial class EpubPageInfoContext : JsonSerializerContext;
    public sealed partial class MainWindow : Window
    {
        // EPUB viewer state
        private bool _isEpubMode = false;
        private string? _currentEpubPath;
        private IArchive? _currentEpubArchive;
        private EpubMetadata? _currentEpubMetadata;
        private List<EpubChapter> _epubChapters = new();
        private int _currentEpubChapterIndex = 0;
        private string? _currentEpubContent;
        private readonly SemaphoreSlim _epubLock = new(1, 1);

        // EPUB data structures
        public class EpubMetadata
        {
            public string Title { get; set; } = "";
            public string Author { get; set; } = "";
            public string Language { get; set; } = "";
            public string Identifier { get; set; } = "";
            public string Publisher { get; set; } = "";
            public string Description { get; set; } = "";
            public DateTime? Published { get; set; }
            public List<string> Subjects { get; set; } = new();
        }

        public class EpubChapter
        {
            public string Title { get; set; } = "";
            public string Href { get; set; } = "";
            public string Id { get; set; } = "";
            public int Order { get; set; }
            public string Content { get; set; } = "";
        }

        #region EPUB File Operations

        private async Task LoadEpubFromFileAsync(StorageFile file)
        {
            try
            {
                ShowTextMode();

                // Close any existing EPUB
                CloseCurrentEpub();

                // Set EPUB mode flag
                _isEpubMode = true;
                _currentTextFilePath = file.Path;

                // Open EPUB as archive
                using var archive = ArchiveFactory.Open(file.Path);
                var epubChapters = new List<EpubChapter>();
                var allContent = new StringBuilder();

                // Find container.xml
                var containerEntry = archive.Entries.FirstOrDefault(e => e.Key?.EndsWith("container.xml", StringComparison.OrdinalIgnoreCase) == true);
                if (containerEntry == null)
                {
                    FileNameText.Text = "EPUB 파일 형식이 올바르지 않습니다 (container.xml 없음)";
                    return;
                }

                // Parse container.xml to find OPF file
                string containerXml;
                using (var containerStream = containerEntry.OpenEntryStream())
                using (var reader = new StreamReader(containerStream))
                {
                    containerXml = await reader.ReadToEndAsync();
                }

                var containerDoc = XDocument.Parse(containerXml);
                var opfPath = containerDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;

                if (string.IsNullOrEmpty(opfPath))
                {
                    FileNameText.Text = "OPF 파일 경로를 찾을 수 없습니다";
                    return;
                }

                // Find and parse OPF file
                var opfEntry = archive.Entries.FirstOrDefault(e => e.Key?.EndsWith(opfPath, StringComparison.OrdinalIgnoreCase) == true);
                if (opfEntry == null)
                {
                    FileNameText.Text = "OPF 파일을 찾을 수 없습니다";
                    return;
                }

                string opfXml;
                using (var opfStream = opfEntry.OpenEntryStream())
                using (var reader = new StreamReader(opfStream))
                {
                    opfXml = await reader.ReadToEndAsync();
                }

                var opfDoc = XDocument.Parse(opfXml);
                var ns = opfDoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Extract metadata
                var metadata = opfDoc.Descendants(ns + "metadata").FirstOrDefault();
                if (metadata != null)
                {
                    _currentEpubMetadata = new EpubMetadata
                    {
                        Title = metadata.Descendants(ns + "title").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(file.Name),                     
                    };
                }
                else
                {
                    _currentEpubMetadata = new EpubMetadata
                    {
                        Title = Path.GetFileNameWithoutExtension(file.Name),
                    };
                }

                // Extract all chapters from spine in order
                var spine = opfDoc.Descendants(ns + "spine").FirstOrDefault();
                if (spine != null)
                {
                    var manifest = opfDoc.Descendants(ns + "manifest").FirstOrDefault();
                    if (manifest != null)
                    {
                        var itemRefs = spine.Descendants(ns + "itemref");
                        foreach (var itemRef in itemRefs)
                        {
                            var idref = itemRef.Attribute("idref")?.Value;
                            if (!string.IsNullOrEmpty(idref))
                            {
                                var item = manifest.Descendants(ns + "item")
                                    .FirstOrDefault(i => i.Attribute("id")?.Value == idref);
                                
                                if (item != null)
                                {
                                    var href = item.Attribute("href")?.Value;
                                    if (!string.IsNullOrEmpty(href))
                                    {
                                        var chapterEntry = archive.Entries.FirstOrDefault(e => 
                                            e.Key?.EndsWith(href, StringComparison.OrdinalIgnoreCase) == true);
                                        
                                        if (chapterEntry != null)
                                        {
                                            string chapterContent;
                                            using (var chapterStream = chapterEntry.OpenEntryStream())
                                            using (var reader = new StreamReader(chapterStream))
                                            {
                                                chapterContent = await reader.ReadToEndAsync();
                                            }

                                            // Process and add to combined content
                                            var processedContent = ProcessEpubContent(chapterContent);
                                            
                                            // Skip if processedContent is empty or null
                                            if (string.IsNullOrWhiteSpace(processedContent) || processedContent.Length < 50)
                                            {
                                                continue;
                                            }
                                            
                                            // Only add content if it has actual text (not just whitespace or empty tags)
                                            var textOnly = System.Text.RegularExpressions.Regex.Replace(processedContent, @"<[^>]+>", "");
                                            textOnly = textOnly.Trim();
                                            
                                            if (!string.IsNullOrWhiteSpace(textOnly) && textOnly.Length > 20)
                                            {
                                                allContent.Append(processedContent);
                                                allContent.AppendLine("<hr style='margin: 2em 0; border: none; border-top: 2px solid #ddd;'>");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Set combined content
                _currentEpubContent = allContent.ToString();
                _epubChapters = epubChapters; // Keep chapter list for navigation
                _currentEpubChapterIndex = 0;

                // Update UI
                FileNameText.Text = _currentEpubMetadata.Title;
                UpdateStatusBarForEpub();

                // Display combined content
                await UpdateEpubViewer();

                // Add to recent items
                _ = AddToRecentAsync();
            }
            catch (Exception ex)
            {
                FileNameText.Text = $"EPUB 파일 로드 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error loading EPUB: {ex.Message}");
            }
        }

        private async Task ParseEpubStructureAsync()
        {
            if (_currentEpubArchive == null) return;

            // Find and parse META-INF/container.xml
            var containerEntry = _currentEpubArchive.Entries
                .FirstOrDefault(e => e.Key?.Equals("META-INF/container.xml", StringComparison.OrdinalIgnoreCase) == true);

            if (containerEntry == null)
            {
                throw new Exception("EPUB container.xml not found");
            }

            string containerXml;
            using (var stream = containerEntry.OpenEntryStream())
            using (var reader = new StreamReader(stream))
            {
                containerXml = await reader.ReadToEndAsync();
            }

            var containerDoc = XDocument.Parse(containerXml);
            var rootfilePath = containerDoc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "rootfile")?
                .Attribute("full-path")?.Value;

            if (string.IsNullOrEmpty(rootfilePath))
            {
                throw new Exception("EPUB rootfile path not found");
            }

            // Parse the OPF file
            var opfEntry = _currentEpubArchive.Entries
                .FirstOrDefault(e => e.Key?.Equals(rootfilePath, StringComparison.OrdinalIgnoreCase) == true);

            if (opfEntry == null)
            {
                throw new Exception("EPUB OPF file not found");
            }

            string opfXml;
            using (var stream = opfEntry.OpenEntryStream())
            using (var reader = new StreamReader(stream))
            {
                opfXml = await reader.ReadToEndAsync();
            }

            await ParseOpfAsync(opfXml, Path.GetDirectoryName(rootfilePath));
        }

        private async Task ParseOpfAsync(string opfXml, string? opfDir)
        {
            var opfDoc = XDocument.Parse(opfXml);
            var ns = opfDoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Parse metadata
            _currentEpubMetadata = new EpubMetadata();
            var metadata = opfDoc.Descendants(ns + "metadata").FirstOrDefault();
            if (metadata != null)
            {
                _currentEpubMetadata.Title = metadata.Descendants(ns + "title").FirstOrDefault()?.Value ?? "";
                _currentEpubMetadata.Author = metadata.Descendants(ns + "creator").FirstOrDefault()?.Value ?? "";
                _currentEpubMetadata.Language = metadata.Descendants(ns + "language").FirstOrDefault()?.Value ?? "";
                _currentEpubMetadata.Identifier = metadata.Descendants(ns + "identifier").FirstOrDefault()?.Value ?? "";
                _currentEpubMetadata.Publisher = metadata.Descendants(ns + "publisher").FirstOrDefault()?.Value ?? "";
                _currentEpubMetadata.Description = metadata.Descendants(ns + "description").FirstOrDefault()?.Value ?? "";

                var subjects = metadata.Descendants(ns + "subject");
                _currentEpubMetadata.Subjects = subjects.Select(s => s.Value ?? "").ToList();

                var dateStr = metadata.Descendants(ns + "date").FirstOrDefault()?.Value;
                if (DateTime.TryParse(dateStr, out var date))
                {
                    _currentEpubMetadata.Published = date;
                }
            }

            // Parse manifest
            var manifest = opfDoc.Descendants(ns + "manifest").FirstOrDefault();
            var manifestItems = manifest?.Descendants(ns + "item").ToList() ?? new List<XElement>();

            // Parse spine
            var spine = opfDoc.Descendants(ns + "spine").FirstOrDefault();
            var spineItems = spine?.Descendants(ns + "itemref").ToList() ?? new List<XElement>();

            // Build chapters list
            _epubChapters.Clear();
            int order = 0;

            foreach (var spineItem in spineItems)
            {
                var idref = spineItem.Attribute("idref")?.Value;
                if (string.IsNullOrEmpty(idref)) continue;

                var manifestItem = manifestItems.FirstOrDefault(m => m.Attribute("id")?.Value == idref);
                if (manifestItem == null) continue;

                var href = manifestItem.Attribute("href")?.Value;
                if (string.IsNullOrEmpty(href)) continue;

                var mediaType = manifestItem.Attribute("media-type")?.Value;
                if (mediaType != "application/xhtml+xml" && mediaType != "text/html") continue;

                // Build full path
                var fullPath = string.IsNullOrEmpty(opfDir) ? href : Path.Combine(opfDir, href).Replace('\\', '/');

                // Get the chapter content
                var chapterEntry = _currentEpubArchive?.Entries
                    .FirstOrDefault(e => e.Key?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true);

                if (chapterEntry != null)
                {
                    string content;
                    using (var stream = chapterEntry.OpenEntryStream())
                    using (var reader = new StreamReader(stream))
                    {
                        content = await reader.ReadToEndAsync();
                    }

                    var chapter = new EpubChapter
                    {
                        Id = idref,
                        Href = href,
                        Title = ExtractChapterTitle(content) ?? $"Chapter {order + 1}",
                        Order = order++,
                        Content = content
                    };

                    _epubChapters.Add(chapter);
                }
            }
        }

        private string? ExtractChapterTitle(string htmlContent)
        {
            try
            {
                var doc = XDocument.Parse(htmlContent);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Try to find title in various ways
                var title = doc.Descendants(ns + "title").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(title)) return title;

                // Look for h1, h2, h3 tags
                var headings = doc.Descendants()
                    .Where(e => e.Name.LocalName == "h1" || e.Name.LocalName == "h2" || e.Name.LocalName == "h3")
                    .FirstOrDefault();

                if (headings != null)
                {
                    return headings.Value?.Trim();
                }
            }
            catch
            {
                // If parsing fails, try regex fallback
                var titleMatch = System.Text.RegularExpressions.Regex.Match(htmlContent, @"<title[^>]*>(.*?)</title>", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (titleMatch.Success)
                {
                    return System.Web.HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }
            }

            return null;
        }

        private async Task LoadEpubChapterAsync(int chapterIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= _epubChapters.Count) return;

            try
            {
                _currentEpubChapterIndex = chapterIndex;
                var chapter = _epubChapters[chapterIndex];
                
                // Process HTML content for better display
                var processedContent = ProcessEpubContent(chapter.Content);
                _currentEpubContent = processedContent;

                await InitializeTextViewerAsync();
                await UpdateEpubViewer();
                UpdateStatusBarForEpub();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading EPUB chapter: {ex.Message}");
            }
        }

        private string ProcessEpubContent(string htmlContent)
        {
            try
            {
                // Quick check: if content has no meaningful text, return empty string
                var quickTextCheck = System.Text.RegularExpressions.Regex.Replace(htmlContent, @"<[^>]+>", "");
                quickTextCheck = quickTextCheck.Trim();
                if (string.IsNullOrWhiteSpace(quickTextCheck) || quickTextCheck.Length < 10)
                {
                    return "";
                }
                
                var doc = XDocument.Parse(htmlContent);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Remove all images and their containers to eliminate empty spaces
                var images = doc.Descendants().Where(e => e.Name.LocalName == "img").ToList();
                foreach (var img in images)
                {
                    img.Remove();
                }

                // Remove image containers and figure elements that might contain images
                var figures = doc.Descendants().Where(e => e.Name.LocalName == "figure" || e.Name.LocalName == "picture").ToList();
                foreach (var fig in figures)
                {
                    fig.Remove();
                }
                
                // Remove svg elements (often used for images)
                var svgs = doc.Descendants().Where(e => e.Name.LocalName == "svg").ToList();
                foreach (var svg in svgs)
                {
                    svg.Remove();
                }

                // Remove empty divs, spans, and other container elements
                var emptyContainers = doc.Descendants().Where(e => 
                    (e.Name.LocalName == "div" || e.Name.LocalName == "span" || e.Name.LocalName == "section" || e.Name.LocalName == "article") &&
                    string.IsNullOrWhiteSpace(e.Value?.Trim()) && !e.HasElements).ToList();
                foreach (var container in emptyContainers)
                {
                    container.Remove();
                }

                // Remove excessive whitespace (2+ consecutive newlines and empty paragraphs)
                var textNodes = doc.Descendants().Where(e => e.NodeType == System.Xml.XmlNodeType.Text).ToList();
                foreach (var textNode in textNodes)
                {
                    var value = textNode.Value ?? "";
                    // Replace multiple consecutive spaces with single space
                    var newValue = System.Text.RegularExpressions.Regex.Replace(value, @"[ \t]+", " ");
                    // Replace 2+ consecutive newlines with single newline
                    newValue = System.Text.RegularExpressions.Regex.Replace(newValue, @"\n{2,}", "\n");
                    // Remove leading/trailing whitespace from each line
                    newValue = System.Text.RegularExpressions.Regex.Replace(newValue, @"^[ \t]+|[ \t]+$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                    // Remove empty lines
                    newValue = System.Text.RegularExpressions.Regex.Replace(newValue, @"^\s*\n", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                    textNode.Value = newValue;
                }

                // Remove empty paragraphs and paragraphs with only whitespace
                var paragraphs = doc.Descendants(ns + "p").ToList();
                foreach (var p in paragraphs)
                {
                    var text = p.Value?.Trim();
                    if (string.IsNullOrEmpty(text) || text.Length < 2 || System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*$"))
                    {
                        p.Remove();
                    }
                }

                // Remove empty list items
                var listItems = doc.Descendants().Where(e => e.Name.LocalName == "li").ToList();
                foreach (var li in listItems)
                {
                    var text = li.Value?.Trim();
                    if (string.IsNullOrEmpty(text) || text.Length < 2)
                    {
                        li.Remove();
                    }
                }

                // Remove consecutive empty lines from the final HTML
                var finalHtml = doc.ToString();
                // Remove multiple consecutive <br> tags
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"(<br\s*/?>\s*){2,}", "<br/>", RegexOptions.IgnoreCase);
                // Remove empty paragraphs with nbsp
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"<p[^>]*>\s*(&nbsp;|\s)*\s*</p>", "", RegexOptions.IgnoreCase);
                // Remove multiple consecutive empty paragraphs
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"(<p[^>]*>\s*</p>\s*){2,}", "", RegexOptions.IgnoreCase);
                // Remove empty divs and spans
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"<div[^>]*>\s*</div>", "", RegexOptions.IgnoreCase);
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"<span[^>]*>\s*</span>", "", RegexOptions.IgnoreCase);
                // Remove excessive whitespace between tags
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @">\s{2,}<", "> <", RegexOptions.Multiline);
                // Normalize line breaks
                finalHtml = System.Text.RegularExpressions.Regex.Replace(finalHtml, @"\n{3,}", "\n\n");

                // Remove head section with custom styles that might interfere
                var head = doc.Descendants(ns + "head").FirstOrDefault();
                if (head != null)
                {
                    // Keep title but remove styles and scripts
                    var title = head.Descendants(ns + "title").FirstOrDefault();
                    head.RemoveAll();
                    if (title != null)
                    {
                        head.Add(title);
                    }
                }

                // Add our own CSS for better reading experience - text only, no images
                var style = new XElement(ns + "style");
                style.Value = $@"
                    body {{
                        font-family: {_currentFontFamily};
                        line-height: 1.6;
                        max-width: 80ch;
                        margin: 0 auto;
                        padding: 20px;
                        color: #333;
                        overflow-wrap: break-word;
                        word-wrap: break-word;
                        background-color: {_textBgColor};
                    }}
                    
                    /* Hide all empty elements and image containers */
                    img, figure, picture {{
                        display: none !important;
                    }}
                    
                    /* Hide empty paragraphs and containers */
                    p:empty, div:empty, span:empty, section:empty, article:empty {{
                        display: none !important;
                    }}
                    
                    /* Minimize spacing between elements */
                    * {{
                        margin-block-start: 0;
                        margin-block-end: 0;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    /* Text styling */
                    h1, h2, h3, h4, h5, h6 {{
                        color: #2c3e50;
                        margin-top: 1.2em;
                        margin-bottom: 0.5em;
                        line-height: 1.3;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    h1 {{
                        font-size: 1.8em;
                    }}
                    
                    h2 {{
                        font-size: 1.5em;
                    }}
                    
                    h3 {{
                        font-size: 1.3em;
                    }}
                    
                    p {{
                        margin-top: 0.3em;
                        margin-bottom: 0.3em;
                        text-align: left;
                        text-indent: 1.5em;
                        line-height: 1.6;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                        white-space: pre-wrap;
                    }}
                    
                    /* First paragraph after heading should not be indented */
                    h1 + p, h2 + p, h3 + p, h4 + p, h5 + p, h6 + p {{
                        text-indent: 0;
                        margin-top: 0;
                    }}
                    
                    /* Chapter separator styling */
                    hr {{
                        margin: 2em 0;
                        border: none;
                        border-top: 2px solid #ddd;
                    }}
                    
                    table {{
                        border-collapse: collapse;
                        width: 100%;
                        max-width: 90ch;
                        margin: 1em 0;
                        table-layout: auto;
                    }}
                    
                    th, td {{
                        border: 1px solid #ddd;
                        padding: 8px;
                        text-align: left;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    th {{
                        background-color: #f2f2f2;
                    }}
                    
                    blockquote {{
                        border-left: 4px solid #ddd;
                        margin: 0.8em 0;
                        padding-left: 1em;
                        font-style: italic;
                    }}
                    
                    code {{
                        background-color: #f4f4f4;
                        padding: 2px 4px;
                        border-radius: 3px;
                        font-family: monospace;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    pre {{
                        background-color: #f4f4f4;
                        padding: 10px;
                        border-radius: 5px;
                        overflow-x: auto;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                        white-space: pre-wrap;
                    }}
                    
                    ul, ol {{
                        margin: 0.5em 0;
                        padding-left: 2em;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    li {{
                        margin: 0.2em 0;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                    
                    /* Remove excessive line breaks */
                    br + br {{
                        display: none;
                    }}
                    
                    /* Compact spacing for better reading */
                    div, section, article {{
                        margin: 0;
                        padding: 0;
                        word-wrap: break-word;
                        overflow-wrap: break-word;
                    }}
                ";

                if (head != null)
                {
                    head.Add(style);
                }
                else
                {
                    // Add head if it doesn't exist
                    var newHead = new XElement(ns + "head");
                    newHead.Add(style);
                    doc.Root?.AddFirst(newHead);
                }

                var result = doc.ToString();
                
                // Final check: verify the result has actual text content
                var finalTextCheck = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", "");
                finalTextCheck = finalTextCheck.Trim();
                if (string.IsNullOrWhiteSpace(finalTextCheck) || finalTextCheck.Length < 10)
                {
                    return "";
                }
                
                return result;
            }
            catch
            {
                // If XML parsing fails, return basic text-only styling
                return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: {_currentFontFamily};
            line-height: 1.8;
            max-width: 100%;
            margin: 0;
            padding: 20px;
            color: #333;
            white-space: pre-wrap;
            overflow-wrap: break-word;
            background-color: {_textBgColor};
        }}
        
        p {{
            margin-bottom: 1em;
            text-align: left;
            text-indent: 1.5em;
        }}
    </style>
</head>
<body>
    {htmlContent}
</body>
</html>";
            }
        }

        private void UpdateStatusBarForEpub()
        {
            if (_currentEpubMetadata != null)
            {
                FileNameText.Text = $"{_currentEpubMetadata.Title} - {_currentEpubMetadata.Author}";
                ImageInfoText.Text = $"EPUB • {_currentEpubMetadata.Language.ToUpper()}";
                // Page info will be updated by the timer
            }
        }

        private async Task UpdateEpubViewer()
        {
            if (string.IsNullOrEmpty(_currentEpubContent) || TextViewer.CoreWebView2 == null) return;

            try
            {
                // Stop existing timer
                _pageInfoTimer?.Stop();
                
                // For large content, write to temp file
                if (_currentEpubContent.Length > MaxNavigateToStringLength)
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), $"epub_chapter_{Guid.NewGuid()}.html");
                    await File.WriteAllTextAsync(tempFile, _currentEpubContent, Encoding.UTF8);
                    
                    TextViewer.CoreWebView2.Navigate(new Uri(tempFile).AbsoluteUri);
                    
                    // Clean up old temp file
                    if (!string.IsNullOrEmpty(_currentTextTempHtmlPath) && File.Exists(_currentTextTempHtmlPath))
                    {
                        try
                        {
                            File.Delete(_currentTextTempHtmlPath);
                        }
                        catch { }
                    }
                    _currentTextTempHtmlPath = tempFile;
                }
                else
                {
                    TextViewer.CoreWebView2.NavigateToString(_currentEpubContent);
                }
                
                // Start timer for continuous page info updates
                _pageInfoTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                _pageInfoTimer.Interval = TimeSpan.FromMilliseconds(200);
                _pageInfoTimer.IsRepeating = true;
                _pageInfoTimer.Tick += (s, e) =>
                {
                    if (_isEpubMode && TextViewer.CoreWebView2 != null)
                    {
                        _ = UpdateEpubPageInfo();
                    }
                };
                _pageInfoTimer.Start();
                
                // Update page info after content loads
                await Task.Delay(1000);
                await UpdateEpubPageInfo();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating EPUB viewer: {ex.Message}");
            }
        }

        private async Task UpdateEpubPageInfo()
        {
            try
            {
                if (TextViewer.CoreWebView2 == null) return;

                // Get vertical scroll info (EPUB always uses vertical scrolling)
                string scrollTopScript = "window.scrollY || window.pageYOffset || document.documentElement.scrollTop";
                string totalHeightScript = "document.documentElement.scrollHeight";
                string viewportHeightScript = "window.innerHeight";
                
                var scrollTopResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(scrollTopScript);
                var totalHeightResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(totalHeightScript);
                var viewportHeightResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(viewportHeightScript);
                
                if (double.TryParse(scrollTopResult, out double scrollTop) &&
                    double.TryParse(totalHeightResult, out double totalHeight) &&
                    double.TryParse(viewportHeightResult, out double viewportHeight) &&
                    viewportHeight > 0)
                {
                    int totalPages = Math.Max(1, (int)Math.Ceiling(totalHeight / viewportHeight));
                    int currentPage = Math.Max(1, (int)Math.Floor(scrollTop / viewportHeight) + 1);
                    
                    // Update UI on the dispatcher thread
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        ImageIndexText.Text = $"Page: {currentPage} / {totalPages}";
                    });
                    
                    System.Diagnostics.Debug.WriteLine($"EPUB Page Info: {currentPage}/{totalPages} (ScrollTop: {scrollTop}, TotalHeight: {totalHeight}, ViewportHeight: {viewportHeight})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating EPUB page info: {ex.Message}");
            }
        }

        private void CloseCurrentEpub()
        {
            if (_currentEpubArchive != null)
            {
                _currentEpubArchive.Dispose();
                _currentEpubArchive = null;
            }

            _currentEpubPath = null;
            _currentEpubMetadata = null;
            _epubChapters.Clear();
            _currentEpubChapterIndex = 0;
            _currentEpubContent = null;

            // Clean up temp files
            if (!string.IsNullOrEmpty(_currentTextTempHtmlPath) && File.Exists(_currentTextTempHtmlPath))
            {
                try
                {
                    File.Delete(_currentTextTempHtmlPath);
                }
                catch { }
                _currentTextTempHtmlPath = null;
            }
        }

        #endregion

        #region EPUB UI

        private void ShowEpubUI()
        {
            _isEpubMode = true;
            _isTextMode = true; // EPUB uses the text viewer infrastructure

            EmptyStatePanel.Visibility = Visibility.Collapsed;
            MainCanvas.Visibility = Visibility.Collapsed;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            TextViewerArea.Visibility = Visibility.Visible;

            TextOptionsButton.Visibility = Visibility.Visible;
            TextSeparator.Visibility = Visibility.Visible;

            // Ensure all buttons remain visible in EPUB mode
            SharpenButton.Visibility = Visibility.Visible;
            SideBySideButton.Visibility = Visibility.Visible;
            NextImageSideButton.Visibility = Visibility.Visible;
            ZoomOutButton.Visibility = Visibility.Visible;
            ZoomInButton.Visibility = Visibility.Visible;
            ZoomFitButton.Visibility = Visibility.Visible;
            ZoomActualButton.Visibility = Visibility.Visible;
            ZoomLevelText.Visibility = Visibility.Visible;

            // Update title
            var title = string.IsNullOrEmpty(_currentEpubMetadata?.Title) 
                ? Path.GetFileName(_currentEpubPath ?? "")
                : _currentEpubMetadata.Title;
            Title = $"Uviewer - {title}";
        }

        private void HideEpubUI()
        {
            _isEpubMode = false;
            _isTextMode = false;
            TextViewerArea.Visibility = Visibility.Collapsed;
            TextOptionsButton.Visibility = Visibility.Visible;
            TextSeparator.Visibility = Visibility.Visible;

            // Restore image specific buttons
            SharpenButton.Visibility = Visibility.Visible;
            SideBySideButton.Visibility = Visibility.Visible;
            NextImageSideButton.Visibility = Visibility.Visible;
            ZoomOutButton.Visibility = Visibility.Visible;
            ZoomInButton.Visibility = Visibility.Visible;
            ZoomFitButton.Visibility = Visibility.Visible;
            ZoomActualButton.Visibility = Visibility.Visible;
            ZoomLevelText.Visibility = Visibility.Visible;

            // Stop page info timer
            _pageInfoTimer?.Stop();
        }

        #endregion

        #region EPUB Navigation

        private async Task NavigateToNextEpubChapterAsync()
        {
            if (_currentEpubChapterIndex < _epubChapters.Count - 1)
            {
                await LoadEpubChapterAsync(_currentEpubChapterIndex + 1);
            }
        }

        private async Task NavigateToPreviousEpubChapterAsync()
        {
            if (_currentEpubChapterIndex > 0)
            {
                await LoadEpubChapterAsync(_currentEpubChapterIndex - 1);
            }
        }

        private async Task NavigateToPreviousEpubPageAsync()
        {
            if (TextViewer.CoreWebView2 == null) return;

            try
            {
                await TextViewer.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    const viewportHeight = window.innerHeight;
    const scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    const documentHeight = document.documentElement.scrollHeight;
    
    // Calculate page height (viewport height with some overlap for readability)
    const pageHeight = Math.floor(viewportHeight * 0.9);
    
    // Calculate new scroll position (move one page up)
    const newScrollTop = Math.max(scrollTop - pageHeight, 0);
    
    // Scroll to the calculated position
    window.scrollTo({
        top: newScrollTop,
        behavior: 'smooth'
    });
    
    return {
        currentScroll: scrollTop,
        newScroll: newScrollTop,
        pageHeight: pageHeight,
        viewportHeight: viewportHeight,
        documentHeight: documentHeight
    };
})()");
                
                // Update page info after navigation
                await Task.Delay(500);
                await UpdateEpubPageInfo();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to previous EPUB page: {ex.Message}");
            }
        }

        private async Task NavigateToNextEpubPageAsync()
        {
            if (TextViewer.CoreWebView2 == null) return;

            try
            {
                await TextViewer.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    const viewportHeight = window.innerHeight;
    const scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    const documentHeight = document.documentElement.scrollHeight;
    const maxScroll = documentHeight - viewportHeight;
    
    // Calculate page height (viewport height with some overlap for readability)
    const pageHeight = Math.floor(viewportHeight * 0.9);
    
    // Calculate new scroll position (move one page down)
    const newScrollTop = Math.min(scrollTop + pageHeight, maxScroll);
    
    // Scroll to the calculated position
    window.scrollTo({
        top: newScrollTop,
        behavior: 'smooth'
    });
    
    return {
        currentScroll: scrollTop,
        newScroll: newScrollTop,
        pageHeight: pageHeight,
        viewportHeight: viewportHeight,
        documentHeight: documentHeight,
        maxScroll: maxScroll
    };
})()");
                
                // Update page info after navigation
                await Task.Delay(500);
                await UpdateEpubPageInfo();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to next EPUB page: {ex.Message}");
            }
        }

        #endregion

        #region EPUB Integration

        private async Task HandleEpubKeyPressAsync(Windows.System.VirtualKey key, bool ctrlPressed)
        {
            if (!_isEpubMode) return;

            switch (key)
            {
                case Windows.System.VirtualKey.Right:
                    await NavigateToNextEpubChapterAsync();
                    break;

                case Windows.System.VirtualKey.Left:
                    await NavigateToPreviousEpubChapterAsync();
                    break;

                case Windows.System.VirtualKey.Home:
                    if (_epubChapters.Count > 0)
                    {
                        await LoadEpubChapterAsync(0);
                    }
                    break;

                case Windows.System.VirtualKey.End:
                    if (_epubChapters.Count > 0)
                    {
                        await LoadEpubChapterAsync(_epubChapters.Count - 1);
                    }
                    break;
            }
        }

        private async Task<double?> GetEpubScrollPositionAsync()
        {
            try
            {
                if (_isEpubMode && TextViewer.CoreWebView2 != null)
                {
                    string scrollTopScript = "window.scrollY || window.pageYOffset || document.documentElement.scrollTop";
                    var scrollTopResult = await TextViewer.CoreWebView2.ExecuteScriptAsync(scrollTopScript);
                    if (double.TryParse(scrollTopResult, out double scrollTop))
                    {
                        return scrollTop;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting EPUB scroll position: {ex.Message}");
            }
            return null;
        }

        private async Task SetEpubScrollPositionAsync(double position)
        {
            try
            {
                if (_isEpubMode && TextViewer.CoreWebView2 != null)
                {
                    var script = $"window.scrollTo({{ top: {position}, behavior: 'auto' }});";
                    await TextViewer.CoreWebView2.ExecuteScriptAsync(script);
                    System.Diagnostics.Debug.WriteLine($"Set EPUB scroll position to: {position}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting EPUB scroll position: {ex.Message}");
            }
        }

        #endregion
    }
}
