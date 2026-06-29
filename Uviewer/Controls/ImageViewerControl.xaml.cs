using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.Foundation;

namespace Uviewer.Controls
{
    public sealed partial class ImageViewerControl : UserControl
    {
        internal event SizeChangedEventHandler? ImageAreaSizeChanged;
        internal event PointerEventHandler? ImageAreaPointerWheelChanged;
        internal event PointerEventHandler? ImageAreaPointerPressed;
        internal event ManipulationStartingEventHandler? ImageAreaManipulationStarting;
        internal event ManipulationDeltaEventHandler? ImageAreaManipulationDelta;
        internal event ManipulationCompletedEventHandler? ImageAreaManipulationCompleted;
        internal event RoutedEventHandler? OpenFileRequested;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? MainCanvasCreateResources;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? MainCanvasDraw;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? LeftCanvasCreateResources;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? LeftCanvasDraw;
        internal event TypedEventHandler<CanvasControl, CanvasCreateResourcesEventArgs>? RightCanvasCreateResources;
        internal event TypedEventHandler<CanvasControl, CanvasDrawEventArgs>? RightCanvasDraw;

        public ImageViewerControl()
        {
            InitializeComponent();
        }

        internal Grid ImageAreaElement => ImageArea;
        internal StackPanel EmptyStatePanelElement => EmptyStatePanel;
        internal Grid FastNavOverlayElement => FastNavOverlay;
        internal TextBlock FastNavTextElement => FastNavText;
        internal CanvasControl MainCanvasElement => MainCanvas;
        internal Grid SideBySideGridElement => SideBySideGrid;
        internal CanvasControl LeftCanvasElement => LeftCanvas;
        internal CanvasControl RightCanvasElement => RightCanvas;

        private void ImageArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
            ImageAreaSizeChanged?.Invoke(sender, e);

        private void ImageArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            ImageAreaPointerWheelChanged?.Invoke(sender, e);

        private void ImageArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            ImageAreaPointerPressed?.Invoke(sender, e);

        private void ImageArea_ManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e) =>
            ImageAreaManipulationStarting?.Invoke(sender, e);

        private void ImageArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) =>
            ImageAreaManipulationDelta?.Invoke(sender, e);

        private void ImageArea_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) =>
            ImageAreaManipulationCompleted?.Invoke(sender, e);

        private void OpenFileButton_Click(object sender, RoutedEventArgs e) =>
            OpenFileRequested?.Invoke(sender, e);

        private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            MainCanvasCreateResources?.Invoke(sender, args);

        private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            MainCanvasDraw?.Invoke(sender, args);

        private void LeftCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            LeftCanvasCreateResources?.Invoke(sender, args);

        private void LeftCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            LeftCanvasDraw?.Invoke(sender, args);

        private void RightCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
            RightCanvasCreateResources?.Invoke(sender, args);

        private void RightCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
            RightCanvasDraw?.Invoke(sender, args);
    }
}
