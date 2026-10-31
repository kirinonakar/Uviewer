using System;
using System.Collections.Generic;

namespace Uviewer.Services;

// Document coordinates stay in doubles; only the visible viewport is drawn by Win2D.
internal sealed class MarkdownScrollLayout
{
    private readonly double[] _tops;

    public MarkdownScrollLayout(IReadOnlyList<double> heights, double topMargin, double bottomMargin)
    {
        _tops = new double[heights.Count + 1];
        _tops[0] = topMargin;
        for (int i = 0; i < heights.Count; i++)
            _tops[i + 1] = _tops[i] + Math.Max(0, heights[i]);
        ExtentHeight = _tops[^1] + bottomMargin;
    }

    public int Count => _tops.Length - 1;
    public double ExtentHeight { get; }
    public double GetTop(int index) => _tops[Math.Clamp(index, 0, Count)];
    public double GetHeight(int index) => GetTop(index + 1) - GetTop(index);

    public static (double Width, double Height) FitImage(double width, double height, double availableWidth)
    {
        double scale = Math.Min(1, availableWidth / Math.Max(1, width));
        return (width * scale, height * scale);
    }

    public int FindBlock(double offset)
    {
        if (Count == 0) return 0;
        int left = 0, right = Count;
        while (left < right)
        {
            int middle = left + (right - left) / 2;
            if (_tops[middle + 1] <= offset) left = middle + 1;
            else right = middle;
        }
        return Math.Min(left, Count - 1);
    }
}
