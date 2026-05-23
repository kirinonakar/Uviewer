using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class DocumentSearchCoordinatorContext
    {
        public DocumentSearchState State { get; init; } = null!;
        public SearchOverlayService OverlayService { get; init; } = null!;
        public SearchHighlightService HighlightService { get; init; } = null!;
        public DocumentSearchService DocumentSearchService { get; init; } = null!;
        public TextDocumentSearchService TextDocumentSearchService { get; init; } = null!;
        public TextSearchHighlightPresenterService TextHighlightPresenterService { get; init; } = null!;
        public TextReaderState TextReaderState { get; init; } = null!;
        public EpubSession EpubSession { get; init; } = null!;
        public EpubDocumentService EpubDocumentService { get; init; } = null!;
        public MainToolbarControl MainToolbar { get; init; } = null!;
        public FrameworkElement RootGrid { get; init; } = null!;
        public ItemsRepeater? TextItemsRepeater { get; init; }
        public CanvasControl? MainCanvas { get; init; }
        public CanvasControl? AozoraTextCanvas { get; init; }
        public CanvasControl? VerticalTextCanvas { get; init; }
        public CanvasControl? EpubTextCanvas { get; init; }
        public IReadOnlyList<TextLine> TextLines { get; init; } = Array.Empty<TextLine>();
        public IReadOnlyList<AozoraBindingModel> AozoraBlocks { get; init; } = Array.Empty<AozoraBindingModel>();

        public bool IsTextMode { get; init; }
        public bool IsEpubMode { get; init; }
        public bool IsVerticalMode { get; init; }
        public bool IsAozoraMode { get; init; }
        public bool IsMarkdownRenderMode { get; init; }
        public bool IsPdfMode { get; init; }
        public string? CurrentPdfPath { get; init; }
        public string EpubCacheKey { get; init; } = string.Empty;
        public int EpubSpineCount { get; init; }
        public int CurrentIndex { get; init; }
        public int ImageEntryCount { get; init; }
        public int CurrentEpubChapterIndex { get; init; }
        public int CurrentEpubPageStartLine { get; init; } = 1;
        public int CurrentVerticalStartLine { get; init; } = 1;
        public int CurrentAozoraStartLine { get; init; } = 1;
        public string EncodingName { get; init; } = "Auto";

        public Action<int> SetCurrentIndex { get; init; } = null!;
        public Action<int> SetCurrentEpubChapterIndex { get; init; } = null!;
        public Action DisableVerticalModeForImageDocument { get; init; } = null!;
        public Action<string, string, string> ShowNotification { get; init; } = null!;
        public Func<int> GetTopVisibleLineIndex { get; init; } = null!;
        public Func<int, int, int> FindAozoraStartBlockIndex { get; init; } = null!;
        public Func<DocumentSearchMatch, int> FindEpubPageIndex { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Func<int, int, Task> PrepareVerticalTextAsync { get; init; } = null!;
        public Func<int, Task> RenderAozoraDynamicPageAsync { get; init; } = null!;
        public Func<int, int, int, Task> LoadEpubChapterAsync { get; init; } = null!;
        public Action UpdateAozoraStatusBar { get; init; } = null!;
        public Action UpdateTextStatusBar { get; init; } = null!;
        public Action<int> ScrollToLine { get; init; } = null!;
        public Action<int> SetEpubPageIndex { get; init; } = null!;
    }

    internal sealed class DocumentSearchCoordinatorService
    {
        public bool CanSearch(DocumentSearchCoordinatorContext context) =>
            (context.IsTextMode && context.TextReaderState.HasContent) ||
            (context.IsEpubMode && context.EpubSession.HasDocument) ||
            (context.IsPdfMode && !string.IsNullOrEmpty(context.CurrentPdfPath));

        public void ShowOverlay(DocumentSearchCoordinatorContext context, FrameworkElement? anchor = null)
        {
            if (!CanSearch(context))
            {
                context.ShowNotification(Strings.SearchUnavailable, "\uE721", "Gray");
                return;
            }

            if (context.IsPdfMode)
            {
                context.DisableVerticalModeForImageDocument();
            }

            context.MainToolbar.ShowSearchOverlay(
                context.OverlayService,
                context.IsPdfMode,
                anchor,
                context.RootGrid);
        }

        public void SetActiveQuery(DocumentSearchCoordinatorContext context, string? query)
        {
            if (context.IsPdfMode)
            {
                context.DisableVerticalModeForImageDocument();
            }

            context.State.SetQuery(query);
            InvalidateHighlights(context);

            if (context.IsPdfMode && context.State.HasQuery)
            {
                _ = RefreshPdfHighlightsAsync(context, context.CurrentIndex);
            }
        }

        public async Task RefreshPdfHighlightsAsync(
            DocumentSearchCoordinatorContext context,
            int pageIndex,
            int currentMatchIndex = -1)
        {
            if (!context.IsPdfMode || string.IsNullOrEmpty(context.CurrentPdfPath) || !context.State.HasQuery)
            {
                return;
            }

            context.DisableVerticalModeForImageDocument();

            var token = context.State.RestartPdfHighlightSearch();
            string pdfPath = context.CurrentPdfPath;
            string query = context.State.Query!;

            try
            {
                var highlights = await context.HighlightService.FindPdfHighlightsAsync(pdfPath, pageIndex, query, token);
                if (token.IsCancellationRequested) return;
                if (!string.Equals(context.CurrentPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase)) return;
                if (context.CurrentIndex != pageIndex) return;
                if (!string.Equals(context.State.Query, query, StringComparison.Ordinal)) return;

                context.State.SetPdfHighlights(highlights, pageIndex, currentMatchIndex);
                context.MainCanvas?.Invalidate();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF search highlight error: {ex.Message}");
            }
        }

        public void InvalidateHighlights(DocumentSearchCoordinatorContext context)
        {
            ApplyHighlightsToRealizedText(context);
            context.MainCanvas?.Invalidate();
            context.AozoraTextCanvas?.Invalidate();
            context.VerticalTextCanvas?.Invalidate();
            context.EpubTextCanvas?.Invalidate();
        }

        public void ApplyHighlightsToTextBlock(
            DocumentSearchCoordinatorContext context,
            TextBlock textBlock,
            string content,
            int lineNumber)
        {
            context.TextHighlightPresenterService.ApplyToTextBlock(
                textBlock,
                content,
                lineNumber,
                context.State,
                context.CurrentEpubChapterIndex);
        }

        public DocumentSearchMatch? GetActiveMatchFor(
            DocumentSearchCoordinatorContext context,
            DocumentSearchKind kind)
        {
            return context.State.GetActiveMatchFor(kind, context.CurrentEpubChapterIndex);
        }

        public async Task<IReadOnlyList<DocumentSearchMatch>> SearchCurrentDocumentAsync(
            DocumentSearchCoordinatorContext context,
            string query,
            CancellationToken token)
        {
            if (context.IsPdfMode && !string.IsNullOrEmpty(context.CurrentPdfPath))
            {
                context.DisableVerticalModeForImageDocument();
                return await context.DocumentSearchService.SearchPdfAsync(context.CurrentPdfPath, query, token);
            }

            if (context.IsEpubMode && context.EpubSession.HasDocument)
            {
                return await context.DocumentSearchService.SearchEpubAsync(
                    context.EpubCacheKey,
                    context.EpubSession,
                    context.EpubDocumentService,
                    query,
                    token);
            }

            if (context.IsTextMode)
            {
                return context.TextDocumentSearchService.Search(
                    context.TextReaderState,
                    context.IsAozoraMode,
                    context.AozoraBlocks,
                    context.IsMarkdownRenderMode,
                    context.EncodingName,
                    query);
            }

            return Array.Empty<DocumentSearchMatch>();
        }

        public long GetCurrentPosition(DocumentSearchCoordinatorContext context)
        {
            if (context.IsPdfMode)
            {
                return context.CurrentIndex + 1;
            }

            if (context.IsEpubMode)
            {
                return DocumentSearchService.CreateEpubSortKey(
                    context.CurrentEpubChapterIndex,
                    context.CurrentEpubPageStartLine);
            }

            if (context.IsVerticalMode)
            {
                return Math.Max(1, context.CurrentVerticalStartLine);
            }

            if (context.IsAozoraMode)
            {
                return Math.Max(1, context.CurrentAozoraStartLine);
            }

            return context.GetTopVisibleLineIndex();
        }

        public async Task NavigateToMatchAsync(
            DocumentSearchCoordinatorContext context,
            DocumentSearchMatch match)
        {
            context.State.SetActiveMatch(match);

            switch (match.Kind)
            {
                case DocumentSearchKind.Pdf:
                    if (context.IsPdfMode && match.PageIndex >= 0 && match.PageIndex < context.ImageEntryCount)
                    {
                        context.State.SetPdfMatchIndex(match.MatchIndex);
                        context.SetCurrentIndex(match.PageIndex);
                        await context.DisplayCurrentImageAsync();
                        await RefreshPdfHighlightsAsync(context, match.PageIndex, match.MatchIndex);
                    }
                    break;

                case DocumentSearchKind.Epub:
                    await NavigateToEpubMatchAsync(context, match);
                    break;

                case DocumentSearchKind.Text:
                    await NavigateToTextMatchAsync(context, match);
                    break;
            }

            if (match.Kind != DocumentSearchKind.Pdf)
            {
                InvalidateHighlights(context);
            }
        }

        private void ApplyHighlightsToRealizedText(DocumentSearchCoordinatorContext context)
        {
            if (context.TextItemsRepeater == null || context.TextLines.Count == 0) return;

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(context.TextItemsRepeater);
                for (int i = 0; i < childCount; i++)
                {
                    if (VisualTreeHelper.GetChild(context.TextItemsRepeater, i) is TextBlock textBlock)
                    {
                        int index = context.TextItemsRepeater.GetElementIndex(textBlock);
                        if (index >= 0 && index < context.TextLines.Count)
                        {
                            ApplyHighlightsToTextBlock(
                                context,
                                textBlock,
                                context.TextLines[index].Content,
                                index + 1);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private async Task NavigateToTextMatchAsync(
            DocumentSearchCoordinatorContext context,
            DocumentSearchMatch match)
        {
            int line = Math.Max(1, match.LineNumber);

            if (context.IsVerticalMode)
            {
                await context.PrepareVerticalTextAsync(line, match.BlockIndex);
                return;
            }

            if (context.IsAozoraMode && context.AozoraBlocks.Count > 0)
            {
                int targetIndex = context.FindAozoraStartBlockIndex(line, match.BlockIndex);
                await context.RenderAozoraDynamicPageAsync(targetIndex);
                context.UpdateAozoraStatusBar();
                return;
            }

            context.ScrollToLine(line);
            context.UpdateTextStatusBar();
        }

        private async Task NavigateToEpubMatchAsync(
            DocumentSearchCoordinatorContext context,
            DocumentSearchMatch match)
        {
            int chapterIndex = Math.Clamp(match.EpubChapterIndex, 0, Math.Max(0, context.EpubSpineCount - 1));
            int line = Math.Max(1, match.LineNumber);

            if (chapterIndex != context.CurrentEpubChapterIndex)
            {
                context.SetCurrentEpubChapterIndex(chapterIndex);
                await context.LoadEpubChapterAsync(chapterIndex, line, match.BlockIndex);
                return;
            }

            if (context.IsVerticalMode)
            {
                await context.LoadEpubChapterAsync(chapterIndex, line, match.BlockIndex);
                return;
            }

            int pageIndex = context.FindEpubPageIndex(match);
            if (pageIndex >= 0)
            {
                context.SetEpubPageIndex(pageIndex);
            }
        }
    }
}
