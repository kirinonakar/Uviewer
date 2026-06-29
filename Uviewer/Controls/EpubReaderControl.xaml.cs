using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Uviewer.Controls
{
    public sealed partial class EpubReaderControl : UserControl
    {
        internal event SizeChangedEventHandler? EpubAreaSizeChanged;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? EpubTextCanvasCreateResources;
        internal event SizeChangedEventHandler? EpubTextCanvasSizeChanged;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? EpubTextCanvasDraw;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? EpubCanvasDisplayDraw;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? EpubCanvasDisplayLeftDraw;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? EpubCanvasDisplayRightDraw;
        internal event PointerEventHandler? EpubTouchOverlayPointerPressed;
        internal event PointerEventHandler? EpubTouchOverlayPointerWheelChanged;

        public EpubReaderControl()
        {
            InitializeComponent();
        }

        internal Grid EpubAreaElement => EpubArea;
        internal Grid EpubImageHostElement => EpubImageHost;
        internal Grid EpubTouchOverlayElement => EpubTouchOverlay;
        internal CanvasControl EpubTextCanvasElement => EpubTextCanvas;
        internal CanvasControl EpubCanvasDisplayElement => EpubCanvasDisplay;
        internal CanvasControl EpubCanvasDisplayLeftElement => EpubCanvasDisplayLeft;
        internal CanvasControl EpubCanvasDisplayRightElement => EpubCanvasDisplayRight;
        internal ColumnDefinition EpubImageLeftColumnElement => EpubImageLeftColumn;
        internal ColumnDefinition EpubImageRightColumnElement => EpubImageRightColumn;

        private void EpubArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            EpubAreaSizeChanged?.Invoke(sender, e);

        private void EpubTextCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            EpubTextCanvasCreateResources?.Invoke(sender, args);

        private void EpubTextCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
            EpubTextCanvasSizeChanged?.Invoke(sender, e);

        private void EpubTextCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            EpubTextCanvasDraw?.Invoke(sender, args);

        private void EpubCanvasDisplay_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            EpubCanvasDisplayDraw?.Invoke(sender, args);

        private void EpubCanvasDisplayLeft_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            EpubCanvasDisplayLeftDraw?.Invoke(sender, args);

        private void EpubCanvasDisplayRight_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            EpubCanvasDisplayRightDraw?.Invoke(sender, args);

        private void EpubTouchOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            EpubTouchOverlayPointerPressed?.Invoke(sender, e);

        private void EpubTouchOverlay_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            EpubTouchOverlayPointerWheelChanged?.Invoke(sender, e);
    }
}
