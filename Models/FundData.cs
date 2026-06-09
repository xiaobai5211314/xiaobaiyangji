namespace 小白养基.Models
{
    public class FundData
    {
        public int Id { get; set; }
        public string FundCode { get; set; } = string.Empty;
        public string FundName { get; set; } = string.Empty;

        public double EstimatedRate { get; set; } // 实时估值涨跌幅
        public DateTime FetchTime { get; set; }   // 抓取时间
        public double ActualRate { get; set; }
        public double DiffRate { get; set; }

        // 真实净值扩展字段
        public string? NavDate { get; set; }        // 真实净值日期 (yyyy-MM-dd)，来自接口 FSRQ/jzrq
        public double? Nav { get; set; }             // 真实单位净值 (DWJZ)
        public string Source { get; set; } = string.Empty; // 数据来源：estimate / official-nav / eastmoney-official
        public bool IsOfficial { get; set; }         // 是否真实净值记录
    }
}
