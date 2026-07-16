using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public delegate List<AozoraBindingModel> EpubBlockPaginator(
        ref int index,
        List<AozoraBindingModel> blocks,
        float maxWidth,
        float pageHeight,
        CanvasDevice? device,
        CancellationToken token);

    public delegate int EpubPreviousPageStartFinder(
        int targetIndex,
        List<AozoraBindingModel> blocks,
        float maxWidth,
        float pageHeight,
        ICanvasResourceCreator device,
        bool isVertical,
        CancellationToken token);

    public sealed class EpubPaginationService
    {
        public async Task<EpubPaginationResult> CreatePagesAsync(
            EpubPaginationRequest request,
            EpubDocumentService documentService,
            EpubBlockPaginator verticalPaginator,
            EpubBlockPaginator horizontalPaginator,
            EpubPreviousPageStartFinder previousPageStartFinder)
        {
            CancellationToken token = request.CancellationToken;
            token.ThrowIfCancellationRequested();

            var pages = new List<EpubWin2DPage>();
            EpubHtmlParseResult parseResult;
            bool parseIsPartial;
            if (request.IsPreview)
            {
                var preview = documentService.ParseHtmlPreview(
                    request.Html,
                    request.CurrentPath,
                    request.ChapterIndex,
                    request.TargetLine,
                    request.PinBlockIndex,
                    token: token);
                parseResult = preview.Result;
                parseIsPartial = preview.IsPartial;
            }
            else
            {
                parseResult = documentService.ParseHtmlToAozoraBlocks(
                    request.Html,
                    request.CurrentPath,
                    request.ChapterIndex,
                    token);
                parseIsPartial = false;
            }

            token.ThrowIfCancellationRequested();

            var allBlocks = parseResult.Blocks;
            if (allBlocks.Count == 0)
            {
                return new EpubPaginationResult(pages, parseResult.TotalLineCount, parseIsPartial);
            }

            var layout = CalculateLayout(request);
            var device = request.Device ?? CanvasDevice.GetSharedDevice();
            int totalBlocks = allBlocks.Count;
            int maxSourceLine = allBlocks[allBlocks.Count - 1].SourceLineNumber;

            if (request.IsPreview)
            {
                int index = FindPreviewStartIndex(allBlocks, request.PinBlockIndex, request.TargetLine);
                while (index < totalBlocks && pages.Count < request.MaxPreviewPages)
                {
                    token.ThrowIfCancellationRequested();
                    var page = PaginateNextPage(
                        ref index,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator,
                        token);

                    if (page != null) pages.Add(page);
                    else index++;
                }

                parseIsPartial = parseIsPartial || index < totalBlocks;
            }
            else if (request.PinBlockIndex >= 0 && request.PinBlockIndex < totalBlocks)
            {
                int forwardIndex = request.PinBlockIndex;
                while (forwardIndex < totalBlocks)
                {
                    token.ThrowIfCancellationRequested();
                    var page = PaginateNextPage(
                        ref forwardIndex,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator,
                        token);

                    if (page != null) pages.Add(page);
                    else forwardIndex++;

                    if (forwardIndex % 100 == 0) await Task.Delay(1, token);
                }

                int backwardIndex = request.PinBlockIndex;
                while (backwardIndex > 0)
                {
                    token.ThrowIfCancellationRequested();
                    int previousStart = previousPageStartFinder(
                        backwardIndex,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        token);

                    if (previousStart >= backwardIndex) break;

                    int tempIndex = previousStart;
                    var page = PaginateNextPage(
                        ref tempIndex,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator,
                        token);

                    if (page != null) pages.Insert(0, page);

                    backwardIndex = previousStart;
                    if (backwardIndex % 100 == 0) await Task.Delay(1, token);
                }
            }
            else
            {
                int index = 0;
                while (index < totalBlocks)
                {
                    token.ThrowIfCancellationRequested();
                    var page = PaginateNextPage(
                        ref index,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator,
                        token);

                    if (page != null) pages.Add(page);
                    else index++;

                    if (index % 100 == 0) await Task.Delay(1, token);
                }
            }

            int total = Math.Max(1, maxSourceLine);
            foreach (var page in pages)
            {
                page.TotalLinesInChapter = total;
            }

            return new EpubPaginationResult(pages, parseResult.TotalLineCount, parseIsPartial);
        }

        private static int FindPreviewStartIndex(
            IReadOnlyList<AozoraBindingModel> blocks,
            int targetBlockIndex,
            int targetLine)
        {
            if (targetBlockIndex >= 0)
                return Math.Clamp(targetBlockIndex, 0, blocks.Count - 1);

            if (targetLine <= 1) return 0;

            int left = 0;
            int right = blocks.Count - 1;
            int result = 0;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                if (blocks[middle].SourceLineNumber < targetLine)
                {
                    result = middle;
                    left = middle + 1;
                }
                else
                {
                    result = middle;
                    right = middle - 1;
                }
            }

            return Math.Clamp(result, 0, blocks.Count - 1);
        }

        private static EpubPageLayout CalculateLayout(EpubPaginationRequest request)
        {
            var margins = request.IsVerticalMode
                ? ReaderPageMargins.EpubVerticalText
                : ReaderPageMargins.HorizontalText;

            float maxWidth = request.AvailableWidth - margins.Horizontal;
            float pageHeight = request.AvailableHeight - margins.Vertical;

            if (!request.IsVerticalMode)
            {
                float limitedWidth = (float)(request.FontSize * 42);
                if (maxWidth > limitedWidth) maxWidth = limitedWidth;
            }

            if (maxWidth < 100) maxWidth = 100;
            if (pageHeight < 100) pageHeight = 100;

            return new EpubPageLayout(maxWidth, pageHeight);
        }

        private static EpubWin2DPage? PaginateNextPage(
            ref int index,
            List<AozoraBindingModel> allBlocks,
            float maxWidth,
            float pageHeight,
            CanvasDevice? device,
            bool isVerticalMode,
            EpubBlockPaginator verticalPaginator,
            EpubBlockPaginator horizontalPaginator,
            CancellationToken token)
        {
            if (index >= allBlocks.Count) return null;

            var block = allBlocks[index];

            if (block.HasImage)
            {
                var imageSource = block.Inlines.OfType<AozoraImage>().FirstOrDefault()?.Source ?? string.Empty;
                var page = new EpubWin2DPage
                {
                    Blocks = new List<AozoraBindingModel> { block },
                    IsImagePage = true,
                    ImagePath = imageSource,
                    StartBlockIndex = index,
                    StartLine = block.SourceLineNumber,
                    LineCount = 1
                };

                index++;
                return page;
            }

            if (block.IsPageBreak)
            {
                index++;
                return PaginateNextPage(
                    ref index,
                    allBlocks,
                    maxWidth,
                    pageHeight,
                    device,
                    isVerticalMode,
                    verticalPaginator,
                    horizontalPaginator,
                    token);
            }

            int pageStart = index;
            var pageBlocks = isVerticalMode
                ? verticalPaginator(ref index, allBlocks, maxWidth, pageHeight, device, token)
                : horizontalPaginator(ref index, allBlocks, maxWidth, pageHeight, device, token);

            if (pageBlocks.Count == 0)
            {
                index++;
                return null;
            }

            return new EpubWin2DPage
            {
                Blocks = pageBlocks,
                IsImagePage = false,
                // The paginator can skip leading blank/page-break blocks. Preserve the
                // first block that is actually visible so layout changes can anchor to
                // the same content instead of drifting to a nearby block.
                StartBlockIndex = pageBlocks[0].OriginalBlockIndex >= 0
                    ? pageBlocks[0].OriginalBlockIndex
                    : pageStart,
                StartLine = pageBlocks[0].SourceLineNumber,
                LineCount = pageBlocks.Count
            };
        }
    }

    public sealed class EpubPaginationRequest
    {
        public EpubPaginationRequest(
            string html,
            string currentPath,
            int chapterIndex,
            float availableWidth,
            float availableHeight,
            double fontSize,
            bool isVerticalMode,
            int pinBlockIndex,
            CanvasDevice? device,
            bool isPreview = false,
            int targetLine = -1,
            int maxPreviewPages = 3,
            CancellationToken cancellationToken = default)
        {
            Html = html;
            CurrentPath = currentPath;
            ChapterIndex = chapterIndex;
            AvailableWidth = availableWidth;
            AvailableHeight = availableHeight;
            FontSize = fontSize;
            IsVerticalMode = isVerticalMode;
            PinBlockIndex = pinBlockIndex;
            Device = device;
            IsPreview = isPreview;
            TargetLine = targetLine;
            MaxPreviewPages = Math.Max(1, maxPreviewPages);
            CancellationToken = cancellationToken;
        }

        public string Html { get; }
        public string CurrentPath { get; }
        public int ChapterIndex { get; }
        public float AvailableWidth { get; }
        public float AvailableHeight { get; }
        public double FontSize { get; }
        public bool IsVerticalMode { get; }
        public int PinBlockIndex { get; }
        public CanvasDevice? Device { get; }
        public bool IsPreview { get; }
        public int TargetLine { get; }
        public int MaxPreviewPages { get; }
        public CancellationToken CancellationToken { get; }
    }

    public sealed class EpubPaginationResult
    {
        public EpubPaginationResult(List<EpubWin2DPage> pages, int totalLineCount, bool isPartial = false)
        {
            Pages = pages;
            TotalLineCount = totalLineCount;
            IsPartial = isPartial;
        }

        public List<EpubWin2DPage> Pages { get; }
        public int TotalLineCount { get; }
        public bool IsPartial { get; }
    }

    public readonly struct EpubPageLayout
    {
        public EpubPageLayout(float maxWidth, float pageHeight)
        {
            MaxWidth = maxWidth;
            PageHeight = pageHeight;
        }

        public float MaxWidth { get; }
        public float PageHeight { get; }
    }
}
