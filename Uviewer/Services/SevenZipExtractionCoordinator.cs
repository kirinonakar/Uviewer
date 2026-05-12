using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public sealed class SevenZipExtractionCoordinator
    {
        private CancellationTokenSource? _extractCts;
        private CancellationTokenSource _jumpCts = new();

        public string? CurrentTempFolder { get; private set; }
        public int LastIndexForJump { get; private set; } = -1;
        public CancellationToken JumpToken => _jumpCts.Token;

        public void CancelExtraction()
        {
            _extractCts?.Cancel();
        }

        public CancellationToken StartNewExtraction()
        {
            _extractCts?.Cancel();
            _extractCts?.Dispose();
            _extractCts = new CancellationTokenSource();
            return _extractCts.Token;
        }

        public string CreateTempFolder()
        {
            string baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
            CurrentTempFolder = Path.Combine(baseTemp, "7z_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CurrentTempFolder);
            return CurrentTempFolder;
        }

        public bool ShouldSignalJump(int currentIndex, int threshold)
        {
            return Math.Abs(currentIndex - LastIndexForJump) > threshold;
        }

        public void MarkCurrentIndex(int currentIndex)
        {
            LastIndexForJump = currentIndex;
        }

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
            try
            {
                _extractCts?.Cancel();
                _extractCts?.Dispose();
                _extractCts = null;

                if (CurrentTempFolder != null)
                {
                    string folderToDelete = CurrentTempFolder;
                    if (Directory.Exists(folderToDelete))
                    {
                        if (immediate)
                        {
                            TryDeleteDirectoryRecursive(folderToDelete);
                        }
                        else
                        {
                            CurrentTempFolder = null;
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(1000);
                                for (int i = 0; i < 3; i++)
                                {
                                    try
                                    {
                                        if (Directory.Exists(folderToDelete))
                                            Directory.Delete(folderToDelete, true);
                                        break;
                                    }
                                    catch
                                    {
                                        await Task.Delay(2000);
                                    }
                                }

                                CleanupTempRoot();
                            });
                        }
                    }
                }

                if (immediate) CleanupTempRoot(force: true);
            }
            catch { }
        }

        public void CleanupZeroByteTempFiles()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    string baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
                    if (Directory.Exists(baseTemp))
                    {
                        var files = Directory.GetFiles(baseTemp, "*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                if (fi.Length == 0)
                                {
                                    fi.Delete();
                                }
                            }
                            catch { }
                        }

                        var dirs = Directory.GetDirectories(baseTemp, "*", SearchOption.AllDirectories)
                            .OrderByDescending(d => d.Length);
                        foreach (var dir in dirs)
                        {
                            try
                            {
                                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                                {
                                    Directory.Delete(dir);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }

        private static void CleanupTempRoot(bool force = false)
        {
            try
            {
                var baseTemp = Path.Combine(Path.GetTempPath(), "Uviewer");
                if (Directory.Exists(baseTemp))
                {
                    if (force)
                    {
                        TryDeleteDirectoryRecursive(baseTemp);
                    }
                    else if (!Directory.EnumerateFileSystemEntries(baseTemp).Any())
                    {
                        Directory.Delete(baseTemp);
                    }
                }
            }
            catch { }
        }

        private static void TryDeleteDirectoryRecursive(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (Directory.Exists(path))
                            Directory.Delete(path, true);
                        return;
                    }
                    catch
                    {
                        if (i < 4) Thread.Sleep(100);
                    }
                }
            }
            catch { }
        }
    }
}
