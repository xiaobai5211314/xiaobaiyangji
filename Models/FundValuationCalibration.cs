using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    /// <summary>
    /// 盘后校准样本：真实净值涨跌幅与盘中估算涨跌幅的误差样本。
    /// CorrectionOffset 为写入该样本时按最近样本计算出的滚动修正值。
    /// </summary>
    public class FundValuationCalibration
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FundCode { get; set; } = string.Empty;

        [MaxLength(160)]
        public string FundName { get; set; } = string.Empty;

        public DateTime TradeDate { get; set; }

        public double EstimatedRate { get; set; }

        public double ActualRate { get; set; }

        public double ErrorRate { get; set; }

        public double EstimatedAssets { get; set; }

        public double ActualAssets { get; set; }

        public double CorrectionOffset { get; set; }

        public int SampleCount { get; set; }

        [MaxLength(20)]
        public string Confidence { get; set; } = "低";

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
