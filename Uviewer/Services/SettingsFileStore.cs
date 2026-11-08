using System;
using System.IO;
using System.Text.Json;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class SettingsFileStore
    {
        private readonly string _path;
        internal SettingsFileStore(string path) => _path = path;

        internal AppSettingsDocument? Read()
        {
            using var gate = ExclusiveFileLock.Acquire(_path + ".lock");
            return ReadCore();
        }

        internal void Update(Func<AppSettingsDocument?, AppSettingsDocument> update)
        {
            using var gate = ExclusiveFileLock.Acquire(_path + ".lock");
            var document = update(ReadCore());
            AtomicFileWriter.Write(_path, output =>
                JsonSerializer.Serialize(output, document, AppSettingsJsonContext.Default.AppSettingsDocument));
        }

        private AppSettingsDocument? ReadCore()
        {
            var document = TryRead(_path);
            return document ?? TryRead(_path + ".bak");
        }

        private static AppSettingsDocument? TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var input = File.OpenRead(path);
                return JsonSerializer.Deserialize(input, AppSettingsJsonContext.Default.AppSettingsDocument)
                    ?? throw new JsonException("Settings document is null.");
            }
            catch (JsonException ex)
            {
                // Preserve the damaged file before any later save can replace it.
                string bad = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N") + ".bad";
                File.Move(path, bad);
                System.Diagnostics.Debug.WriteLine($"Invalid settings preserved at {bad}: {ex.Message}");
                return null;
            }
        }
    }
}
