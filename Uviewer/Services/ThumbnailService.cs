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
                // Filter items that actually need loading
                var itemList = items.Where(i => i.Thumbnail == null && 
                                              !i.IsThumbnailLoading && 
                                              (i.IsImage || i.IsArchive || i.IsEpub)).ToList();

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
                            await LoadArchiveThumbnailAsync(item, dispatcher, ct);
                        }
                        else if (item.IsImage)
                        {
                            await LoadImageThumbnailAsync(item, dispatcher, ct);
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
                                bitmap.DecodePixelWidth = 200;
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

        private async Task LoadGeneralArchiveThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                using var archive = ArchiveFactory.OpenArchive(item.FullPath);
                var entry = archive.Entries
                    .Where(e => !e.IsDirectory &&
                           FileExplorerService.SupportedImageExtensions.Contains(Path.GetExtension(e.Key)?.ToLowerInvariant() ?? ""))
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
                                bitmap.DecodePixelWidth = 200;
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

        private async Task LoadImageThumbnailAsync(FileItem item, DispatcherQueue dispatcher, CancellationToken ct)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 200);

                if (thumbnail != null && !ct.IsCancellationRequested)
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

                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 200;
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
            catch { }
        }
    }
}