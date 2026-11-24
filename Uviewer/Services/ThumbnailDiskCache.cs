using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class ThumbnailDiskCache
    {
        private const int MaxEntryBytes = 2 * 1024 * 1024;
        private const long MaxBytes = 512 * 1024 * 1024;
        private const int MaxEntries = 8192;
        private const string ArchiveCacheName = "ArchiveThumbnails";
        internal const string PeleCacheName = "PeleRaster-win2d-v2";
        private const string SessionPrefix = "session_";
        private static readonly SemaphoreSlim Gate = new(1);
        private static LeasedTempDirectory? _sessionDirectory;
        private static bool _closed;
        private readonly string _cacheName;
        private readonly long _maxBytes;
        private readonly int _maxEntries;
        private bool _sizeKnown;
        private long _estimatedBytes;
        private int _estimatedCount;
        private int _writesSinceScan;

        private static string CacheRoot => Path.Combine(TemporaryFiles.Root, "Cache");

        public ThumbnailDiskCache(string cacheName = ArchiveCacheName, long maxBytes = MaxBytes, int maxEntries = MaxEntries)
        {
            _maxBytes = maxBytes;
            _maxEntries = maxEntries;
            _cacheName = cacheName;
        }

        internal static void CleanupAbandoned()
        {
            string root = CacheRoot;
            LeasedTempDirectory.CleanupAbandoned(root, SessionPrefix + "*", protectRecentUnleasedFolders: false);
            // Earlier versions stored these disposable caches in shared directories.
            LeasedTempDirectory.CleanupAbandoned(root, ArchiveCacheName, protectRecentUnleasedFolders: false);
            LeasedTempDirectory.CleanupAbandoned(root, PeleCacheName, protectRecentUnleasedFolders: false);
        }

        internal static Task CleanupForShutdownAsync() => Task.Run(async () =>
        {
            LeasedTempDirectory? directory;
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Queued thumbnail and diagram workers must not recreate the cache.
                _closed = true;
                directory = _sessionDirectory;
                _sessionDirectory = null;
            }
            finally { Gate.Release(); }

            if (directory != null) await directory.DeleteAsync().ConfigureAwait(false);
        });

        public static string? CreateKey(string archivePath, int pixelWidth)
        {
            try
            {
                var info = new FileInfo(archivePath);
                string identity = $"v1\n{info.FullName.ToUpperInvariant()}\n{info.Length}\n{info.LastWriteTimeUtc.Ticks}\n{pixelWidth}";
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public async Task<byte[]?> ReadAsync(string key, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_sessionDirectory == null) return null;
                string path = Path.Combine(_sessionDirectory.Folder, _cacheName, key + ".thumb");
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > MaxEntryBytes) return null;
                var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return bytes;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            finally { Gate.Release(); }
        }

        public async Task WriteAsync(string key, byte[] bytes, CancellationToken token)
        {
            if (bytes.Length == 0 || bytes.Length > MaxEntryBytes) return;
            await Gate.WaitAsync(token).ConfigureAwait(false);
            string? partial = null;
            try
            {
                if (_closed) return;
                _sessionDirectory ??= LeasedTempDirectory.Create(CacheRoot, SessionPrefix);
                string folder = Path.Combine(_sessionDirectory.Folder, _cacheName);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, key + ".thumb");
                partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
                await File.WriteAllBytesAsync(partial, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(partial, path, overwrite: true);
                partial = null;
                // Overwrites overestimate usage, so these counters cannot defer necessary eviction.
                _estimatedBytes += bytes.Length;
                _estimatedCount++;
                _writesSinceScan++;
                if (!_sizeKnown || _estimatedBytes > _maxBytes || _estimatedCount > _maxEntries || _writesSinceScan >= 256)
                    Trim(folder);
            }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine(ex); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine(ex); }
            finally
            {
                if (partial != null) Delete(partial);
                Gate.Release();
            }
        }

        private void Trim(string folder)
        {
            var files = new DirectoryInfo(folder).GetFiles("*.thumb")
                .OrderBy(f => f.LastWriteTimeUtc).ToList();
            long bytes = files.Sum(f => f.Length);
            int count = files.Count;
            // Leave room for new entries to avoid scanning a full cache on every write.
            long targetBytes = bytes > _maxBytes ? _maxBytes * 9 / 10 : _maxBytes;
            int targetCount = count > _maxEntries ? _maxEntries * 9 / 10 : _maxEntries;
            foreach (var file in files)
            {
                if (bytes <= targetBytes && count <= targetCount) break;
                if (Delete(file.FullName))
                {
                    bytes -= file.Length;
                    count--;
                }
            }
            _estimatedBytes = bytes;
            _estimatedCount = count;
            _sizeKnown = true;
            _writesSinceScan = 0;
        }

        private static bool Delete(string path)
        {
            try { File.Delete(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
