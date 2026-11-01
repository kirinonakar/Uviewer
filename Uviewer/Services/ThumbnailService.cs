using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SharpCompress.Archives;
using Uviewer;
using Uviewer.Models;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Uviewer.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4);
        private readonly ThumbnailDiskCache _diskCache = new();
        private readonly ThumbnailMemoryCache<BitmapImage> _memoryCache = new();
        private readonly ConcurrentDictionary<string, FolderCover> _folderCovers = new(StringComparer.OrdinalIgnoreCase);
        private sealed record FolderCover(long ModifiedTicks, string? ImagePath);

        public async Task LoadThumbnailsAsync(
            IEnumerable<FileItem> items,
            DispatcherQueue dispatcher,
            CancellationToken token,
            int decodePixelWidth,
            bool includeFolderThumbnails)
        {
            try
            {
                decodePixelWidth = Math.Clamp(decodePixelWidth, 128, 512);

                // Filter items that actually need loading
                var itemList = items.Where(i => i.Thumbnail == null && 
                                              !i.IsThumbnailLoading && 
                                              (i.IsImage || i.IsArchive || i.IsEpub ||
                                               (includeFolderThumbnails && i.IsDirectory && !i.IsParentDirectory && !i.IsDrive && !i.IsWebDav))).ToList();

                if (itemList.Count == 0) return;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = token
                };

                await Parallel.ForEachAsync(itemList, parallelOptions, async (item, ct) =>
                {
                    if (ct.IsCancellationRequested) return;

                    item.IsThumbnailLoading = true;
                    try
                    {
                        await LoadItemThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load thumbnail for {item.Name}: {ex.Message}");
                    }
                    finally
                    {
                        item.IsThumbnailLoading = false;
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail load loop error: {ex.Message}");
            }
        }

        private async Task LoadItemThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int size)
        {
            string? sourcePath = item.IsDirectory ? GetFolderCoverPath(item.FullPath) : item.FullPath;
            if (sourcePath == null) return;
            ct.ThrowIfCancellationRequested();
            string? key = ThumbnailDiskCache.CreateKey(sourcePath, size);
            if (key != null)
            {
                if (await TrySetMemoryThumbnailAsync(item, dispatcher, ct, key)) return;
                var bytes = await _diskCache.ReadAsync(key, ct);
                if (bytes != null && await SetThumbnailAsync(item, dispatcher, ct, size, bytes, key)) return;
            }

            if (item.IsArchive || item.IsEpub)
                await LoadArchiveThumbnailAsync(item, dispatcher, ct, size, key);
            else
                await LoadImageThumbnailAsync(item, sourcePath, dispatcher, ct, size, key);
        }

        private string? GetFolderCoverPath(string folderPath)
        {
            long modified = Directory.GetLastWriteTimeUtc(folderPath).Ticks;
            if (_folderCovers.TryGetValue(folderPath, out var cover) && cover.ModifiedTicks == modified &&
                (cover.ImagePath == null || File.Exists(cover.ImagePath)))
                return cover.ImagePath;

            string? firstImage = Directory.EnumerateFiles(folderPath)
                .Where(path => FileExplorerService.GetSupportedFileKind(path) == SupportedFileKind.Image)
                .OrderBy(Path.GetFileName, NaturalSortComparer.Default)
                .FirstOrDefault();
            if (_folderCovers.Count >= 1024) _folderCovers.Clear();
            _folderCovers[folderPath] = new FolderCover(modified, firstImage);
            return firstImage;
        }

        private async Task<bool> TrySetMemoryThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, string key)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            bool enqueued = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (_memoryCache.TryGet(key, out var bitmap))
                    {
                        item.Thumbnail = bitmap;
                        tcs.TrySetResult(true);
                    }
                    else tcs.TrySetResult(false);
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(ct); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            if (!enqueued) tcs.TrySetResult(false);
            return await tcs.Task;
        }

        private async Task LoadArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth, string? cacheKey)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                using var image = new MemoryStream();
                if (Path.GetExtension(item.FullPath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                    using var archive = new SevenZipExtractor.ArchiveFile(item.FullPath, libraryPath);
                    var entry = archive.Entries
                        .Where(e => !e.IsFolder && FileExplorerService.GetSupportedFileKind(e.FileName) == SupportedFileKind.Image)
                        .OrderBy(e => e.FileName, NaturalSortComparer.Default)
                        .FirstOrDefault();
                    if (entry == null) return;
                    entry.Extract(image);
                }
                else
                {
                    using var archive = ArchiveFactory.OpenArchive(item.FullPath);
                    var entry = archive.Entries
                        .Where(e => !e.IsDirectory && FileExplorerService.GetSupportedFileKind(e.Key) == SupportedFileKind.Image)
                        .OrderBy(e => e.Key, NaturalSortComparer.Default)
                        .FirstOrDefault();
                    if (entry == null) return;
                    using var entryStream = entry.OpenEntryStream();
                    await entryStream.CopyToAsync(image, ct);
                }

                ct.ThrowIfCancellationRequested();
                byte[]? encoded = null;
                try
                {
                    encoded = await ArchiveThumbnailEncoder.EncodeAsync(image, decodePixelWidth, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Thumbnail cache encoding skipped: {ex.Message}");
                }

                await StoreAndSetThumbnailAsync(item, item.FullPath, dispatcher, ct, decodePixelWidth,
                    encoded ?? image.ToArray(), encoded == null ? null : cacheKey);
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        private async Task<bool> SetThumbnailAsync(
            FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth, byte[] bytes, string? cacheKey = null)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            bool enqueued = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    // The callback owns its stream, including when cancellation releases the caller early.
                    using var stream = new MemoryStream(bytes, writable: false);
                    using var source = stream.AsRandomAccessStream();
                    var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth };
                    await bitmap.SetSourceAsync(source);
                    if (cacheKey != null)
                        _memoryCache.Add(cacheKey, bitmap, Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4));
                    ct.ThrowIfCancellationRequested();
                    item.Thumbnail = bitmap;
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(ct); }
                catch { tcs.TrySetResult(false); }
            });
            if (!enqueued) tcs.TrySetResult(false);
            return await tcs.Task;
        }

        private async Task StoreAndSetThumbnailAsync(
            FileItem item, string sourcePath, DispatcherQueue dispatcher, CancellationToken ct,
            int size, byte[] bytes, string? cacheKey)
        {
            if (cacheKey != null && cacheKey == ThumbnailDiskCache.CreateKey(sourcePath, size))
                await _diskCache.WriteAsync(cacheKey, bytes, ct);
            // Commit the generated bytes before waiting for XAML. Leaving the folder during
            // display should not discard a completed thumbnail and force regeneration.
            await SetThumbnailAsync(item, dispatcher, ct, size, bytes, cacheKey);
        }

        private async Task LoadImageThumbnailAsync(
            FileItem item, string sourcePath, DispatcherQueue dispatcher, CancellationToken ct,
            int decodePixelWidth, string? cacheKey)
        {
            var file = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var thumbnail = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                (uint)decodePixelWidth);
            if (thumbnail != null)
                await SetThumbnailFromStreamAsync(item, dispatcher, ct, decodePixelWidth, thumbnail, sourcePath, cacheKey);
        }

        private async Task SetThumbnailFromStreamAsync(
            FileItem item,
            DispatcherQueue dispatcher,
            CancellationToken ct,
            int decodePixelWidth,
            IRandomAccessStream thumbnail,
            string sourcePath,
            string? cacheKey)
        {
            // StorageItemThumbnail belongs to the worker. Snapshot it before dispatching so
            // cancellation cannot dispose it while XAML's native decoder is still reading.
            using var bytes = new MemoryStream();
            using var source = thumbnail.AsStreamForRead();
            await source.CopyToAsync(bytes, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await StoreAndSetThumbnailAsync(item, sourcePath, dispatcher, ct, decodePixelWidth, bytes.ToArray(), cacheKey).ConfigureAwait(false);
        }
    }
}
