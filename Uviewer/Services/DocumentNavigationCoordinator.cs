using System;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class DocumentNavigationHandlers
    {
        public Func<bool> IsVerticalMode { get; init; } = null!;
        public Func<bool> IsEpubMode { get; init; } = null!;
        public Func<bool> IsTextMode { get; init; } = null!;
        public Func<bool> IsAozoraMode { get; init; } = null!;
        public Action<int> NavigateVerticalPage { get; init; } = null!;
        public Func<int, Task> NavigateEpubAsync { get; init; } = null!;
        public Action<int> NavigateAozoraPage { get; init; } = null!;
        public Action<int> NavigateTextPage { get; init; } = null!;
        public Func<Task> NavigatePreviousImageAsync { get; init; } = null!;
        public Func<Task> NavigateNextImageAsync { get; init; } = null!;
    }

    internal sealed class DocumentNavigationCoordinator
    {
        private readonly DocumentNavigationHandlers _handlers;

        public DocumentNavigationCoordinator(DocumentNavigationHandlers handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public Task NavigatePreviousAsync() => NavigatePageAsync(-1);

        public Task NavigateNextAsync() => NavigatePageAsync(1);

        public async Task NavigatePageAsync(int direction)
        {
            if (direction < 0)
            {
                await NavigatePreviousInternalAsync();
                return;
            }

            if (direction > 0)
            {
                await NavigateNextInternalAsync();
            }
        }

        private async Task NavigatePreviousInternalAsync()
        {
            if (_handlers.IsVerticalMode())
            {
                _handlers.NavigateVerticalPage(_handlers.IsEpubMode() ? 1 : -1);
                return;
            }

            if (_handlers.IsEpubMode())
            {
                await _handlers.NavigateEpubAsync(-1);
                return;
            }

            if (_handlers.IsTextMode())
            {
                NavigateTextLikePage(-1);
                return;
            }

            await _handlers.NavigatePreviousImageAsync();
        }

        private async Task NavigateNextInternalAsync()
        {
            if (_handlers.IsVerticalMode())
            {
                _handlers.NavigateVerticalPage(_handlers.IsEpubMode() ? -1 : 1);
                return;
            }

            if (_handlers.IsEpubMode())
            {
                await _handlers.NavigateEpubAsync(1);
                return;
            }

            if (_handlers.IsTextMode())
            {
                NavigateTextLikePage(1);
                return;
            }

            await _handlers.NavigateNextImageAsync();
        }

        private void NavigateTextLikePage(int direction)
        {
            if (_handlers.IsAozoraMode())
            {
                _handlers.NavigateAozoraPage(direction);
                return;
            }

            _handlers.NavigateTextPage(direction);
        }
    }
}
