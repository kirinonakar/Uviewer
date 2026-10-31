using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Renderers;
using Uviewer.Services;
using Windows.Foundation;

namespace Uviewer;

internal sealed partial class DocumentReaderController
{
    private bool IsMarkdownScrollMode => _isTextMode && !_isEpubMode && _isAozoraMode && _isMarkdownRenderMode;
    private ScrollViewer? _markdownScrollViewer;
    private Border? _markdownScrollExtent;
    private MarkdownScrollLayout? _markdownScrollLayout;
    private readonly HashSet<string> _markdownRequestedImages = new();
    private float _markdownContentWidth;
    private bool _markdownUpdatingLayout;

    private void ConfigureMarkdownScrollView()
    {
        if (IsMarkdownScrollMode && _markdownScrollViewer == null)
        {
            _markdownScrollExtent = new Border { Background = new SolidColorBrush(Colors.Transparent) };
            _markdownScrollViewer = new ScrollViewer
            {
                Content = _markdownScrollExtent,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                ZoomMode = ZoomMode.Disabled,
                IsTabStop = false
            };
            // The scroll extent receives native wheel/touch input. A viewport-sized
            // canvas above it renders only visible blocks, even for long documents.
            TextArea.Children.Insert(TextArea.Children.IndexOf(AozoraTextCanvas), _markdownScrollViewer);
            _markdownScrollViewer.ViewChanged += (_, _) =>
            {
                if (IsMarkdownScrollMode && !_markdownUpdatingLayout)
                    UpdateMarkdownViewport();
            };
            _markdownScrollViewer.SizeChanged += (_, _) =>
            {
                if (IsMarkdownScrollMode && !_markdownUpdatingLayout && _markdownScrollLayout != null)
                    RebuildMarkdownScrollLayout(preservePosition: true);
            };
        }

        if (_markdownScrollViewer != null)
            _markdownScrollViewer.Visibility = IsMarkdownScrollMode ? Visibility.Visible : Visibility.Collapsed;
        AozoraTextCanvas.IsHitTestVisible = !IsMarkdownScrollMode;
    }

    private async Task PrepareMarkdownScrollDisplayAsync(string content, int targetLine, int targetBlockIndex, CancellationToken token)
    {
        ConfigureMarkdownScrollView();
        TextScrollViewer.Visibility = Visibility.Collapsed;
        VerticalTextCanvas.Visibility = Visibility.Collapsed;
        AozoraTextCanvas.Visibility = Visibility.Visible;
        TextArea.Visibility = Visibility.Visible;
        TextArea.Background = _settingsManager.GetThemeBackground();
        TextFastNavOverlay.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;

        if (_aozoraBlocks.Count == 0 || _isAozoraParsePartial)
        {
            int generation = ++_aozoraParseGeneration;
            double fontSize = _settingsManager.FontSize;
            // A source window can begin inside a code fence, table or diagram.
            // Parse the whole Markdown document once so scrolling has a stable flow.
            var document = await Task.Run(() => _textBlockDocumentService.Parse(content, true, fontSize), token);
            token.ThrowIfCancellationRequested();
            if (generation != _aozoraParseGeneration || !IsMarkdownScrollMode) return;
            _aozoraBlocks = document.Blocks;
            _aozoraTotalLineCount = document.Blocks.Count;
            _aozoraTotalLineCountInSource = document.SourceLineCount;
            _textTotalLineCountInSource = document.SourceLineCount;
            _isAozoraParsePartial = false;
            _aozoraLoadedWindowStartLine = 1;
            _aozoraLoadedWindowEndLine = document.SourceLineCount;
            _aozoraHasLeadingGap = _aozoraHasTrailingGap = false;
            _tocService.SetProvider(new TextTocProvider(content, _aozoraBlocks));
            _ = _tocService.LoadTocAsync(token);
        }

        token.ThrowIfCancellationRequested();
        ResetAozoraPageCalculationForDocumentChange();
        ClearBackwardCache();
        _markdownRequestedImages.Clear();
        _markdownScrollLayout = null;
        TextArea.UpdateLayout();
        RebuildMarkdownScrollLayout(preservePosition: false);
        int index = _textBlockDocumentService.FindStartBlockIndex(_aozoraBlocks, targetLine, targetBlockIndex);
        ScrollMarkdownToBlock(index);
        _aozoraPendingTargetLine = 0;
    }

    private void RebuildMarkdownScrollLayout(bool preservePosition)
    {
        if (!IsMarkdownScrollMode || _markdownScrollViewer == null || _markdownScrollExtent == null) return;
        var oldLayout = _markdownScrollLayout;
        double offset = _markdownScrollViewer.VerticalOffset;
        int anchor = oldLayout?.FindBlock(offset) ?? 0;
        double withinBlock = oldLayout != null ? offset - oldLayout.GetTop(anchor) : 0;
        var margins = ReaderPageMargins.HorizontalText;
        _markdownContentWidth = Math.Max(100, (float)AozoraTextCanvas.ActualWidth - margins.Horizontal);
        var device = AozoraTextCanvas.Device ?? CanvasDevice.GetSharedDevice();
        var heights = new double[_aozoraBlocks.Count];
        for (int i = 0; i < heights.Length; i++)
        {
            var block = _aozoraBlocks[i];
            if (block.HasImage)
            {
                string source = block.Inlines.OfType<AozoraImage>().First().Source;
                var bitmap = _imageResourceService.TryGetCached(ImageResourceService.GetTextCacheKey(source));
                heights[i] = bitmap != null
                    ? GetMarkdownImageSize(bitmap).Height + 16
                    : DoesAozoraImageExist(source) ? 120 : 0;
            }
            else if (!block.IsPageBreak)
            {
                heights[i] = _aozoraBlockMeasurer.MeasureHorizontalBlockHeight(device, block,
                    _markdownContentWidth, (float)(_settingsManager.FontSize * block.FontSizeScale),
                    _settingsManager.FontFamily, GetFontWeightForFamily);
            }
        }

        _markdownScrollLayout = new MarkdownScrollLayout(heights, margins.Top, margins.Bottom);
        _markdownUpdatingLayout = true;
        try
        {
            _markdownScrollExtent.Height = _markdownScrollLayout.ExtentHeight;
            _markdownScrollViewer.UpdateLayout();
            if (preservePosition && oldLayout != null)
                _markdownScrollViewer.ChangeView(null, Math.Max(0, _markdownScrollLayout.GetTop(anchor) + withinBlock), null, true);
        }
        finally { _markdownUpdatingLayout = false; }
        UpdateMarkdownViewport();
    }

    private Size GetMarkdownImageSize(CanvasBitmap bitmap)
    {
        var size = MarkdownScrollLayout.FitImage(bitmap.Size.Width, bitmap.Size.Height, _markdownContentWidth);
        return new Size(size.Width, size.Height);
    }

    private void ScrollMarkdownToBlock(int index)
    {
        if (_markdownScrollViewer == null || _markdownScrollLayout == null) return;
        _markdownScrollViewer.ChangeView(null, Math.Max(0,
            _markdownScrollLayout.GetTop(index) - ReaderPageMargins.HorizontalText.Top), null, true);
        UpdateMarkdownViewport();
    }

    private void ScrollMarkdownBy(double distance)
    {
        if (_markdownScrollViewer == null) return;
        _markdownScrollViewer.ChangeView(null, Math.Clamp(_markdownScrollViewer.VerticalOffset + distance,
            0, _markdownScrollViewer.ScrollableHeight), null, true);
    }

    private void UpdateMarkdownViewport()
    {
        if (!IsMarkdownScrollMode || _markdownScrollViewer == null || _markdownScrollLayout == null) return;
        double offset = _markdownScrollViewer.VerticalOffset;
        if (_aozoraBlocks.Count == 0)
        {
            _aozoraPageState.SetEmptyPage();
            ClearAozoraSelection();
            AozoraTextCanvas.Invalidate();
            UpdateMarkdownStatusBar();
            return;
        }
        int first = _markdownScrollLayout.FindBlock(offset);
        int last = _markdownScrollLayout.FindBlock(offset + AozoraTextCanvas.ActualHeight);
        _aozoraPageState.SetPage(_aozoraBlocks.GetRange(first, last - first + 1), first, last + 1);
        AozoraTextCanvas.Invalidate();
        UpdateMarkdownStatusBar();
        var blocks = _aozoraBlocks;
        for (int i = first; i <= last; i++)
        {
            foreach (var image in blocks[i].Inlines.OfType<AozoraImage>())
            {
                if (_imageResourceService.TryGetCached(ImageResourceService.GetTextCacheKey(image.Source)) == null &&
                    _markdownRequestedImages.Add(image.Source))
                    _ = LoadMarkdownInlineImageAsync(image.Source, blocks, _globalTextCts?.Token ?? default);
            }
        }
    }

    private async Task LoadMarkdownInlineImageAsync(string source, List<AozoraBindingModel> blocks, CancellationToken token)
    {
        try
        {
            await _imageResourceService.LoadAsync(ImageResourceService.GetTextCacheKey(source), source,
                AozoraTextCanvas.Device ?? CanvasDevice.GetSharedDevice(), CreateViewingContext(),
                _sharpenEnabled, CreateSharpenParams());
            if (!token.IsCancellationRequested && IsMarkdownScrollMode && ReferenceEquals(blocks, _aozoraBlocks))
                RebuildMarkdownScrollLayout(preservePosition: true);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Markdown image load error: {ex.Message}"); }
    }

    private void DrawMarkdownViewport(CanvasDrawingSession ds)
    {
        if (_markdownScrollLayout == null || _markdownScrollViewer == null || _aozoraBlocks.Count == 0) return;
        object pageToken = _aozoraBlocks;
        var ranges = CanvasTextSelectionHelper.BuildRangesForDraw(_aozoraSelection, _aozoraSelectionGeometry, pageToken);
        var geometry = new CanvasTextGeometry(pageToken);
        var margins = ReaderPageMargins.HorizontalText;
        double offset = _markdownScrollViewer.VerticalOffset;
        int first = _currentAozoraStartBlockIndex;
        int end = Math.Min(_aozoraBlocks.Count, _currentAozoraEndBlockIndex + 1);
        for (int i = first; i < end;)
        {
            float y = (float)(_markdownScrollLayout.GetTop(i) - offset);
            var block = _aozoraBlocks[i];
            if (block.HasImage)
            {
                string source = block.Inlines.OfType<AozoraImage>().First().Source;
                var bitmap = _imageResourceService.TryGetCached(ImageResourceService.GetTextCacheKey(source));
                if (bitmap != null)
                {
                    var size = GetMarkdownImageSize(bitmap);
                    ds.DrawImage(bitmap, new Rect(margins.Left + (_markdownContentWidth - size.Width) / 2,
                        y + 8, size.Width, size.Height));
                }
                i++;
                continue;
            }
            if (block.IsPageBreak) { i++; continue; }
            int start = i++;
            while (i < end && !_aozoraBlocks[i].HasImage && !_aozoraBlocks[i].IsPageBreak) i++;
            HorizontalRenderer.RenderBlocks(ds, _aozoraBlocks.GetRange(start, i - start), GetVerticalTextColor(),
                margins.Left, y, _markdownContentWidth, _settingsManager.FontSize, _settingsManager.FontFamily,
                GetFontWeightForFamily, _activeSearchQuery, GetActiveSearchMatchFor(DocumentSearchKind.Text),
                DocumentSearchKind.Text, start, geometry, ranges, start);
        }
        CanvasTextSelectionHelper.ApplyGeometry(ref _aozoraSelectionGeometry, geometry, _aozoraSelection, pageToken);
    }

    private void UpdateMarkdownStatusBar()
    {
        if (_markdownScrollViewer == null) return;
        int line = _aozoraBlocks.Count > 0 ? Math.Max(1, _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber) : 1;
        int total = Math.Max(1, _aozoraTotalLineCountInSource);
        bool atEnd = _markdownScrollViewer.VerticalOffset >= _markdownScrollViewer.ScrollableHeight - 1;
        ImageInfoText.Text = Strings.LineInfo(line, total);
        TextProgressText.Text = _readingProgressService.FormatPercent(_readingProgressService.CalculateLineProgress(line, total, atEnd));
        ImageIndexText.Text = string.Empty;
        if (_lastRecentSaveLine != line)
        {
            _lastRecentSaveLine = line;
            _ = AddToRecentAsync(false);
        }
    }
}
