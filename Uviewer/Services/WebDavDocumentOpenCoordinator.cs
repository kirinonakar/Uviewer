using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class WebDavDocumentOpenHandlers
    {
        public Func<string, Task> LoadFolderAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentPdfAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentEpubAsync { get; init; } = null!;
        public Func<Task<bool>> CloseCurrentArchiveAsync { get; init; } = null!;
        public Action<string> SetCurrentItemPath { get; init; } = null!;
        public Action ClearImageResources { get; init; } = null!;
        public Action<string> SetStatusText { get; init; } = null!;
        public Func<string, string> CreateLoadingStatus { get; init; } = name => name;
        public Func<string> CreateDownloadFailedStatus { get; init; } = () => "Download failed";
        public Func<Exception, string> CreateFileOpenFailedStatus { get; init; } = ex => $"File open failed: {ex.Message}";
        public Func<Exception, string> CreateArchiveOpenFailedStatus { get; init; } = ex => $"Archive open failed: {ex.Message}";
        public Func<CancellationToken> RestartOperation { get; init; } = null!;
        public Func<string, CancellationToken, Task<string?>> DownloadToTempFileAsync { get; init; } = null!;
        public Func<string, CancellationToken, Task<Stream?>> DownloadFileAsync { get; init; } = null!;
        public Func<string, Task> OpenLocalArchiveAsync { get; init; } = null!;
        public Func<string, Task> OpenLocalPdfAsync { get; init; } = null!;
        public Func<string, string, ImageEntry?> PrepareSequentialEntries { get; init; } = null!;
        public Func<StorageFile, ImageEntry?, CancellationToken, Task> OpenEpubFileAsync { get; init; } = null!;
        public Func<Task> DisplayCurrentImageAsync { get; init; } = null!;
        public Action StartPreload { get; init; } = null!;
        public Func<string, Stream, Task> OpenArchiveStreamAsync { get; init; } = null!;
        public Action<string> Log { get; init; } = _ => { };
    }

    internal sealed class WebDavDocumentOpenCoordinator
    {
        private readonly WebDavDocumentOpenHandlers _handlers;

        public WebDavDocumentOpenCoordinator(WebDavDocumentOpenHandlers handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public async Task OpenItemAsync(FileItem item)
        {
            if (!IsValidWebDavItem(item)) return;

            if (item.IsDirectory)
            {
                await _handlers.LoadFolderAsync(item.WebDavPath!);
                return;
            }

            if (item.IsArchive && !RequiresTempDownload(item))
            {
                await OpenStreamedArchiveAsync(item);
                return;
            }

            if (item.IsArchive || item.IsImage || item.IsText || item.IsEpub || item.IsPdf)
            {
                await OpenDownloadedFileAsync(item);
            }
        }

        public async Task OpenDownloadedFileAsync(FileItem item)
        {
            if (!IsValidWebDavItem(item)) return;
            if (!await PrepareOpenAsync(item)) return;

            var token = _handlers.RestartOperation();

            try
            {
                var tempPath = await _handlers.DownloadToTempFileAsync(item.WebDavPath!, token);
                if (string.IsNullOrEmpty(tempPath))
                {
                    _handlers.SetStatusText(_handlers.CreateDownloadFailedStatus());
                    return;
                }

                await OpenDownloadedTempFileAsync(item, tempPath, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _handlers.SetStatusText(_handlers.CreateFileOpenFailedStatus(ex));
                _handlers.Log($"WebDAV open file error: {ex.Message}");
            }
        }

        public async Task OpenStreamedArchiveAsync(FileItem item)
        {
            if (!IsValidWebDavItem(item)) return;
            if (!await PrepareOpenAsync(item)) return;

            var token = _handlers.RestartOperation();

            try
            {
                var stream = await _handlers.DownloadFileAsync(item.WebDavPath!, token);
                if (stream == null)
                {
                    _handlers.SetStatusText(_handlers.CreateDownloadFailedStatus());
                    return;
                }

                await _handlers.OpenArchiveStreamAsync(item.WebDavPath!, stream);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _handlers.SetStatusText(_handlers.CreateArchiveOpenFailedStatus(ex));
                _handlers.Log($"WebDAV archive error: {ex.Message}");
            }
        }

        private async Task OpenDownloadedTempFileAsync(FileItem item, string tempPath, CancellationToken token)
        {
            var fileKind = FileExplorerService.GetSupportedFileKind(item.WebDavPath ?? item.Name);

            switch (fileKind)
            {
                case SupportedFileKind.Archive:
                    await _handlers.OpenLocalArchiveAsync(tempPath);
                    return;

                case SupportedFileKind.Pdf:
                    await _handlers.OpenLocalPdfAsync(tempPath);
                    return;

                case SupportedFileKind.Epub:
                    var epubEntry = _handlers.PrepareSequentialEntries(item.WebDavPath!, tempPath);
                    var epubFile = await StorageFile.GetFileFromPathAsync(tempPath);
                    await _handlers.OpenEpubFileAsync(epubFile, epubEntry, token);
                    return;

                default:
                    _handlers.PrepareSequentialEntries(item.WebDavPath!, tempPath);
                    await _handlers.DisplayCurrentImageAsync();
                    _handlers.StartPreload();
                    return;
            }
        }

        private async Task<bool> PrepareOpenAsync(FileItem item)
        {
            if (!await _handlers.CloseCurrentPdfAsync()) return false;
            if (!await _handlers.CloseCurrentEpubAsync()) return false;
            if (!await _handlers.CloseCurrentArchiveAsync()) return false;

            _handlers.SetCurrentItemPath(item.WebDavPath!);
            _handlers.ClearImageResources();
            _handlers.SetStatusText(_handlers.CreateLoadingStatus(item.Name));
            return true;
        }

        private static bool IsValidWebDavItem(FileItem item) =>
            item.IsWebDav && !string.IsNullOrEmpty(item.WebDavPath);

        private static bool RequiresTempDownload(FileItem item) =>
            string.Equals(
                Path.GetExtension(item.WebDavPath ?? item.Name),
                ".7z",
                StringComparison.OrdinalIgnoreCase);
    }
}
