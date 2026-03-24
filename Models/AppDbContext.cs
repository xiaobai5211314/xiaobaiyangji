using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace 估值助手.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<FundData> FundRecords { get; set; }
        public DbSet<MyFundConfig> MyFunds { get; set; }
        public DbSet<User> Users { get; set; } // 👉 新增这行：真实账户表
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 极其轻量级，直接在程序运行目录下生成一个 db 文件
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=fund_data.db");
            }
        }
    }
}
