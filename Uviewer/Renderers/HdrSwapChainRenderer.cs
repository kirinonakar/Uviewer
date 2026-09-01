using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Uviewer.Models;
using Uviewer.Services;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace Uviewer.Renderers
{
    /// <summary>
    /// Presents linear scRGB bitmaps through an FP16 flip-model swap chain. A
    /// CanvasControl is always 8-bit, so it cannot carry values above SDR white.
    /// </summary>
    internal sealed class HdrSwapChainRenderer : IDisposable
    {
        private readonly Dictionary<CanvasSwapChainPanel, SurfaceState> _surfaces = new();

        public bool DrawMain(
            CanvasSwapChainPanel panel,
            Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sizingCanvas,
            CanvasBitmap? bitmap,
            bool isHdrOutputActive,
            IReadOnlyList<ImageEntry> imageEntries,
            ImageCacheManager imageCache,
            int currentIndex,
            double zoomLevel,
            bool isCurrentViewSideBySide,
            bool sharpenEnabled,
            bool preferAnimationSpeed,
            double panX,
            ref double panY)
        {
            if (!Prepare(panel, sizingCanvas, bitmap, isHdrOutputActive, out var swapChain)) return false;

            try
            {
                using (var ds = swapChain.CreateDrawingSession(Microsoft.UI.Colors.Black))
                {
                    ImageCanvasRenderer.DrawMainSurface(
                        ds,
                        sizingCanvas.Size,
                        bitmap,
                        imageEntries,
                        imageCache,
                        currentIndex,
                        zoomLevel,
                        isPdfMode: false,
                        isCurrentViewSideBySide,
                        sharpenEnabled,
                        preferAnimationSpeed,
                        panX,
                        ref panY);
                }

                swapChain.Present(1);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HDR swap-chain draw failed: {ex.Message}");
                panel.Visibility = Visibility.Collapsed;
                return false;
            }
        }

        public bool DrawSide(
            CanvasSwapChainPanel panel,
            Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sizingCanvas,
            CanvasBitmap? bitmap,
            bool isHdrOutputActive,
            double zoomLevel,
            bool alignRight)
        {
            if (!Prepare(panel, sizingCanvas, bitmap, isHdrOutputActive, out var swapChain)) return false;

            try
            {
                using (var ds = swapChain.CreateDrawingSession(Microsoft.UI.Colors.Black))
                {
                    ImageCanvasRenderer.DrawSideSurface(
                        ds,
                        sizingCanvas.Size,
                        bitmap,
                        zoomLevel,
                        alignRight);
                }

                swapChain.Present(1);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HDR side-by-side draw failed: {ex.Message}");
                panel.Visibility = Visibility.Collapsed;
                return false;
            }
        }

        public void Hide(CanvasSwapChainPanel panel)
        {
            panel.Visibility = Visibility.Collapsed;
        }

        private bool Prepare(
            CanvasSwapChainPanel panel,
            Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sizingCanvas,
            CanvasBitmap? bitmap,
            bool isHdrOutputActive,
            out CanvasSwapChain swapChain)
        {
            swapChain = null!;
            if (!isHdrOutputActive || !HdrImageDecoder.IsHdrBitmap(bitmap) ||
                sizingCanvas.Size.Width <= 0 || sizingCanvas.Size.Height <= 0)
            {
                panel.Visibility = Visibility.Collapsed;
                return false;
            }

            try
            {
                var device = bitmap!.Device ?? sizingCanvas.Device ?? CanvasDevice.GetSharedDevice();
                if (!device.IsPixelFormatSupported(DirectXPixelFormat.R16G16B16A16Float))
                {
                    panel.Visibility = Visibility.Collapsed;
                    return false;
                }

                float width = Math.Max(1, (float)sizingCanvas.Size.Width);
                float height = Math.Max(1, (float)sizingCanvas.Size.Height);
                float dpi = Math.Max(1, sizingCanvas.Dpi);

                if (!_surfaces.TryGetValue(panel, out var state) || state.Device != device)
                {
                    ReleaseSurface(panel);
                    swapChain = new CanvasSwapChain(
                        device,
                        width,
                        height,
                        dpi,
                        DirectXPixelFormat.R16G16B16A16Float,
                        2,
                        CanvasAlphaMode.Ignore);
                    panel.SwapChain = swapChain;
                    _surfaces[panel] = new SurfaceState(device, swapChain, width, height, dpi);
                }
                else
                {
                    swapChain = state.SwapChain;
                    if (Math.Abs(state.Width - width) > 0.5f ||
                        Math.Abs(state.Height - height) > 0.5f ||
                        Math.Abs(state.Dpi - dpi) > 0.1f)
                    {
                        swapChain.ResizeBuffers(
                            width,
                            height,
                            dpi,
                            DirectXPixelFormat.R16G16B16A16Float,
                            2);
                        _surfaces[panel] = state with { Width = width, Height = height, Dpi = dpi };
                    }
                }

                panel.Visibility = Visibility.Visible;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HDR swap-chain creation failed: {ex.Message}");
                panel.Visibility = Visibility.Collapsed;
                return false;
            }
        }

        private void ReleaseSurface(CanvasSwapChainPanel panel)
        {
            if (_surfaces.Remove(panel, out var state))
            {
                try { panel.SwapChain = null; } catch { }
                try { state.SwapChain.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            foreach (var panel in new List<CanvasSwapChainPanel>(_surfaces.Keys))
                ReleaseSurface(panel);
        }

        private sealed record SurfaceState(
            CanvasDevice Device,
            CanvasSwapChain SwapChain,
            float Width,
            float Height,
            float Dpi);
    }
}
