using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Uviewer.Models;

namespace Uviewer.Services
{
    public interface IThumbnailService
    {
        Task LoadThumbnailsAsync(
            IEnumerable<FileItem> items,
            DispatcherQueue dispatcher,
            CancellationToken token,
            int decodePixelWidth,
            bool includeFolderThumbnails);
    }
}
