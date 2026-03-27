namespace 估值助手.Models
{
    public class FundData
    {
        public int Id { get; set; }
        public string FundCode { get; set; } = string.Empty;
        public string FundName { get; set; } = string.Empty;
        public double EstimatedRate { get; set; } // 实时估值涨跌幅
        public DateTime FetchTime { get; set; }   // 抓取时间
        // 👇 新增：雷达探针专属储物间
        public double ActualRate { get; set; }
        public double DiffRate { get; set; }

    }
}
