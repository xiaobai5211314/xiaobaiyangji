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

       
    }
}
