using Microsoft.Graphics.Canvas;
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
            }
        }

        float total = preferred.Sum();
        if (total <= availableWidth) return preferred;
        float minimum = Math.Min(fontSize * 2 + 20, availableWidth / columns);
        float extra = Math.Max(0, availableWidth - minimum * columns);
        float demand = preferred.Sum(w => Math.Max(0, w - minimum));
        return preferred.Select(w => minimum + (demand > 0 ? extra * Math.Max(0, w - minimum) / demand : extra / columns)).ToArray();
    }
}
