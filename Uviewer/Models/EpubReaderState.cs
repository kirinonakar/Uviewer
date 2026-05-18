using System.Collections.Generic;
using System.Threading;

namespace Uviewer.Models
{
    public sealed class EpubReaderState
    {
        public List<EpubWin2DPage> Pages { get; set; } = new();
        public int CurrentPageIndex { get; set; }
        public bool IsShowingTwoPages { get; set; }
        public Dictionary<int, List<EpubWin2DPage>> PreloadCache { get; } = new();
        public Dictionary<int, bool> ChapterHasText { get; } = new();
        public CancellationTokenSource? PreloadCts { get; private set; }

        public EpubWin2DPage? CurrentPage =>
            Pages.Count > 0 &&
            CurrentPageIndex >= 0 &&
            CurrentPageIndex < Pages.Count
                ? Pages[CurrentPageIndex]
                : null;

        public CancellationToken RestartPreload()
        {
            PreloadCts?.Cancel();
            PreloadCts = new CancellationTokenSource();
            return PreloadCts.Token;
        }

        public void ClearPreload()
        {
            PreloadCts?.Cancel();
            PreloadCache.Clear();
            ChapterHasText.Clear();
        }

        public void ClearAll()
        {
            ClearPreload();
            Pages.Clear();
            CurrentPageIndex = 0;
            IsShowingTwoPages = false;
        }
    }
}
