using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ExplorerController
    {
        private readonly ExplorerState _state;
        private readonly IThumbnailService _thumbnailService;
        private readonly DispatcherQueue _dispatcher;
        private const int DescendantSearchDelayMs = 250;
        private CancellationTokenSource? _descendantSearchCts;

        public int ThumbnailDecodePixelWidth { get; set; } = 200;
        public bool ShowFolderThumbnails { get; set; }
        public IReadOnlyList<FileItem> AllItems => _state.AllItems;
        public IReadOnlyList<FileItem> VisibleItems => _state.Items;
        public bool IsFilterActive => _state.IsFilterActive;
        public bool HasNoFilterResults => _state.HasNoFilterResults;
        public event EventHandler? ItemsChanged
        {
            add => _state.ItemsChanged += value;
            remove => _state.ItemsChanged -= value;
        }

        /// <summary>
        /// WebDAV처럼 원격 폴더를 하위까지 검색할 때 사용하는 검색 소스. 로컬 모드에서는 null입니다.
        /// </summary>
        public IExplorerRemoteSearchSource? RemoteSearchSource { get; set; }

        public void SetFilter(string text, ExplorerFilterKind kind)
        {
            _state.SetFilter(text, kind);
            RestartDescendantSearch();
        }

        /// <summary>현재 필터로 하위 폴더 검색을 다시 시작합니다(폴더를 이동한 뒤 호출).</summary>
        public void RefreshFilterSearch() => RestartDescendantSearch();

        /// <summary>진행 중인 하위 폴더 검색을 중단합니다.</summary>
        public void CancelFilterSearch()
        {
            _descendantSearchCts?.Cancel();
            _descendantSearchCts = null;
        }

        public ExplorerController(
            ExplorerState state,
            IThumbnailService thumbnailService,
            DispatcherQueue dispatcher)
        {
            _state = state;
            _thumbnailService = thumbnailService;
            _dispatcher = dispatcher;
        }

        public void LoadFolder(
            string path,
            Action<string> onPathChanged,
            Action<Exception> onLoadError,
            Action? onItemsLoaded = null)
        {
            _state.CurrentPath = path;
            onPathChanged(path);
            _ = LoadFolderCoreAsync(path, onLoadError, onItemsLoaded);
        }

        public void SetSortMode(ExplorerSortMode sortMode)
        {
            _state.SortMode = sortMode;
        }

        public void SetViewMode(bool isGrid)
        {
            _state.IsGridView = isGrid;
        }

        private async Task LoadFolderCoreAsync(
            string path,
            Action<Exception> onLoadError,
            Action? onItemsLoaded)
        {
            try
            {
                var newItems = await FileExplorerService
                    .GetFolderContentsAsync(path, _state.SortMode)
                    .ConfigureAwait(false);

                _dispatcher.TryEnqueue(() =>
                {
                    if (_state.CurrentPath != path) return;

                    _state.ReplaceItems(newItems);
                    onItemsLoaded?.Invoke();
                    if (_state.IsFilterActive) RestartDescendantSearch();
                    StartThumbnailLoading();
                });
            }
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() => onLoadError(ex));
            }
        }

        /// <summary>
        /// 필터가 활성화되면 현재 폴더의 하위 폴더까지 검색해 결과를 반영합니다.
        /// 타이핑 중 불필요한 탐색을 줄이기 위해 잠시 기다린 뒤 시작하고, 이전 탐색은 취소합니다.
        /// </summary>
        private void RestartDescendantSearch()
        {
            _descendantSearchCts?.Cancel();
            _descendantSearchCts = null;

            if (!_state.IsFilterActive) return;

            var filterText = _state.FilterText;
            var kind = _state.FilterKind;
            var generation = _state.ItemsGeneration;

            var remoteSource = RemoteSearchSource;
            if (remoteSource is { CanSearch: true })
            {
                var remoteCts = new CancellationTokenSource();
                _descendantSearchCts = remoteCts;
                _ = SearchRemoteDescendantItemsAsync(remoteSource, filterText, kind, generation, remoteCts);
                return;
            }

            var rootPath = _state.CurrentPath;
            if (string.IsNullOrEmpty(rootPath) || rootPath == FileExplorerService.LocalRootPath || !Directory.Exists(rootPath)) return;

            var cts = new CancellationTokenSource();
            _descendantSearchCts = cts;

            _ = SearchDescendantItemsAsync(rootPath, filterText, kind, generation, cts);
        }

        private async Task SearchDescendantItemsAsync(
            string rootPath,
            string filterText,
            ExplorerFilterKind kind,
            int generation,
            CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await Task.Delay(DescendantSearchDelayMs, token).ConfigureAwait(false);

                var items = await FileExplorerService
                    .GetDescendantContentsAsync(rootPath, filterText, kind, _state.SortMode, token)
                    .ConfigureAwait(false);

                _dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;

                    _state.ApplyDescendantItems(items, filterText, kind, generation);
                    if (items.Count > 0) StartThumbnailLoading();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Descendant filter search failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 원격(WebDAV) 폴더를 병렬로 탐색합니다. 결과는 찾는 즉시 배치로 도착해 순서대로 목록에 추가됩니다.
        /// </summary>
        private async Task SearchRemoteDescendantItemsAsync(
            IExplorerRemoteSearchSource source,
            string filterText,
            ExplorerFilterKind kind,
            int generation,
            CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await Task.Delay(DescendantSearchDelayMs, token).ConfigureAwait(false);

                await source.SearchAsync(filterText, kind, items =>
                {
                    if (token.IsCancellationRequested) return;

                    _dispatcher.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        _state.AppendDescendantItems(items, filterText, kind, generation);
                    });
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Remote descendant filter search failed: {ex.Message}");
            }
        }

        private void StartThumbnailLoading()
        {
            var token = _state.RestartThumbnailLoading();
            _ = LoadThumbnailsAsync(token);
        }

        public void RefreshThumbnails(bool clearExisting)
        {
            _state.CancelThumbnailLoading();
            foreach (var item in _state.AllItems.Concat(_state.Items).Distinct())
            {
                if (clearExisting || (item.IsDirectory && !item.IsParentDirectory))
                {
                    item.Thumbnail = null;
                    item.IsThumbnailLoading = false;
                }
            }
            StartThumbnailLoading();
        }

        private Task LoadThumbnailsAsync(CancellationToken token)
        {
            return _thumbnailService.LoadThumbnailsAsync(
                _state.Items.ToArray(),
                _dispatcher,
                token,
                ThumbnailDecodePixelWidth,
                ShowFolderThumbnails);
        }
    }
}
