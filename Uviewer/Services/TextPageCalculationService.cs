using Microsoft.UI.Xaml.Controls;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextPageCalculationService
    {
        public async Task<bool> CalculateAsync(
            TextReaderState state,
            ScrollViewer scrollViewer,
            double fontSize,
            string fontFamily,
            CancellationToken token)
        {
            double viewportWidth = GetViewportWidth(scrollViewer);
            double viewportHeight = GetViewportHeight(scrollViewer);

            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                await Task.Delay(100, token);
                viewportWidth = GetViewportWidth(scrollViewer);
                viewportHeight = GetViewportHeight(scrollViewer);
                if (viewportWidth <= 0 || viewportHeight <= 0) return false;
            }

            if (state.Lines.Count == 0) return false;

            var result = await TextPaginationCalculator.CalculatePagesAsync(
                state.Lines,
                viewportWidth,
                viewportHeight,
                (float)fontSize,
                fontFamily,
                token);

            if (result == null) return false;

            state.CompletePageCalculation(result);
            return true;
        }

        public void CompleteFallback(TextReaderState state, ScrollViewer scrollViewer)
        {
            state.CompletePageCalculationFallback(scrollViewer.ExtentHeight);
        }

        private static double GetViewportWidth(ScrollViewer scrollViewer)
        {
            return scrollViewer.ViewportWidth > 0
                ? scrollViewer.ViewportWidth
                : scrollViewer.ActualWidth;
        }

        private static double GetViewportHeight(ScrollViewer scrollViewer)
        {
            return scrollViewer.ViewportHeight > 0
                ? scrollViewer.ViewportHeight
                : scrollViewer.ActualHeight;
        }
    }
}
