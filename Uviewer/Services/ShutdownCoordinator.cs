using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ShutdownContext
    {
        public bool WasPdfOpen { get; init; }
        public Func<Task>? SaveCurrentPositionAsync { get; init; }
        public Action? SaveWindowSettings { get; init; }
        public Action? StopNotificationTimer { get; init; }
        public Action? StopVerticalResizeTimer { get; init; }
        public Action? StopOverlayTimers { get; init; }
        public PreloadManager? PreloadManager { get; init; }
        public SearchOverlayService? SearchOverlayService { get; init; }
        public CancellationTokenSource? ImageLoadingCts { get; init; }
        public TextReaderState? TextReaderState { get; init; }
        public DocumentSearchState? DocumentSearchState { get; init; }
        public Action? ShutdownPdfResources { get; init; }
        public Func<Task>? ShutdownEpubResourcesAsync { get; init; }
        public FastNavigationService? FastNavigationService { get; init; }
        public ImageViewportNavigationService? ImageViewportNavigationService { get; init; }
        public ArchiveSession? ArchiveSession { get; init; }
        public EpubSession? EpubSession { get; init; }
        public ImageViewerState? ImageViewerState { get; init; }
        public ImageCacheManager? ImageCache { get; init; }
        public IEnumerable<ImageEntry>? ImageEntries { get; init; }
        public SevenZipExtractionCoordinator? SevenZipExtraction { get; init; }
        public Action? CleanupWebDavTempFiles { get; init; }
        public RecentService? RecentService { get; init; }
        public FavoritesService? FavoritesService { get; init; }
        public WebDavService? WebDavService { get; init; }
        public WebDavState? WebDavState { get; init; }
        public IAnimatedWebpService? AnimatedWebpService { get; init; }
        public Action? RequestApplicationExit { get; init; }
    }

    internal interface IProcessTerminator
    {
        void Terminate(int exitCode = 0);
    }

    internal sealed class ProcessTerminator : IProcessTerminator
    {
        private int _terminationRequested;

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        public void Terminate(int exitCode = 0)
        {
            if (Interlocked.Exchange(ref _terminationRequested, 1) != 0) return;

            try
            {
                if (!TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode)))
                {
                    Environment.Exit(exitCode);
                }
            }
            catch
            {
                Environment.Exit(exitCode);
            }
        }
    }

    internal sealed class ShutdownCoordinator
    {
        private readonly IProcessTerminator _processTerminator;
        private readonly TimeSpan _asyncStepTimeout;
        private readonly TimeSpan _closeFallbackDelay;
        private readonly TimeSpan _naturalExitGracePeriod;
        private int _closeRequested;
        private int _shutdownStarted;
        private int _shutdownCompleted;
        private int _hardWatchdogStarted;
        private int _postCleanupFallbackScheduled;

        public ShutdownCoordinator()
            : this(
                new ProcessTerminator(),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(4))
        {
        }

        internal ShutdownCoordinator(
            IProcessTerminator processTerminator,
            TimeSpan asyncStepTimeout,
            TimeSpan closeFallbackDelay,
            TimeSpan naturalExitGracePeriod)
        {
            _processTerminator = processTerminator ?? throw new ArgumentNullException(nameof(processTerminator));
            _asyncStepTimeout = asyncStepTimeout;
            _closeFallbackDelay = closeFallbackDelay;
            _naturalExitGracePeriod = naturalExitGracePeriod;
        }

        public void RequestClose(Action closeWindow)
        {
            if (Interlocked.Exchange(ref _closeRequested, 1) != 0) return;

            ArmHardTerminationWatchdog();

            try
            {
                closeWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shutdown close request failed: {ex}");
                _processTerminator.Terminate();
            }
        }

        public async Task ShutdownAsync(ShutdownContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
            ArmHardTerminationWatchdog();

            await RunStepAsync("save current document position", context.SaveCurrentPositionAsync);

            RunStep("dispose preload manager", () => context.PreloadManager?.Dispose());
            RunStep("stop notification timer", context.StopNotificationTimer);
            RunStep("stop vertical resize timer", context.StopVerticalResizeTimer);
            RunStep("stop overlay timers", context.StopOverlayTimers);
            RunStep("stop animated WebP", () => context.AnimatedWebpService?.Stop());
            RunStep("dispose search overlay", () => context.SearchOverlayService?.Dispose());

            RunStep("cancel image loading", () => context.ImageLoadingCts?.Cancel());
            RunStep("cancel text load", () => context.TextReaderState?.CancelGlobalLoad());
            RunStep("cancel text page calculation", () => context.TextReaderState?.CancelPageCalculation());
            RunStep("shutdown PDF resources", context.ShutdownPdfResources);
            await RunStepAsync("shutdown EPUB resources", context.ShutdownEpubResourcesAsync);
            RunStep("dispose fast navigation", () => context.FastNavigationService?.Dispose());
            RunStep("dispose viewport navigation", () => context.ImageViewportNavigationService?.Dispose());

            RunStep("close archive handles", () => context.ArchiveSession?.CloseOpenHandles());
            RunStep("dispose EPUB session", () => context.EpubSession?.Dispose());
            RunStep("clear image viewer bitmaps", () => context.ImageViewerState?.ClearBitmaps());

            if (!context.WasPdfOpen)
            {
                RunStep("dispose image cache", () => context.ImageCache?.Dispose());
            }

            RunStep("clear image entry paths", () => ClearImageEntryFilePaths(context.ImageEntries));

            if (!context.WasPdfOpen)
            {
                RunStep("collect native resources", CollectNativeResources);
            }

            RunStep("cleanup 7z temp data", () => context.SevenZipExtraction?.CleanupTempData(immediate: true));
            RunStep("cleanup WebDAV temp files", context.CleanupWebDavTempFiles);
            RunStep("save window settings", context.SaveWindowSettings);

            await RunStepAsync("save recent items", () => context.RecentService?.SaveRecentItemsAsync() ?? Task.CompletedTask);
            await RunStepAsync("save favorites", () => context.FavoritesService?.SaveFavoritesAsync() ?? Task.CompletedTask);

            RunStep("dispose archive session", () => context.ArchiveSession?.Dispose());
            RunStep("dispose WebDAV service", () => context.WebDavService?.Dispose());
            RunStep("dispose WebDAV state", () => context.WebDavState?.Dispose());
            RunStep("dispose image loading token", () => context.ImageLoadingCts?.Dispose());
            RunStep("dispose text reader state", () => context.TextReaderState?.Dispose());
            RunStep("dispose document search state", () => context.DocumentSearchState?.Dispose());
            RunStep("dispose animated WebP service", () => context.AnimatedWebpService?.Dispose());

            Interlocked.Exchange(ref _shutdownCompleted, 1);
            RunStep("request normal application exit", context.RequestApplicationExit);

            // All durable state has been saved at this point. WinUI/Win2D can still keep
            // native threads alive after Application.Exit(), so finish the process here
            // instead of relying on native component teardown.
            _processTerminator.Terminate();
        }

        private void ArmHardTerminationWatchdog()
        {
            if (Interlocked.Exchange(ref _hardWatchdogStarted, 1) != 0) return;

            var watchdog = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(_closeFallbackDelay);
                    Debug.WriteLine("Hard shutdown deadline elapsed; terminating the remaining process.");
                    _processTerminator.Terminate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hard shutdown watchdog failed: {ex}");
                }
            })
            {
                IsBackground = true,
                Name = "Uviewer shutdown watchdog"
            };
            watchdog.Start();
        }

        private void SchedulePostCleanupFallback()
        {
            if (Interlocked.Exchange(ref _postCleanupFallbackScheduled, 1) != 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_naturalExitGracePeriod);
                    Debug.WriteLine("Normal shutdown grace period elapsed; terminating remaining process as last resort.");
                    _processTerminator.Terminate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Shutdown post-cleanup fallback failed: {ex}");
                }
            });
        }

        private async Task RunStepAsync(string name, Func<Task>? step)
        {
            if (step == null) return;

            try
            {
                Task stepTask = step();
                Task completed = await Task.WhenAny(stepTask, Task.Delay(_asyncStepTimeout));
                if (completed != stepTask)
                {
                    Debug.WriteLine($"Shutdown step timed out: {name}");
                    ObserveTimedOutStep(name, stepTask);
                    return;
                }

                await stepTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shutdown step failed ({name}): {ex}");
            }
        }

        private static void ObserveTimedOutStep(string name, Task stepTask)
        {
            _ = stepTask.ContinueWith(
                completedTask => Debug.WriteLine($"Shutdown timed-out step later failed ({name}): {completedTask.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private static void RunStep(string name, Action? step)
        {
            if (step == null) return;

            try
            {
                step();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shutdown step failed ({name}): {ex}");
            }
        }

        private static void ClearImageEntryFilePaths(IEnumerable<ImageEntry>? entries)
        {
            if (entries == null) return;

            foreach (var entry in entries)
            {
                entry.FilePath = null;
            }
        }

        private static void CollectNativeResources()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
