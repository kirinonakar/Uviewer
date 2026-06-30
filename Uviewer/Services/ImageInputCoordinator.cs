using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer.Services
{
    internal sealed class ImageInputCoordinator
    {
        private readonly IImageInputHost _host;
        private readonly Func<ImageViewportNavigationContext> _createNavigationContext;
        private readonly Func<bool, Task> _navigatePreviousAsync;
        private readonly Func<bool, Task> _navigateNextAsync;
        private readonly Action _applyZoom;

        public ImageInputCoordinator(
            IImageInputHost host,
            Func<ImageViewportNavigationContext> createNavigationContext,
            Func<bool, Task> navigatePreviousAsync,
            Func<bool, Task> navigateNextAsync,
            Action applyZoom)
        {
            _host = host;
            _createNavigationContext = createNavigationContext;
            _navigatePreviousAsync = navigatePreviousAsync;
            _navigateNextAsync = navigateNextAsync;
            _applyZoom = applyZoom;
        }

        public void ImageAreaSizeChanged(SizeChangedEventArgs e)
        {
            _host.LastCanvasWidth = e.NewSize.Width;

            if (_host.CurrentBitmap != null &&
                (_host.MainCanvas.Visibility == Visibility.Visible ||
                 _host.SideBySideGrid.Visibility == Visibility.Visible))
            {
                _applyZoom();
            }
        }

        public async Task HandlePointerWheelAsync(PointerRoutedEventArgs e)
        {
            try
            {
                var properties = e.GetCurrentPoint(_host.ImageArea).Properties;
                var wheelDelta = properties.MouseWheelDelta;
                var isHorizontal = properties.IsHorizontalMouseWheel;

                var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                if (ctrl.HasFlag(CoreVirtualKeyStates.Down))
                {
                    if (_host.CurrentBitmap != null && (!_host.IsCurrentViewSideBySide || _host.IsPdfMode))
                    {
                        double zoomMultiplier = Math.Exp(wheelDelta * 0.001);
                        var point = e.GetCurrentPoint(_host.ImageArea).Position;
                        _host.ImageViewportNavigationService.StartSmoothZoom(
                            _createNavigationContext(),
                            zoomMultiplier,
                            point);

                        e.Handled = true;
                        return;
                    }
                }

                if (_host.CurrentBitmap != null &&
                    (_host.IsPdfMode || (_host.ZoomLevel > 1.01 && !_host.IsCurrentViewSideBySide)))
                {
                    if (isHorizontal)
                    {
                        await HandleScrollAsync(wheelDelta, 0);
                    }
                    else
                    {
                        await HandleScrollAsync(0, wheelDelta);
                    }
                    e.Handled = true;
                    return;
                }

                if (Math.Abs(wheelDelta) >= 40)
                {
                    if (wheelDelta < 0) await _navigateNextAsync(false);
                    else await _navigatePreviousAsync(false);
                }

                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerWheelChanged: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ManipulationStarting(ManipulationStartingRoutedEventArgs e)
        {
            e.Container = _host.ImageArea;
            e.Mode = ManipulationModes.All;
        }

        public async Task ManipulationDeltaAsync(ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
                if (_host.CurrentBitmap == null || (_host.IsCurrentViewSideBySide && !_host.IsPdfMode)) return;

                if (e.Delta.Scale != 1.0f)
                {
                    _host.ImageViewportNavigationService.ZoomAtPosition(
                        _createNavigationContext(),
                        e.Delta.Scale,
                        e.Position);
                }

                await HandleScrollAsync(e.Delta.Translation.X, e.Delta.Translation.Y);
                e.Handled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_ManipulationDelta: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public void ManipulationCompleted()
        {
            _host.ImageViewportNavigationService.IsTransitioning = false;
            if (_host.IsPdfMode)
            {
                _ = _host.RerenderPdfCurrentPageAsync();
            }
        }

        public Task HandleScrollAsync(double deltaX, double deltaY) =>
            _host.ImageViewportNavigationService.HandleScrollAsync(
                _createNavigationContext(),
                deltaX,
                deltaY);

        public async Task PointerPressedAsync(PointerRoutedEventArgs e)
        {
            try
            {
                if (_host.ImageEntries.Count <= 1)
                    return;

                var point = e.GetCurrentPoint(_host.ImageArea);
                if (!point.Properties.IsLeftButtonPressed)
                    return;

                if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
                {
                    if (_host.ZoomLevel > 1.01 || _host.IsPdfMode)
                        return;
                }

                double half = _host.ImageArea.ActualWidth * 0.5;
                if (point.Position.X < half)
                {
                    if (_host.ShouldInvertControls) await _navigateNextAsync(true);
                    else await _navigatePreviousAsync(true);
                }
                else
                {
                    if (_host.ShouldInvertControls) await _navigatePreviousAsync(true);
                    else await _navigateNextAsync(true);
                }
                e.Handled = true;
                _host.FocusRoot();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ImageArea_PointerPressed: {ex.Message}");
                _host.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }
    }
}
