using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uviewer.Services;

internal sealed class WebDavServerMappingStore(string path)
{
    internal Dictionary<string, string> Read()
    {
        using var gate = ExclusiveFileLock.Acquire(path + ".lock");
        return ReadCore();
    }

    internal void Update(Action<Dictionary<string, string>> update)
    {
        using var gate = ExclusiveFileLock.Acquire(path + ".lock");
        var mappings = ReadCore();
        update(mappings);
        var json = JsonSerializer.SerializeToUtf8Bytes(mappings, WebDavMappingJsonContext.Default.DictionaryStringString);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        var bytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(encrypted));
        AtomicFileWriter.Write(path, output => output.Write(bytes));
    }

    private Dictionary<string, string> ReadCore()
    {
        bool hasPrimary = File.Exists(path);
        bool hasBackup = File.Exists(path + ".bak");
        if (!hasPrimary && !hasBackup) return new();

        Exception? primaryError = null;
        if (hasPrimary)
        {
            try { return ReadFile(path); }
            catch (Exception ex) when (IsCorrupt(ex)) { primaryError = ex; }
        }

        if (hasBackup)
        {
            Dictionary<string, string> recovered;
            try { recovered = ReadFile(path + ".bak"); }
            catch (Exception ex) when (IsCorrupt(ex))
            {
                throw new InvalidDataException("WebDAV server mappings and backup could not be read. Files were preserved.", ex);
            }
            // Do not replace a valid backup with the damaged primary on the next save.
            if (hasPrimary)
                File.Move(path, path + "." + Guid.NewGuid().ToString("N") + ".bad");
            return recovered;
        }

        // Keep the damaged file in place: later saves must also fail until recovery.
        throw new InvalidDataException("WebDAV server mappings could not be read. The file was preserved.", primaryError);
    }

    private static Dictionary<string, string> ReadFile(string file)
    {
        var encrypted = Convert.FromBase64String(File.ReadAllText(file));
        var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        var mappings = JsonSerializer.Deserialize(json, WebDavMappingJsonContext.Default.DictionaryStringString)
            ?? throw new JsonException("WebDAV server mappings are null.");
        foreach (var value in mappings.Values)
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("WebDAV server URL is empty.");
        return mappings;
    }

    private static bool IsCorrupt(Exception ex) => ex is FormatException or CryptographicException or JsonException;
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class WebDavMappingJsonContext : JsonSerializerContext { }
