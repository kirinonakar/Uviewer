using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.Foundation;

namespace Uviewer.Controls
{
    public sealed partial class TextReaderControl : UserControl
    {
        internal event TypedEventHandler<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs>? TextItemsRepeaterElementPrepared;
        internal event PointerEventHandler? TextAreaPointerPressed;
        internal event PointerEventHandler? TextAreaPointerWheelChanged;
        internal event SizeChangedEventHandler? TextAreaSizeChanged;
        internal event TypedEventHandler<ScrollViewer, ScrollViewerViewChangedEventArgs>? TextScrollViewerViewChanged;
        internal event SizeChangedEventHandler? TextScrollViewerSizeChanged;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? AozoraTextCanvasCreateResources;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? AozoraTextCanvasDraw;
        internal event PointerEventHandler? AozoraTextCanvasPointerPressed;
        internal event PointerEventHandler? AozoraTextCanvasPointerWheelChanged;
        internal event SizeChangedEventHandler? AozoraTextCanvasSizeChanged;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? VerticalTextCanvasCreateResources;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? VerticalTextCanvasDraw;
        internal event PointerEventHandler? VerticalTextCanvasPointerPressed;
        internal event PointerEventHandler? VerticalTextCanvasPointerWheelChanged;
        internal event SizeChangedEventHandler? VerticalTextCanvasSizeChanged;

        public TextReaderControl()
        {
            InitializeComponent();
        }

        internal Grid TextAreaElement => TextArea;
        internal ScrollViewer TextScrollViewerElement => TextScrollViewer;
        internal ItemsRepeater TextItemsRepeaterElement => TextItemsRepeater;
        internal CanvasControl AozoraTextCanvasElement => AozoraTextCanvas;
        internal Grid TextFastNavOverlayElement => TextFastNavOverlay;
        internal TextBlock TextFastNavTextElement => TextFastNavText;
        internal CanvasControl VerticalTextCanvasElement => VerticalTextCanvas;

        private void TextItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) =>
            TextItemsRepeaterElementPrepared?.Invoke(sender, args);

        private void TextArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            TextAreaPointerPressed?.Invoke(sender, e);

        private void TextArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            TextAreaPointerWheelChanged?.Invoke(sender, e);

        private void TextArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            TextAreaSizeChanged?.Invoke(sender, e);

        private void TextScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            TextScrollViewerViewChanged?.Invoke(TextScrollViewer, e);

        private void TextScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
            TextScrollViewerSizeChanged?.Invoke(sender, e);

        private void AozoraTextCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            AozoraTextCanvasCreateResources?.Invoke(sender, args);

        private void AozoraTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            AozoraTextCanvasDraw?.Invoke(sender, args);

        private void AozoraTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            AozoraTextCanvasPointerPressed?.Invoke(sender, e);

        private void AozoraTextCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            AozoraTextCanvasPointerWheelChanged?.Invoke(sender, e);

        private void AozoraTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            AozoraTextCanvasSizeChanged?.Invoke(sender, e);

        private void VerticalTextCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            VerticalTextCanvasCreateResources?.Invoke(sender, args);

        private void VerticalTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            VerticalTextCanvasDraw?.Invoke(sender, args);

        private void VerticalTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            VerticalTextCanvasPointerPressed?.Invoke(sender, e);

        private void VerticalTextCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            VerticalTextCanvasPointerWheelChanged?.Invoke(sender, e);

        private void VerticalTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            VerticalTextCanvasSizeChanged?.Invoke(sender, e);
    }
}
