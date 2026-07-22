using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageNavigationHandlers
    {
        public Func<IList<ImageEntry>> GetImageEntries { get; init; } = null!;
        public Func<int> GetCurrentIndex { get; init; } = null!;
        public Action<int> SetCurrentIndex { get; init; } = null!;
        public Func<bool> IsCurrentViewSideBySide { get; init; } = null!;
        public Action<int> SetScrollDirection { get; init; } = null!;
        public FastNavigationService FastNavigationService { get; init; } = null!;
        public Func<Task> ResetFastNavigationAsync { get; init; } = null!;
        public Action UpdateFastNavigationUi { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Func<Task> SaveCurrentPositionAsync { get; init; } = null!;
        public Func<bool> ShouldPreloadAfterNavigate { get; init; } = null!;
        public Action<bool> StartPreload { get; init; } = null!;
        public Func<bool> IsArchiveOpen { get; init; } = null!;
        public Action ShowLastPageOverlay { get; init; } = null!;
        public Action FocusViewer { get; init; } = null!;
    }

    internal sealed class ImageNavigationCoordinator
    {
        private readonly ImageNavigationHandlers _handlers;

        public ImageNavigationCoordinator(ImageNavigationHandlers handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public Task NavigatePreviousAsync(bool isManualClick = false) =>
            NavigateAsync(forward: false, isManualClick);

        public Task NavigateNextAsync(bool isManualClick = false) =>
            NavigateAsync(forward: true, isManualClick);

        private async Task NavigateAsync(bool forward, bool isManualClick)
        {
            _handlers.SetScrollDirection(forward ? 1 : -1);

            var entries = _handlers.GetImageEntries();
            int currentIndex = _handlers.GetCurrentIndex();
            bool isSideBySide = _handlers.IsCurrentViewSideBySide();
            bool currentSpreadIncludesLastImage = false;

            if (forward && isSideBySide)
            {
                int pairedIndex = FileExplorerService.GetNextImageIndex(entries, currentIndex, 1, true);
                if (pairedIndex != currentIndex)
                {
                    int indexAfterPair = FileExplorerService.GetNextImageIndex(entries, pairedIndex, 1, true);
                    currentSpreadIncludesLastImage = indexAfterPair == pairedIndex;
                }
            }

            bool canNavigate = forward
                ? currentIndex < entries.Count - 1 && !currentSpreadIncludesLastImage
                : currentIndex > 0;

            if (canNavigate)
            {
                bool isFast = !isManualClick &&
                    _handlers.FastNavigationService.DetectFastNavigation(_handlers.ResetFastNavigationAsync);

                int step = isSideBySide ? 2 : 1;
                int nextIndex = FileExplorerService.GetNextImageIndex(entries, currentIndex, step, forward);
                _handlers.SetCurrentIndex(nextIndex);

                if (isFast)
                {
                    _handlers.UpdateFastNavigationUi();
                    return;
                }

                await _handlers.DisplayCurrentImageAsync();
                await _handlers.SaveCurrentPositionAsync();

                if (_handlers.ShouldPreloadAfterNavigate())
                {
                    _handlers.StartPreload(forward);
                }
            }
            else if (forward && entries.Count > 0 && _handlers.IsArchiveOpen())
            {
                _handlers.ShowLastPageOverlay();
            }

            _handlers.FocusViewer();
        }
    }
}
