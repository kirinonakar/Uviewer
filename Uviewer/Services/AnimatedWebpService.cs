using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Buffers.Binary;
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
        private int _animationGeneration;

        private readonly object _animatedWebpBitmapCacheLock = new();
        private readonly Dictionary<int, CanvasBitmap> _animatedWebpFrameCache = new();
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
            Interlocked.Increment(ref _animationGeneration);
            _isDecodingAnimatedImage = false; // 진행 중인 백그라운드 디코딩 중지
            
            _animatedWebpTimer?.Stop();
            _animatedWebpTimer = null;

            // [안정성 수정] 캔버스 참조를 먼저 끊어서 더 이상 프레임이 전파되지 않도록 합니다.
            _currentCanvas = null;

            _animatedWebpFramePixels?.Clear();
            _animatedWebpFramePixels = null;
            _animatedWebpDelaysMs?.Clear();
            _animatedWebpDelaysMs = null;

            _animatedWebpWidth = 0;
            _animatedWebpHeight = 0;
            _animatedWebpFrameIndex = 0;

            // [안정성 수정] Stop()을 호출한 측에서 _currentBitmap을 null로 설정한 뒤에
            // 캐시를 해제하도록 AnimationStopped 이벤트를 먼저 발행합니다.
            RaiseAnimationStopped();

            List<CanvasBitmap> bitmapsToDispose;
            lock (_animatedWebpBitmapCacheLock)
            {
                bitmapsToDispose = _animatedWebpFrameCache.Values
                    .Concat(_animatedWebpSharpenedCache.Values)
                    .Distinct()
                    .ToList();
                _animatedWebpFrameCache.Clear();
                _animatedWebpSharpenedCache.Clear();
            }

            DisposeBitmapsOnDispatcher(bitmapsToDispose);
        }

        private void RaiseAnimationStopped()
        {
            if (AnimationStopped == null) return;

            if (_dispatcherQueue.HasThreadAccess)
            {
                AnimationStopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            using var completed = new ManualResetEventSlim(false);
            if (_dispatcherQueue.TryEnqueue(() =>
            {
                try { AnimationStopped?.Invoke(this, EventArgs.Empty); }
                finally { completed.Set(); }
            }))
            {
                completed.Wait(TimeSpan.FromMilliseconds(500));
            }
        }

        private void DisposeBitmapsOnDispatcher(List<CanvasBitmap> bitmaps)
        {
            if (bitmaps.Count == 0) return;

            void DisposeAll()
            {
                foreach (var bmp in bitmaps)
                {
                    try { bmp.Dispose(); }
                    catch (Exception ex) { Debug.WriteLine($"Animated frame dispose error: {ex.Message}"); }
                }
            }

            if (_dispatcherQueue.HasThreadAccess)
            {
                DisposeAll();
            }
            else if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DisposeAll))
            {
                DisposeAll();
            }
        }

        public async Task StartAsync(ImageEntry entry, CanvasControl canvas, CancellationToken token, 
            float upscaleFactor, float sharpenAmount, float sharpenThreshold, float unsharpAmount, float unsharpRadius, bool sharpenEnabled)
        {
            Stop();
            int animationGeneration = Volatile.Read(ref _animationGeneration);
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

                var webpFrameDelaysMs = TryReadWebpFrameDelays(imageBytes);
                var (framePixels, delaysMs, w, h) = await TryLoadAnimatedImageFramesNativeAsync(
                    imageBytes,
                    webpFrameDelaysMs,
                    canvas,
                    animationGeneration);
                if (framePixels != null
                    && !token.IsCancellationRequested
                    && animationGeneration == Volatile.Read(ref _animationGeneration))
                {
                    _animatedWebpFramePixels = framePixels;
                    _animatedWebpDelaysMs = delaysMs;
                    _animatedWebpWidth = w;
                    _animatedWebpHeight = h;

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (!token.IsCancellationRequested
                            && animationGeneration == Volatile.Read(ref _animationGeneration))
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
            var framePixels = _animatedWebpFramePixels;
            var delaysMs = _animatedWebpDelaysMs;
            var canvas = _currentCanvas;
            if (framePixels == null || delaysMs == null || canvas == null || framePixels.Count == 0)
                return;

            PrimeFrameBitmapCache();

            if (_animatedWebpFramePixels != framePixels
                || _animatedWebpDelaysMs != delaysMs
                || _currentCanvas != canvas
                || _animatedWebpFrameIndex >= delaysMs.Count)
            {
                return;
            }

            _animatedWebpTimer = _dispatcherQueue.CreateTimer();
            _animatedWebpTimer.Interval = TimeSpan.FromMilliseconds(delaysMs[_animatedWebpFrameIndex]);
            _animatedWebpTimer.Tick += AnimatedWebpTimer_Tick;
            _animatedWebpTimer.Start();
        }

        private async void AnimatedWebpTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            // [안정성 수정] 로컬 변수에 스냅샷을 캡처하여 도중에 Stop()이 호출되어도 안전하게 접근
            var framePixels = _animatedWebpFramePixels;
            var delaysMs = _animatedWebpDelaysMs;
            var canvas = _currentCanvas;
            if (framePixels == null || delaysMs == null || canvas == null)
                return;

            // [안정성 수정] 두 리스트의 최소 Count를 기준으로 바운드 체크 (백그라운드 디코딩 중 비원자적 추가 대응)
            int safeFrameCount = Math.Min(framePixels.Count, delaysMs.Count);
            if (safeFrameCount == 0) return;

            var stopwatch = Stopwatch.StartNew();
            sender.Stop();

            try
            {
                if (_animatedWebpFrameIndex >= safeFrameCount)
                {
                    _animatedWebpFrameIndex = 0;
                }

                int nextIndex = _animatedWebpFrameIndex + 1;
                if (nextIndex >= safeFrameCount)
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

                // 재검증: Stop()이 호출되었거나 인덱스가 범위를 벗어나면 중단
                if (canvas.Device == null || _animatedWebpFrameIndex >= safeFrameCount) return;

                CanvasBitmap? newBitmap = null;

                if (_sharpenEnabled)
                {
                    lock (_animatedWebpBitmapCacheLock)
                    {
                        if (_animatedWebpSharpenedCache.TryGetValue(_animatedWebpFrameIndex, out var cached))
                        {
                            newBitmap = cached;
                        }
                    }

                    if (newBitmap == null)
                    {
                        var originalBitmap = GetOrCreateFrameBitmap(canvas, framePixels, _animatedWebpFrameIndex);
                        if (originalBitmap == null) return;

                        newBitmap = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);

                        if (_animatedWebpFramePixels != framePixels || _currentCanvas != canvas)
                        {
                            if (newBitmap != null && !ReferenceEquals(newBitmap, originalBitmap))
                            {
                                newBitmap.Dispose();
                            }
                            return;
                        }

                        if (newBitmap != null)
                        {
                            lock (_animatedWebpBitmapCacheLock)
                            {
                                if (_animatedWebpFramePixels == framePixels && _currentCanvas == canvas)
                                {
                                    _animatedWebpSharpenedCache[_animatedWebpFrameIndex] = newBitmap;
                                }
                                else if (!ReferenceEquals(newBitmap, originalBitmap))
                                {
                                    newBitmap.Dispose();
                                    return;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                    }
                }

                if (newBitmap == null)
                {
                    newBitmap = GetOrCreateFrameBitmap(canvas, framePixels, _animatedWebpFrameIndex);
                }

                if (newBitmap != null)
                {
                    FrameUpdated?.Invoke(this, newBitmap);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Animation Error: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                var currentDelays = _animatedWebpDelaysMs;
                if (currentDelays != null && _animatedWebpFrameIndex < currentDelays.Count)
                {
                    int targetDelay = currentDelays[_animatedWebpFrameIndex];
                    int adjustedDelay = Math.Max(1, targetDelay - (int)stopwatch.ElapsedMilliseconds);
                    sender.Interval = TimeSpan.FromMilliseconds(adjustedDelay);
                    sender.Start();
                }
            }
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            lock (_animatedWebpBitmapCacheLock)
            {
                return _animatedWebpFrameCache.ContainsValue(bitmap)
                    || _animatedWebpSharpenedCache.ContainsValue(bitmap);
            }
        }

        private CanvasBitmap? GetOrCreateFrameBitmap(CanvasControl canvas, List<byte[]> framePixels, int frameIndex)
        {
            lock (_animatedWebpBitmapCacheLock)
            {
                if (_animatedWebpFramePixels != framePixels || _currentCanvas != canvas)
                {
                    return null;
                }

                if (_animatedWebpFrameCache.TryGetValue(frameIndex, out var cached))
                {
                    return cached;
                }
            }

            var created = CanvasBitmap.CreateFromBytes(
                canvas,
                framePixels[frameIndex],
                _animatedWebpWidth,
                _animatedWebpHeight,
                DirectXPixelFormat.B8G8R8A8UIntNormalized);

            lock (_animatedWebpBitmapCacheLock)
            {
                if (_animatedWebpFramePixels != framePixels || _currentCanvas != canvas)
                {
                    created.Dispose();
                    return null;
                }

                if (_animatedWebpFrameCache.TryGetValue(frameIndex, out var cached))
                {
                    created.Dispose();
                    return cached;
                }

                _animatedWebpFrameCache[frameIndex] = created;
                return created;
            }
        }

        private void PrimeFrameBitmapCache()
        {
            var framePixels = _animatedWebpFramePixels;
            var canvas = _currentCanvas;
            if (framePixels == null || canvas == null) return;

            for (int i = 0; i < framePixels.Count; i++)
            {
                if (GetOrCreateFrameBitmap(canvas, framePixels, i) == null)
                {
                    return;
                }
            }
        }

        private async Task<(List<byte[]>? framePixels, List<int>? delaysMs, int width, int height)> TryLoadAnimatedImageFramesNativeAsync(
            byte[] imageBytes,
            IReadOnlyList<int>? webpFrameDelaysMs,
            CanvasControl canvas,
            int animationGeneration)
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
                    var (delay0, disposal0, rect0) = await DecodeAndDrawSingleFrameAsync(
                        decoder,
                        0,
                        renderTarget,
                        GetFrameDelay(webpFrameDelaysMs, 0));
                    delaysMs.Add(delay0);
                    framePixels.Add(renderTarget.GetPixelBytes());

                    initialDisposal = disposal0;
                    initialRect = rect0;
                }

                if (animationGeneration != Volatile.Read(ref _animationGeneration))
                {
                    return (null, null, 0, 0);
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
                            if (animationGeneration != Volatile.Read(ref _animationGeneration)) break;

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

                            var (delay, disposal, rect) = await DecodeAndDrawSingleFrameAsync(
                                decoder,
                                i,
                                bgRenderTarget,
                                GetFrameDelay(webpFrameDelaysMs, i));
                            var pixels = bgRenderTarget.GetPixelBytes();

                            previousDisposal = disposal;
                            previousRect = rect;

                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (animationGeneration == Volatile.Read(ref _animationGeneration))
                                {
                                    delaysMs.Add(delay);
                                    framePixels.Add(pixels);

                                    if (_animatedWebpFramePixels == framePixels && _currentCanvas == canvas)
                                    {
                                        GetOrCreateFrameBitmap(canvas, framePixels, framePixels.Count - 1);
                                    }
                                }
                            });
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"Bg Decode Error: {ex.Message}"); }
                    finally
                    {
                        if (animationGeneration == Volatile.Read(ref _animationGeneration))
                        {
                            _isDecodingAnimatedImage = false;
                        }
                    }
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
            CanvasRenderTarget renderTarget,
            int? encodedWebpDelayMs)
        {
            var frame = await decoder.GetFrameAsync(frameIndex);
            int delayMs = encodedWebpDelayMs ?? DefaultWebpFrameDelayMs;
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
                            if (!encodedWebpDelayMs.HasValue)
                            {
                                int delay10ms = Convert.ToInt32(p.Value);
                                if (delay10ms > 1) delayMs = delay10ms * 10;
                            }
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

        private static int? GetFrameDelay(IReadOnlyList<int>? frameDelaysMs, uint frameIndex)
        {
            if (frameDelaysMs == null || frameIndex >= frameDelaysMs.Count)
            {
                return null;
            }

            return frameDelaysMs[(int)frameIndex];
        }

        private static IReadOnlyList<int>? TryReadWebpFrameDelays(byte[] imageBytes)
        {
            if (imageBytes.Length < 12
                || !MatchesFourCc(imageBytes, 0, "RIFF")
                || !MatchesFourCc(imageBytes, 8, "WEBP"))
            {
                return null;
            }

            var delaysMs = new List<int>();
            int chunkOffset = 12;

            while (chunkOffset <= imageBytes.Length - 8)
            {
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(imageBytes.AsSpan(chunkOffset + 4, 4));
                long chunkDataOffset = chunkOffset + 8L;
                long nextChunkOffset = chunkDataOffset + chunkSize + (chunkSize & 1u);

                if (chunkDataOffset + chunkSize > imageBytes.Length || nextChunkOffset > int.MaxValue)
                {
                    return null;
                }

                if (MatchesFourCc(imageBytes, chunkOffset, "ANMF") && chunkSize >= 16)
                {
                    int durationOffset = (int)chunkDataOffset + 12;
                    int durationMs = imageBytes[durationOffset]
                        | (imageBytes[durationOffset + 1] << 8)
                        | (imageBytes[durationOffset + 2] << 16);

                    delaysMs.Add(durationMs > 0 ? durationMs : DefaultWebpFrameDelayMs);
                }

                chunkOffset = (int)nextChunkOffset;
            }

            return delaysMs.Count > 0 ? delaysMs : null;
        }

        private static bool MatchesFourCc(byte[] bytes, int offset, string fourCc)
        {
            return offset >= 0
                && offset <= bytes.Length - 4
                && fourCc.Length == 4
                && bytes[offset] == fourCc[0]
                && bytes[offset + 1] == fourCc[1]
                && bytes[offset + 2] == fourCc[2]
                && bytes[offset + 3] == fourCc[3];
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
