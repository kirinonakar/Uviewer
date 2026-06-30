using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ImageDocumentEntryCoordinator
    {
        private readonly IImageDocumentEntryHost _host;

        public ImageDocumentEntryCoordinator(IImageDocumentEntryHost host)
        {
            _host = host;
        }

        public async Task DisplayTextEntryAsync(ImageEntry entry)
        {
            if (!_host.IsTextMode ||
                _host.CurrentTextFilePath != entry.FilePath ||
                _host.CurrentTextArchiveEntryKey != entry.ArchiveEntryKey)
            {
                await _host.LoadTextEntryAsync(entry);
            }
            else
            {
                if (_host.AozoraPendingTargetLine != 0)
                {
                    string fileName = Path.GetFileName(_host.CurrentTextFilePath ?? "");
                    await _host.ReloadTextDisplayFromCacheAsync(fileName, _host.AozoraPendingTargetLine);
                }
                else
                {
                    if (_host.IsVerticalMode) _host.InvalidateVerticalTextCanvas();
                    else if (_host.IsAozoraMode) _host.InvalidateAozoraTextCanvas();
                    else _host.TextScrollViewer?.InvalidateArrange();
                }
            }

            await _host.AddToRecentAsync(false);
        }

        public async Task DisplayEpubEntryAsync(ImageEntry entry, CancellationToken token)
        {
            if (!_host.IsEpubMode || _host.CurrentEpubFilePath != entry.FilePath)
            {
                await _host.LoadEpubEntryAsync(entry, token);
            }
            else
            {
                if (_host.PendingEpubChapterIndex >= 0 || _host.AozoraPendingTargetLine != 0)
                {
                    int targetChapter = _host.PendingEpubChapterIndex >= 0
                        ? _host.PendingEpubChapterIndex
                        : _host.CurrentEpubChapterIndex;

                    await _host.LoadEpubChapterAsync(
                        targetChapter,
                        _host.AozoraPendingTargetLine,
                        _host.PendingEpubStartBlockIndex,
                        _host.PendingEpubPageIndex);

                    _host.PendingEpubChapterIndex = -1;
                    _host.PendingEpubPageIndex = -1;
                    _host.AozoraPendingTargetLine = 0;
                    _host.PendingEpubStartBlockIndex = -1;
                }
                else
                {
                    if (_host.IsVerticalMode) _host.InvalidateVerticalTextCanvas();
                    else if (_host.CurrentEpubWin2DPage?.IsImagePage == true) _host.ShowEpubImagePage(_host.CurrentEpubWin2DPage);
                    else _host.InvalidateEpubTextCanvas();
                }
            }

            await _host.AddToRecentAsync(false);
        }
    }
}
