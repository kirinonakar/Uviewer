using System;
using System.Collections.Generic;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed record BookmarkCaptureSnapshot(
        bool IsNavigatingRecent,
        bool IsTextMode,
        bool IsEpubMode,
        bool IsWebDavMode,
        bool IsVerticalMode,
        bool IsAozoraMode,
        bool HasArchive,
        bool HasPdfDocument,
        bool HasVisibleContent,
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
        bool IsAozoraParsePartial,
        IReadOnlyList<AozoraBindingModel> AozoraBlocks,
        int CurrentAozoraStartBlockIndex,
        int CurrentVerticalStartLine,
        int CurrentVerticalStartBlockIndex,
        bool CurrentVerticalHasContent,
        int TopVisibleLineIndex,
        int TextLineCount,
        int LastRecentSaveLine,
        double? TextScrollOffset);

    internal sealed class BookmarkCaptureContextFactory
    {
        private readonly Func<BookmarkCaptureSnapshot> _captureSnapshot;

        public BookmarkCaptureContextFactory(Func<BookmarkCaptureSnapshot> captureSnapshot)
        {
            _captureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
        }

        public FavoriteCaptureContext CreateFavoriteContext()
        {
            var snapshot = _captureSnapshot();

            return new FavoriteCaptureContext(
                IsTextMode: snapshot.IsTextMode,
                IsEpubMode: snapshot.IsEpubMode,
                IsWebDavMode: snapshot.IsWebDavMode,
                IsVerticalMode: snapshot.IsVerticalMode,
                IsAozoraMode: snapshot.IsAozoraMode,
                HasArchive: snapshot.HasArchive,
                HasPdfDocument: snapshot.HasPdfDocument,
                HasVisibleContent: snapshot.HasVisibleContent,
                CurrentTextFilePath: snapshot.CurrentTextFilePath,
                CurrentEpubFilePath: snapshot.CurrentEpubFilePath,
                CurrentArchivePath: snapshot.CurrentArchivePath,
                CurrentExplorerPath: snapshot.CurrentExplorerPath,
                CurrentWebDavPath: snapshot.CurrentWebDavPath,
                CurrentWebDavItemPath: snapshot.CurrentWebDavItemPath,
                WebDavServerName: snapshot.WebDavServerName,
                CurrentIndex: snapshot.CurrentIndex,
                ImageEntries: snapshot.ImageEntries,
                CurrentEpubPageIndex: snapshot.CurrentEpubPageIndex,
                CurrentEpubChapterIndex: snapshot.CurrentEpubChapterIndex,
                CurrentEpubPage: snapshot.CurrentEpubPage,
                EpubPages: snapshot.EpubPages,
                EpubSpineCount: snapshot.EpubSpineCount,
                EpubPageCount: snapshot.EpubPageCount,
                TextTotalLineCountInSource: snapshot.TextTotalLineCountInSource,
                AozoraTotalLineCountInSource: snapshot.AozoraTotalLineCountInSource,
                IsAozoraParsePartial: snapshot.IsAozoraParsePartial,
                AozoraBlocks: snapshot.AozoraBlocks,
                CurrentAozoraStartBlockIndex: snapshot.CurrentAozoraStartBlockIndex,
                CurrentVerticalStartLine: snapshot.CurrentVerticalStartLine,
                CurrentVerticalStartBlockIndex: snapshot.CurrentVerticalStartBlockIndex,
                TopVisibleLineIndex: snapshot.TopVisibleLineIndex,
                TextScrollOffset: snapshot.TextScrollOffset);
        }

        public RecentCaptureContext CreateRecentContext()
        {
            var snapshot = _captureSnapshot();

            return new RecentCaptureContext(
                IsNavigatingRecent: snapshot.IsNavigatingRecent,
                IsTextMode: snapshot.IsTextMode,
                IsEpubMode: snapshot.IsEpubMode,
                IsWebDavMode: snapshot.IsWebDavMode,
                IsVerticalMode: snapshot.IsVerticalMode,
                IsAozoraMode: snapshot.IsAozoraMode,
                HasArchive: snapshot.HasArchive,
                HasPdfDocument: snapshot.HasPdfDocument,
                CurrentTextFilePath: snapshot.CurrentTextFilePath,
                CurrentEpubFilePath: snapshot.CurrentEpubFilePath,
                CurrentArchivePath: snapshot.CurrentArchivePath,
                CurrentExplorerPath: snapshot.CurrentExplorerPath,
                CurrentWebDavPath: snapshot.CurrentWebDavPath,
                CurrentWebDavItemPath: snapshot.CurrentWebDavItemPath,
                WebDavServerName: snapshot.WebDavServerName,
                CurrentIndex: snapshot.CurrentIndex,
                ImageEntries: snapshot.ImageEntries,
                CurrentEpubPageIndex: snapshot.CurrentEpubPageIndex,
                CurrentEpubChapterIndex: snapshot.CurrentEpubChapterIndex,
                CurrentEpubPage: snapshot.CurrentEpubPage,
                EpubPages: snapshot.EpubPages,
                EpubSpineCount: snapshot.EpubSpineCount,
                EpubPageCount: snapshot.EpubPageCount,
                TextTotalLineCountInSource: snapshot.TextTotalLineCountInSource,
                AozoraTotalLineCountInSource: snapshot.AozoraTotalLineCountInSource,
                AozoraBlocks: snapshot.AozoraBlocks,
                CurrentAozoraStartBlockIndex: snapshot.CurrentAozoraStartBlockIndex,
                CurrentVerticalStartLine: snapshot.CurrentVerticalStartLine,
                CurrentVerticalHasContent: snapshot.CurrentVerticalHasContent,
                TopVisibleLineIndex: snapshot.TopVisibleLineIndex,
                TextLineCount: snapshot.TextLineCount,
                LastRecentSaveLine: snapshot.LastRecentSaveLine,
                TextScrollOffset: snapshot.TextScrollOffset);
        }
    }
}
