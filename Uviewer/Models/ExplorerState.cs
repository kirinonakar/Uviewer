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

        public string? CurrentPath { get; set; }
        public ObservableCollection<FileItem> Items { get; } = new();
        public IReadOnlyList<FileItem> AllItems => _allItems;
        public bool IsGridView { get; set; }
        public ExplorerSortMode SortMode { get; set; } = ExplorerSortMode.Name;
        public string FilterText { get; private set; } = "";
        public ExplorerFilterKind FilterKind { get; private set; }
        public bool IsFilterActive => FilterText.Length > 0 || FilterKind != ExplorerFilterKind.All;
        public bool HasNoFilterResults => IsFilterActive && !Items.Any(item => !item.IsParentDirectory);
        public event EventHandler? ItemsChanged;

        public void ReplaceItems(IEnumerable<FileItem> items)
        {
            // Snapshot before clearing: callers may pass the visible collection itself.
            _allItems = items.ToList();
            ApplyFilter();
        }

        public void SetFilter(string? text, ExplorerFilterKind kind)
        {
            string normalizedText = text?.Trim() ?? "";
            if (FilterText == normalizedText && FilterKind == kind) return;

            FilterText = normalizedText;
            FilterKind = kind;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Items.Clear();
            foreach (var item in _allItems)
            {
                if (MatchesFilter(item)) Items.Add(item);
            }
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool MatchesFilter(FileItem item)
        {
            if (item.IsParentDirectory) return true;
            if (!item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) return false;

            // Keep matching folders available for navigation with any file-type filter.
            if (item.IsDirectory) return true;
            return FilterKind switch
            {
                ExplorerFilterKind.All => true,
                ExplorerFilterKind.Image => item.IsImage,
                ExplorerFilterKind.Text => item.IsText,
                ExplorerFilterKind.Pdf => item.IsPdf,
                ExplorerFilterKind.Epub => item.IsEpub,
                ExplorerFilterKind.Archive => item.IsArchive,
                _ => false
            };
        }

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
