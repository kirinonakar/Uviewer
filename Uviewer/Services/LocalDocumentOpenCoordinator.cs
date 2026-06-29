using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Uviewer.Services
{
    internal sealed class LocalDocumentOpenHandlers
    {
        public Func<string, Task> OpenArchiveAsync { get; init; } = null!;
        public Func<string, Task> OpenPdfAsync { get; init; } = null!;
        public Func<StorageFile, bool, Task> OpenStorageFileAsync { get; init; } = null!;
        public Func<StorageFolder, Task> OpenFolderAsync { get; init; } = null!;
        public Func<Task>? SaveCurrentPositionAsync { get; init; }
        public Action<string> LoadExplorerFolder { get; init; } = null!;
        public Action<string>? LoadExplorerFolderInBackground { get; init; }
        public Func<string, bool>? ShouldLoadExplorerFolder { get; init; }
        public Action? HideEmptyState { get; init; }
    }

    internal sealed class LocalDocumentOpenCoordinator
    {
        private readonly LocalDocumentOpenHandlers _handlers;

        public LocalDocumentOpenCoordinator(LocalDocumentOpenHandlers handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        public void AddPickerFileTypeFilters(IList<string> fileTypeFilter)
        {
            foreach (var extension in FileExplorerService.SupportedFileExtensions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                fileTypeFilter.Add(extension);
            }
        }

        public async Task OpenLaunchPathAsync(string launchPath)
        {
            var path = NormalizePath(launchPath);

            if (File.Exists(path))
            {
                _handlers.HideEmptyState?.Invoke();

                await OpenFilePathAsync(
                    path,
                    isInitial: true,
                    saveBeforeOpen: false,
                    saveOnlyForImageOrText: false,
                    allowUnknownAsStorageFile: true);

                LoadContainingExplorerFolder(path, inBackground: true, onlyIfChanged: false);
                return;
            }

            if (Directory.Exists(path))
            {
                _handlers.LoadExplorerFolder(path);
            }
        }

        public async Task OpenPickedFileAsync(StorageFile file)
        {
            if (file == null) return;

            await OpenStorageFileAsync(
                file,
                isInitial: false,
                saveBeforeOpen: true,
                saveOnlyForImageOrText: true,
                allowUnknownAsStorageFile: false);

            LoadContainingExplorerFolder(file.Path, inBackground: false, onlyIfChanged: true);
        }

        public async Task OpenDroppedStorageItemAsync(IStorageItem item)
        {
            switch (item)
            {
                case StorageFile file:
                    await OpenStorageFileAsync(
                        file,
                        isInitial: false,
                        saveBeforeOpen: false,
                        saveOnlyForImageOrText: false,
                        allowUnknownAsStorageFile: false);
                    LoadContainingExplorerFolder(file.Path, inBackground: false, onlyIfChanged: false);
                    break;

                case StorageFolder folder:
                    _handlers.LoadExplorerFolder(folder.Path);
                    await _handlers.OpenFolderAsync(folder);
                    break;
            }
        }

        public Task OpenExistingFilePathAsync(string path, bool saveCurrentPositionBeforeOpen)
        {
            return OpenFilePathAsync(
                path,
                isInitial: false,
                saveBeforeOpen: saveCurrentPositionBeforeOpen,
                saveOnlyForImageOrText: false,
                allowUnknownAsStorageFile: true);
        }

        private async Task<bool> OpenFilePathAsync(
            string path,
            bool isInitial,
            bool saveBeforeOpen,
            bool saveOnlyForImageOrText,
            bool allowUnknownAsStorageFile)
        {
            var kind = ClassifyExtension(Path.GetExtension(path));
            if (kind == SupportedFileKind.Unsupported && !allowUnknownAsStorageFile)
            {
                return false;
            }

            if (saveBeforeOpen && (!saveOnlyForImageOrText || IsImageOrText(kind)))
            {
                await SaveCurrentPositionAsync();
            }

            switch (kind)
            {
                case SupportedFileKind.Archive:
                    await _handlers.OpenArchiveAsync(path);
                    return true;

                case SupportedFileKind.Pdf:
                    await _handlers.OpenPdfAsync(path);
                    return true;

                case SupportedFileKind.Epub:
                case SupportedFileKind.Image:
                case SupportedFileKind.Text:
                case SupportedFileKind.Unsupported:
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    await _handlers.OpenStorageFileAsync(file, isInitial);
                    return true;

                default:
                    return false;
            }
        }

        private async Task<bool> OpenStorageFileAsync(
            StorageFile file,
            bool isInitial,
            bool saveBeforeOpen,
            bool saveOnlyForImageOrText,
            bool allowUnknownAsStorageFile)
        {
            var kind = ClassifyExtension(Path.GetExtension(file.Path));
            if (kind == SupportedFileKind.Unsupported && !allowUnknownAsStorageFile)
            {
                return false;
            }

            if (saveBeforeOpen && (!saveOnlyForImageOrText || IsImageOrText(kind)))
            {
                await SaveCurrentPositionAsync();
            }

            switch (kind)
            {
                case SupportedFileKind.Archive:
                    await _handlers.OpenArchiveAsync(file.Path);
                    return true;

                case SupportedFileKind.Pdf:
                    await _handlers.OpenPdfAsync(file.Path);
                    return true;

                case SupportedFileKind.Epub:
                case SupportedFileKind.Image:
                case SupportedFileKind.Text:
                case SupportedFileKind.Unsupported:
                    await _handlers.OpenStorageFileAsync(file, isInitial);
                    return true;

                default:
                    return false;
            }
        }

        private async Task SaveCurrentPositionAsync()
        {
            if (_handlers.SaveCurrentPositionAsync != null)
            {
                await _handlers.SaveCurrentPositionAsync();
            }
        }

        private void LoadContainingExplorerFolder(string filePath, bool inBackground, bool onlyIfChanged)
        {
            var folderPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            if (onlyIfChanged && _handlers.ShouldLoadExplorerFolder?.Invoke(folderPath) == false)
            {
                return;
            }

            if (inBackground && _handlers.LoadExplorerFolderInBackground != null)
            {
                _handlers.LoadExplorerFolderInBackground(folderPath);
                return;
            }

            _handlers.LoadExplorerFolder(folderPath);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path.Trim('"'));
        }

        private static SupportedFileKind ClassifyExtension(string? extension) =>
            FileExplorerService.GetSupportedFileKind(extension);

        private static bool IsImageOrText(SupportedFileKind kind) =>
            kind == SupportedFileKind.Image || kind == SupportedFileKind.Text;
    }
}
