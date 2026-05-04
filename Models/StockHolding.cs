using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class StockHolding
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(10)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(30)]
        public string SecId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public double HoldShares { get; set; }
        public double CostAmount { get; set; }
        public double MarketValue { get; set; }
        public double RealizedProfit { get; set; }

        public double LastPrice { get; set; }
        public double TodayRate { get; set; }
        public DateTime? LastQuoteTime { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
