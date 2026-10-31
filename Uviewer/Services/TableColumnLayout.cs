using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Uviewer.Models;
using Windows.UI.Text;

namespace Uviewer.Services;

internal static class TableColumnLayout
{
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<(float, float, string, ushort), float[]>> Cache = new();
    public static float[] Measure(ICanvasResourceCreator creator, AozoraBindingModel block, float availableWidth,
        float fontSize, string font, FontWeight weight, Func<AozoraImage, CanvasBitmap?>? getImage = null)
    {
        if (block.TableContentRows.SelectMany(row => row).SelectMany(cell => cell).Any(item => item is AozoraImage))
            return MeasureCore(creator, block, availableWidth, fontSize, font, weight, getImage);
        object identity = block.TableContentRows.Count > 0 ? block.TableContentRows : block.TableRows;
        var cache = Cache.GetOrCreateValue(identity);
        return cache.GetOrAdd((availableWidth, fontSize, font, weight.Weight), _ => MeasureCore(creator, block, availableWidth, fontSize, font, weight));
    }

    private static float[] MeasureCore(ICanvasResourceCreator creator, AozoraBindingModel block, float availableWidth,
        float fontSize, string font, FontWeight weight, Func<AozoraImage, CanvasBitmap?>? getImage = null)
    {
        int columns = block.TableRows[0].Count;
        if (columns == 0) return [];
        var preferred = Enumerable.Repeat(fontSize + 20, columns).ToArray();
        var minimum = Enumerable.Repeat(20f, columns).ToArray();
        int rowCount = Math.Max(1, block.TableContentRows.Count);
        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                IEnumerable<object> content = block.TableContentRows.Count > 0
                    ? block.TableContentRows[r][c] : TableCellLayout.GetCell(block, c);
                using var cell = new TableCellLayout(creator, content, getImage != null ? availableWidth : 100000, fontSize, font,
                    r == 0 ? Microsoft.UI.Text.FontWeights.Bold : weight, Colors.Black, wrap: false, getImage: getImage);
                preferred[c] = Math.Max(preferred[c], (float)cell.Layout.LayoutBounds.Width + 20);
                // Reserve the widest unbreakable word (with its actual styling),
                // rather than shrinking every column to the same two-em floor.
                // NoWrap is useful for max-content measurement, but min-content
                // must allow normal word boundaries before asking DirectWrite.
                cell.Layout.WordWrapping = CanvasWordWrapping.Wrap;
                minimum[c] = Math.Max(minimum[c], MathF.Ceiling(cell.Layout.GetMinimumLineLength()) + 20);
                preferred[c] = Math.Max(preferred[c], minimum[c]);
            }
        }

        float total = preferred.Sum();
        if (total <= availableWidth) return preferred;
        float minimumTotal = minimum.Sum();
        if (minimumTotal <= availableWidth)
        {
            float extra = availableWidth - minimumTotal;
            float demand = total - minimumTotal;
            return preferred.Select((width, c) => minimum[c] +
                (demand > 0 ? extra * (width - minimum[c]) / demand : extra / columns)).ToArray();
        }

        // Only fall back to character wrapping when even whole-word minimums
        // cannot fit in the viewport (for example, at very narrow window sizes).
        float floor = Math.Min(fontSize * 2 + 20, availableWidth / columns);
        float remaining = Math.Max(0, availableWidth - floor * columns);
        float shortage = minimum.Sum(width => Math.Max(0, width - floor));
        return minimum.Select(width => floor +
            (shortage > 0 ? remaining * Math.Max(0, width - floor) / shortage : remaining / columns)).ToArray();
    }
}
