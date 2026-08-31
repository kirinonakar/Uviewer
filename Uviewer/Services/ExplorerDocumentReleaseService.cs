using System;
using System.Diagnostics;
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

            await ReleaseCurrentDocumentAsync();
        }

        public async Task ReleaseCurrentDocumentAsync(bool reduceMemory = false)
        {
            RunStep("cancel extraction", _handlers.CancelExtraction);
            RunStep("cancel image loading", _handlers.CancelImageLoading);
            RunStep("cancel preloading", _handlers.CancelPreloading);
            RunStep("cancel text loading", _handlers.CancelTextLoading);

            await RunStepAsync("close PDF", _handlers.CloseCurrentPdfAsync);
            await RunStepAsync("close EPUB", _handlers.CloseCurrentEpubAsync);
            await RunStepAsync("close archive", _handlers.CloseCurrentArchiveAsync);
            RunStep("close text", _handlers.CloseCurrentText);

            ResetViewerAfterExplorerOperation();

            await DocumentMemoryReclaimer.CollectAsync(reduceMemory);
        }

        public void ResetViewerAfterExplorerOperation()
        {
            RunStep("stop animated images", _handlers.StopAnimatedImages);
            RunStep("stop fast navigation", _handlers.StopFastNavigation);
            RunStep("clear image cache", _handlers.ClearImageCache);
            RunStep("reset image state", _handlers.ResetImageState);
            RunStep("apply cleared viewer UI", _handlers.ApplyClearedImageUi);
        }

        private static void RunStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Document release step failed ({name}): {ex.Message}");
            }
        }

        private static async Task RunStepAsync(string name, Func<Task<bool>> step)
        {
            try
            {
                if (!await step())
                {
                    Debug.WriteLine($"Document release step did not complete ({name}).");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Document release step failed ({name}): {ex.Message}");
            }
        }
    }
}
