using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class ReaderPageMapCalculationService
    {
        public async Task<bool> CalculateAsync(
            ReaderPageState pageState,
            AozoraPageMapCalculator calculator,
            IReadOnlyList<AozoraBindingModel> blocks,
            AozoraBlockPaginationContext context,
            AozoraPageOrientation orientation,
            CancellationToken token)
        {
            if (blocks.Count == 0) return false;

            var result = await calculator.CalculateAsync(
                blocks,
                context,
                orientation,
                token);

            if (result == null || token.IsCancellationRequested)
            {
                return false;
            }

            pageState.SetPageMap(result.BlockToPageMap, result.TotalPages);
            if (!pageState.SyncCalculatedCurrentPageFromMap())
            {
                pageState.CalculatedCurrentPage = 1;
            }

            return true;
        }
    }
}
