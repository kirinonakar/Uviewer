using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public sealed class SevenZipExtractionCoordinator
    {
        private CancellationTokenSource? _extractCts;
        private CancellationTokenSource _jumpCts = new();
        private LeasedTempDirectory? _tempDirectory;
        private Task _extractionTask = Task.CompletedTask;
        private readonly List<Task> _cleanupTasks = new();
        private readonly object _stateGate = new();
        private readonly string _tempRoot;

        public SevenZipExtractionCoordinator() : this(TemporaryFiles.Root) { }
        internal SevenZipExtractionCoordinator(string tempRoot) => _tempRoot = tempRoot;

        public string? CurrentTempFolder => _tempDirectory?.Folder;
        public int LastIndexForJump { get; private set; } = -1;
        public CancellationToken JumpToken => _jumpCts.Token;

        public void CancelExtraction() => _extractCts?.Cancel();

        public CancellationToken StartNewExtraction()
        {
            CleanupTempData();
            lock (_stateGate)
            {
                _extractCts = new CancellationTokenSource();
                return _extractCts.Token;
            }
        }

        internal Task<string> CreateTempFolderAsync(CancellationToken token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var directory = LeasedTempDirectory.Create(_tempRoot, "7z_");
            lock (_stateGate)
            {
                if (_extractCts != null && _extractCts.Token == token && !token.IsCancellationRequested)
                {
                    _tempDirectory = directory;
                    return directory.Folder;
                }
            }
            directory.Dispose();
            throw new OperationCanceledException(token);
        }, token);

        internal void TrackExtraction(Task task)
        {
            lock (_stateGate) _extractionTask = task;
        }

        public bool ShouldSignalJump(int currentIndex, int threshold) => Math.Abs(currentIndex - LastIndexForJump) > threshold;
        public void MarkCurrentIndex(int currentIndex) => LastIndexForJump = currentIndex;

        public void SignalJump(int currentIndex)
        {
            try
            {
                LastIndexForJump = currentIndex;
                var old = _jumpCts;
                _jumpCts = new CancellationTokenSource();
                old.Cancel();
                old.Dispose();
            }
            catch { }
        }

        public void CleanupTempData(bool immediate = false)
        {
            CancellationTokenSource? cancellation;
            LeasedTempDirectory? directory;
            Task extraction;
            lock (_stateGate)
            {
                cancellation = _extractCts;
                _extractCts = null;
                directory = _tempDirectory;
                _tempDirectory = null;
                extraction = _extractionTask;
                _extractionTask = Task.CompletedTask;
            }
            cancellation?.Cancel();
            if (immediate && extraction.IsCompleted)
            {
                directory?.Dispose();
            }
            _cleanupTasks.RemoveAll(task => task.IsCompleted);
            _cleanupTasks.Add(Task.Run(async () =>
            {
                // Native extraction can still be writing after cancellation. Keep
                // the lease until every worker has closed its output stream.
                try { await extraction.ConfigureAwait(false); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                finally
                {
                    if (directory != null) await directory.DeleteAsync().ConfigureAwait(false);
                    cancellation?.Dispose();
                }
            }));
        }

        public Task CleanupTempDataAsync()
        {
            CleanupTempData();
            return Task.WhenAll(_cleanupTasks);
        }
    }
}
