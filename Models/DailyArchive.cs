using System;

namespace 估值助手.Models
{
    // 🗄️ 历史战略档案表
    public class DailyArchive
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string FundCode { get; set; } // 💡 战术约定："TOTAL" 代表您的总持仓，其余为具体的基金代码
        public string FundName { get; set; }
        public DateTime RecordDate { get; set; } // 封存日期
        public double Assets { get; set; }       // 当日总资产/市值
        public double DailyProfit { get; set; }  // 当日盈亏金额
        public double DailyRate { get; set; }    // 当日涨跌幅百分比
        public double TotalProfit { get; set; } // 🚀 新增：累计历史盈亏
        public double TotalRate { get; set; }   // 🚀 新增：累计历史收益率
    }

    // 用于接收前端传来的封存数据包
    public class ArchiveRequest
    {
        public string Username { get; set; }
        public string DateStr { get; set; }
        public DailyArchive Total { get; set; }
        public List<DailyArchive> Funds { get; set; }
    }
}