using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private bool _isVerticalMode = false;
        private List<string> _verticalPages = new();
        private int _currentVerticalPageIndex = 0;

        private void VerticalToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isVerticalMode = VerticalToggleButton.IsChecked ?? false;
            SaveTextSettings();
            ToggleVerticalMode();
        }

        private async void ToggleVerticalMode()
        {
            if (_isVerticalMode)
            {
                VerticalTextCanvas.Visibility = Visibility.Visible;
                TextScrollViewer.Visibility = Visibility.Collapsed;
                AozoraPageContainer.Visibility = Visibility.Collapsed;
                
                int currentLine = 1;
                if (_isAozoraMode) currentLine = _aozoraBlocks[_currentAozoraStartBlockIndex].SourceLineNumber;
                else if (TextScrollViewer != null) currentLine = GetTopVisibleLineIndex();

                await PrepareVerticalTextAsync(currentLine);
            }
            else
            {
                int currentLine = _verticalPageInfos.Count > 0 && _currentVerticalPageIndex < _verticalPageInfos.Count 
                    ? _verticalPageInfos[_currentVerticalPageIndex].StartLine : 1;

                VerticalTextCanvas.Visibility = Visibility.Collapsed;
                if (_isAozoraMode)
                {
                    AozoraPageContainer.Visibility = Visibility.Visible;
                    await PrepareAozoraDisplayAsync(_currentTextContent, currentLine);
                }
                else
                {
                    TextScrollViewer.Visibility = Visibility.Visible;
                    await LoadTextLinesProgressivelyAsync(_currentTextContent, currentLine);
                }
            }
            UpdateTextStatusBar();
        }

        private struct VerticalPageInfo
        {
            public string Content;
            public int StartLine;
        }
        private List<VerticalPageInfo> _verticalPageInfos = new();

        private async Task PrepareVerticalTextAsync(int targetLine = 1)
        {
            if (string.IsNullOrEmpty(_currentTextContent)) return;

            var lines = _currentTextContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            _verticalPageInfos.Clear();
            
            // Heuristic for pages: 25-30 lines per page depending on size
            int linesPerPage = 25; 
            if (_textFontSize < 16) linesPerPage = 35;
            else if (_textFontSize > 24) linesPerPage = 20;

            for (int i = 0; i < lines.Length; i += linesPerPage)
            {
                int count = Math.Min(linesPerPage, lines.Length - i);
                var pageLines = lines.Skip(i).Take(count);
                _verticalPageInfos.Add(new VerticalPageInfo {
                     Content = string.Join("\n", pageLines),
                     StartLine = i + 1
                });
            }

            // Restore position
            if (targetLine > 1)
            {
                // Find page that contains or is closest to targetLine
                int pageIndex = _verticalPageInfos.FindIndex(p => p.StartLine >= targetLine);
                if (pageIndex == -1) pageIndex = _verticalPageInfos.Count - 1;
                // If the found page starts AFTER targetLine, we might want the previous one if it exists
                if (pageIndex > 0 && _verticalPageInfos[pageIndex].StartLine > targetLine) pageIndex--;
                
                _currentVerticalPageIndex = Math.Max(0, pageIndex);
            }
            else if (targetLine < 0) // Legacy page support
            {
                _currentVerticalPageIndex = Math.Clamp(-targetLine, 0, _verticalPageInfos.Count - 1);
            }
            else
            {
                _currentVerticalPageIndex = 0;
            }
            
            if (_currentVerticalPageIndex >= _verticalPageInfos.Count)
                _currentVerticalPageIndex = Math.Max(0, _verticalPageInfos.Count - 1);

            VerticalTextCanvas.Invalidate();
            UpdateTextStatusBar();
        }

        private void VerticalTextCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
        }

        private void VerticalTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!_isVerticalMode || _verticalPageInfos.Count == 0 || _currentVerticalPageIndex >= _verticalPageInfos.Count) return;

            var ds = args.DrawingSession;
            var size = sender.Size;
            string pageText = _verticalPageInfos[_currentVerticalPageIndex].Content;

            using var textFormat = new CanvasTextFormat
            {
                FontSize = (float)_textFontSize,
                FontFamily = _textFontFamily,
                VerticalGlyphOrientation = CanvasVerticalGlyphOrientation.Default,
                Direction = CanvasTextDirection.TopToBottomThenRightToLeft,
                WordWrapping = CanvasWordWrapping.WholeWord
            };

            try 
            {
                using var textLayout = new CanvasTextLayout(ds, pageText, textFormat, (float)size.Width - 80, (float)size.Height - 80);
                Color textColor = GetVerticalTextColor();
                ds.DrawTextLayout(textLayout, 40, 40, textColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Vertical draw error: {ex.Message}");
            }
        }

        private Color GetVerticalTextColor()
        {
            if (_themeIndex == 2) return Colors.White; // Dark
            if (_themeIndex == 1) return Color.FromArgb(255, 60, 40, 20); // Sepia/Beige
            return Colors.Black; // Light
        }

        private void VerticalTextCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(VerticalTextCanvas).Position;
            var width = VerticalTextCanvas.ActualWidth;

            // Click left half to go forward (next page), right half to go backward
            if (pt.X < width / 2)
            {
                NavigateVerticalPage(1);
            }
            else
            {
                NavigateVerticalPage(-1);
            }
        }

        private void VerticalTextCanvas_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(VerticalTextCanvas).Properties.MouseWheelDelta;
            if (delta > 0) NavigateVerticalPage(-1);
            else NavigateVerticalPage(1);
        }

        private void NavigateVerticalPage(int direction)
        {
            int nextIndex = _currentVerticalPageIndex + direction;
            if (nextIndex >= 0 && nextIndex < _verticalPageInfos.Count)
            {
                _currentVerticalPageIndex = nextIndex;
                VerticalTextCanvas.Invalidate();
                UpdateTextStatusBar();
            }
        }

        private void UpdateVerticalStatusBar()
        {
            if (!_isVerticalMode || _verticalPageInfos.Count == 0) return;
            
            int totalPages = _verticalPageInfos.Count;
            int currentPage = _currentVerticalPageIndex + 1;
            int currentLine = _verticalPageInfos[_currentVerticalPageIndex].StartLine;
            int totalLines = _currentTextContent.Split('\n').Length;
            
            ImageInfoText.Text = $"Line {currentLine} / {totalLines}";
            ImageIndexText.Text = $"{currentPage} / {totalPages}";
            
            double progress = totalPages > 1 ? (double)_currentVerticalPageIndex / (totalPages - 1) * 100.0 : 100.0;
            if (progress > 100) progress = 100;
            TextProgressText.Text = $"{progress:F1}%";
        }

        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isVerticalMode)
            {
                _ = PrepareVerticalTextAsync();
            }
        }
    }
}
