using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    /// <summary>
    /// 盘中估值快照：保存某天某只基金在盘中的原始估算涨跌幅，供盘后净值确认后做误差校准。
    /// </summary>
    public class FundValuationEstimate
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

        public double EstimatedAssets { get; set; }

        [MaxLength(40)]
        public string Source { get; set; } = "intraday";

        public DateTime EstimatedAt { get; set; }

        public string? RawPayloadJson { get; set; }
    }
}
