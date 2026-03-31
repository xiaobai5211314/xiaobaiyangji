using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace 估值助手.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<FundData> FundRecords { get; set; }
        public DbSet<MyFundConfig> MyFunds { get; set; }
        public DbSet<User> Users { get; set; } // 真实账户表

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // 👇 就是这个新增的方法，它是给数据库装上“高铁轨道”的核心 👇
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 必须先调用基类的方法
            base.OnModelCreating(modelBuilder);

            // 🚀 1. 提速最明显的地方：给估值记录表的“基金代码”和“抓取时间”加上复合索引
            // 以后查询今天的数据时，数据库会直接跳到对应的位置，耗时从几秒降到几毫秒
            modelBuilder.Entity<FundData>()
                .HasIndex(r => new { r.FundCode, r.FetchTime })
                .HasDatabaseName("IX_FundRecord_Code_Time"); 
            
            // 🚀 2. 给个人持仓配置表加上“用户名”索引
            modelBuilder.Entity<MyFundConfig>()
                .HasIndex(f => f.Username)
                .HasDatabaseName("IX_MyFundConfig_Username");

            // 🚀 3. 给用户表加上用户名“唯一”索引（加速登录，且防止重名注册）
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique() // 强制唯一
                .HasDatabaseName("IX_User_Username");
        }
    }
}
