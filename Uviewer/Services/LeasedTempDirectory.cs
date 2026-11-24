using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class LeasedTempDirectory : IDisposable
    {
        private const string LeaseName = ".active";
        private FileStream? _lease;
        internal string Folder { get; }

        private LeasedTempDirectory(string folder, FileStream lease) => (Folder, _lease) = (folder, lease);

        internal static LeasedTempDirectory Create(string root, string prefix)
        {
            // Serialize creation with scavenging, including the mkdir -> open lease gap.
            using var gate = ExclusiveFileLock.Acquire(Path.Combine(root, ".cleanup.lock"));
            string folder = Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return new LeasedTempDirectory(folder,
                new FileStream(Path.Combine(folder, LeaseName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
        }

        internal static void CleanupAbandoned(string root, string pattern, bool protectRecentUnleasedFolders = true)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (string folder in Directory.EnumerateDirectories(root, pattern))
                    TryDeleteInactive(folder, protectRecentUnleasedFolders);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Temp cleanup failed: {ex.Message}"); }
        }

        public void Dispose()
        {
            var lease = Interlocked.Exchange(ref _lease, null);
            lease?.Dispose();
            try
            {
                TryDeleteInactive(Folder, protectRecentUnleasedFolders: false);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Temp disposal failed: {ex.Message}"); }
        }

        internal async Task DeleteAsync()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Dispose();
                if (!Directory.Exists(Folder)) return;
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
            // Keep the marker if a stream still prevents deletion; startup retries it.
        }

        private static void TryDeleteInactive(string folder, bool protectRecentUnleasedFolders)
        {
            try
            {
                string leasePath = Path.Combine(folder, LeaseName);
                string gatePath = Path.Combine(Path.GetDirectoryName(folder)!, ".cleanup.lock");
                FileStream lease;
                using (var gate = ExclusiveFileLock.Acquire(gatePath))
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return;
                    // Older builds did not lease their folders. Give those folders a grace period.
                    if (protectRecentUnleasedFolders && !File.Exists(leasePath) &&
                        Directory.GetLastWriteTimeUtc(folder) > DateTime.UtcNow.AddDays(-1)) return;
                    lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                using (lease)
                {
                    // Only this folder's lease is held during bulk I/O. Creating
                    // another owner's folder never waits for recursive deletion.
                    foreach (string item in Directory.EnumerateFileSystemEntries(folder))
                    {
                        if (string.Equals(item, leasePath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (Directory.Exists(item)) Directory.Delete(item, recursive: true);
                        else File.Delete(item);
                    }
                    using var gate = ExclusiveFileLock.Acquire(gatePath);
                    lease.Dispose();
                    File.Delete(leasePath);
                    try { Directory.Delete(folder); }
                    catch
                    {
                        // Preserve immediate startup reclamation even if only the
                        // final directory removal failed after deleting the lease.
                        if (Directory.Exists(folder))
                        {
                            using var marker = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        }
                        throw;
                    }
                }
            }
            catch (IOException) { } // A live owner (or open image/stream) keeps its folder.
            catch (UnauthorizedAccessException) { }
        }
    }
}
