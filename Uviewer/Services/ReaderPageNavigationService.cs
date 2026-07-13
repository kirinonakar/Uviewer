using System;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ReaderPageNavigationService
    {
        public int? GetTargetStartIndex(
            ReaderPageState pageState,
            int blockCount,
            int direction,
            Func<int> findPreviousStart)
        {
            if (direction > 0)
            {
                return pageState.EndBlockIndex < blockCount - 1
                    ? pageState.EndBlockIndex + 1
                    : null;
            }

            if (direction < 0)
            {
                return pageState.NavigationStartBlockIndex > 0
                    ? findPreviousStart()
                    : null;
            }

            return null;
        }

        public void AdvanceCalculatedPage(ReaderPageState pageState, int direction)
        {
            if (!pageState.IsPageCalculationCompleted || direction == 0)
            {
                return;
            }

            pageState.AdvanceCalculatedPage(direction > 0 ? 1 : -1);
        }
    }
}
