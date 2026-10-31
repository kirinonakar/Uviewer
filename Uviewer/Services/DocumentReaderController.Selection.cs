using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    /// <summary>
    /// 텍스트 뷰어(일반 텍스트 / 아오자라 / 세로쓰기)의 Ctrl+드래그 텍스트 선택 기능입니다.
    /// 선택이 끝나면 선택한 텍스트를 자동으로 클립보드에 복사합니다.
    /// </summary>
    internal sealed partial class DocumentReaderController
    {
        private CanvasTextGeometry? _aozoraSelectionGeometry;
        private CanvasTextGeometry? _verticalSelectionGeometry;
        private readonly CanvasTextSelectionState _aozoraSelection = new();
        private readonly CanvasTextSelectionState _verticalSelection = new();

        private readonly PlainTextSelectionState _plainTextSelection = new();
        private readonly PlainTextSelectionPresenter _plainTextSelectionPresenter = new();

        // --- 캔버스(아오자라/세로쓰기) 공통 처리 ---

        internal bool TryBeginAozoraTextSelection(PointerRoutedEventArgs e) =>
            TryBeginCanvasTextSelection(e, AozoraTextCanvas, _aozoraSelection, _aozoraSelectionGeometry);

        internal void AozoraTextCanvas_PointerMoved(object sender, PointerRoutedEventArgs e) =>
            UpdateCanvasTextSelection(e, AozoraTextCanvas, _aozoraSelection, _aozoraSelectionGeometry);

        internal void AozoraTextCanvas_PointerReleased(object sender, PointerRoutedEventArgs e) =>
            EndCanvasTextSelection(e, AozoraTextCanvas, _aozoraSelection, _aozoraSelectionGeometry);

        internal bool TryBeginVerticalTextSelection(PointerRoutedEventArgs e) =>
            TryBeginCanvasTextSelection(e, VerticalTextCanvas, _verticalSelection, _verticalSelectionGeometry);

        internal void VerticalTextCanvas_PointerMoved(object sender, PointerRoutedEventArgs e) =>
            UpdateCanvasTextSelection(e, VerticalTextCanvas, _verticalSelection, _verticalSelectionGeometry);

        internal void VerticalTextCanvas_PointerReleased(object sender, PointerRoutedEventArgs e) =>
            EndCanvasTextSelection(e, VerticalTextCanvas, _verticalSelection, _verticalSelectionGeometry);

        private bool TryBeginCanvasTextSelection(
            PointerRoutedEventArgs e,
            CanvasControl? canvas,
            CanvasTextSelectionState state,
            CanvasTextGeometry? geometry)
        {
            if (canvas == null) return false;
            if (!CanvasTextSelectionHelper.IsControlKeyDown()) return false;
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return false;

            var point = e.GetCurrentPoint(canvas);
            if (!point.Properties.IsLeftButtonPressed) return false;

            state.Reset();
            state.PageToken = geometry?.PageToken;
            state.PointerId = e.Pointer.PointerId;
            state.IsDragging = true;

            if (geometry != null &&
                geometry.TryHitTest(new Vector2((float)point.Position.X, (float)point.Position.Y), out var caret))
            {
                state.Anchor = caret;
                state.Focus = caret;
            }

            // Markdown's drawing canvas is transparent to input so its underlying
            // ScrollViewer can pan. Capture on the parent for Ctrl+drag selection.
            UIElement captureTarget = IsMarkdownScrollMode && ReferenceEquals(canvas, AozoraTextCanvas) ? TextArea : canvas;
            captureTarget.CapturePointer(e.Pointer);
            canvas.Invalidate();
            return true;
        }

        private void UpdateCanvasTextSelection(
            PointerRoutedEventArgs e,
            CanvasControl? canvas,
            CanvasTextSelectionState state,
            CanvasTextGeometry? geometry)
        {
            if (canvas == null || geometry == null) return;
            if (!state.IsDragging || state.PointerId != e.Pointer.PointerId) return;

            e.Handled = true;
            if (!state.Anchor.HasValue) return;

            var point = e.GetCurrentPoint(canvas);
            if (!point.Properties.IsLeftButtonPressed) return;

            if (!geometry.TryHitTest(new Vector2((float)point.Position.X, (float)point.Position.Y), out var caret)) return;
            if (state.Focus.HasValue && state.Focus.Value.Equals(caret)) return;

            state.Focus = caret;
            canvas.Invalidate();
        }

        private void EndCanvasTextSelection(
            PointerRoutedEventArgs e,
            CanvasControl? canvas,
            CanvasTextSelectionState state,
            CanvasTextGeometry? geometry)
        {
            if (canvas == null) return;
            if (!state.IsDragging || state.PointerId != e.Pointer.PointerId) return;

            state.IsDragging = false;
            UIElement captureTarget = IsMarkdownScrollMode && ReferenceEquals(canvas, AozoraTextCanvas) ? TextArea : canvas;
            captureTarget.ReleasePointerCaptures();
            e.Handled = true;

            if (!state.HasSelection || geometry == null) return;

            string text = CanvasTextSelectionHelper.ExtractText(geometry.Blocks, state.Anchor!.Value, state.Focus!.Value);
            if (string.IsNullOrWhiteSpace(text)) return;

            if (TextSelectionClipboard.TryCopy(text))
            {
                ShowNotification(Strings.TextSelectionCopied, "\uE8C8", "Gold");
            }
        }

        private void ClearCanvasSelectionGeometry(ref CanvasTextGeometry? slot, CanvasTextSelectionState state)
        {
            var previous = slot;
            slot = null;
            previous?.Dispose();
            state.Reset();
        }

        internal void ClearAozoraSelection() =>
            ClearCanvasSelectionGeometry(ref _aozoraSelectionGeometry, _aozoraSelection);

        internal void ClearVerticalSelection() =>
            ClearCanvasSelectionGeometry(ref _verticalSelectionGeometry, _verticalSelection);

        // --- 일반 텍스트(TextBlock) 처리 ---

        internal bool TryBeginPlainTextSelection(PointerRoutedEventArgs e)
        {
            if (_isAozoraMode || _isVerticalMode || !_isTextMode) return false;
            if (!CanvasTextSelectionHelper.IsControlKeyDown()) return false;
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return false;

            var point = e.GetCurrentPoint(TextArea);
            if (!point.Properties.IsLeftButtonPressed) return false;

            _plainTextSelection.Reset();
            _plainTextSelection.PointerId = e.Pointer.PointerId;
            _plainTextSelection.IsDragging = true;
            _plainTextSelection.Generation = _textContentLoadGeneration;

            if (PlainTextSelectionHitTester.TryHitTest(
                    TextItemsRepeater,
                    _textLines,
                    e.GetCurrentPoint(TextItemsRepeater).Position,
                    GetFontWeightForFamily,
                    out int lineIndex,
                    out int charIndex))
            {
                _plainTextSelection.AnchorLine = lineIndex;
                _plainTextSelection.AnchorChar = charIndex;
                _plainTextSelection.FocusLine = lineIndex;
                _plainTextSelection.FocusChar = charIndex;
            }

            TextArea.CapturePointer(e.Pointer);
            _plainTextSelectionPresenter.Refresh(TextItemsRepeater, _textLines, _plainTextSelection);
            return true;
        }

        internal void TextArea_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (IsMarkdownScrollMode)
            {
                UpdateCanvasTextSelection(e, AozoraTextCanvas, _aozoraSelection, _aozoraSelectionGeometry);
                return;
            }
            if (!_plainTextSelection.IsDragging || _plainTextSelection.PointerId != e.Pointer.PointerId) return;

            e.Handled = true;
            if (!IsPlainTextSelectionCurrent()) return;

            if (!PlainTextSelectionHitTester.TryHitTest(
                    TextItemsRepeater,
                    _textLines,
                    e.GetCurrentPoint(TextItemsRepeater).Position,
                    GetFontWeightForFamily,
                    out int lineIndex,
                    out int charIndex))
            {
                return;
            }

            if (lineIndex == _plainTextSelection.FocusLine && charIndex == _plainTextSelection.FocusChar) return;

            _plainTextSelection.FocusLine = lineIndex;
            _plainTextSelection.FocusChar = charIndex;
            _plainTextSelectionPresenter.Refresh(TextItemsRepeater, _textLines, _plainTextSelection);
        }

        internal void TextArea_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (IsMarkdownScrollMode)
            {
                EndCanvasTextSelection(e, AozoraTextCanvas, _aozoraSelection, _aozoraSelectionGeometry);
                return;
            }
            if (!_plainTextSelection.IsDragging || _plainTextSelection.PointerId != e.Pointer.PointerId) return;

            _plainTextSelection.IsDragging = false;
            TextArea.ReleasePointerCaptures();
            e.Handled = true;

            if (!IsPlainTextSelectionCurrent()) return;

            string text = PlainTextSelectionHitTester.ExtractText(_textLines, _plainTextSelection);
            if (string.IsNullOrWhiteSpace(text)) return;

            if (TextSelectionClipboard.TryCopy(text))
            {
                ShowNotification(Strings.TextSelectionCopied, "\uE8C8", "Gold");
            }
        }

        internal void ClearPlainTextSelection()
        {
            _plainTextSelection.Reset();
            _plainTextSelectionPresenter.Clear();
        }

        private bool IsPlainTextSelectionCurrent()
        {
            if (_plainTextSelection.Generation == _textContentLoadGeneration) return true;

            ClearPlainTextSelection();
            return false;
        }

        /// <summary>가상화된 TextBlock에 현재 선택 하이라이트를 적용합니다.</summary>
        internal void ApplyPlainTextSelectionHighlight(TextBlock textBlock, int lineIndex, TextLine line)
        {
            _plainTextSelectionPresenter.ForgetElement(textBlock);
            if (!IsPlainTextSelectionCurrent()) return;

            _plainTextSelectionPresenter.ApplyToElement(textBlock, lineIndex, line, _plainTextSelection);
        }
    }
}
