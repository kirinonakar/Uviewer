using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public interface ITocProvider
    {
        Task<List<TocItem>> GetTocAsync(CancellationToken token = default);
    }

    public class TocService
    {
        private ITocProvider? _provider;
        private List<TocItem> _currentToc = new();
        private CancellationTokenSource? _tocCts;

        public List<TocItem> CurrentToc => _currentToc;

        public void SetProvider(ITocProvider provider)
        {
            _provider = provider;
            _currentToc.Clear();
        }

        public async Task LoadTocAsync(CancellationToken externalToken = default)
        {
            if (_provider == null) return;

            _tocCts?.Cancel();
            _tocCts = new CancellationTokenSource();
            
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _tocCts.Token);
            var token = linkedCts.Token;

            try
            {
                var items = await _provider.GetTocAsync(token);
                if (!token.IsCancellationRequested)
                {
                    _currentToc = items;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TocService Load Error: {ex.Message}");
            }
        }

        public void Clear()
        {
            _tocCts?.Cancel();
            _provider = null;
            _currentToc.Clear();
        }
    }

    public class PdfTocProvider : ITocProvider
    {
        private readonly string _pdfPath;

        public PdfTocProvider(string pdfPath)
        {
            _pdfPath = pdfPath;
        }

        public Task<List<TocItem>> GetTocAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var toc = new List<TocItem>();
                // We use UglyToad directly here for faster background parsing
                try
                {
                    using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(_pdfPath);
                    if (pdfDocument.TryGetBookmarks(out var bookmarks))
                    {
                        ParseBookmarks(bookmarks.GetNodes().ToList(), 1, toc);
                    }
                }
                catch { }
                return toc;
            }, token);
        }

        private void ParseBookmarks(IReadOnlyList<UglyToad.PdfPig.Outline.BookmarkNode> nodes, int level, List<TocItem> targetList)
        {
            foreach (var node in nodes)
            {
                int pageIndex = -1;
                if (node is UglyToad.PdfPig.Outline.DocumentBookmarkNode docNode)
                {
                    pageIndex = docNode.PageNumber - 1;
                }

                targetList.Add(new TocItem
                {
                    HeadingText = node.Title,
                    HeadingLevel = level,
                    SourceLineNumber = pageIndex,
                    Tag = "PDF"
                });

                if (node.Children != null && node.Children.Count > 0)
                {
                    ParseBookmarks(node.Children, level + 1, targetList);
                }
            }
        }
    }

    public class EpubTocProvider : ITocProvider
    {
        private readonly ZipArchive _archive;
        private readonly SemaphoreSlim? _archiveLock;
        private readonly string _tocPath;
        private readonly List<string> _spine;

        private static readonly Regex RxEpubXmlns = new Regex("xmlns=\"[^\"]*\"", RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxNav = new Regex("<navPoint[^>]*>([\\s\\S]*?)</navPoint>", RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxText = new Regex("<text[^>]*>(.*?)</text>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEpubNcxContent = new Regex("<content[^>]*src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubNavAnchor = new Regex("<a[^>]*href=[\"']([^\"']+)[\"'][^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEpubAnyTag = new Regex("<[^>]+>", RegexOptions.Compiled);

        public EpubTocProvider(
            ZipArchive archive,
            string tocPath,
            List<string> spine,
            SemaphoreSlim? archiveLock = null)
        {
            _archive = archive;
            _tocPath = tocPath;
            _spine = spine;
            _archiveLock = archiveLock;
        }

        public async Task<List<TocItem>> GetTocAsync(CancellationToken token = default)
        {
            var toc = new List<TocItem>();
            bool lockTaken = false;
            try
            {
                if (_archiveLock != null)
                {
                    await _archiveLock.WaitAsync(token);
                    lockTaken = true;
                }

                var entry = _archive.GetEntry(_tocPath);
                if (entry != null)
                {
                    string content;
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        content = await reader.ReadToEndAsync(token);
                    }

                    string ext = Path.GetExtension(_tocPath).ToLower();
                    if (ext == ".ncx")
                    {
                        ParseNcxToc(content, toc, token);
                    }
                    else if (ext == ".html" || ext == ".xhtml" || ext == ".htm")
                    {
                        ParseNavToc(content, toc, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EpubTocProvider Error: {ex.Message}");
            }
            finally
            {
                if (lockTaken) _archiveLock!.Release();
            }

            if (toc.Count == 0 && _spine.Count > 0)
            {
                for (int i = 0; i < _spine.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    toc.Add(new TocItem
                    {
                        HeadingText = $"Chapter {i + 1}",
                        EpubLink = _spine[i],
                        HeadingLevel = 1
                    });
                }
            }

            return toc;
        }

        private void ParseNcxToc(string xml, List<TocItem> toc, CancellationToken token)
        {
            xml = RxEpubXmlns.Replace(xml, "");
            var matches = RxEpubNcxNav.Matches(xml);
            foreach (Match m in matches)
            {
                token.ThrowIfCancellationRequested();
                string inner = m.Groups[1].Value;
                string title = "";
                var tm = RxEpubNcxText.Match(inner);
                if (tm.Success) title = RxEpubAnyTag.Replace(tm.Groups[1].Value, "").Trim();

                string src = "";
                var cm = RxEpubNcxContent.Match(inner);
                if (cm.Success) src = cm.Groups[1].Value;

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(src))
                {
                    string fullSrc = ResolveRelativePath(_tocPath, src);
                    toc.Add(new TocItem { HeadingText = title, EpubLink = fullSrc, HeadingLevel = 1 });
                }
            }
        }

        private void ParseNavToc(string html, List<TocItem> toc, CancellationToken token)
        {
            var matches = RxEpubNavAnchor.Matches(html);
            foreach (Match m in matches)
            {
                token.ThrowIfCancellationRequested();
                string src = m.Groups[1].Value;
                string title = RxEpubAnyTag.Replace(m.Groups[2].Value, "").Trim();

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(src))
                {
                    string fullSrc = ResolveRelativePath(_tocPath, src);
                    toc.Add(new TocItem { HeadingText = title, EpubLink = fullSrc, HeadingLevel = 1 });
                }
            }
        }

        private string ResolveRelativePath(string baseXhtmlPath, string relativePath)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath)) return "";
                relativePath = Uri.UnescapeDataString(relativePath);
                if (relativePath.StartsWith("/")) return relativePath.TrimStart('/');

                string baseDir = Path.GetDirectoryName(baseXhtmlPath)?.Replace("\\", "/") ?? "";
                string combined = string.IsNullOrEmpty(baseDir) ? relativePath : baseDir + "/" + relativePath;
                var parts = combined.Replace("\\", "/").Split('/');
                var stack = new Stack<string>();

                foreach (var part in parts)
                {
                    if (part == "." || string.IsNullOrEmpty(part)) continue;
                    if (part == "..") { if (stack.Count > 0) stack.Pop(); }
                    else stack.Push(part);
                }

                return string.Join("/", stack.Reverse());
            }
            catch { return relativePath; }
        }
    }

    public class TextTocProvider : ITocProvider
    {
        private readonly string _content;
        private readonly List<AozoraBindingModel>? _blocks;

        public TextTocProvider(string content, List<AozoraBindingModel>? blocks = null)
        {
            _content = content;
            _blocks = blocks;
        }

        public async Task<List<TocItem>> GetTocAsync(CancellationToken token = default)
        {
            if (_blocks != null && _blocks.Count > 0)
            {
                return _blocks
                    .Where(b => b.HeadingLevel > 0)
                    .Select(b => new TocItem
                    {
                        HeadingText = b.HeadingText,
                        SourceLineNumber = b.SourceLineNumber,
                        HeadingLevel = b.HeadingLevel
                    })
                    .ToList();
            }

            return await Task.Run(() =>
            {
                var list = new List<TocItem>();
                var rawLines = _content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                for (int i = 0; i < rawLines.Length; i++)
                {
                    var line = rawLines[i].Trim();
                    int level = 0;
                    string text = "";

                    if (line.Contains("［＃大見出し］") || line.StartsWith("# ")) { level = 1; text = line.Replace("［＃大見出し］", "").TrimStart('#', ' '); }
                    else if (line.Contains("［＃中見出し］") || line.StartsWith("## ")) { level = 2; text = line.Replace("［＃中見出し］", "").TrimStart('#', ' '); }
                    else if (line.Contains("［＃小見出し］") || line.StartsWith("### ")) { level = 3; text = line.Replace("［＃小見出し］", "").TrimStart('#', ' '); }

                    if (level > 0)
                    {
                        text = Regex.Replace(text, @"［＃[^］]+］|\[.*?\]|[#]", "").Trim();
                        list.Add(new TocItem { HeadingText = text, SourceLineNumber = i + 1, HeadingLevel = level });
                    }
                }
                return list;
            }, token);
        }
    }
}
