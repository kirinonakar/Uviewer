using System;
using System.Diagnostics;
using System.IO;

namespace Uviewer.Services;

internal static class TemporaryFiles
{
    private const string AnimationPrefix = "Uviewer-animation-";

    internal static string Root => Path.Combine(Path.GetTempPath(), "Uviewer");
    internal static string WebDavRoot => Path.Combine(Root, "WebDav");

    internal static string NewAnimationPath() => Path.Combine(Path.GetTempPath(), $"{AnimationPrefix}{Guid.NewGuid():N}.tmp");

    internal static void CleanupAbandoned()
    {
        ThumbnailDiskCache.CleanupAbandoned();
        LeasedTempDirectory.CleanupAbandoned(Root, "7z_*");
        LeasedTempDirectory.CleanupAbandoned(WebDavRoot, "*");
        DeleteAbandonedSpools(WebDavRoot, "stream_", "*");
        DeleteAbandonedSpools(Path.GetTempPath(), AnimationPrefix, "*.tmp");
    }

    private static void DeleteAbandonedSpools(string directory, string prefix, string suffixPattern)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.EnumerateFiles(directory, prefix + suffixPattern))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != prefix.Length + 32 ||
                    !Guid.TryParseExact(name.AsSpan(prefix.Length), "N", out _)) continue;

                // FileShare.None protects spools belonging to a running instance.
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException ex) { Debug.WriteLine(ex); }
        catch (UnauthorizedAccessException ex) { Debug.WriteLine(ex); }
    }
}
