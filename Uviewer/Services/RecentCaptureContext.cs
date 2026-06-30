using System.Collections.Generic;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed record RecentCaptureContext(
        bool IsNavigatingRecent,
        bool IsTextMode,
        bool IsEpubMode,
        bool IsWebDavMode,
        bool IsVerticalMode,
        bool IsAozoraMode,
        bool HasArchive,
        bool HasPdfDocument,
        string? CurrentTextFilePath,
        string? CurrentEpubFilePath,
        string? CurrentArchivePath,
        string? CurrentExplorerPath,
        string? CurrentWebDavPath,
        string? CurrentWebDavItemPath,
        string? WebDavServerName,
        int CurrentIndex,
        IReadOnlyList<ImageEntry> ImageEntries,
        int CurrentEpubPageIndex,
        int CurrentEpubChapterIndex,
        EpubWin2DPage? CurrentEpubPage,
        IReadOnlyList<EpubWin2DPage> EpubPages,
        int EpubSpineCount,
        int EpubPageCount,
        int TextTotalLineCountInSource,
        int AozoraTotalLineCountInSource,
        IReadOnlyList<AozoraBindingModel> AozoraBlocks,
        int CurrentAozoraStartBlockIndex,
        int CurrentVerticalStartLine,
        bool CurrentVerticalHasContent,
        int TopVisibleLineIndex,
        int TextLineCount,
        int LastRecentSaveLine,
        double? TextScrollOffset);
}
