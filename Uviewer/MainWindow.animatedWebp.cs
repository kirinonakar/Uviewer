using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using System.Diagnostics;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // Animated WebP
        private DispatcherQueueTimer? _animatedWebpTimer;
        private List<byte[]>? _animatedWebpFramePixels;
        private List<int>? _animatedWebpDelaysMs;
        private int _animatedWebpFrameIndex;
        private int _animatedWebpWidth;
        private int _animatedWebpHeight;
        private const int DefaultWebpFrameDelayMs = 30;
        private volatile bool _isDecodingAnimatedImage = false;

        // Sharpen (Win2D GPU-based sharpening)
        private bool _sharpenEnabled;
        private const float SharpenAmount = 5.0f;      // Very strong sharpening (increased from 1.5)
        private const float SharpenThreshold = 0.0f;   // Apply to all details

        // Sharpened image caching
        private readonly Dictionary<int, CanvasBitmap> _sharpenedImageCache = new();
        private const int MaxSharpenedCacheSize = 20; // Limit cache size to prevent memory issues
        private readonly Dictionary<int, CanvasBitmap> _animatedWebpSharpenedCache = new();

        #region Sharpened Image Caching

        private async Task<CanvasBitmap?> ApplySharpenToBitmapAsync(CanvasBitmap originalBitmap, CanvasControl canvas, bool skipUpscale = false)
        {
            try
            {
                if (canvas.Device == null || originalBitmap.Device != canvas.Device)
                    return originalBitmap;

                // 1. 업스케일 조건 확인 (1024 미만)
                bool shouldUpscale = !skipUpscale && (originalBitmap.Size.Width < 1024 || originalBitmap.Size.Height < 1024);

                float finalWidth = (float)originalBitmap.Size.Width;
                float finalHeight = (float)originalBitmap.Size.Height;

                // 업스케일 시 최대 강도(10.0f), 아닐 경우 기본 강도(5.0f)
                float currentSharpenAmount = shouldUpscale ? 10.0f : SharpenAmount;
                float currentThreshold = shouldUpscale ? 0.03f : 0.01f;

                CanvasBitmap processedSource;

                if (shouldUpscale)
                {
                    // --- 저해상도: 노이즈 억제 + 2배 업스케일 ---
                    var deNoiseEffect = new GaussianBlurEffect
                    {
                        Source = originalBitmap,
                        BlurAmount = 0.4f,
                        Optimization = EffectOptimization.Balanced
                    };

                    finalWidth *= 2.0f;
                    finalHeight *= 2.0f;
                    var upscaledSize = new Windows.Foundation.Size(finalWidth, finalHeight);

                    var upscaledTarget = new CanvasRenderTarget(canvas, upscaledSize);
                    using (var ds = upscaledTarget.CreateDrawingSession())
                    {
                        ds.DrawImage(deNoiseEffect,
                            new Windows.Foundation.Rect(0, 0, finalWidth, finalHeight),
                            originalBitmap.GetBounds(canvas),
                            1.0f,
                            CanvasImageInterpolation.HighQualityCubic);
                    }
                    processedSource = upscaledTarget;
                }
                else
                {
                    processedSource = originalBitmap;
                }

                // 3. 샤프닝 적용 (첫 번째)
                var sharpenEffect1 = new SharpenEffect
                {
                    Source = processedSource,
                    Amount = currentSharpenAmount,
                    Threshold = currentThreshold
                };

                ICanvasImage finalEffect = sharpenEffect1;

                // 4. [추가] 작은 이미지인 경우 샤프닝 한 번 더 중첩
                if (shouldUpscale)
                {
                    finalEffect = new SharpenEffect
                    {
                        Source = sharpenEffect1, // 첫 번째 샤프닝 결과를 소스로 사용
                        Amount = 5.0f,
                        Threshold = 0.03f
                    };
                }

                // 5. 최종 결과물 렌더링
                var finalTarget = new CanvasRenderTarget(canvas, new Windows.Foundation.Size(finalWidth, finalHeight));
                using (var ds = finalTarget.CreateDrawingSession())
                {
                    ds.Antialiasing = CanvasAntialiasing.Antialiased;
                    ds.DrawImage(finalEffect);
                }

                // 6. 메모리 해제
                if (shouldUpscale)
                {
                    processedSource.Dispose();
                }

                return finalTarget;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Processing: {ex.Message}");
                return originalBitmap;
            }
        }

        private void CacheSharpenedImage(int index, CanvasBitmap sharpenedBitmap)
        {
            lock (_sharpenedImageCache)
            {
                // Remove oldest entries if cache is full
                if (_sharpenedImageCache.Count >= MaxSharpenedCacheSize)
                {
                    var keysToRemove = _sharpenedImageCache.Keys.Take(_sharpenedImageCache.Count - MaxSharpenedCacheSize + 1).ToList();
                    foreach (var key in keysToRemove)
                    {
                        if (_sharpenedImageCache.TryGetValue(key, out var bitmap))
                        {
                            bitmap?.Dispose();
                        }
                        _sharpenedImageCache.Remove(key);
                    }
                }

                _sharpenedImageCache[index] = sharpenedBitmap;
            }
        }

        #endregion


        private bool IsAnimationSupported(ImageEntry entry)
        {
            string? ext = null;
            if (entry.FilePath != null) ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();
            else if (entry.ArchiveEntryKey != null) ext = Path.GetExtension(entry.ArchiveEntryKey).ToLowerInvariant();

            // 압축 파일 내의 애니메이션은 재생하지 않음
            if (entry.IsArchiveEntry) return false;

            return ext == ".webp" || ext == ".gif";
        }



        private void StopAnimatedWebp()
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
                    if (bmp != _currentBitmap && bmp != _leftBitmap && bmp != _rightBitmap)
                    {
                        bmp.Dispose();
                    }
                }
                _animatedWebpSharpenedCache.Clear();
            }
        }

        private void StartAnimatedWebpTimer()
        {
            if (_animatedWebpFramePixels == null || _animatedWebpDelaysMs == null || _animatedWebpFramePixels.Count == 0)
                return;

            _animatedWebpTimer = DispatcherQueue.CreateTimer();
            _animatedWebpTimer.Interval = TimeSpan.FromMilliseconds(_animatedWebpDelaysMs[_animatedWebpFrameIndex]);
            _animatedWebpTimer.Tick += AnimatedWebpTimer_Tick;
            _animatedWebpTimer.Start();
        }

        private async void AnimatedWebpTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_animatedWebpFramePixels == null || _animatedWebpDelaysMs == null || _animatedWebpFramePixels.Count == 0)
                return;

            var stopwatch = Stopwatch.StartNew();
            sender.Stop();

            try
            {
                // 인덱스가 범위를 벗어났다면 0으로 안전하게 초기화
                if (_animatedWebpFrameIndex >= _animatedWebpFramePixels.Count)
                {
                    _animatedWebpFrameIndex = 0;
                }

                // 스트리밍을 위한 인덱스 계산 로직 변경
                int nextIndex = _animatedWebpFrameIndex + 1;
                if (nextIndex >= _animatedWebpFramePixels.Count)
                {
                    if (_isDecodingAnimatedImage)
                    {
                        // 아직 백그라운드에서 다음 프레임을 디코딩 중이면, 인덱스를 넘기지 않고 대기
                        nextIndex = _animatedWebpFrameIndex; 
                    }
                    else
                    {
                        // 모든 프레임 로드가 완료되었으면 처음으로 루프
                        nextIndex = 0;
                    }
                }
                _animatedWebpFrameIndex = nextIndex;

                if (MainCanvas.Device == null) return;

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
                            MainCanvas,
                            _animatedWebpFramePixels[_animatedWebpFrameIndex],
                            _animatedWebpWidth,
                            _animatedWebpHeight,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized);

                        newBitmap = await ApplySharpenToBitmapAsync(originalBitmap, MainCanvas, skipUpscale: true);

                        if (_animatedWebpFramePixels == null || MainCanvas.Device == null)
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
                        MainCanvas,
                        _animatedWebpFramePixels[_animatedWebpFrameIndex],
                        _animatedWebpWidth,
                        _animatedWebpHeight,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized);
                }

                var oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;

                if (oldBitmap != null && !IsBitmapInCache(oldBitmap))
                {
                    bool isInAnimationCache = false;
                    lock (_animatedWebpSharpenedCache)
                    {
                        isInAnimationCache = _animatedWebpSharpenedCache.ContainsValue(oldBitmap);
                    }
                    if (!isInAnimationCache)
                    {
                        oldBitmap.Dispose();
                    }
                }

                MainCanvas.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animation Error: {ex.Message}");
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


        private async Task<(List<byte[]>? framePixels, List<int>? delaysMs, int width, int height)> TryLoadAnimatedImageFramesNativeAsync(byte[] imageBytes)
        {
            try
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(imageBytes));
                stream.Seek(0);

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                if (decoder.FrameCount <= 1) return (null, null, 0, 0);

                int w = (int)decoder.PixelWidth;
                int h = (int)decoder.PixelHeight;

                var framePixels = new List<byte[]>();
                var delaysMs = new List<int>();

                // 스레드에 구애받지 않고 디코딩하기 위해 공유 디바이스 사용
                var device = CanvasDevice.GetSharedDevice();

                int initialDisposal = 0;
                Windows.Foundation.Rect initialRect = Windows.Foundation.Rect.Empty;

                // 1. 프레임 0(첫 화면)만 동기적으로 즉시 추출하여 대기 시간 최소화
                using (var renderTarget = new CanvasRenderTarget(device, w, h, 96.0f))
                {
                    using (var ds = renderTarget.CreateDrawingSession())
                    {
                        ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    }
                    var (delay0, disposal0, rect0) = await DecodeAndDrawSingleFrameAsync(decoder, 0, renderTarget);
                    delaysMs.Add(delay0);
                    framePixels.Add(renderTarget.GetPixelBytes());

                    // 0번 프레임의 처분 방식 저장
                    initialDisposal = disposal0;
                    initialRect = rect0;
                }

                // 2. 나머지 프레임들은 백그라운드 스레드에서 비동기로 스트리밍 로드
                _isDecodingAnimatedImage = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var bgRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f);
                        using var backupRenderTarget = new CanvasRenderTarget(device, w, h, 96.0f); // Disposal=3 복원용 백업

                        using (var ds = bgRenderTarget.CreateDrawingSession())
                        {
                            ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                            // 1번 프레임을 그리기 전, 0번 프레임의 이미지를 베이스로 깔아둠 (상태 복원)
                            using var bmp0 = CanvasBitmap.CreateFromBytes(device, framePixels[0], w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
                            ds.DrawImage(bmp0);
                        }

                        int previousDisposal = initialDisposal;
                        Windows.Foundation.Rect previousRect = initialRect;

                        for (uint i = 1; i < decoder.FrameCount; i++)
                        {
                            if (!_isDecodingAnimatedImage) break; // 사용자가 창을 넘기면 로딩 즉시 중단

                            // [중요] 다음 프레임을 그리기 전, 이전 프레임의 메타데이터 요구에 따라 캔버스를 정리합니다.
                            if (previousDisposal == 2) // Restore to Background
                            {
                                using (var ds = bgRenderTarget.CreateDrawingSession())
                                {
                                    // 이전 프레임이 그려졌던 영역만 다시 투명하게 덮어씌웁니다.
                                    ds.Blend = CanvasBlend.Copy;
                                    ds.FillRectangle(previousRect, Windows.UI.Color.FromArgb(0, 0, 0, 0));
                                }
                            }
                            else if (previousDisposal == 3) // Restore to Previous
                            {
                                using (var ds = bgRenderTarget.CreateDrawingSession())
                                {
                                    // 이전 프레임이 그려지기 전의 전체 상태로 되돌립니다.
                                    ds.Blend = CanvasBlend.Copy;
                                    ds.DrawImage(backupRenderTarget);
                                }
                            }

                            // 현재 상태를 백업 (다음 루프에서 Disposal=3을 만날 경우를 대비)
                            using (var ds = backupRenderTarget.CreateDrawingSession())
                            {
                                ds.Blend = CanvasBlend.Copy;
                                ds.DrawImage(bgRenderTarget);
                            }

                            // 현재 프레임 디코딩 및 그리기
                            var (delay, disposal, rect) = await DecodeAndDrawSingleFrameAsync(decoder, i, bgRenderTarget);
                            var pixels = bgRenderTarget.GetPixelBytes();

                            // 다음 루프를 위해 현재 프레임의 데이터 업데이트
                            previousDisposal = disposal;
                            previousRect = rect;

                            // 디코딩 완료된 프레임을 UI 스레드의 List에 실시간 병합
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (_isDecodingAnimatedImage)
                                {
                                    delaysMs.Add(delay);
                                    framePixels.Add(pixels);
                                }
                            });
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Bg Decode Error: {ex.Message}"); }
                    finally { _isDecodingAnimatedImage = false; }
                });

                // 프레임 1개만 로드된 상태로 즉시 반환하여 애니메이션 바로 시작
                return (framePixels, delaysMs, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Native Decode Error: {ex.Message}");
                return (null, null, 0, 0);
            }
        }

        // 헬퍼 메서드: 단일 프레임을 디코딩하고 Delay, Disposal, Frame 영역을 반환합니다.
        private async Task<(int delayMs, int disposal, Windows.Foundation.Rect frameRect)> DecodeAndDrawSingleFrameAsync(
            Windows.Graphics.Imaging.BitmapDecoder decoder, 
            uint frameIndex, 
            CanvasRenderTarget renderTarget)
        {
            var frame = await decoder.GetFrameAsync(frameIndex);
            int delayMs = DefaultWebpFrameDelayMs;
            int disposal = 0; // 0: Unspecified, 1: Do not dispose, 2: Restore to background, 3: Restore to previous
            double offsetX = 0, offsetY = 0;

            try
            {
                // Disposal 속성(처분 방법)을 추가로 읽어옵니다.
                var props = await frame.BitmapProperties.GetPropertiesAsync(new[] { 
                    "/grctlext/Delay", 
                    "/imgdesc/Left", 
                    "/imgdesc/Top",
                    "/grctlext/Disposal" 
                });

                if (props.TryGetValue("/grctlext/Delay", out var dProp) && dProp.Value != null)
                {
                    int delay10ms = Convert.ToInt32(dProp.Value);
                    if (delay10ms > 1) delayMs = delay10ms * 10;
                }
                if (props.TryGetValue("/imgdesc/Left", out var lProp) && lProp.Value != null)
                    offsetX = Convert.ToDouble(lProp.Value);
                if (props.TryGetValue("/imgdesc/Top", out var tProp) && tProp.Value != null)
                    offsetY = Convert.ToDouble(tProp.Value);
                if (props.TryGetValue("/grctlext/Disposal", out var dispProp) && dispProp.Value != null)
                    disposal = Convert.ToInt32(dispProp.Value);
            }
            catch { }

            using var softwareBitmap = await frame.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                
            using var canvasBmp = CanvasBitmap.CreateFromSoftwareBitmap(renderTarget.Device, softwareBitmap);

            // 그려지는 프레임의 실제 영역
            var frameRect = new Windows.Foundation.Rect(offsetX, offsetY, canvasBmp.SizeInPixels.Width, canvasBmp.SizeInPixels.Height);

            using (var ds = renderTarget.CreateDrawingSession())
            {
                // 델타 프레임을 위해 Blend.Copy 없이 일반 덧그리기(SourceOver) 사용
                ds.DrawImage(canvasBmp, frameRect, canvasBmp.Bounds);
            }

            return (delayMs, disposal, frameRect);
        }

    }
}