using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Uviewer.Services;

namespace Uviewer.Models
{
    public sealed class ExplorerState : IDisposable
    {
        private CancellationTokenSource? _thumbnailLoadingCts;

        public string? CurrentPath { get; set; }
        public ObservableCollection<FileItem> Items { get; } = new();
        public bool IsGridView { get; set; }
        public ExplorerSortMode SortMode { get; set; } = ExplorerSortMode.Name;

        public void ReplaceItems(IEnumerable<FileItem> items)
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
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
