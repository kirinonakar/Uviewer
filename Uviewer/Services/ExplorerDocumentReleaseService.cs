using System;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class ExplorerDocumentReleaseHandlers
    {
        public Func<string, bool, bool> IsTargetOpen { get; init; } = null!;
        public Action CancelExtraction { get; init; } = null!;
        public Action CancelImageLoading { get; init; } = null!;
        public Action CancelPreloading { get; init; } = null!;
        public Action CancelTextLoading { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentPdfAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentEpubAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentArchiveAsync { get; init; } = null!;
        public Action CloseCurrentText { get; init; } = null!;
        public Action StopAnimatedImages { get; init; } = null!;
        public Action StopFastNavigation { get; init; } = null!;
        public Action ClearImageCache { get; init; } = null!;
        public Action ResetImageState { get; init; } = null!;
        public Action ApplyClearedImageUi { get; init; } = null!;
    }

    internal sealed class ExplorerDocumentReleaseService
    {
        private readonly ExplorerDocumentReleaseHandlers _handlers;

        public ExplorerDocumentReleaseService(ExplorerDocumentReleaseHandlers handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public async Task ReleaseForExplorerOperationAsync(string targetPath, bool targetIsDirectory)
        {
            if (!_handlers.IsTargetOpen(targetPath, targetIsDirectory))
            {
                return;
            }

            _handlers.CancelExtraction();
            _handlers.CancelImageLoading();
            _handlers.CancelPreloading();
            _handlers.CancelTextLoading();

            await _handlers.CloseCurrentPdfAsync();
            await _handlers.CloseCurrentEpubAsync();
            await _handlers.CloseCurrentArchiveAsync();
            _handlers.CloseCurrentText();

            ResetViewerAfterExplorerOperation();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ResetViewerAfterExplorerOperation()
        {
            _handlers.StopAnimatedImages();
            _handlers.StopFastNavigation();
            _handlers.ClearImageCache();
            _handlers.ResetImageState();
            _handlers.ApplyClearedImageUi();
        }
    }
}
