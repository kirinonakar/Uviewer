using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class AozoraPreviousPageCache
    {
        private readonly AozoraBlockMeasurer _measurer;
        private readonly AozoraBlockPaginator _paginator;
        private readonly Dictionary<int, int> _previousPageStarts = new();

        private CancellationTokenSource? _cachingCts;
        private float _lastWidth;
        private float _lastHeight;
        private double _lastFontSize;
        private AozoraPageOrientation _lastOrientation;
        private IReadOnlyList<AozoraBindingModel>? _lastBlocksRef;
        private int _lastTargetIndex = -1;
        private int _cacheGeneration;

        public AozoraPreviousPageCache(
            AozoraBlockMeasurer measurer,
            AozoraBlockPaginator paginator)
        {
            _measurer = measurer;
            _paginator = paginator;
        }

        public void Clear()
        {
            Interlocked.Increment(ref _cacheGeneration);
            _cachingCts?.Cancel();
            _cachingCts = null;
            lock (_previousPageStarts)
            {
                _previousPageStarts.Clear();
            }

            _lastWidth = 0;
            _lastHeight = 0;
            _lastFontSize = 0;
            _lastOrientation = default;
            _lastBlocksRef = null;
            _lastTargetIndex = -1;
            _measurer.Clear();
        }

        public int GetOrFindPreviousPageStart(
            int targetIndex,
            IReadOnlyList<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation)
        {
            Validate(context, orientation, blocks, targetIndex);

            lock (_previousPageStarts)
            {
                if (_previousPageStarts.TryGetValue(targetIndex, out int cachedStart))
                    return cachedStart;
            }

            int start = FindPreviousPageStart(targetIndex, blocks, context, orientation);
            lock (_previousPageStarts)
            {
                _previousPageStarts[targetIndex] = start;
            }

            return start;
        }

        public int FindPreviousPageStart(
            int targetIndex,
            IReadOnlyList<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation)
        {
            if (targetIndex <= 0) return 0;

            int bestStart = Math.Max(0, targetIndex - 1);
            int scanStart = Math.Max(0, targetIndex - 1);
            int safetyLimit = 300;
            var blockList = ToList(blocks);

            for (int i = scanStart; i >= 0 && safetyLimit > 0; i--, safetyLimit--)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int tempIndex = i;
                var pageBlocks = orientation == AozoraPageOrientation.Vertical
                    ? _paginator.PaginateVerticalPage(ref tempIndex, blockList, context)
                    : _paginator.PaginateHorizontalPage(ref tempIndex, blockList, context);

                if (tempIndex >= targetIndex)
                {
                    bestStart = i;
                }
                else
                {
                    break;
                }
            }

            return bestStart;
        }

        public void StartCaching(
            int currentStartIndex,
            IReadOnlyList<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation)
        {
            if (currentStartIndex <= 0 || blocks.Count == 0)
            {
                _cachingCts?.Cancel();
                return;
            }

            Validate(context, orientation, blocks, currentStartIndex);

            _cachingCts?.Cancel();
            _cachingCts = new CancellationTokenSource();
            var token = _cachingCts.Token;
            var cachingContext = context.WithCancellation(token);
            int cacheGeneration = Volatile.Read(ref _cacheGeneration);

            Task.Run(() =>
            {
                int targetIndex = currentStartIndex;

                for (int i = 0; i < 10; i++)
                {
                    if (token.IsCancellationRequested || targetIndex <= 0) break;

                    int previousStart = FindPreviousPageStart(targetIndex, blocks, cachingContext, orientation);
                    if (token.IsCancellationRequested ||
                        cacheGeneration != Volatile.Read(ref _cacheGeneration)) break;

                    lock (_previousPageStarts)
                    {
                        if (token.IsCancellationRequested ||
                            cacheGeneration != Volatile.Read(ref _cacheGeneration)) break;
                        _previousPageStarts[targetIndex] = previousStart;
                    }

                    if (previousStart >= targetIndex) break;
                    targetIndex = previousStart;
                }
            }, token);
        }

        private void Validate(
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation,
            IReadOnlyList<AozoraBindingModel> blocks,
            int currentIndex)
        {
            bool layoutChanged =
                Math.Abs(_lastWidth - context.AvailableWidth) > 1f ||
                Math.Abs(_lastHeight - context.AvailableHeight) > 1f ||
                Math.Abs(_lastFontSize - context.FontSize) > 0.01 ||
                _lastOrientation != orientation;

            bool documentChanged = !ReferenceEquals(_lastBlocksRef, blocks);
            bool jumped = _lastTargetIndex >= 0 && Math.Abs(currentIndex - _lastTargetIndex) > 300;

            if (layoutChanged || documentChanged || jumped)
            {
                Clear();
                _lastWidth = context.AvailableWidth;
                _lastHeight = context.AvailableHeight;
                _lastFontSize = context.FontSize;
                _lastOrientation = orientation;
                _lastBlocksRef = blocks;
            }

            _lastTargetIndex = currentIndex;
        }

        private static List<AozoraBindingModel> ToList(IReadOnlyList<AozoraBindingModel> blocks)
        {
            return blocks as List<AozoraBindingModel> ?? new List<AozoraBindingModel>(blocks);
        }
    }
}
