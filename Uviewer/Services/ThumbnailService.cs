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
                                               (includeFolderThumbnails && i.IsDirectory && !i.IsParentDirectory && !i.IsWebDav))).ToList();

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
                        // Add a small jitter to stagger heavy operations
                        await Task.Delay(5, ct);

                        if (item.IsArchive || item.IsEpub)
                        {
                            await LoadArchiveThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
                        }
                        else if (item.IsDirectory)
                        {
                            await LoadFolderThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
                        }
                        else if (item.IsImage)
                        {
                            await LoadImageThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
                        }
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

        private async Task LoadArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth)
        {
            string ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
            if (ext == ".7z")
            {
                await Load7zThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
            }
            else
            {
                await LoadGeneralArchiveThumbnailAsync(item, dispatcher, ct, decodePixelWidth);
            }
        }

        private async Task Load7zThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                string libraryPath = Path.Combine(AppContext.BaseDirectory, "Libs", "7z.dll");
                using var archive = new SevenZipExtractor.ArchiveFile(item.FullPath, libraryPath);
                var entry = archive.Entries
                    .Where(e => !e.IsFolder &&
                           FileExplorerService.GetSupportedFileKind(e.FileName) == SupportedFileKind.Image)
                    .OrderBy(e => e.FileName, NaturalSortComparer.Default)
                    .FirstOrDefault();

                if (entry != null)
                {
                    var memStream = new MemoryStream();
                    try
                    {
                        entry.Extract(memStream);
                        memStream.Position = 0;

                        // 비동기 연속 실행 옵션 부여 및 제네릭 타입 사용
                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        
                        // Cancellation Token을 TCS에 연결하여 Deadlock 방지
                        using var registration = ct.Register(() => tcs.TrySetCanceled());

                        bool enqueued = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
                        {
                            try
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    tcs.TrySetCanceled();
                                    return;
                                }
                                
                                var bitmap = new BitmapImage();
                                bitmap.DecodePixelWidth = decodePixelWidth;
                                await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                                item.Thumbnail = bitmap;
                                tcs.TrySetResult(true);
                            }
                            catch
                            {
                                tcs.TrySetResult(false);
                            }
                        });

                        if (enqueued)
                        {
                            try { await tcs.Task; } catch (OperationCanceledException) { }
                        }
                    }
                    finally
                    {
                        memStream.Dispose();
                    }
                }
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        private async Task LoadGeneralArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                using var archive = ArchiveFactory.OpenArchive(item.FullPath);
                var entry = archive.Entries
                    .Where(e => !e.IsDirectory &&
                           FileExplorerService.GetSupportedFileKind(e.Key) == SupportedFileKind.Image)
                    .OrderBy(e => e.Key, NaturalSortComparer.Default)
                    .FirstOrDefault();

                if (entry != null)
                {
                    using var entryStream = entry.OpenEntryStream();
                    var memStream = new MemoryStream();
                    try
                    {
                        await entryStream.CopyToAsync(memStream, ct);
                        memStream.Position = 0;

                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        using var registration = ct.Register(() => tcs.TrySetCanceled());

                        bool enqueued = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
                        {
                            try
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    tcs.TrySetCanceled();
                                    return;
                                }

                                var bitmap = new BitmapImage();
                                bitmap.DecodePixelWidth = decodePixelWidth;
                                await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                                item.Thumbnail = bitmap;
                                tcs.TrySetResult(true);
                            }
                            catch
                            {
                                tcs.TrySetResult(false);
                            }
                        });

                        if (enqueued)
                        {
                            try { await tcs.Task; } catch (OperationCanceledException) { }
                        }
                    }
                    finally
                    {
                        memStream.Dispose();
                    }
                }
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }

        private async Task LoadImageThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                using var thumbnail = await file.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                    (uint)decodePixelWidth);

                if (thumbnail != null && !ct.IsCancellationRequested)
                {
                    await SetThumbnailOnDispatcherAsync(item, dispatcher, ct, decodePixelWidth, thumbnail);
                }
            }
            catch { }
        }

        private async Task LoadFolderThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct, int decodePixelWidth)
        {
            try
            {
                var firstImagePath = Directory.EnumerateFiles(item.FullPath)
                    .Where(path => FileExplorerService.GetSupportedFileKind(path) == SupportedFileKind.Image)
                    .OrderBy(Path.GetFileName, NaturalSortComparer.Default)
                    .FirstOrDefault();

                if (firstImagePath == null || ct.IsCancellationRequested) return;

                var file = await StorageFile.GetFileFromPathAsync(firstImagePath);
                using var thumbnail = await file.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                    (uint)decodePixelWidth);

                if (thumbnail != null && !ct.IsCancellationRequested)
                {
                    await SetThumbnailOnDispatcherAsync(item, dispatcher, ct, decodePixelWidth, thumbnail);
                }
            }
            catch { }
        }

        private static async Task SetThumbnailOnDispatcherAsync(
            FileItem item,
            DispatcherQueue dispatcher,
            CancellationToken ct,
            int decodePixelWidth,
            Windows.Storage.FileProperties.StorageItemThumbnail thumbnail)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => tcs.TrySetCanceled());

            bool enqueued = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
            {
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    var bitmap = new BitmapImage
                    {
                        DecodePixelWidth = decodePixelWidth
                    };
                    await bitmap.SetSourceAsync(thumbnail);
                    item.Thumbnail = bitmap;
                    tcs.TrySetResult(true);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });

            if (enqueued)
            {
                try { await tcs.Task; } catch (OperationCanceledException) { }
            }
        }
    }
}
