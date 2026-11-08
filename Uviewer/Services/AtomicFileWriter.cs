using System;
using System.IO;

namespace Uviewer.Services;

internal static class AtomicFileWriter
{
    // The caller holds the file's ExclusiveFileLock across its read/modify/write.
    internal static void Write(string path, Action<Stream> write)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(output);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
