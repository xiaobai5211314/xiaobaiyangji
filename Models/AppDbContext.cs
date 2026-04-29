using Microsoft.EntityFrameworkCore;

namespace 估值助手.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<FundData> FundRecords { get; set; }
        public DbSet<MyFundConfig> MyFunds { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<DailyArchive> DailyArchives { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<FundData>()
                .HasIndex(r => new { r.FundCode, r.FetchTime })
                .HasDatabaseName("IX_FundRecord_Code_Time");

            modelBuilder.Entity<FundData>()
                .HasIndex(r => r.FetchTime)
                .HasDatabaseName("IX_FundRecord_Time");

            modelBuilder.Entity<MyFundConfig>()
                .HasIndex(f => f.Username)
                .HasDatabaseName("IX_MyFundConfig_Username");

            modelBuilder.Entity<MyFundConfig>()
                .HasIndex(f => new { f.Username, f.FundCode })
                .IsUnique()
                .HasDatabaseName("IX_MyFundConfig_User_Code");

            modelBuilder.Entity<DailyArchive>()
                .HasIndex(a => new { a.Username, a.RecordDate, a.FundCode })
                .HasDatabaseName("IX_DailyArchive_User_Date_Code");

            modelBuilder.Entity<DailyArchive>()
                .HasIndex(a => new { a.Username, a.FundCode, a.RecordDate })
                .HasDatabaseName("IX_DailyArchive_User_Code_Date");


            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("IX_User_Username");
        }
    }
}
