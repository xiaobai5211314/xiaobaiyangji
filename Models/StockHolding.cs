using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class StockHolding
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

        public decimal Shares { get; set; }

        public decimal CostPrice { get; set; }

        public decimal CostAmount { get; set; }

        public decimal? LastPrice { get; set; }

        public decimal? LastRate { get; set; }

        public decimal? LastMarketValue { get; set; }

        public decimal? LastProfit { get; set; }

        public decimal? LastProfitRate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
