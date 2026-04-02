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

        public bool ShouldSkipPreload(int index, bool isPdfEntry, double currentZoom, bool isPreviewQuality, bool requireSharpening = false)
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
                            if (!isLowRes || isPreviewQuality) return true;
                        }
                    }
                    else
                    {
                        // [추가] 일반 이미지의 경우 샤프닝이 필요한데 캐시에 없다면 스킵하지 않음
                        if (requireSharpening)
                        {
                            if (!_sharpenedImageCache.ContainsKey(index)) return false;
                        }
                        return true;
                    }
                }
                return false;
            }
        }

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

                // 기존 이미지가 있고, 방금 새로 가져온 이미지와 다르고, 현재 화면에 보여지는 이미지가 아니라면
                if (hasOld && oldBitmap != null && oldBitmap != bitmap && oldBitmap != currentDisplayingBitmap)
                {
                    // [수정] 다른 캐시(Sharpened 등)에서 아직 사용 중인지 확인 후 안전하게 해제
                    if (!IsBitmapInCache(oldBitmap))
                    {
                        SafeDisposeBitmap(oldBitmap);
                    }
                }
            }
        }

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
                        // [수정] IsBitmapInCache가 정상 작동하도록 컬렉션에서 먼저 제거
                        _preloadedImages.Remove(key);
                        _pdfPreloadZoomLevels.Remove(key);
                        _pdfLowResPageIndices.Remove(key);

                        bool isActive = false;
                        foreach (var active in activeBitmaps)
                        {
                            if (active != null && active == bitmap)
                            {
                                isActive = true;
                                break;
                            }
                        }

                        // [수정] Sharpened 캐시 등에 아직 남아있는지 교차 검증
                        if (!isActive && !IsBitmapInCache(bitmap))
                        {
                            SafeDisposeBitmap(bitmap);
                        }
                    }
                }
            }
        }

        public bool NeedsHighResUpgrade(int index)
        {
            lock (_lockObject)
            {
                return _pdfLowResPageIndices.Contains(index);
            }
        }

        public void SafeDisposeBitmap(CanvasBitmap? bitmap)
        {
            if (bitmap == null) return;

            _dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    // [수정] bitmap.Device 접근 제거. 파괴된 객체의 속성에 접근하면 예외가 발생함.
                    // Dispose()는 중복 호출되거나 이미 해제된 상태여도 안전하게 처리됨.
                    bitmap?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Bitmap Dispose Error: {ex.Message}");
                }
            });
        }

        public CanvasBitmap? GetPreloadedImage(int index, double? requiredZoom = null)
        {
            lock (_lockObject)
            {
                if (_preloadedImages.TryGetValue(index, out var bitmap))
                {
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
                            // [수정] 컬렉션에서 먼저 지워야 IsBitmapInCache가 논리적으로 맞게 동작함
                            _sharpenedImageCache.Remove(key);
                            
                            if (!IsBitmapInCache(bitmap))
                            {
                                SafeDisposeBitmap(bitmap);
                            }
                        }
                    }
                }
                _sharpenedImageCache[index] = sharpenedBitmap;
            }
        }

        public bool IsBitmapInCache(CanvasBitmap bitmap)
        {
            if (bitmap == null) return false;
            
            // [수정] 외부에서 호출될 때의 스레드 안전성을 위해 Lock 추가
            lock (_lockObject)
            {
                return _preloadedImages.ContainsValue(bitmap) || _sharpenedImageCache.ContainsValue(bitmap);
            }
        }

        public void ClearSharpenedCache(params CanvasBitmap?[] activeBitmaps)
        {
            lock (_lockObject)
            {
                // [수정] 복사본을 만들고 먼저 Clear 해야 IsBitmapInCache 검증이 안전함
                var copy = _sharpenedImageCache.ToList();
                _sharpenedImageCache.Clear();

                foreach (var kvp in copy)
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

                    if (!isActive && !IsBitmapInCache(kvp.Value))
                    {
                        SafeDisposeBitmap(kvp.Value);
                    }
                }
            }
        }

        public void ClearAll()
        {
            lock (_lockObject)
            {
                // [수정] 중복된 참조가 양쪽 캐시에 있을 경우 두 번 Dispose 되는 것을 막기 위해 Distinct 처리
                var allBitmaps = _preloadedImages.Values
                    .Concat(_sharpenedImageCache.Values)
                    .Where(b => b != null)
                    .Distinct()
                    .ToList();
                
                _preloadedImages.Clear();
                _pdfPreloadZoomLevels.Clear();
                _loadingIndices.Clear();
                _pdfLowResPageIndices.Clear();
                _sharpenedImageCache.Clear();

                foreach (var img in allBitmaps) 
                {
                    SafeDisposeBitmap(img);
                }
            }
        }

        public void Dispose()
        {
            ClearAll();
        }
    }
}