using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public static class TextPaginationCalculator
    {
        /// <summary>
        /// Calculates pagination for a list of TextLine objects using Win2D for text measurement.
        /// Performs calculation in a background task to prevent UI blocking.
        /// </summary>
        public static async Task<PaginationResult?> CalculatePagesAsync(
            List<TextLine> linesToCalc,
            double viewportWidth,
            double viewportHeight,
            float fontSize,
            string fontFamily,
            CancellationToken token)
        {
            if (linesToCalc == null || linesToCalc.Count == 0) return null;

            return await Task.Run(() =>
            {
                int count = linesToCalc.Count;
                int[] pages = new int[count];
                double totalH = 0;
                double currentPageHeight = 0;
                int currentPage = 1;

                // LineHeight 일관성 유지: Math.Ceiling(line.FontSize * 1.8)
                double lineHeight = Math.Ceiling(fontSize * 1.8);

                // Win2D 기반 정밀 측정을 위해 리소스 생성 (백그라운드 스레드에서 활용 가능)
                var device = CanvasDevice.GetSharedDevice();
                using var format = new CanvasTextFormat
                {
                    FontSize = fontSize,
                    FontFamily = fontFamily,
                    WordWrapping = CanvasWordWrapping.Wrap
                };

                for (int i = 0; i < count; i++)
                {
                    if (token.IsCancellationRequested) return null;
                    var line = linesToCalc[i];
                    if (line == null) continue;

                    double lineH = lineHeight;
                    double maxWidth = line.MaxWidth > 0 ? line.MaxWidth : viewportWidth;

                    // Fast Path: 확실히 한 줄인 경우(짧은 줄) 측정을 생략하여 속도를 획기적으로 개선
                    bool mustMeasure = false;
                    if (line.Content.Length > 0)
                    {
                        // 영문/기호 기반: 한 글자 너비 최소 임계치(0.5 font size) 기준 체크
                        if (line.Content.Length * (fontSize * 0.5) > maxWidth)
                        {
                            mustMeasure = true;
                        }
                        // CJK 기반: 글자 너비가 크므로 별도 체크
                        else if (ContainsCJK(line.Content) && line.Content.Length * fontSize > maxWidth)
                        {
                            mustMeasure = true;
                        }
                    }

                    if (mustMeasure)
                    {
                        try
                        {
                            // Win2D 레이아웃 엔진으로 고속 정밀 측정 (TextBlock.Measure 대비 수십 배 빠름)
                            using var layout = new CanvasTextLayout(device, line.Content, format, (float)maxWidth, 0);
                            int lineCount = layout.LineCount;
                            lineH = lineCount * lineHeight;
                        }
                        catch { /* Fallback to single line */ }
                    }

                    if (currentPageHeight > 0 && currentPageHeight + lineH > viewportHeight)
                    {
                        currentPage++;
                        currentPageHeight = 0;
                    }

                    pages[i] = currentPage;
                    currentPageHeight += lineH;
                    totalH += lineH;
                }

                return new PaginationResult
                {
                    Pages = pages,
                    TotalPages = currentPage,
                    TotalHeight = totalH
                };
            }, token);
        }

        /// <summary>
        /// Detects if the string contains CJK characters.
        /// </summary>
        public static bool ContainsCJK(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= 0x2E80 && c <= 0x9FFF) return true; // Han
                if (c >= 0xAC00 && c <= 0xD7AF) return true; // Hangul
                if (c >= 0x3040 && c <= 0x30FF) return true; // Kana
            }
            return false;
        }
    }
}
