using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Windows.Storage;
using Windows.Storage.Streams;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextDocumentLoadService
    {
        private readonly ArchiveSession _archiveSession;

        public TextDocumentLoadService(ArchiveSession archiveSession)
        {
            _archiveSession = archiveSession;
        }

        public async Task<string> ReadLocalFileAsync(
            StorageFile file,
            string encodingName,
            CancellationToken token = default)
        {
            string content = await ReadAsync(file, encodingName);
            token.ThrowIfCancellationRequested();
            return content;
        }

        public async Task<string> ReadArchiveEntryAsync(
            ImageEntry entry,
            string encodingName,
            CancellationToken token = default)
        {
            if (entry.ArchiveEntryKey == null)
            {
                return string.Empty;
            }

            var bytes = await _archiveSession.ReadEntryBytesAsync(entry.ArchiveEntryKey, token);
            token.ThrowIfCancellationRequested();
            return bytes == null
                ? string.Empty
                : Decode(bytes, encodingName);
        }

        private static async Task<string> ReadAsync(StorageFile file, string encodingName)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            using var dataReader = DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[buffer.Length];
            dataReader.ReadBytes(bytes);

            return Decode(bytes, encodingName);
        }

        private static string Decode(byte[] bytes, string encodingName)
        {
            Encoding encoding = TextEncodingService.GetTextEncoding(bytes, encodingName);
            return encoding.GetString(bytes);
        }
    }
}
