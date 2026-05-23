using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class StockQuoteSnapshot
    {
        public int Id { get; set; }

        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(120)]
        public string StockName { get; set; } = string.Empty;

        public decimal LatestPrice { get; set; }

        public decimal ChangeAmount { get; set; }

        public decimal ChangeRate { get; set; }

        public decimal OpenPrice { get; set; }

        public decimal HighPrice { get; set; }

        public decimal LowPrice { get; set; }

        public decimal PreviousClose { get; set; }

        public decimal Volume { get; set; }

        public decimal Amount { get; set; }

        public DateTime QuoteTime { get; set; } = DateTime.Now;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
