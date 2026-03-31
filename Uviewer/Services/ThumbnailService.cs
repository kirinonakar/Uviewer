using System;
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

namespace Uviewer.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4);

        public async Task LoadThumbnailsAsync(IEnumerable<FileItem> items, DispatcherQueue dispatcher, CancellationToken token)
        {
            try
            {
                var itemList = items.ToList();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 2,
                    CancellationToken = token
                };

                await Parallel.ForEachAsync(itemList, parallelOptions, async (item, ct) =>
                {
                    if (!item.IsImage && !item.IsArchive && !item.IsEpub) return;

                    await Task.Delay(10, ct);

                    try
                    {
                        if (item.IsArchive || item.IsEpub)
                        {
                            await LoadArchiveThumbnailAsync(item, dispatcher, ct);
                        }
                        else if (item.IsImage)
                        {
                            await LoadImageThumbnailAsync(item, dispatcher, ct);
                        }
                    }
                    catch
                    {
                        // Ignore individual failures
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Normal termination on cancellation
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail load error: {ex.Message}");
            }
        }

        private async Task LoadArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            string ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
            if (ext == ".7z")
            {
                await Load7zThumbnailAsync(item, dispatcher, ct);
            }
            else
            {
                await LoadGeneralArchiveThumbnailAsync(item, dispatcher, ct);
            }
        }

        private async Task Load7zThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                using var archive = new SevenZipExtractor.ArchiveFile(item.FullPath, libraryPath);
                var entry = archive.Entries
                    .Where(e => !e.IsFolder &&
                           FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.FileName)?.ToLowerInvariant() ?? ""))
                    .OrderBy(e => e.FileName, NaturalSortComparer.Default)
                    .FirstOrDefault();

                if (entry != null)
                {
                    var memStream = new MemoryStream();
                    entry.Extract(memStream);
                    memStream.Position = 0;

                    dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                    {
                        if (ct.IsCancellationRequested)
                        {
                            memStream.Dispose();
                            return;
                        }

                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 200;
                            await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                            item.Thumbnail = bitmap;
                        }
                        catch
                        {
                            memStream.Dispose();
                        }
                    });
                }
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        private async Task LoadGeneralArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                using var archive = ArchiveFactory.Open(item.FullPath);
                var entry = archive.Entries
                    .Where(e => !e.IsDirectory &&
                           FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.Key)?.ToLowerInvariant() ?? ""))
                    .OrderBy(e => e.Key, NaturalSortComparer.Default)
                    .FirstOrDefault();

                if (entry != null)
                {
                    using var entryStream = entry.OpenEntryStream();
                    var memStream = new MemoryStream();
                    await entryStream.CopyToAsync(memStream, ct);
                    memStream.Position = 0;

                    dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                    {
                        if (ct.IsCancellationRequested)
                        {
                            memStream.Dispose();
                            return;
                        }

                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 200;
                            await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                            item.Thumbnail = bitmap;
                        }
                        catch
                        {
                            memStream.Dispose();
                        }
                    });
                }
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        private async Task LoadImageThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 200);

                if (thumbnail != null)
                {
                    dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                    {
                        if (ct.IsCancellationRequested) return;
                        var bitmap = new BitmapImage();
                        bitmap.DecodePixelWidth = 200;
                        await bitmap.SetSourceAsync(thumbnail);
                        item.Thumbnail = bitmap;
                    });
                }
            }
            catch
            {
                // Handle or ignore specific image load failures
            }
        }
    }
}
