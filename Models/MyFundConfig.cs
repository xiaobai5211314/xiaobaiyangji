using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class MyFundConfig
    {
        [Key]
        public int Id { get; set; } // 独立主键
        public string Username { get; set; } = string.Empty; // 区分不同用户的代号
        public string FundCode { get; set; } = string.Empty;
        public string FundName { get; set; } = string.Empty;
        public double HoldAmount { get; set; } // 持仓本金
        // ... 原本的 Username, FundCode, HoldAmount 等字段保留
        
        // 👇 新增：财务防重刷锁（记录最后一次滚存的日期）
        public string LastSettledDate { get; set; }

    }
}