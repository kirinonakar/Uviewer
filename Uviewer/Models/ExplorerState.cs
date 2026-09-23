using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Uviewer.Services;

namespace Uviewer.Models
{
    public sealed class ExplorerState : IDisposable
    {
        private CancellationTokenSource? _thumbnailLoadingCts;
        private List<FileItem> _allItems = new();
        private List<FileItem> _descendantItems = new();
        private readonly HashSet<string> _descendantPaths = new(StringComparer.OrdinalIgnoreCase);
        private int _itemsGeneration;

        public string? CurrentPath { get; set; }
        public ObservableCollection<FileItem> Items { get; } = new();
        public ObservableCollection<FileItem> FolderItems { get; } = new();
        public ObservableCollection<FileItem> VisibleImageItems { get; } = new();
        public IReadOnlyList<FileItem> AllItems => _allItems;
        public bool IsGridView { get; set; }
        public ExplorerSortMode SortMode { get; set; } = ExplorerSortMode.Name;
        public string FilterText { get; private set; } = "";
        public ExplorerFilterKind FilterKind { get; private set; }
        public bool IsFilterActive => FilterText.Length > 0 || FilterKind != ExplorerFilterKind.All;
        public bool HasNoFilterResults => IsFilterActive && !Items.Any(item => !item.IsParentDirectory);
        public int ItemsGeneration => _itemsGeneration;
        public CancellationToken ThumbnailLoadingToken => _thumbnailLoadingCts?.Token ?? CancellationToken.None;
        public event EventHandler? ItemsChanged;

        public void ReplaceItems(IEnumerable<FileItem> items)
        {
            // Snapshot before clearing: callers may pass the visible collection itself.
            _allItems = items.ToList();
            FolderItems.Clear();
            foreach (var folder in _allItems.Where(item => item.IsDirectory && !item.IsParentDirectory))
            {
                FolderItems.Add(folder);
            }
            ClearDescendantItems();
            _itemsGeneration++;
            ApplyFilter();
        }

        public void SetFilter(string? text, ExplorerFilterKind kind)
        {
            string normalizedText = text?.Trim() ?? "";
            if (FilterText == normalizedText && FilterKind == kind) return;

            FilterText = normalizedText;
            FilterKind = kind;
            if (!IsFilterActive) ClearDescendantItems();
            ApplyFilter();
        }

        /// <summary>
        /// 하위 폴더 검색 결과로 이전 하위 결과를 교체합니다.
        /// 검색이 시작된 뒤 폴더나 필터가 바뀌면 오래된 결과를 버립니다.
        /// </summary>
        public void ApplyDescendantItems(IReadOnlyList<FileItem>? items, string filterText, ExplorerFilterKind kind, int generation)
        {
            if (generation != _itemsGeneration) return;
            if (filterText != FilterText || kind != FilterKind) return;

            ClearDescendantItems();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(item.FullPath) && !_descendantPaths.Add(item.FullPath)) continue;
                    _descendantItems.Add(item);
                }
            }

            ApplyFilter();
        }

        /// <summary>
        /// 하위 폴더 검색에서 찾은 항목을 도착한 순서대로 목록 끝에 추가합니다.
        /// 같은 경로는 건너뛰므로 병렬 검색 결과를 그대로 넘겨도 안전합니다.
        /// </summary>
        public void AppendDescendantItems(IReadOnlyList<FileItem>? items, string filterText, ExplorerFilterKind kind, int generation)
        {
            if (items == null || items.Count == 0) return;
            if (generation != _itemsGeneration) return;
            if (filterText != FilterText || kind != FilterKind) return;

            var added = false;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.FullPath) && !_descendantPaths.Add(item.FullPath)) continue;

                _descendantItems.Add(item);
                if (!MatchesFilter(item)) continue;

                Items.Add(item);
                if (item.IsImage) VisibleImageItems.Add(item);
                added = true;
            }

            if (added) ItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearDescendantItems()
        {
            _descendantItems = new List<FileItem>();
            _descendantPaths.Clear();
        }

        private void ApplyFilter()
        {
            Items.Clear();
            VisibleImageItems.Clear();

            var listedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _allItems)
            {
                if (!string.IsNullOrEmpty(item.FullPath)) listedPaths.Add(item.FullPath);
                if (!MatchesFilter(item)) continue;
                Items.Add(item);
                if (item.IsImage) VisibleImageItems.Add(item);
            }

            foreach (var item in _descendantItems)
            {
                // 현재 폴더 목록에 이미 표시된 항목은 중복 추가하지 않습니다.
                if (!string.IsNullOrEmpty(item.FullPath) && !listedPaths.Add(item.FullPath)) continue;
                if (!MatchesFilter(item)) continue;
                Items.Add(item);
                if (item.IsImage) VisibleImageItems.Add(item);
            }

            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool MatchesFilter(FileItem item)
            => FileExplorerService.MatchesFilter(item, FilterText, FilterKind);

        public CancellationToken RestartThumbnailLoading()
        {
            CancelThumbnailLoading();
            _thumbnailLoadingCts = new CancellationTokenSource();
            return _thumbnailLoadingCts.Token;
        }

        public void CancelThumbnailLoading()
        {
            if (_thumbnailLoadingCts == null) return;

            _thumbnailLoadingCts.Cancel();
            _thumbnailLoadingCts.Dispose();
            _thumbnailLoadingCts = null;
        }

        public void Dispose()
        {
            CancelThumbnailLoading();
        }
    }
}
