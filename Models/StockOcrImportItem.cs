using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class StockOcrImportItem
    {
        public int Id { get; set; }

        public int BatchId { get; set; }

        public StockOcrImportBatch? Batch { get; set; }

        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(120)]
        public string StockName { get; set; } = string.Empty;

        [MaxLength(120)]
        public string RecognizedName { get; set; } = string.Empty;

        public decimal? Shares { get; set; }

        public decimal? CostPrice { get; set; }

        public decimal? CostAmount { get; set; }

        public decimal? MarketValue { get; set; }

        public decimal? FloatingProfit { get; set; }

        public decimal? FloatingProfitRate { get; set; }

        [MaxLength(30)]
        public string Action { get; set; } = "holding";

        [MaxLength(200)]
        public string Note { get; set; } = string.Empty;
    }
}
