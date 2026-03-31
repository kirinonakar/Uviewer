using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class AnimatedWebpService : IAnimatedWebpService
    {
        private readonly ISharpeningService _sharpeningService;
        private readonly DispatcherQueue _dispatcherQueue;

        private DispatcherQueueTimer? _animatedWebpTimer;
        private List<byte[]>? _animatedWebpFramePixels;
        private List<int>? _animatedWebpDelaysMs;
        private int _animatedWebpFrameIndex;
        private int _animatedWebpWidth;
        private int _animatedWebpHeight;
        private const int DefaultWebpFrameDelayMs = 30;
        private volatile bool _isDecodingAnimatedImage = false;

        private readonly Dictionary<int, CanvasBitmap> _animatedWebpSharpenedCache = new();
        private CanvasControl? _currentCanvas;
        
        // Settings for sharpening (cached during animation)
        private bool _sharpenEnabled;
        private float _upscaleFactor;
        private float _sharpenAmountParam;
        private float _sharpenThresholdParam;
        private float _unsharpAmount;
        private float _unsharpRadius;

        public bool IsDecoding => _isDecodingAnimatedImage;

        public event EventHandler<CanvasBitmap>? FrameUpdated;
        public event EventHandler? AnimationStopped;

        public AnimatedWebpService(ISharpeningService sharpeningService, DispatcherQueue dispatcherQueue)
        {
            _sharpeningService = sharpeningService;
            _dispatcherQueue = dispatcherQueue;
        }

        public bool IsAnimationSupported(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey).ToLowerInvariant();

            // 압축 파일 내의 애니메이션은 재생하지 않음
            if (entry.IsArchiveEntry) return false;

            return ext == ".webp" || ext == ".gif";
        }

        public void Stop()
        {
            _isDecodingAnimatedImage = false; // 진행 중인 백그라운드 디코딩 중지
            
            _animatedWebpTimer?.Stop();
            _animatedWebpTimer = null;

            _animatedWebpFramePixels?.Clear();
            _animatedWebpFramePixels = null;
            _animatedWebpDelaysMs?.Clear();
            _animatedWebpDelaysMs = null;

            _animatedWebpWidth = 0;
            _animatedWebpHeight = 0;
            _animatedWebpFrameIndex = 0;

            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values)
                {
                    bmp.Dispose();
                }
                _animatedWebpSharpenedCache.Clear();
            }

            AnimationStopped?.Invoke(this, EventArgs.Empty);
        }

        public async Task StartAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token, 
            float upscaleFactor, float sharpenAmount, float sharpenThreshold, float unsharpAmount, float unsharpRadius, bool sharpenEnabled)
        {
            Stop();
            _currentCanvas = canvas;
            _upscaleFactor = upscaleFactor;
            _sharpenAmountParam = sharpenAmount;
            _sharpenThresholdParam = sharpenThreshold;
            _unsharpAmount = unsharpAmount;
            _unsharpRadius = unsharpRadius;
            _sharpenEnabled = sharpenEnabled;

            try
            {
                byte[]? imageBytes = null;
                if (entry.FilePath != null)
                {
                    imageBytes = await File.ReadAllBytesAsync(entry.FilePath, token);
                }

                if (imageBytes == null || token.IsCancellationRequested) return;

                var (framePixels, delaysMs, w, h) = await TryLoadAnimatedImageFramesNativeAsync(imageBytes);
                if (framePixels != null && !token.IsCancellationRequested)
                {
                    _animatedWebpFramePixels = framePixels;
                    _animatedWebpDelaysMs = delaysMs;
                    _animatedWebpWidth = w;
                    _animatedWebpHeight = h;

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            StartAnimatedWebpTimer();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting animated WebP: {ex.Message}");
            }
        }

        private void StartAnimatedWebpTimer()
        {
            if (_animatedWebpFramePixels == null || _animatedWebpDelaysMs == null || _animatedWebpFramePixels.Count == 0)
                return;

            _animatedWebpTimer = _dispatcherQueue.CreateTimer();
            _animatedWebpTimer.Interval = TimeSpan.FromMilliseconds(_animatedWebpDelaysMs[_animatedWebpFrameIndex]);
            _animatedWebpTimer.Tick += AnimatedWebpTimer_Tick;
            _animatedWebpTimer.Start();
        }

        private async void AnimatedWebpTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_animatedWebpFramePixels == null || _animatedWebpDelaysMs == null || _animatedWebpFramePixels.Count == 0 || _currentCanvas == null)
                return;

            var stopwatch = Stopwatch.StartNew();
            sender.Stop();

            try
            {
                if (_animatedWebpFrameIndex >= _animatedWebpFramePixels.Count)
                {
                    _animatedWebpFrameIndex = 0;
                }

                int nextIndex = _animatedWebpFrameIndex + 1;
                if (nextIndex >= _animatedWebpFramePixels.Count)
                {
                    if (_isDecodingAnimatedImage)
                    {
                        nextIndex = _animatedWebpFrameIndex; 
                    }
                    else
                    {
                        nextIndex = 0;
                    }
                }
                _animatedWebpFrameIndex = nextIndex;

                if (_currentCanvas.Device == null) return;

                CanvasBitmap? newBitmap = null;

                if (_sharpenEnabled)
                {
                    lock (_animatedWebpSharpenedCache)
                    {
                        if (_animatedWebpSharpenedCache.TryGetValue(_animatedWebpFrameIndex, out var cached))
                        {
                            newBitmap = cached;
                        }
                    }

                    if (newBitmap == null)
                    {
                        using var originalBitmap = CanvasBitmap.CreateFromBytes(
                            _currentCanvas,
                            _animatedWebpFramePixels[_animatedWebpFrameIndex],
                            _animatedWebpWidth,
                            _animatedWebpHeight,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized);

                        newBitmap = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);

                        if (_animatedWebpFramePixels == null || _currentCanvas.Device == null)
                        {
                            newBitmap?.Dispose();
                            return;
                        }

                        if (newBitmap != null)
                        {
                            lock (_animatedWebpSharpenedCache)
                            {
                                _animatedWebpSharpenedCache[_animatedWebpFrameIndex] = newBitmap;
                            }
                        }
                    }
                }

                if (newBitmap == null)
                {
                    newBitmap = CanvasBitmap.CreateFromBytes(
                        _currentCanvas,
                        _animatedWebpFramePixels[_animatedWebpFrameIndex],
                        _animatedWebpWidth,
                        _animatedWebpHeight,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized);
                }

                FrameUpdated?.Invoke(this, newBitmap);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Animation Error: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                if (_animatedWebpDelaysMs != null && _animatedWebpFrameIndex < _animatedWebpDelaysMs.Count)
                {
                    int targetDelay = _animatedWebpDelaysMs[_animatedWebpFrameIndex];
                    int adjustedDelay = Math.Max(1, targetDelay - (int)stopwatch.ElapsedMilliseconds);
                    sender.Interval = TimeSpan.FromMilliseconds(adjustedDelay);
                    sender.Start();
                }
            }
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            lock (_animatedWebpSharpenedCache)
            {
                return _animatedWebpSharpenedCache.ContainsValue(bitmap);
            }
        }

        private async Task<(List<byte[]>? framePixels, List<int>? delaysMs, int width, int height)> TryLoadAnimatedImageFramesNativeAsync(byte[] imageBytes)
        {
            try
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(imageBytes));
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.FrameCount <= 1) return (null, null, 0, 0);

                int w = (int)decoder.PixelWidth;
                int h = (int)decoder.PixelHeight;

                var framePixels = new List<byte[]>();
                var delaysMs = new List<int>();

                var device = CanvasDevice.GetSharedDevice();

                int initialDisposal = 0;
                Windows.Foundation.Rect initialRect = Windows.Foundation.Rect.Empty;

                using (var renderTarget = new CanvasRenderTarget(device, w, h, 96.0f))
                {
                    using (var ds = renderTarget.CreateDrawingSession())
                    {
                        ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    }
                    var (delay0, disposal0, rect0) = await DecodeAndDrawSingleFrameAsync(decoder, 0, renderTarget);
                    delaysMs.Add(delay0);
                    framePixels.Add(renderTarget.GetPixelBytes());

                    initialDisposal = disposal0;
                    initialRect = rect0;
                }

                _isDecodingAnimatedImage = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var bgRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f);
                        using var backupRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f);

                        using (var ds = backupRenderTarget.CreateDrawingSession())
                        {
                            ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                        }

                        using (var ds = bgRenderTarget.CreateDrawingSession())
                        {
                            ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                            using var bmp0 = CanvasBitmap.CreateFromBytes(device, framePixels[0], w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
                            ds.DrawImage(bmp0);
                        }

                        int previousDisposal = initialDisposal;
                        Windows.Foundation.Rect previousRect = initialRect;

                        for (uint i = 1; i < decoder.FrameCount; i++)
                        {
                            if (!_isDecodingAnimatedImage) break;

                            if (previousDisposal == 2)
                            {
                                using (var ds = bgRenderTarget.CreateDrawingSession())
                                {
                                    ds.Blend = CanvasBlend.Copy; 
                                    ds.FillRectangle(previousRect, Windows.UI.Color.FromArgb(0, 0, 0, 0));
                                }
                            }
                            else if (previousDisposal == 3)
                            {
                                using (var ds = bgRenderTarget.CreateDrawingSession())
                                {
                                    ds.Blend = CanvasBlend.Copy;
                                    ds.DrawImage(backupRenderTarget);
                                }
                            }

                            using (var ds = backupRenderTarget.CreateDrawingSession())
                            {
                                ds.Blend = CanvasBlend.Copy;
                                ds.DrawImage(bgRenderTarget);
                            }

                            var (delay, disposal, rect) = await DecodeAndDrawSingleFrameAsync(decoder, i, bgRenderTarget);
                            var pixels = bgRenderTarget.GetPixelBytes();

                            previousDisposal = disposal;
                            previousRect = rect;

                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (_isDecodingAnimatedImage)
                                {
                                    delaysMs.Add(delay);
                                    framePixels.Add(pixels);
                                }
                            });
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"Bg Decode Error: {ex.Message}"); }
                    finally { _isDecodingAnimatedImage = false; }
                });

                return (framePixels, delaysMs, w, h);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Native Decode Error: {ex.Message}");
                return (null, null, 0, 0);
            }
        }

        private async Task<(int delayMs, int disposal, Windows.Foundation.Rect frameRect)> DecodeAndDrawSingleFrameAsync(
            BitmapDecoder decoder, 
            uint frameIndex, 
            CanvasRenderTarget renderTarget)
        {
            var frame = await decoder.GetFrameAsync(frameIndex);
            int delayMs = DefaultWebpFrameDelayMs;
            int disposal = 0; 
            double offsetX = 0, offsetY = 0;

            string[] propertiesToRead = { "/grctlext/Delay", "/imgdesc/Left", "/imgdesc/Top", "/grctlext/Disposal" };
            
            foreach (var propName in propertiesToRead)
            {
                try
                {
                    var prop = await frame.BitmapProperties.GetPropertiesAsync(new[] { propName });
                    if (prop.TryGetValue(propName, out var p) && p.Value != null)
                    {
                        if (propName == "/grctlext/Delay")
                        {
                            int delay10ms = Convert.ToInt32(p.Value);
                            if (delay10ms > 1) delayMs = delay10ms * 10;
                        }
                        else if (propName == "/imgdesc/Left") offsetX = Convert.ToDouble(p.Value);
                        else if (propName == "/imgdesc/Top") offsetY = Convert.ToDouble(p.Value);
                        else if (propName == "/grctlext/Disposal") disposal = Convert.ToInt32(p.Value);
                    }
                }
                catch { }
            }

            using var softwareBitmap = await frame.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
                
            using var canvasBmp = CanvasBitmap.CreateFromSoftwareBitmap(renderTarget.Device, softwareBitmap);
            var frameRect = new Windows.Foundation.Rect(offsetX, offsetY, canvasBmp.SizeInPixels.Width, canvasBmp.SizeInPixels.Height);

            using (var ds = renderTarget.CreateDrawingSession())
            {
                ds.DrawImage(canvasBmp, frameRect, canvasBmp.Bounds);
            }

            return (delayMs, disposal, frameRect);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
