using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public class ImageCacheManager : IDisposable
    {
        // 프리로드 및 캐시 상태 관리
        private readonly Dictionary<int, CanvasBitmap> _preloadedImages = new();
        private readonly Dictionary<int, double> _pdfPreloadZoomLevels = new();
        private readonly HashSet<int> _loadingIndices = new();
        private readonly HashSet<int> _pdfLowResPageIndices = new();
        private readonly Dictionary<int, CanvasBitmap> _sharpenedImageCache = new();
        private const int MaxSharpenedCacheSize = 20;
        
        private readonly DispatcherQueue _dispatcher;
        private readonly object _lockObject = new(); // 스레드 안전성을 위한 락 객체

        public ImageCacheManager(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        // 특정 인덱스의 이미지가 현재 로딩 중인지 확인하고, 아니라면 로딩 상태로 마킹
        public bool TryMarkForLoading(int index)
        {
            lock (_lockObject)
            {
                if (_loadingIndices.Contains(index)) return false;
                _loadingIndices.Add(index);
                return true;
            }
        }

        public void UnmarkLoading(int index)
        {
            lock (_lockObject)
            {
                _loadingIndices.Remove(index);
            }
        }

        // 캐시에 이미지가 있는지, 업그레이드(저해상도->고해상도)가 필요한지 판별
        public bool ShouldSkipPreload(int index, bool isPdfEntry, double currentZoom, bool isPreviewQuality)
        {
            lock (_lockObject)
            {
                if (_preloadedImages.TryGetValue(index, out _))
                {
                    if (isPdfEntry)
                    {
                        _pdfPreloadZoomLevels.TryGetValue(index, out var cachedZoom);
                        bool zoomMatches = Math.Abs(cachedZoom - currentZoom) <= 0.01;
                        if (zoomMatches)
                        {
                            bool isLowRes = _pdfLowResPageIndices.Contains(index);
                            
                            // 풀 해상도 캐시가 있거나, (저해상도 캐시가 있는데 또 저해상도 요청인 경우) 스킵
                            if (!isLowRes || isPreviewQuality) return true;
                        }
                    }
                    else
                    {
                        // PDF가 아닌 일반 이미지는 캐시가 있으면 무조건 스킵
                        return true;
                    }
                }
                return false;
            }
        }

        // 로드된 이미지를 캐시에 저장 (기존 이미지가 있다면 안전하게 교체 및 해제)
        public void UpdateCache(int index, CanvasBitmap bitmap, bool isPdf, double currentZoom, bool isPreviewQuality, CanvasBitmap? currentDisplayingBitmap = null)
        {
            lock (_lockObject)
            {
                bool hasOld = _preloadedImages.TryGetValue(index, out var oldBitmap);
                
                _preloadedImages[index] = bitmap;
                if (isPdf)
                {
                    _pdfPreloadZoomLevels[index] = currentZoom;
                    if (isPreviewQuality) _pdfLowResPageIndices.Add(index);
                    else _pdfLowResPageIndices.Remove(index);
                }

                // 기존 이미지가 있고, 방금 새로 가져온 이미지와 다르고, 현재 화면에 보여지는 이미지가 아니라면 메모리 해제
                if (hasOld && oldBitmap != null && oldBitmap != bitmap && oldBitmap != currentDisplayingBitmap)
                {
                    SafeDisposeBitmap(oldBitmap);
                }
            }
        }

        // 현재 인덱스를 기준으로 범위를 벗어난 오래된 캐시 정리
        public void CleanupOldPreloadedImages(int currentIndex, bool isPdfMode, int basePreloadCount, params CanvasBitmap?[] activeBitmaps)
        {
            lock (_lockObject)
            {
                int keepRange = isPdfMode ? 20 : basePreloadCount * 2;
                var keysToRemove = _preloadedImages.Keys
                    .Where(index => Math.Abs(index - currentIndex) > keepRange)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_preloadedImages.TryGetValue(key, out var bitmap))
                    {
                        // 현재 사용 중인 비트맵(Main, Left, Right)은 삭제 금지
                        bool isActive = false;
                        foreach (var active in activeBitmaps)
                        {
                            if (active != null && active == bitmap)
                            {
                                isActive = true;
                                break;
                            }
                        }

                        if (!isActive)
                        {
                            SafeDisposeBitmap(bitmap);
                        }
                    }
                    _preloadedImages.Remove(key);
                    _pdfPreloadZoomLevels.Remove(key);
                    _pdfLowResPageIndices.Remove(key);
                }
            }
        }

        // 저해상도 캐시 업그레이드 필요 여부 확인
        public bool NeedsHighResUpgrade(int index)
        {
            lock (_lockObject)
            {
                return _pdfLowResPageIndices.Contains(index);
            }
        }

        // 안전한 Win2D 메모리 해제 (UI 스레드 위임)
        public void SafeDisposeBitmap(CanvasBitmap? bitmap)
        {
            if (bitmap == null) return;

            _dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    if (bitmap != null && bitmap.Device != null) 
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Bitmap Dispose Error: {ex.Message}");
                }
            });
        }

        // 캐시 데이터 조회용
        public CanvasBitmap? GetPreloadedImage(int index, double? requiredZoom = null)
        {
            lock (_lockObject)
            {
                if (_preloadedImages.TryGetValue(index, out var bitmap))
                {
                    // PDF인 경우 줌 레벨도 확인 (전달되었을 때만)
                    if (requiredZoom.HasValue && _pdfPreloadZoomLevels.TryGetValue(index, out var cachedZoom))
                    {
                        if (Math.Abs(cachedZoom - requiredZoom.Value) > 0.01)
                            return null;
                    }
                    return bitmap;
                }
                return null;
            }
        }

        public CanvasBitmap? GetSharpenedImage(int index)
        {
            lock (_lockObject)
            {
                _sharpenedImageCache.TryGetValue(index, out var bitmap);
                return bitmap;
            }
        }

        public void CacheSharpenedImage(int index, CanvasBitmap sharpenedBitmap, int currentIndex)
        {
            lock (_lockObject)
            {
                if (_sharpenedImageCache.Count >= MaxSharpenedCacheSize)
                {
                    var keysToRemove = _sharpenedImageCache.Keys
                        .OrderByDescending(k => Math.Abs(k - currentIndex))
                        .Take(_sharpenedImageCache.Count - MaxSharpenedCacheSize + 1)
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        if (_sharpenedImageCache.TryGetValue(key, out var bitmap))
                        {
                            if (!IsBitmapInCache(bitmap))
                            {
                                SafeDisposeBitmap(bitmap);
                            }
                        }
                        _sharpenedImageCache.Remove(key);
                    }
                }
                _sharpenedImageCache[index] = sharpenedBitmap;
            }
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            lock (_lockObject)
            {
                return _preloadedImages.ContainsValue(bitmap) || _sharpenedImageCache.ContainsValue(bitmap);
            }
        }

        public void ClearSharpenedCache(params CanvasBitmap?[] activeBitmaps)
        {
            lock (_lockObject)
            {
                foreach (var kvp in _sharpenedImageCache.ToList())
                {
                    bool isActive = false;
                    foreach (var active in activeBitmaps)
                    {
                        if (active != null && active == kvp.Value)
                        {
                            isActive = true;
                            break;
                        }
                    }

                    if (!isActive)
                    {
                        SafeDisposeBitmap(kvp.Value);
                    }
                }
                _sharpenedImageCache.Clear();
            }
        }

        public void ClearAll()
        {
            lock (_lockObject)
            {
                foreach (var img in _preloadedImages.Values) SafeDisposeBitmap(img);
                foreach (var img in _sharpenedImageCache.Values) SafeDisposeBitmap(img);
                
                _preloadedImages.Clear();
                _pdfPreloadZoomLevels.Clear();
                _loadingIndices.Clear();
                _pdfLowResPageIndices.Clear();
                _sharpenedImageCache.Clear();
            }
        }

        public void Dispose()
        {
            ClearAll();
        }
    }
}