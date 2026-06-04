using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class MyFundConfig
    {
        [Key]
        public int Id { get; set; } // 独立主键
        [MaxLength(50)] // 👈 告诉 EF 这个字符串最多 50 个字符，别搞成无限长
        public string Username { get; set; } = string.Empty; // 区分不同用户的代号
        [MaxLength(20)] // 👈 告诉 EF 这个字符串最多 50 个字符，别搞成无限长
        public string FundCode { get; set; } = string.Empty;
        public string FundName { get; set; } = string.Empty;
        public double HoldAmount { get; set; } // 持仓本金
        public double HoldShares { get; set; } // 🚀 新增：持仓份额
        public string? LastSettledDate { get; set; }
        public double LastSettledProfit { get; set; } = 0;
        public double LastSettledRate { get; set; } = 0;
        public double CostAmount { get; set; }
        // 🚀 新增：落袋为安小金库（记录历史变现的利润）
        public double RealizedProfit { get; set; } = 0;
        // 平台累计收益（OCR 从蚂蚁基金识别的"累计收益"，赎回待确认时优先显示）
        public double PlatformCumulativeProfit { get; set; } = 0;
        // 🚀 之前的落袋小金库


        // 🚀 新增：加仓时间戳与金额（用于 T+1 收益过滤）

        // 注意：如果您的数据库允许它们为空，建议写成 public string? LastTradeDate { get; set; }
        public string? LastTradeDate { get; set; } // 注意有个问号，允许为空
        public double LastAddAmount { get; set; } = 0;


    }
}