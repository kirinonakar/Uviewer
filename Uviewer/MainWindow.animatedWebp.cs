using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SharpCompress.Archives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using System.Diagnostics;
using SixLabors.ImageSharp.Processing;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        // Animated WebP
        private DispatcherQueueTimer? _animatedWebpTimer;
        private List<byte[]>? _animatedWebpFramePixels;
        private List<int>? _animatedWebpDelaysMs;
        private int _animatedWebpFrameIndex;
        private int _animatedWebpWidth;  // <-- 추가: 프레임 너비
        private int _animatedWebpHeight; // <-- 추가: 프레임 높이
        private const int DefaultWebpFrameDelayMs = 100;

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
            _animatedWebpTimer?.Stop();
            _animatedWebpTimer = null;
            // _animatedWebpBitmap = null; // <-- 삭제
            _animatedWebpFramePixels = null;
            _animatedWebpDelaysMs = null;
            _animatedWebpWidth = 0;        // <-- 추가
            _animatedWebpHeight = 0;       // <-- 추가

            // Clear sharpened frame cache
            lock (_animatedWebpSharpenedCache)
            {
                foreach (var bmp in _animatedWebpSharpenedCache.Values)
                {
                    // 현재 사용 중인 비트맵(화면에 출력 중인 것)은 해제하지 않습니다.
                    // 나중에 _currentBitmap이 교체될 때 IsBitmapInCache 체크를 통해 해제됩니다.
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

            // 1. 처리 시간 측정을 위한 스톱워치 시작
            var stopwatch = Stopwatch.StartNew();

            sender.Stop(); // 타이머 일시 정지 (다음 딜레이 설정을 위해)

            try
            {
                // 1. 다음 프레임 인덱스 계산
                _animatedWebpFrameIndex = (_animatedWebpFrameIndex + 1) % _animatedWebpFramePixels.Count;

                // 2. 현재 캔버스 디바이스 확인 (창이 닫히거나 디바이스가 없으면 중단)
                if (MainCanvas.Device == null) return;

                CanvasBitmap? newBitmap = null;

                // 3. 샤프닝이 활성화된 경우 캐시 확인 및 적용
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
                        // 원본 프레임 생성
                        using var originalBitmap = CanvasBitmap.CreateFromBytes(
                            MainCanvas,
                            _animatedWebpFramePixels[_animatedWebpFrameIndex],
                            _animatedWebpWidth,
                            _animatedWebpHeight,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized);

                        // 샤프닝 적용
                        newBitmap = await ApplySharpenToBitmapAsync(originalBitmap, MainCanvas, skipUpscale: true);

                        // 다시 UI 스레드로 돌아왔을 때 애니메이션이 중단되었는지 확인
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

                // 4. 샤프닝이 없거나 실패한 경우 일반 프레임 생성
                if (newBitmap == null)
                {
                    newBitmap = CanvasBitmap.CreateFromBytes(
                        MainCanvas,
                        _animatedWebpFramePixels[_animatedWebpFrameIndex],
                        _animatedWebpWidth,
                        _animatedWebpHeight,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized);
                }

                // 5. 기존 비트맵 정리 및 교체 (캐시된 비트맵은 Dispose하지 않도록 주의)
                var oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;

                if (oldBitmap != null && !IsBitmapInCache(oldBitmap))
                {
                    // 애니메이션 캐시에도 없는 경우에만 Dispose
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

                // 6. 화면 갱신 요청
                MainCanvas.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animation Error: {ex.Message}");
            }
            finally
            {
                // 2. 스톱워치 종료 및 시간 계산
                stopwatch.Stop();
                long elapsedMs = stopwatch.ElapsedMilliseconds;

                // 7. 다음 프레임 딜레이 설정 및 타이머 재시작
                if (_animatedWebpDelaysMs != null)
                {
                    // 원래 기다려야 할 시간
                    int targetDelay = _animatedWebpDelaysMs[_animatedWebpFrameIndex];

                    // 보정된 시간 = (원래 딜레이) - (작업에 걸린 시간)
                    // 최소 0ms (음수가 되면 즉시 실행해야 함)
                    int adjustedDelay = Math.Max(0, targetDelay - (int)elapsedMs);

                    sender.Interval = TimeSpan.FromMilliseconds(adjustedDelay);
                    sender.Start();
                }
            }
        }


        private async Task<(List<byte[]>? framePixels, List<int>? delaysMs, int width, int height)> TryLoadAnimatedImageFramesAsync(byte[] imageBytes)
        {
            try
            {
                using var inputStream = new MemoryStream(imageBytes);
                
                // [최적화 1] Bgra32로 로드
                using var image = await Task.Run(() => SixLabors.ImageSharp.Image.Load<Bgra32>(inputStream));

                if (image.Frames.Count <= 1)
                    return (null, null, 0, 0);

                int w = image.Width;
                int h = image.Height;
                var framePixels = new List<byte[]>();
                var delaysMs = new List<int>();

                int bytesPerFrame = w * h * 4;

                // 프레임 구성을 위한 베이스 이미지
                using var composed = new SixLabors.ImageSharp.Image<Bgra32>(w, h);

                for (int i = 0; i < image.Frames.Count; i++)
                {
                    var currentFrame = image.Frames[i];
                    
                    await Task.Run(() =>
                    {
                        using var frameImg = image.Frames.CloneFrame(i);
                        
                        // [핵심] 겹침 현상(Ghosting) 해결
                        // PixelAlphaCompositionMode.Src를 사용하여 투명한 영역도 이전 프레임을 덮어씌우도록 합니다.
                        // 이 방식은 대부분의 전체 크기 애니메이션 GIF의 잔상 문제를 해결합니다.
                        composed.Mutate(ctx => ctx.DrawImage(frameImg, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Src, 1f));
                    });

                    var bytes = new byte[bytesPerFrame];
                    composed.CopyPixelDataTo(bytes);
                    framePixels.Add(bytes);

                    int delayMs = DefaultWebpFrameDelayMs;
                    
                    // GIF 딜레이 정보
                    var gifFrameMeta = currentFrame.Metadata.GetGifMetadata();
                    // WebP 딜레이 정보
                    var webpFrameMeta = currentFrame.Metadata.GetWebpMetadata();

                    if (webpFrameMeta != null && webpFrameMeta.FrameDelay > 0)
                    {
                        // WebP delay (ms)
                        delayMs = (int)Math.Max(10, webpFrameMeta.FrameDelay);
                    }
                    else if (gifFrameMeta != null && gifFrameMeta.FrameDelay > 0)
                    {
                        // GIF delay: 1/100s -> ms
                        delayMs = (int)Math.Max(10, gifFrameMeta.FrameDelay * 10);
                        if (delayMs <= 20) delayMs = 100; // 브라우저 관례 보정
                    }

                    delaysMs.Add(delayMs);
                }

                return (framePixels, delaysMs, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animated Image Parse Error: {ex.Message}");
                return (null, null, 0, 0);
            }
        }

    }
}