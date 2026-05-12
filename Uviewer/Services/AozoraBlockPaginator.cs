using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class AozoraBlockPaginator
    {
        private readonly AozoraBlockMeasurer _measurer;

        public AozoraBlockPaginator(AozoraBlockMeasurer measurer)
        {
            _measurer = measurer;
        }

        public List<AozoraBindingModel> PaginateHorizontalPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context)
        {
            var pageBlocks = new List<AozoraBindingModel>();
            float usedHeight = 0;

            AozoraBindingModel? currentMergedBlock = null;
            float currentMergedBlockHeight = 0;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                if (pageBlocks.Count == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                {
                    index++;
                    continue;
                }

                if (block.HasImage || block.IsPageBreak)
                {
                    if (pageBlocks.Count > 0) break;

                    if (block.IsPageBreak)
                    {
                        index++;
                        continue;
                    }

                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null && !context.ImageExists(aozoraImg.Source))
                    {
                        index++;
                        continue;
                    }

                    pageBlocks.Add(block);
                    index++;
                    break;
                }

                if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                {
                    var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                    tempMerged.Inlines.AddRange(block.Inlines);

                    float fontSize = (float)(context.FontSize * tempMerged.FontSizeScale);
                    float newHeight = _measurer.MeasureHorizontalBlockHeight(
                        context.Device,
                        tempMerged,
                        context.AvailableWidth,
                        fontSize,
                        context.DefaultFontFamily,
                        context.GetFontWeight);
                    float heightDiff = newHeight - currentMergedBlockHeight;
                    float bottomToleranceMerged = fontSize * 1.2f;

                    if (usedHeight + heightDiff > context.AvailableHeight + bottomToleranceMerged && pageBlocks.Count > 0)
                    {
                        break;
                    }

                    pageBlocks[pageBlocks.Count - 1] = tempMerged;
                    currentMergedBlock = tempMerged;
                    usedHeight += heightDiff;
                    currentMergedBlockHeight = newHeight;
                    index++;
                    continue;
                }

                float fontSizeBase = (float)(context.FontSize * block.FontSizeScale);
                float blockHeight = _measurer.MeasureHorizontalBlockHeight(
                    context.Device,
                    block,
                    context.AvailableWidth,
                    fontSizeBase,
                    context.DefaultFontFamily,
                    context.GetFontWeight);
                float bottomTolerance = fontSizeBase * 1.2f;

                bool isKeigakomi = block.BorderThickness.Top > 0 && block.BorderThickness.Bottom > 0 && block.BorderThickness.Left > 0 && block.BorderThickness.Right > 0;
                bool wasKeigakomi = pageBlocks.Count > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Top > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Bottom > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Left > 0 &&
                                    pageBlocks[pageBlocks.Count - 1].BorderThickness.Right > 0;

                if (isKeigakomi && !wasKeigakomi) blockHeight += 20f;
                if (!isKeigakomi && wasKeigakomi) blockHeight += 20f + fontSizeBase * 2.1f;

                if (pageBlocks.Count > 0 && usedHeight + blockHeight > context.AvailableHeight + bottomTolerance)
                {
                    break;
                }

                var blockCopy = AozoraParserService.CloneBlockProperties(block, true);
                pageBlocks.Add(blockCopy);
                usedHeight += blockHeight;

                if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                {
                    currentMergedBlock = blockCopy;
                    currentMergedBlockHeight = blockHeight;
                }
                else
                {
                    currentMergedBlock = null;
                }

                index++;
                if (usedHeight >= context.AvailableHeight + bottomTolerance) break;
            }

            return pageBlocks;
        }

        public List<AozoraBindingModel> PaginateVerticalPage(
            ref int index,
            List<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context)
        {
            var pageBlocks = new List<AozoraBindingModel>();
            float usedWidth = 0;

            AozoraBindingModel? currentMergedBlock = null;
            float currentMergedBlockWidth = 0;

            while (index < blocks.Count)
            {
                var block = blocks[index];

                if (pageBlocks.Count == 0 && block.IsBlankLine && !block.HasImage && !block.IsPageBreak)
                {
                    index++;
                    continue;
                }

                if (block.HasImage || block.IsPageBreak)
                {
                    if (pageBlocks.Count > 0) break;

                    if (block.IsPageBreak)
                    {
                        index++;
                        continue;
                    }

                    var aozoraImg = block.Inlines.OfType<AozoraImage>().FirstOrDefault();
                    if (aozoraImg != null && !context.ImageExists(aozoraImg.Source))
                    {
                        index++;
                        continue;
                    }

                    pageBlocks.Add(block);
                    index++;
                    currentMergedBlock = null;

                    if (context.ImagePairingEnabled && aozoraImg != null && context.ShouldPairImage(aozoraImg.Source))
                    {
                        while (index < blocks.Count)
                        {
                            var nextBlock = blocks[index];
                            if (nextBlock.HasImage)
                            {
                                var nextImg = nextBlock.Inlines.OfType<AozoraImage>().FirstOrDefault();
                                if (nextImg != null && context.ImageExists(nextImg.Source) && context.ShouldPairImage(nextImg.Source))
                                {
                                    pageBlocks.Add(nextBlock);
                                    index++;
                                }
                                break;
                            }

                            bool isWhitespace = nextBlock.Inlines.All(inline =>
                                inline is string s && string.IsNullOrWhiteSpace(s) || inline is AozoraLineBreak);

                            if (isWhitespace)
                            {
                                index++;
                                continue;
                            }

                            break;
                        }
                    }

                    break;
                }

                if (block.IsParagraphContinuation && currentMergedBlock != null && !block.IsTable && block.HeadingLevel == 0)
                {
                    var tempMerged = AozoraParserService.CloneBlockProperties(currentMergedBlock, true);
                    tempMerged.Inlines.AddRange(block.Inlines);

                    float fontSize = (float)(context.FontSize * tempMerged.FontSizeScale);
                    float newWidth = _measurer.MeasureVerticalBlockWidth(
                        context.Device,
                        tempMerged,
                        context.AvailableHeight,
                        fontSize,
                        context.DefaultFontFamily,
                        context.GetFontWeight);
                    float widthDiff = newWidth - currentMergedBlockWidth;
                    float leftToleranceMerged = fontSize * 0.8f;

                    if (usedWidth + widthDiff > context.AvailableWidth + leftToleranceMerged && pageBlocks.Count > 0)
                    {
                        break;
                    }

                    pageBlocks[pageBlocks.Count - 1] = tempMerged;
                    currentMergedBlock = tempMerged;
                    usedWidth += widthDiff;
                    currentMergedBlockWidth = newWidth;
                    index++;
                    continue;
                }

                float fontSizeBase = (float)(context.FontSize * block.FontSizeScale);
                float blockWidth = _measurer.MeasureVerticalBlockWidth(
                    context.Device,
                    block,
                    context.AvailableHeight,
                    fontSizeBase,
                    context.DefaultFontFamily,
                    context.GetFontWeight);
                float leftTolerance = fontSizeBase * 0.8f;

                bool isKeigakomi = block.BorderColor != null || block.BorderThickness.Top > 0;
                bool wasKeigakomi = pageBlocks.Count > 0 &&
                                    (pageBlocks[pageBlocks.Count - 1].BorderColor != null ||
                                     pageBlocks[pageBlocks.Count - 1].BorderThickness.Top > 0);

                if (isKeigakomi && !wasKeigakomi) blockWidth += 20f;
                if (!isKeigakomi && wasKeigakomi) blockWidth += 20f;

                if (pageBlocks.Count > 0 && usedWidth + blockWidth > context.AvailableWidth + leftTolerance)
                {
                    break;
                }

                var blockCopy = AozoraParserService.CloneBlockProperties(block, true);
                pageBlocks.Add(blockCopy);
                usedWidth += blockWidth;

                if (!block.IsTable && !block.IsPageBreak && block.HeadingLevel == 0)
                {
                    currentMergedBlock = blockCopy;
                    currentMergedBlockWidth = blockWidth;
                }
                else
                {
                    currentMergedBlock = null;
                }

                index++;
                if (usedWidth >= context.AvailableWidth + leftTolerance) break;
            }

            return pageBlocks;
        }
    }

    public sealed class AozoraBlockPaginationContext
    {
        public AozoraBlockPaginationContext(
            CanvasDevice? device,
            float availableWidth,
            float availableHeight,
            double fontSize,
            string defaultFontFamily,
            Func<string, Windows.UI.Text.FontWeight> getFontWeight,
            Func<string, bool> imageExists,
            bool imagePairingEnabled = false,
            Func<string, bool>? shouldPairImage = null)
        {
            Device = device;
            AvailableWidth = availableWidth;
            AvailableHeight = availableHeight;
            FontSize = fontSize;
            DefaultFontFamily = defaultFontFamily;
            GetFontWeight = getFontWeight;
            ImageExists = imageExists;
            ImagePairingEnabled = imagePairingEnabled;
            ShouldPairImage = shouldPairImage ?? (_ => false);
        }

        public CanvasDevice? Device { get; }
        public float AvailableWidth { get; }
        public float AvailableHeight { get; }
        public double FontSize { get; }
        public string DefaultFontFamily { get; }
        public Func<string, Windows.UI.Text.FontWeight> GetFontWeight { get; }
        public Func<string, bool> ImageExists { get; }
        public bool ImagePairingEnabled { get; }
        public Func<string, bool> ShouldPairImage { get; }
    }
}
