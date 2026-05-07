using Microsoft.UI.Dispatching;
using System;
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

        public void ToggleViewMode()
        {
            _state.IsGridView = !_state.IsGridView;
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
                    StartThumbnailLoading();
                });
            }
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() => onLoadError(ex));
            }
        }

        private void StartThumbnailLoading()
        {
            var token = _state.RestartThumbnailLoading();
            _ = LoadThumbnailsAsync(token);
        }

        private Task LoadThumbnailsAsync(CancellationToken token)
        {
            return _thumbnailService.LoadThumbnailsAsync(_state.Items, _dispatcher, token);
        }
    }
}
