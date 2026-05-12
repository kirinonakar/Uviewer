using Microsoft.Graphics.Canvas;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public enum AozoraPageOrientation
    {
        Horizontal,
        Vertical
    }

    public sealed class AozoraPageMapCalculator
    {
        private readonly AozoraBlockMeasurer _measurer;

        public AozoraPageMapCalculator(AozoraBlockMeasurer measurer)
        {
            _measurer = measurer;
        }

        public Task<AozoraPageMapResult?> CalculateAsync(
            IReadOnlyList<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation,
            CancellationToken token)
        {
            return Task.Run(async () =>
            {
                int pageCount = 1;
                float currentPageExtent = 0;
                var blockToPageMap = new Dictionary<int, int>();
                AozoraBindingModel? currentMergedBlock = null;
                float currentMergedBlockExtent = 0;

                for (int i = 0; i < blocks.Count; i++)
                {
                    if (token.IsCancellationRequested) return null;

                    var block = blocks[i];

                    if (currentPageExtent == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                    {
                        blockToPageMap[i] = pageCount;
                        continue;
                    }

                    if (block.HasImage || block.IsPageBreak)
                    {
                        if (currentPageExtent > 0)
                        {
                            pageCount++;
                            currentPageExtent = 0;
                        }

                        blockToPageMap[i] = pageCount;

                        if (block.IsPageBreak)
                        {
                            if (i < blocks.Count - 1) pageCount++;
                            currentMergedBlock = null;
                            currentMergedBlockExtent = 0;
                            continue;
                        }

                        var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                        if (aozoraImg != null && !context.ImageExists(aozoraImg.Source))
                        {
                            continue;
                        }

                        if (orientation == AozoraPageOrientation.Vertical &&
                            context.ImagePairingEnabled &&
                            aozoraImg != null &&
                            context.ShouldPairImage(aozoraImg.Source) &&
                            i < blocks.Count - 1)
                        {
                            i = MapPairedVerticalImagePage(blocks, i, pageCount, blockToPageMap, context);
                        }

                        if (i < blocks.Count - 1) pageCount++;
                        currentMergedBlock = null;
                        currentMergedBlockExtent = 0;
                        continue;
                    }

                    if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                    {
                        var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                        tempMerged.Inlines.AddRange(block.Inlines);

                        float fontSize = (float)(context.FontSize * tempMerged.FontSizeScale);
                        float newExtent = MeasureBlock(context, orientation, tempMerged, fontSize);
                        float extentDiff = newExtent - currentMergedBlockExtent;
                        float tolerance = orientation == AozoraPageOrientation.Vertical
                            ? fontSize * 0.8f
                            : fontSize * 1.2f;

                        if (currentPageExtent + extentDiff > GetPageLimit(context, orientation) + tolerance && currentPageExtent > 0)
                        {
                            pageCount++;
                            currentPageExtent = 0;
                        }
                        else
                        {
                            blockToPageMap[i] = pageCount;
                            currentPageExtent += extentDiff;
                            currentMergedBlock = tempMerged;
                            currentMergedBlockExtent = newExtent;
                            continue;
                        }
                    }

                    float fontSizeBase = (float)(context.FontSize * block.FontSizeScale);
                    float blockExtent = MeasureBlock(context, orientation, block, fontSizeBase);
                    blockExtent += GetKeigakomiAdjustment(block, currentMergedBlock, fontSizeBase, orientation);

                    float blockTolerance = orientation == AozoraPageOrientation.Vertical
                        ? fontSizeBase * 0.8f
                        : fontSizeBase * 1.2f;

                    if (currentPageExtent > 0 && currentPageExtent + blockExtent > GetPageLimit(context, orientation) + blockTolerance)
                    {
                        pageCount++;
                        currentPageExtent = 0;
                    }

                    blockToPageMap[i] = pageCount;
                    currentPageExtent += blockExtent;

                    if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                    {
                        currentMergedBlock = block;
                        currentMergedBlockExtent = blockExtent;
                    }
                    else
                    {
                        currentMergedBlock = null;
                    }

                    if (i % 50 == 0) await Task.Delay(1, token);
                }

                if (token.IsCancellationRequested) return null;
                return new AozoraPageMapResult(blockToPageMap, pageCount);
            }, token);
        }

        private int MapPairedVerticalImagePage(
            IReadOnlyList<AozoraBindingModel> blocks,
            int currentIndex,
            int pageCount,
            Dictionary<int, int> blockToPageMap,
            AozoraBlockPaginationContext context)
        {
            int nextIndex = currentIndex + 1;
            while (nextIndex < blocks.Count)
            {
                var nextBlock = blocks[nextIndex];
                if (nextBlock.HasImage)
                {
                    var nextImg = nextBlock.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (nextImg != null && context.ImageExists(nextImg.Source) && context.ShouldPairImage(nextImg.Source))
                    {
                        blockToPageMap[nextIndex] = pageCount;
                        return nextIndex;
                    }

                    break;
                }

                bool isWhitespace = nextBlock.Inlines.All(inline =>
                    inline is string s && string.IsNullOrWhiteSpace(s) || inline is AozoraLineBreak);

                if (!isWhitespace) break;

                blockToPageMap[nextIndex] = pageCount;
                nextIndex++;
            }

            return currentIndex;
        }

        private float MeasureBlock(
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation,
            AozoraBindingModel block,
            float fontSize)
        {
            if (orientation == AozoraPageOrientation.Vertical)
            {
                return _measurer.MeasureVerticalBlockWidth(
                    context.Device,
                    block,
                    context.AvailableHeight,
                    fontSize,
                    context.DefaultFontFamily,
                    context.GetFontWeight);
            }

            return _measurer.MeasureHorizontalBlockHeight(
                context.Device,
                block,
                context.AvailableWidth,
                fontSize,
                context.DefaultFontFamily,
                context.GetFontWeight);
        }

        private static float GetPageLimit(AozoraBlockPaginationContext context, AozoraPageOrientation orientation)
        {
            return orientation == AozoraPageOrientation.Vertical
                ? context.AvailableWidth
                : context.AvailableHeight;
        }

        private static float GetKeigakomiAdjustment(
            AozoraBindingModel block,
            AozoraBindingModel? previousBlock,
            float fontSize,
            AozoraPageOrientation orientation)
        {
            if (orientation == AozoraPageOrientation.Vertical)
            {
                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                bool wasKeigakomi = previousBlock != null && (previousBlock.BorderColor != null || previousBlock.BorderThickness.Top > 0);

                if (isKeigakomi && !wasKeigakomi) return 20f;
                if (!isKeigakomi && wasKeigakomi) return 20f;
                return 0f;
            }

            bool horizontalKeigakomi = block.BorderThickness.Top > 0 &&
                                       block.BorderThickness.Bottom > 0 &&
                                       block.BorderThickness.Left > 0 &&
                                       block.BorderThickness.Right > 0;
            bool previousHorizontalKeigakomi = previousBlock != null &&
                                               previousBlock.BorderThickness.Top > 0 &&
                                               previousBlock.BorderThickness.Bottom > 0 &&
                                               previousBlock.BorderThickness.Left > 0 &&
                                               previousBlock.BorderThickness.Right > 0;

            if (horizontalKeigakomi && !previousHorizontalKeigakomi) return 20f;
            if (!horizontalKeigakomi && previousHorizontalKeigakomi) return 20f + fontSize * 2.1f;
            return 0f;
        }
    }

    public sealed class AozoraPageMapResult
    {
        public AozoraPageMapResult(Dictionary<int, int> blockToPageMap, int totalPages)
        {
            BlockToPageMap = blockToPageMap;
            TotalPages = totalPages;
        }

        public Dictionary<int, int> BlockToPageMap { get; }
        public int TotalPages { get; }
    }
}
