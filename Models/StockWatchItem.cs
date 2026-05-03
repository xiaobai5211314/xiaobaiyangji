using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class StockWatchItem
    {
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(120)]
        public string StockName { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
