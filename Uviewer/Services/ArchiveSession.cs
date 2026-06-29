using SharpCompress.Archives;
using SevenZipExtractor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ArchiveSession : IDisposable
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public IArchive? CurrentArchive { get; set; }
        public ArchiveFile? Current7zArchive { get; set; }
        public string? CurrentPath { get; set; }
        public SemaphoreSlim Lock => _lock;

        public bool HasArchive => CurrentArchive != null || Current7zArchive != null;
        public bool IsSevenZipArchive => Current7zArchive != null;

        public async Task<IReadOnlyList<ImageEntry>> OpenLocalAsync(string archivePath)
        {
            await _lock.WaitAsync();
            try
            {
                CloseOpenHandles();
                CurrentPath = archivePath;

                string extension = Path.GetExtension(archivePath).ToLowerInvariant();
                if (extension == ".7z")
                {
                    string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                    Current7zArchive = new ArchiveFile(archivePath, libraryPath);
                    return BuildSevenZipEntries(Current7zArchive);
                }

                CurrentArchive = ArchiveFactory.OpenArchive(archivePath);
                return BuildSharpCompressEntries(CurrentArchive);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IReadOnlyList<ImageEntry>> OpenStreamAsync(string displayPath, Stream stream)
        {
            await _lock.WaitAsync();
            try
            {
                CloseOpenHandles();
                CurrentPath = displayPath;
                CurrentArchive = ArchiveFactory.OpenArchive(stream);
                return BuildSharpCompressEntries(CurrentArchive);
            }
            finally
            {
                _lock.Release();
            }
        }

        public bool Close(TimeSpan timeout)
        {
            if (!HasArchive) return true;

            if (!_lock.Wait(timeout))
            {
                Debug.WriteLine("Archive lock timeout - cleanup deferred to avoid disposing while in use");
                return false;
            }

            try
            {
                CloseOpenHandles();
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> CloseAsync(TimeSpan timeout)
        {
            if (!HasArchive) return true;

            if (!await _lock.WaitAsync(timeout))
            {
                Debug.WriteLine("Archive lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseOpenHandles();
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void CloseOpenHandles()
        {
            CurrentArchive?.Dispose();
            CurrentArchive = null;

            Current7zArchive?.Dispose();
            Current7zArchive = null;

            DeleteWebDavTempArchiveIfNeeded(CurrentPath);
            CurrentPath = null;
        }

        public async Task<byte[]?> ReadEntryBytesAsync(string entryKey, CancellationToken token)
        {
            await _lock.WaitAsync(token);
            try
            {
                if (CurrentArchive != null)
                {
                    var archiveEntry = CurrentArchive.Entries.FirstOrDefault(e => e.Key == entryKey);
                    if (archiveEntry == null) return null;

                    using var memoryStream = new MemoryStream();
                    using var entryStream = archiveEntry.OpenEntryStream();
                    await entryStream.CopyToAsync(memoryStream, token);
                    return memoryStream.ToArray();
                }

                if (Current7zArchive != null)
                {
                    var archiveEntry = Current7zArchive.Entries.FirstOrDefault(e => e.FileName == entryKey);
                    if (archiveEntry == null) return null;

                    using var memoryStream = new MemoryStream();
                    archiveEntry.Extract(memoryStream);
                    return memoryStream.ToArray();
                }

                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public bool ContainsEntryLoose(string entryKey)
        {
            if (!_lock.Wait(TimeSpan.FromMilliseconds(50)))
            {
                return true;
            }

            try
            {
                return FindEntryKeyLoose(entryKey) != null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<byte[]?> ReadEntryBytesLooseAsync(string entryKey, CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                string? resolvedKey = FindEntryKeyLoose(entryKey);
                if (resolvedKey == null) return null;

                if (CurrentArchive != null)
                {
                    var archiveEntry = CurrentArchive.Entries.FirstOrDefault(e => e.Key == resolvedKey);
                    if (archiveEntry == null) return null;

                    using var memoryStream = new MemoryStream();
                    using var entryStream = archiveEntry.OpenEntryStream();
                    await entryStream.CopyToAsync(memoryStream, token);
                    return memoryStream.ToArray();
                }

                if (Current7zArchive != null)
                {
                    var archiveEntry = Current7zArchive.Entries.FirstOrDefault(e => e.FileName == resolvedKey);
                    if (archiveEntry == null) return null;

                    using var memoryStream = new MemoryStream();
                    archiveEntry.Extract(memoryStream);
                    return memoryStream.ToArray();
                }

                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ExtractSevenZipEntriesInBackgroundAsync(
            string archivePath,
            IReadOnlyList<ImageEntry> imageEntries,
            Func<int> getCurrentIndex,
            SevenZipExtractionCoordinator extraction,
            CancellationToken token)
        {
            try
            {
                string tempFolder = extraction.CreateTempFolder();

                var total = imageEntries.Count;
                var extracted = new bool[total];
                var lockObj = new object();

                int threadCount = Math.Min(Environment.ProcessorCount, 6);
                var tasks = new List<Task>();

                for (int t = 0; t < threadCount; t++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        ExtractSevenZipEntriesWorker(
                            archivePath,
                            imageEntries,
                            getCurrentIndex,
                            extraction,
                            tempFolder,
                            extracted,
                            lockObj,
                            token);
                    }, token));
                }

                await Task.WhenAll(tasks);
            }
            catch { }
        }

        public void Dispose()
        {
            CloseOpenHandles();
            _lock.Dispose();
        }

        private static List<ImageEntry> BuildSevenZipEntries(ArchiveFile archive)
        {
            return archive.Entries
                .Where(e => !e.IsFolder &&
                    FileExplorerService.GetSupportedFileKind(e.FileName) == SupportedFileKind.Image)
                .OrderBy(e => e.FileName, NaturalSortComparer.Default)
                .Select(e => new ImageEntry
                {
                    DisplayName = Path.GetFileName(e.FileName ?? "Unknown"),
                    ArchiveEntryKey = e.FileName
                })
                .ToList();
        }

        private static List<ImageEntry> BuildSharpCompressEntries(IArchive archive)
        {
            return archive.Entries
                .Where(e => !e.IsDirectory &&
                    FileExplorerService.GetSupportedFileKind(e.Key) == SupportedFileKind.Image)
                .OrderBy(e => e.Key, NaturalSortComparer.Default)
                .Select(e => new ImageEntry
                {
                    DisplayName = Path.GetFileName(e.Key ?? "Unknown"),
                    ArchiveEntryKey = e.Key
                })
                .ToList();
        }

        private static void ExtractSevenZipEntriesWorker(
            string archivePath,
            IReadOnlyList<ImageEntry> imageEntries,
            Func<int> getCurrentIndex,
            SevenZipExtractionCoordinator extraction,
            string tempFolder,
            bool[] extracted,
            object lockObj,
            CancellationToken token)
        {
            try
            {
                string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                using var archive = new ArchiveFile(archivePath, libraryPath);
                var entries = archive.Entries
                    .Where(e => !e.IsFolder && FileExplorerService.GetSupportedFileKind(e.FileName) == SupportedFileKind.Image)
                    .ToList();
                var entryMap = entries.ToDictionary(e => e.FileName!, e => e);

                while (!token.IsCancellationRequested)
                {
                    int targetIndex = FindNextExtractionIndex(extracted, getCurrentIndex, lockObj);
                    if (targetIndex == -1) break;

                    var imageEntry = imageEntries[targetIndex];
                    if (imageEntry.ArchiveEntryKey == null ||
                        !entryMap.TryGetValue(imageEntry.ArchiveEntryKey, out var archiveEntry))
                    {
                        continue;
                    }

                    ExtractSevenZipEntry(
                        archiveEntry,
                        imageEntry,
                        targetIndex,
                        extracted,
                        lockObj,
                        extraction,
                        tempFolder,
                        token);
                }
            }
            catch { }
        }

        private static int FindNextExtractionIndex(
            bool[] extracted,
            Func<int> getCurrentIndex,
            object lockObj)
        {
            int targetIndex = -1;
            lock (lockObj)
            {
                int current = getCurrentIndex();
                int bestDist = int.MaxValue;
                for (int i = 0; i < extracted.Length; i++)
                {
                    if (extracted[i]) continue;
                    int dist = Math.Abs(i - current);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        targetIndex = i;
                    }
                }

                if (targetIndex != -1) extracted[targetIndex] = true;
            }

            return targetIndex;
        }

        private static void ExtractSevenZipEntry(
            Entry archiveEntry,
            ImageEntry imageEntry,
            int targetIndex,
            bool[] extracted,
            object lockObj,
            SevenZipExtractionCoordinator extraction,
            string tempFolder,
            CancellationToken token)
        {
            string? tempExtractPath = null;

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, extraction.JumpToken);
                var linkedToken = linkedCts.Token;

                string ext = Path.GetExtension(imageEntry.ArchiveEntryKey ?? "") ?? "";
                string fileId = Guid.NewGuid().ToString("N");
                string outputPath = Path.Combine(tempFolder, fileId + ext);
                tempExtractPath = Path.Combine(tempFolder, fileId + ".tmp");

                linkedToken.ThrowIfCancellationRequested();
                archiveEntry.Extract(tempExtractPath);
                linkedToken.ThrowIfCancellationRequested();

                var fi = new FileInfo(tempExtractPath);
                if (fi.Exists && fi.Length > 0)
                {
                    File.Move(tempExtractPath, outputPath, true);
                    imageEntry.FilePath = outputPath;
                }
                else
                {
                    if (fi.Exists) fi.Delete();
                    lock (lockObj) extracted[targetIndex] = false;
                }
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempExtractPath);
                lock (lockObj) extracted[targetIndex] = false;
            }
            catch
            {
                TryDeleteFile(tempExtractPath);
            }
        }

        private static void DeleteWebDavTempArchiveIfNeeded(string? archivePath)
        {
            if (archivePath == null ||
                !archivePath.Contains(Path.Combine("Uviewer", "WebDav")))
            {
                return;
            }

            TryDeleteFile(archivePath);
        }

        private static void TryDeleteFile(string? path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        private string? FindEntryKeyLoose(string entryKey)
        {
            string normalizedTarget = NormalizeEntryKey(entryKey);

            if (CurrentArchive != null)
            {
                return CurrentArchive.Entries
                    .Select(e => e.Key)
                    .FirstOrDefault(key => key != null && NormalizeEntryKey(key) == normalizedTarget)
                    ?? CurrentArchive.Entries
                        .Select(e => e.Key)
                        .FirstOrDefault(key => key != null &&
                            string.Equals(NormalizeEntryKey(key), normalizedTarget, StringComparison.OrdinalIgnoreCase));
            }

            if (Current7zArchive != null)
            {
                return Current7zArchive.Entries
                    .Select(e => e.FileName)
                    .FirstOrDefault(key => key != null && NormalizeEntryKey(key) == normalizedTarget)
                    ?? Current7zArchive.Entries
                        .Select(e => e.FileName)
                        .FirstOrDefault(key => key != null &&
                            string.Equals(NormalizeEntryKey(key), normalizedTarget, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static string NormalizeEntryKey(string entryKey)
        {
            return entryKey.Replace('\\', '/').TrimStart('/');
        }
    }
}
