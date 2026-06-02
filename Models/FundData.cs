using System.ComponentModel.DataAnnotations.Schema;

namespace 小白养基.Models
{
    public class FundData
    {
        public int Id { get; set; }
        public string FundCode { get; set; } = string.Empty;
        public string FundName { get; set; } = string.Empty;

        public double EstimatedRate { get; set; } // 实时估值涨跌幅
        public DateTime FetchTime { get; set; }   // 抓取时间

        [NotMapped]
        public string EstimateSource { get; set; } = "fundgz_1234567";

        [NotMapped]
        public bool QuoteOk { get; set; } = true;

        [NotMapped]
        public bool IsFallback { get; set; }

        [NotMapped]
        public bool IsStale { get; set; }

        [NotMapped]
        public bool IsActualNav { get; set; }

        [NotMapped]
        public string ActualSource { get; set; } = string.Empty;

        [NotMapped]
        public string EstimateMessage { get; set; } = string.Empty;

        [NotMapped]
        public string RawTime { get; set; } = string.Empty;
        // 👇 新增：雷达探针专属储物间
        public double ActualRate { get; set; }
        public double DiffRate { get; set; }

    }
}
