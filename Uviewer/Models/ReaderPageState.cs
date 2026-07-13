using System;
using System.Collections.Generic;

namespace Uviewer.Models
{
    public sealed class ReaderPageState
    {
        public ReaderPageInfo CurrentPage { get; set; } = ReaderPageInfo.Empty;
        public int StartBlockIndex { get; set; }
        public int NavigationStartBlockIndex { get; set; }
        public int EndBlockIndex { get; set; }
        public int TotalPages { get; set; }
        public bool IsPageCalculationCompleted { get; set; }
        public Dictionary<int, int> BlockToPageMap { get; set; } = new();
        public int CalculatedCurrentPage { get; set; } = 1;

        public bool HasPageBlocks => CurrentPage.Blocks != null && CurrentPage.Blocks.Count > 0;

        public void Clear()
        {
            CurrentPage = ReaderPageInfo.Empty;
            StartBlockIndex = 0;
            NavigationStartBlockIndex = 0;
            EndBlockIndex = 0;
            ResetPageCalculation();
            BlockToPageMap.Clear();
        }

        public void SetEmptyPage()
        {
            CurrentPage = ReaderPageInfo.Empty;
            StartBlockIndex = 0;
            NavigationStartBlockIndex = 0;
            EndBlockIndex = 0;
        }

        public void SetPage(
            List<AozoraBindingModel> pageBlocks,
            int startBlockIndex,
            int nextBlockIndex,
            int? navigationStartBlockIndex = null)
        {
            StartBlockIndex = Math.Max(0, startBlockIndex);
            NavigationStartBlockIndex = Math.Max(0, navigationStartBlockIndex ?? startBlockIndex);
            EndBlockIndex = nextBlockIndex > startBlockIndex ? nextBlockIndex - 1 : StartBlockIndex;
            CurrentPage = ReaderPageInfo.FromBlocks(pageBlocks);
        }

        public void ResetPageCalculation()
        {
            IsPageCalculationCompleted = false;
            TotalPages = 0;
            CalculatedCurrentPage = 1;
        }

        public void SetPageMap(Dictionary<int, int> blockToPageMap, int totalPages)
        {
            BlockToPageMap = blockToPageMap;
            TotalPages = totalPages;
            IsPageCalculationCompleted = true;
        }

        public bool SyncCalculatedCurrentPageFromMap()
        {
            if (BlockToPageMap.TryGetValue(StartBlockIndex, out int mappedPage))
            {
                CalculatedCurrentPage = mappedPage;
                return true;
            }

            return false;
        }

        public void AdvanceCalculatedPage(int delta)
        {
            CalculatedCurrentPage = Math.Max(1, CalculatedCurrentPage + delta);
        }
    }
}
