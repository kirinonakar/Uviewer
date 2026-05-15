using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public delegate List<AozoraBindingModel> EpubBlockPaginator(
        ref int index,
        List<AozoraBindingModel> blocks,
        float maxWidth,
        float pageHeight,
        CanvasDevice? device);

    public delegate int EpubPreviousPageStartFinder(
        int targetIndex,
        List<AozoraBindingModel> blocks,
        float maxWidth,
        float pageHeight,
        ICanvasResourceCreator device,
        bool isVertical);

    public sealed class EpubPaginationService
    {
        public async Task<EpubPaginationResult> CreatePagesAsync(
            EpubPaginationRequest request,
            EpubDocumentService documentService,
            EpubBlockPaginator verticalPaginator,
            EpubBlockPaginator horizontalPaginator,
            EpubPreviousPageStartFinder previousPageStartFinder)
        {
            var pages = new List<EpubWin2DPage>();
            var parseResult = documentService.ParseHtmlToAozoraBlocks(
                request.Html,
                request.CurrentPath,
                request.ChapterIndex);

            var allBlocks = parseResult.Blocks;
            if (allBlocks.Count == 0)
            {
                return new EpubPaginationResult(pages, parseResult.TotalLineCount);
            }

            var layout = CalculateLayout(request);
            var device = request.Device ?? CanvasDevice.GetSharedDevice();
            int totalBlocks = allBlocks.Count;
            int maxSourceLine = allBlocks[allBlocks.Count - 1].SourceLineNumber;

            if (request.PinBlockIndex >= 0 && request.PinBlockIndex < totalBlocks)
            {
                int forwardIndex = request.PinBlockIndex;
                while (forwardIndex < totalBlocks)
                {
                    var page = PaginateNextPage(
                        ref forwardIndex,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator);

                    if (page != null) pages.Add(page);
                    else forwardIndex++;

                    if (forwardIndex % 100 == 0) await Task.Delay(1);
                }

                int backwardIndex = request.PinBlockIndex;
                while (backwardIndex > 0)
                {
                    int previousStart = previousPageStartFinder(
                        backwardIndex,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode);

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
                        horizontalPaginator);

                    if (page != null) pages.Insert(0, page);

                    backwardIndex = previousStart;
                    if (backwardIndex % 100 == 0) await Task.Delay(1);
                }
            }
            else
            {
                int index = 0;
                while (index < totalBlocks)
                {
                    var page = PaginateNextPage(
                        ref index,
                        allBlocks,
                        layout.MaxWidth,
                        layout.PageHeight,
                        device,
                        request.IsVerticalMode,
                        verticalPaginator,
                        horizontalPaginator);

                    if (page != null) pages.Add(page);
                    else index++;

                    if (index % 100 == 0) await Task.Delay(1);
                }
            }

            int total = Math.Max(1, maxSourceLine);
            foreach (var page in pages)
            {
                page.TotalLinesInChapter = total;
            }

            return new EpubPaginationResult(pages, parseResult.TotalLineCount);
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
            EpubBlockPaginator horizontalPaginator)
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
                    horizontalPaginator);
            }

            int pageStart = index;
            var pageBlocks = isVerticalMode
                ? verticalPaginator(ref index, allBlocks, maxWidth, pageHeight, device)
                : horizontalPaginator(ref index, allBlocks, maxWidth, pageHeight, device);

            if (pageBlocks.Count == 0)
            {
                index++;
                return null;
            }

            return new EpubWin2DPage
            {
                Blocks = pageBlocks,
                IsImagePage = false,
                StartBlockIndex = pageStart,
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
            CanvasDevice? device)
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
    }

    public sealed class EpubPaginationResult
    {
        public EpubPaginationResult(List<EpubWin2DPage> pages, int totalLineCount)
        {
            Pages = pages;
            TotalLineCount = totalLineCount;
        }

        public List<EpubWin2DPage> Pages { get; }
        public int TotalLineCount { get; }
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
