using System;

namespace Uviewer.Models
{
    public class PaginationResult
    {
        public int[] Pages { get; set; } = Array.Empty<int>();
        public int TotalPages { get; set; }
        public double TotalHeight { get; set; }
    }
}
