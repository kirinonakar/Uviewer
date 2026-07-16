using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class EpubSession : IDisposable
    {
        private readonly SemaphoreSlim _archiveLock = new(1, 1);

        public ZipArchive? Archive { get; set; }
        public SemaphoreSlim ArchiveLock => _archiveLock;
        public List<string> Spine { get; private set; } = new();
        public string? TocPath { get; set; }
        public int Version { get; private set; }

        public bool HasDocument => Archive != null && Spine.Count > 0;

        public async Task<EpubPackageInfo> OpenAsync(
            Stream stream,
            EpubDocumentService documentService,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CloseOpenArchive();

            Archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Version++;

            var packageInfo = await documentService.LoadPackageInfoAsync(Archive, _archiveLock, token);
            ReplacePackageInfo(packageInfo);
            return packageInfo;
        }

        public void ReplaceSpine(List<string> spine)
        {
            Spine = spine ?? new List<string>();
        }

        public void ReplacePackageInfo(EpubPackageInfo packageInfo)
        {
            Spine = packageInfo.Spine ?? new List<string>();
            TocPath = packageInfo.TocPath;
        }

        public async Task<string?> ReadEntryTextAsync(
            string path,
            EpubDocumentService documentService,
            CancellationToken token = default)
        {
            return Archive == null
                ? null
                : await documentService.ReadEntryTextAsync(Archive, path, _archiveLock, token);
        }

        public bool ContainsEntryLoose(string path)
        {
            if (Archive == null) return false;

            if (!_archiveLock.Wait(TimeSpan.FromMilliseconds(50)))
            {
                return true;
            }

            try
            {
                return FindEntryLoose(path) != null;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        public async Task<byte[]?> ReadEntryBytesLooseAsync(string path, CancellationToken token = default)
        {
            if (Archive == null) return null;

            await _archiveLock.WaitAsync(token);
            try
            {
                var entry = FindEntryLoose(path);
                if (entry == null) return null;

                using var stream = entry.Open();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, token);
                return memoryStream.ToArray();
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        public bool Close(TimeSpan timeout)
        {
            if (Archive == null) return true;

            if (!_archiveLock.Wait(timeout))
            {
                System.Diagnostics.Debug.WriteLine("EPUB lock timeout - cleanup deferred to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseOpenArchive();
                return true;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        public async Task<bool> CloseAsync(TimeSpan timeout)
        {
            if (Archive == null) return true;

            if (!await _archiveLock.WaitAsync(timeout))
            {
                System.Diagnostics.Debug.WriteLine("EPUB lock timeout - aborting format switch to avoid unsafe dispose");
                return false;
            }

            try
            {
                CloseOpenArchive();
                return true;
            }
            finally
            {
                _archiveLock.Release();
            }
        }

        public void Dispose()
        {
            // Never dispose the semaphore/archive while an EPUB read is still holding it.
            // A failed bounded close is left for process teardown rather than racing a reader.
            if (Close(TimeSpan.FromSeconds(2)))
            {
                _archiveLock.Dispose();
            }
        }

        private void CloseOpenArchive()
        {
            Archive?.Dispose();
            Archive = null;
            Spine.Clear();
            TocPath = null;
            Version++;
        }

        private ZipArchiveEntry? FindEntryLoose(string path)
        {
            if (Archive == null) return null;

            string normalizedPath = NormalizeZipPath(path);
            var entry = Archive.Entries.FirstOrDefault(e =>
                    NormalizeZipPath(e.FullName) == normalizedPath)
                ?? Archive.Entries.FirstOrDefault(e =>
                    string.Equals(NormalizeZipPath(e.FullName), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (entry != null) return entry;

            string fileName = Path.GetFileName(normalizedPath);
            if (string.IsNullOrEmpty(fileName)) return null;

            return Archive.Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(NormalizeZipPath(e.FullName)), fileName, StringComparison.OrdinalIgnoreCase))
                ?? Archive.Entries.FirstOrDefault(e =>
                    NormalizeZipPath(e.FullName).EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeZipPath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }
    }
}
