using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextDocumentLoadService
    {
        private readonly TextFileReaderService _textFileReaderService;
        private readonly ArchiveSession _archiveSession;

        public TextDocumentLoadService(
            TextFileReaderService textFileReaderService,
            ArchiveSession archiveSession)
        {
            _textFileReaderService = textFileReaderService;
            _archiveSession = archiveSession;
        }

        public async Task<string> ReadLocalFileAsync(
            StorageFile file,
            string encodingName,
            CancellationToken token = default)
        {
            string content = await _textFileReaderService.ReadAsync(file, encodingName);
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
                : _textFileReaderService.Decode(bytes, encodingName);
        }
    }
}
