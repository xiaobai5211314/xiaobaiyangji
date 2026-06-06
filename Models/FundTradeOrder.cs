using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class FundTradeOrder
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FundCode { get; set; } = string.Empty;

        public string FundName { get; set; } = string.Empty;

        /// <summary>Buy / Sell</summary>
        [MaxLength(10)]
        public string Direction { get; set; } = "Buy";

        public double Amount { get; set; }

        /// <summary>交易时间，精确到秒，如 14:57:54</summary>
        [MaxLength(20)]
        public string? TradeTime { get; set; }

        /// <summary>交易日期，yyyy-MM-dd</summary>
        [MaxLength(20)]
        public string? TradeDate { get; set; }

        /// <summary>15:00 截止日，用于推算 T+N</summary>
        [MaxLength(20)]
        public string? CutoffDate { get; set; }

        /// <summary>Pending / Confirmed / Cancelled / Closed</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        /// <summary>份额确认日</summary>
        [MaxLength(20)]
        public string? ConfirmDate { get; set; }

        /// <summary>首次可查看收益日</summary>
        [MaxLength(20)]
        public string? FirstProfitDate { get; set; }

        /// <summary>ocr_transaction / manual / import</summary>
        [MaxLength(40)]
        public string Source { get; set; } = "manual";

        /// <summary>OCR 原始文本</summary>
        public string? RawText { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
