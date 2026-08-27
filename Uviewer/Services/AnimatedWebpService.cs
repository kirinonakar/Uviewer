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
using System.Runtime.InteropServices;
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
        private List<CanvasBitmap>? _animatedWebpFrameBitmaps;
        private List<int>? _animatedWebpDelaysMs;
        private int _animatedWebpFrameIndex;
        private int _animatedWebpWidth;
        private int _animatedWebpHeight;
        private const int DefaultWebpFrameDelayMs = 30;
        private volatile bool _isDecodingAnimatedImage = false;
        private int _animationGeneration;
        private int _highResolutionTimerActive;

        private readonly object _animatedWebpBitmapCacheLock = new();
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


        private readonly struct WebpFrameInfo
        {
            public WebpFrameInfo(int delayMs, int disposalMethod, double offsetX, double offsetY, bool noBlend)
            {
                DelayMs = delayMs;
                DisposalMethod = disposalMethod;
                OffsetX = offsetX;
                OffsetY = offsetY;
                NoBlend = noBlend;
            }

            public int DelayMs { get; }
            public int DisposalMethod { get; }
            public double OffsetX { get; }
            public double OffsetY { get; }
            public bool NoBlend { get; }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private void EnsureHighResolutionTimer()
        {
            if (Interlocked.CompareExchange(ref _highResolutionTimerActive, 1, 0) == 0)
            {
                try { timeBeginPeriod(1); }
                catch (Exception ex) { Debug.WriteLine($"timeBeginPeriod failed: {ex.Message}"); }
            }
        }

        private void RestoreTimerResolution()
        {
            if (Interlocked.CompareExchange(ref _highResolutionTimerActive, 0, 1) == 1)
            {
                try { timeEndPeriod(1); }
                catch (Exception ex) { Debug.WriteLine($"timeEndPeriod failed: {ex.Message}"); }
            }
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
            RestoreTimerResolution();

            // [안정성 수정] 캔버스 참조를 먼저 끊어서 더 이상 프레임이 전파되지 않도록 합니다.
            _currentCanvas = null;

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
                var staleFrameBitmaps = _animatedWebpFrameBitmaps;
                _animatedWebpFrameBitmaps = null;

                bitmapsToDispose = _animatedWebpSharpenedCache.Values.ToList();
                _animatedWebpSharpenedCache.Clear();

                if (staleFrameBitmaps != null)
                {
                    bitmapsToDispose.AddRange(staleFrameBitmaps);
                }
            }

            DisposeBitmapsOnDispatcher(bitmapsToDispose.Distinct().ToList());
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

                var webpFrameInfos = TryReadWebpFrameInfos(imageBytes);
                var (frameBitmaps, _, _, _) = await TryLoadAnimatedImageFramesNativeAsync(
                    imageBytes,
                    webpFrameInfos,
                    animationGeneration);
                if (frameBitmaps != null
                    && !token.IsCancellationRequested
                    && animationGeneration == Volatile.Read(ref _animationGeneration))
                {
                    // 상태(_animatedWebpFrameBitmaps 등) 할당은 TryLoadAnimatedImageFramesNativeAsync 내부에서 수행된다.
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
            var frameBitmaps = _animatedWebpFrameBitmaps;
            var delaysMs = _animatedWebpDelaysMs;
            var canvas = _currentCanvas;
            if (frameBitmaps == null || delaysMs == null || canvas == null || frameBitmaps.Count == 0)
                return;

            if (_animatedWebpFrameBitmaps != frameBitmaps
                || _animatedWebpDelaysMs != delaysMs
                || _currentCanvas != canvas
                || _animatedWebpFrameIndex >= delaysMs.Count)
            {
                return;
            }

            // [성능 수정] 시스템 타이머 해상도를 1ms로 올린다.
            // 기본 해상도(15.6ms)에서는 33ms 간격 요청이 실제로는 약 46.8ms에 발화하여
            // 샤프닝 여부와 무관하게 재생이 느려진다.
            EnsureHighResolutionTimer();

            _animatedWebpTimer = _dispatcherQueue.CreateTimer();
            _animatedWebpTimer.Interval = TimeSpan.FromMilliseconds(delaysMs[_animatedWebpFrameIndex]);
            _animatedWebpTimer.Tick += AnimatedWebpTimer_Tick;
            _animatedWebpTimer.Start();
        }

        private async void AnimatedWebpTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            // [안정성 수정] 로컬 변수에 스냅샷을 캡처하여 도중에 Stop()이 호출되어도 안전하게 접근
            var frameBitmaps = _animatedWebpFrameBitmaps;
            var delaysMs = _animatedWebpDelaysMs;
            var canvas = _currentCanvas;
            if (frameBitmaps == null || delaysMs == null || canvas == null)
                return;

            // [안정성 수정] 두 리스트의 최소 Count를 기준으로 바운드 체크 (백그라운드 디코딩 중 비원자적 추가 대응)
            int safeFrameCount = Math.Min(frameBitmaps.Count, delaysMs.Count);
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
                        var originalBitmap = GetValidatedFrameBitmap(frameBitmaps, canvas, _animatedWebpFrameIndex);
                        if (originalBitmap == null) return;

                        newBitmap = await _sharpeningService.ApplySharpenToBitmapAsync(originalBitmap, _upscaleFactor, _sharpenAmountParam, _sharpenThresholdParam, _unsharpAmount, _unsharpRadius, skipUpscale: false);

                        if (_animatedWebpFrameBitmaps != frameBitmaps || _currentCanvas != canvas)
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
                                if (_animatedWebpFrameBitmaps == frameBitmaps && _currentCanvas == canvas)
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
                else
                {
                    // [성능 수정] 프레임 비트맵은 디코딩 시점에 이미 생성되어 있으므로 그대로 재사용한다.
                    newBitmap = GetValidatedFrameBitmap(frameBitmaps, canvas, _animatedWebpFrameIndex);
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
                if (_animatedWebpFrameBitmaps != null && _animatedWebpFrameBitmaps.Contains(bitmap))
                {
                    return true;
                }

                return _animatedWebpSharpenedCache.ContainsValue(bitmap);
            }
        }

        private CanvasBitmap? GetValidatedFrameBitmap(List<CanvasBitmap> frameBitmaps, CanvasControl canvas, int frameIndex)
        {
            lock (_animatedWebpBitmapCacheLock)
            {
                if (_animatedWebpFrameBitmaps != frameBitmaps || _currentCanvas != canvas)
                {
                    return null;
                }

                if (frameIndex < 0 || frameIndex >= frameBitmaps.Count)
                {
                    return null;
                }

                return frameBitmaps[frameIndex];
            }
        }

        private static CanvasRenderTarget SnapshotRenderTarget(CanvasDevice device, CanvasRenderTarget source, int width, int height)
        {
            var snapshot = new CanvasRenderTarget(device, width, height, 96.0f);
            using (var ds = snapshot.CreateDrawingSession())
            {
                ds.DrawImage(source);
            }
            return snapshot;
        }

        private async Task<(List<CanvasBitmap>? frameBitmaps, List<int>? delaysMs, int width, int height)> TryLoadAnimatedImageFramesNativeAsync(
            byte[] imageBytes,
            IReadOnlyList<WebpFrameInfo>? webpFrameInfos,
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

                var frameBitmaps = new List<CanvasBitmap>();
                var delaysMs = new List<int>();

                var device = CanvasDevice.GetSharedDevice();

                // [성능 수정] 렌더 타깃 소유권은 백그라운드 작업으로 이전된다.
                // (outer using으로 감싸면 메서드 반환 시점에 백그라운드 디코딩 중인 타깃이 해제되므로 금지)
                var bgRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f);
                var backupRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f);

                try
                {
                    using (var ds = backupRenderTarget.CreateDrawingSession())
                    {
                        ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    }

                    using (var ds = bgRenderTarget.CreateDrawingSession())
                    {
                        ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    }

                    int initialDisposal = 0;
                    Windows.Foundation.Rect initialRect = Windows.Foundation.Rect.Empty;

                    // 1프레임은 동기적으로 디코딩해서 즉시 표시할 수 있게 한다.
                    var (delay0, disposal0, rect0) = await DecodeAndDrawSingleFrameAsync(
                        decoder,
                        0,
                        bgRenderTarget,
                        GetFrameInfo(webpFrameInfos, 0));
                    delaysMs.Add(delay0);
                    frameBitmaps.Add(SnapshotRenderTarget(device, bgRenderTarget, w, h));

                    initialDisposal = disposal0;
                    initialRect = rect0;

                    if (animationGeneration != Volatile.Read(ref _animationGeneration))
                    {
                        DisposeAll(frameBitmaps);
                        bgRenderTarget.Dispose();
                        backupRenderTarget.Dispose();
                        return (null, null, 0, 0);
                    }

                    // [성능 수정] 상태 할당을 백그라운드 작업 시작 전으로 옮겨서
                    // 디코더 쪽 큐잉 콜백과 UI가 같은 리스트 인스턴스를 공유하도록 한다.
                    _animatedWebpFrameBitmaps = frameBitmaps;
                    _animatedWebpDelaysMs = delaysMs;
                    _animatedWebpWidth = w;
                    _animatedWebpHeight = h;

                    _isDecodingAnimatedImage = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
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
                                    GetFrameInfo(webpFrameInfos, i));

                                previousDisposal = disposal;
                                previousRect = rect;

                                // [성능 수정] GetPixelBytes()로 프레임마다 수 MB~수십 MB 배열을 CPU로
                                // 강제 동기화 복사하는 대신 GPU 스냅샷을 만들어 그대로 화면에 재사용한다.
                                var snapshot = SnapshotRenderTarget(device, bgRenderTarget, w, h);

                                _dispatcherQueue.TryEnqueue(() =>
                                {
                                    if (animationGeneration == Volatile.Read(ref _animationGeneration)
                                        && _animatedWebpFrameBitmaps == frameBitmaps)
                                    {
                                        delaysMs.Add(delay);
                                        frameBitmaps.Add(snapshot);
                                    }
                                    else
                                    {
                                        try { snapshot.Dispose(); }
                                        catch (Exception ex) { Debug.WriteLine($"Orphan snapshot dispose error: {ex.Message}"); }
                                    }
                                });
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"Bg Decode Error: {ex.Message}"); }
                        finally
                        {
                            try { bgRenderTarget.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Render target dispose error: {ex.Message}"); }
                            try { backupRenderTarget.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Render target dispose error: {ex.Message}"); }

                            // 서비스가 리스트를 채택하지 못한 경우(취소/세대 불일치) 로컬에서 정리한다.
                            if (animationGeneration != Volatile.Read(ref _animationGeneration)
                                || _animatedWebpFrameBitmaps != frameBitmaps)
                            {
                                DisposeAll(frameBitmaps);
                            }

                            if (animationGeneration == Volatile.Read(ref _animationGeneration))
                            {
                                _isDecodingAnimatedImage = false;
                            }
                        }
                    });

                    return (frameBitmaps, delaysMs, w, h);
                }
                catch
                {
                    DisposeAll(frameBitmaps);
                    try { bgRenderTarget.Dispose(); } catch { }
                    try { backupRenderTarget.Dispose(); } catch { }
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Native Decode Error: {ex.Message}");
                return (null, null, 0, 0);
            }
        }

        private static void DisposeAll(List<CanvasBitmap> bitmaps)
        {
            foreach (var bmp in bitmaps)
            {
                try { bmp.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"Frame dispose error: {ex.Message}"); }
            }

            bitmaps.Clear();
        }

        private async Task<(int delayMs, int disposal, Windows.Foundation.Rect frameRect)> DecodeAndDrawSingleFrameAsync(
            BitmapDecoder decoder, 
            uint frameIndex, 
            CanvasRenderTarget renderTarget,
            WebpFrameInfo? webpFrameInfo)
        {
            var frame = await decoder.GetFrameAsync(frameIndex);
            int delayMs = DefaultWebpFrameDelayMs;
            int disposal = 0; 
            double offsetX = 0, offsetY = 0;
            bool noBlend = false;

            if (webpFrameInfo.HasValue)
            {
                // [성능 수정] WebP는 RIFF(ANMF) 청크에서 직접 읽은 메타데이터를 사용한다.
                // GIF 전용 메타데이터 조회(/grctlext/* 등)는 WebP에서 프레임마다 4회 예외를 유발하므로 생략한다.
                var info = webpFrameInfo.Value;
                delayMs = info.DelayMs;
                disposal = info.DisposalMethod;
                offsetX = info.OffsetX;
                offsetY = info.OffsetY;
                noBlend = info.NoBlend;
            }
            else
            {
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
            }

            using var softwareBitmap = await frame.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
                
            using var canvasBmp = CanvasBitmap.CreateFromSoftwareBitmap(renderTarget.Device, softwareBitmap);
            var frameRect = new Windows.Foundation.Rect(offsetX, offsetY, canvasBmp.SizeInPixels.Width, canvasBmp.SizeInPixels.Height);

            using (var ds = renderTarget.CreateDrawingSession())
            {
                if (noBlend)
                {
                    // WebP "no blend": 알파 블렌딩 없이 해당 영역을 통째로 덮어쓴다.
                    ds.Blend = CanvasBlend.Copy;
                }

                ds.DrawImage(canvasBmp, frameRect, canvasBmp.Bounds);
            }

            return (delayMs, disposal, frameRect);
        }

        private static WebpFrameInfo? GetFrameInfo(IReadOnlyList<WebpFrameInfo>? frameInfos, uint frameIndex)
        {
            if (frameInfos == null || frameIndex >= frameInfos.Count)
            {
                return null;
            }

            return frameInfos[(int)frameIndex];
        }

        private static IReadOnlyList<WebpFrameInfo>? TryReadWebpFrameInfos(byte[] imageBytes)
        {
            if (imageBytes.Length < 12
                || !MatchesFourCc(imageBytes, 0, "RIFF")
                || !MatchesFourCc(imageBytes, 8, "WEBP"))
            {
                return null;
            }

            var frameInfos = new List<WebpFrameInfo>();
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
                    int p = (int)chunkDataOffset;
                    int offsetX = imageBytes[p] | (imageBytes[p + 1] << 8) | (imageBytes[p + 2] << 16);
                    int offsetY = imageBytes[p + 3] | (imageBytes[p + 4] << 8) | (imageBytes[p + 5] << 16);
                    int durationMs = imageBytes[p + 12]
                        | (imageBytes[p + 13] << 8)
                        | (imageBytes[p + 14] << 16);
                    byte flags = imageBytes[p + 15];

                    bool disposeToBackground = (flags & 0x01) != 0;
                    bool noBlend = (flags & 0x02) != 0;

                    frameInfos.Add(new WebpFrameInfo(
                        durationMs > 0 ? durationMs : DefaultWebpFrameDelayMs,
                        disposeToBackground ? 2 : 0,
                        offsetX,
                        offsetY,
                        noBlend));
                }

                chunkOffset = (int)nextChunkOffset;
            }

            return frameInfos.Count > 0 ? frameInfos : null;
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
