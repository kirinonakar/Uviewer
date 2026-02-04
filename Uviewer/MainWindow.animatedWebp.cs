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

        #region Sharpened Image Caching

        private async Task<CanvasBitmap?> ApplySharpenToBitmapAsync(CanvasBitmap originalBitmap, CanvasControl canvas)
        {
            try
            {
                if (canvas.Device == null || originalBitmap.Device != canvas.Device)
                    return originalBitmap;

                // 1. 업스케일 조건 확인 (1024 미만)
                bool shouldUpscale = originalBitmap.Size.Width < 1024 || originalBitmap.Size.Height < 1024;

                float finalWidth = (float)originalBitmap.Size.Width;
                float finalHeight = (float)originalBitmap.Size.Height;

                // 업스케일 시 최대 강도(10.0f), 아닐 경우 기본 강도(5.0f)
                float currentSharpenAmount = shouldUpscale ? 10.0f : SharpenAmount;
                float currentThreshold = shouldUpscale ? 0.03f : 0.0f;

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
                            CanvasImageInterpolation.Cubic);
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
                    if (shouldUpscale) ds.Antialiasing = CanvasAntialiasing.Aliased;
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
                        _sharpenedImageCache.Remove(key);
                    }
                }

                _sharpenedImageCache[index] = sharpenedBitmap;
            }
        }

        #endregion


        private void StopAnimatedWebp()
        {
            _animatedWebpTimer?.Stop();
            _animatedWebpTimer = null;
            // _animatedWebpBitmap = null; // <-- 삭제
            _animatedWebpFramePixels = null;
            _animatedWebpDelaysMs = null;
            _animatedWebpWidth = 0;        // <-- 추가
            _animatedWebpHeight = 0;       // <-- 추가
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

        private void AnimatedWebpTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_animatedWebpFramePixels == null || _animatedWebpDelaysMs == null || _animatedWebpFramePixels.Count == 0)
                return;

            sender.Stop(); // 타이머 일시 정지 (다음 딜레이 설정을 위해)

            try
            {
                // 1. 다음 프레임 인덱스 계산
                _animatedWebpFrameIndex = (_animatedWebpFrameIndex + 1) % _animatedWebpFramePixels.Count;

                // 2. 현재 캔버스 디바이스 확인 (창이 닫히거나 디바이스가 없으면 중단)
                if (MainCanvas.Device == null) return;

                // 3. 픽셀 데이터로 CanvasBitmap 생성 (이것이 Win2D가 그리는 객체입니다)
                var newBitmap = CanvasBitmap.CreateFromBytes(
                    MainCanvas,
                    _animatedWebpFramePixels[_animatedWebpFrameIndex],
                    _animatedWebpWidth,
                    _animatedWebpHeight,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized);

                // 4. 기존 비트맵 정리 및 교체
                var oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;
                oldBitmap?.Dispose(); // 메모리 누수 방지를 위해 이전 프레임 해제

                // 5. 화면 갱신 요청
                MainCanvas.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animation Error: {ex.Message}");
            }

            // 6. 다음 프레임 딜레이 설정 및 타이머 재시작
            if (_animatedWebpDelaysMs != null)
            {
                sender.Interval = TimeSpan.FromMilliseconds(Math.Max(10, _animatedWebpDelaysMs[_animatedWebpFrameIndex]));
                sender.Start();
            }
        }


        private async Task<(List<byte[]>? framePixels, List<int>? delaysMs, int width, int height)> TryLoadAnimatedWebpFramesAsync(byte[] webpBytes)
        {
            try
            {
                using var inputStream = new MemoryStream(webpBytes);

                // [최적화 1] Rgba32 대신 Win2D가 선호하는 Bgra32로 바로 로드
                // (ImageSharp.PixelFormats 네임스페이스 필요)
                using var image = await Task.Run(() => SixLabors.ImageSharp.Image.Load<Bgra32>(inputStream));

                if (image.Frames.Count <= 1)
                    return (null, null, 0, 0);

                int w = image.Width;
                int h = image.Height;
                var framePixels = new List<byte[]>();
                var delaysMs = new List<int>();

                // 미리 버퍼 크기 계산
                int bytesPerFrame = w * h * 4;

                for (int i = 0; i < image.Frames.Count; i++)
                {
                    var currentFrame = image.Frames[i];
                    var bytes = new byte[bytesPerFrame];

                    // [최적화 2] 픽셀 루프 제거 -> 메모리 블록 복사로 대체 (수백 배 빠름)
                    await Task.Run(() =>
                    {
                        currentFrame.CopyPixelDataTo(bytes);
                    });

                    framePixels.Add(bytes);

                    int delayMs = DefaultWebpFrameDelayMs;
                    if (currentFrame.Metadata.TryGetWebpFrameMetadata(out var webpFrameMeta) && webpFrameMeta.FrameDelay > 0)
                        delayMs = (int)Math.Max(10, webpFrameMeta.FrameDelay);

                    delaysMs.Add(delayMs);
                }

                return (framePixels, delaysMs, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebP Parse Error: {ex.Message}");
                return (null, null, 0, 0);
            }
        }

    }
}