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
    /// Presents animations independently of CanvasControl's XAML refresh timer,
    /// and preserves extended-range HDR pixels through an FP16 swap chain.
    /// </summary>
    internal sealed class ImageSwapChainRenderer : IDisposable
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
            Windows.UI.Color backgroundColor,
            double panX,
            ref double panY)
        {
            if (!Prepare(panel, sizingCanvas, bitmap, isHdrOutputActive, preferAnimationSpeed, out var swapChain)) return false;

            try
            {
                using (var ds = swapChain.CreateDrawingSession(
                    swapChain.Format == DirectXPixelFormat.R16G16B16A16Float
                        ? Microsoft.UI.Colors.Black : backgroundColor))
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

                // The media clock already paces animation. Do not add another
                // vertical-blank wait or queue old frames behind a delayed one.
                swapChain.Present(preferAnimationSpeed ? 0 : 1);
                panel.Visibility = Visibility.Visible;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image swap-chain draw failed: {ex.Message}");
                Hide(panel);
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
            if (!Prepare(panel, sizingCanvas, bitmap, isHdrOutputActive, false, out var swapChain)) return false;

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
                panel.Visibility = Visibility.Visible;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HDR side-by-side draw failed: {ex.Message}");
                Hide(panel);
                return false;
            }
        }

        public void Hide(CanvasSwapChainPanel panel)
        {
            panel.Visibility = Visibility.Collapsed;
            ReleaseSurface(panel);
        }

        public void ClearAndRelease(Windows.UI.Color backgroundColor)
        {
            // XAML can defer a panel detach while its window is hidden. Replace
            // the last submitted image before hiding so a retained compositor
            // surface contains only the background when the window is restored.
            foreach (var panel in new List<CanvasSwapChainPanel>(_surfaces.Keys))
            {
                var state = _surfaces[panel];
                try
                {
                    using (state.SwapChain.CreateDrawingSession(
                        state.Format == DirectXPixelFormat.R16G16B16A16Float
                            ? Microsoft.UI.Colors.Black : backgroundColor))
                    {
                    }
                    state.SwapChain.Present(0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Image swap-chain clear failed: {ex.Message}");
                }
                finally
                {
                    Hide(panel);
                }
            }
        }

        private bool Prepare(
            CanvasSwapChainPanel panel,
            Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sizingCanvas,
            CanvasBitmap? bitmap,
            bool isHdrOutputActive,
            bool isAnimated,
            out CanvasSwapChain swapChain)
        {
            swapChain = null!;
            bool isHdr = isHdrOutputActive && HdrImageDecoder.IsHdrBitmap(bitmap);
            if (bitmap == null || (!isHdr && !isAnimated) ||
                sizingCanvas.Visibility != Visibility.Visible ||
                sizingCanvas.Size.Width <= 0 || sizingCanvas.Size.Height <= 0)
            {
                Hide(panel);
                return false;
            }

            try
            {
                var device = bitmap!.Device ?? sizingCanvas.Device ?? CanvasDevice.GetSharedDevice();
                var format = isHdr ? DirectXPixelFormat.R16G16B16A16Float
                    : DirectXPixelFormat.B8G8R8A8UIntNormalized;
                if (!device.IsPixelFormatSupported(format))
                {
                    Hide(panel);
                    return false;
                }

                float width = Math.Max(1, (float)sizingCanvas.Size.Width);
                float height = Math.Max(1, (float)sizingCanvas.Size.Height);
                float dpi = Math.Max(1, sizingCanvas.Dpi);

                if (!_surfaces.TryGetValue(panel, out var state) || state.Device != device || state.Format != format)
                {
                    ReleaseSurface(panel);
                    swapChain = new CanvasSwapChain(
                        device,
                        width,
                        height,
                        dpi,
                        format,
                        2,
                        CanvasAlphaMode.Ignore);
                    // Track before assigning the panel so an attachment failure
                    // also releases the newly allocated buffers.
                    _surfaces[panel] = new SurfaceState(device, swapChain, format, width, height, dpi);
                    panel.SwapChain = swapChain;
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
                            format,
                            2);
                        _surfaces[panel] = state with { Width = width, Height = height, Dpi = dpi };
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image swap-chain creation failed: {ex.Message}");
                Hide(panel);
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
                Hide(panel);
        }

        private sealed record SurfaceState(
            CanvasDevice Device,
            CanvasSwapChain SwapChain,
            DirectXPixelFormat Format,
            float Width,
            float Height,
            float Dpi);
    }
}
