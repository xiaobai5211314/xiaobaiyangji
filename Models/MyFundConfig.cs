using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
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
        public string? LastSettledDate { get; set; }
        public double CostAmount { get; set; }


    }
}