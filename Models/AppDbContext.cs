using Microsoft.EntityFrameworkCore;

namespace 估值助手.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<FundData> FundRecords { get; set; }
        public DbSet<MyFundConfig> MyFunds { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<DailyArchive> DailyArchives { get; set; }
        public DbSet<FundValuationEstimate> FundValuationEstimates { get; set; }
        public DbSet<FundValuationCalibration> FundValuationCalibrations { get; set; }
        public DbSet<UserUiState> UserUiStates { get; set; }
        public DbSet<UserInsightSnapshot> UserInsightSnapshots { get; set; }
        public DbSet<OcrCorrection> OcrCorrections { get; set; }

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
                .HasKey(u => u.Username);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("IX_User_Username");

            modelBuilder.Entity<User>()
                .Property(u => u.Username)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.PasswordHash)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.AvatarDataUrl)
                .IsRequired(false);

            modelBuilder.Entity<OcrCorrection>()
                .HasIndex(x => new { x.Username, x.OcrName })
                .IsUnique()
                .HasDatabaseName("IX_OcrCorrection_User_OcrName");

            modelBuilder.Entity<OcrCorrection>()
                .Property(x => x.Username)
                .IsRequired();

            modelBuilder.Entity<OcrCorrection>()
                .Property(x => x.OcrName)
                .IsRequired();

            modelBuilder.Entity<OcrCorrection>()
                .Property(x => x.FundCode)
                .IsRequired();

            modelBuilder.Entity<OcrCorrection>()
                .Property(x => x.FundName)
                .IsRequired();
            modelBuilder.Entity<FundValuationEstimate>()
    .HasIndex(x => new { x.Username, x.FundCode, x.TradeDate })
    .IsUnique()
    .HasDatabaseName("IX_FundValuationEstimate_User_Code_Date");

            modelBuilder.Entity<FundValuationEstimate>()
                .HasIndex(x => new { x.Username, x.TradeDate })
                .HasDatabaseName("IX_FundValuationEstimate_User_Date");

            modelBuilder.Entity<FundValuationCalibration>()
                .HasIndex(x => new { x.Username, x.FundCode, x.TradeDate })
                .IsUnique()
                .HasDatabaseName("IX_FundValuationCalibration_User_Code_Date");

            modelBuilder.Entity<FundValuationCalibration>()
                .HasIndex(x => new { x.Username, x.FundCode })
                .HasDatabaseName("IX_FundValuationCalibration_User_Code");

            modelBuilder.Entity<UserUiState>()
                .HasIndex(x => new { x.Username, x.StateKey })
                .IsUnique()
                .HasDatabaseName("IX_UserUiState_User_Key");

            modelBuilder.Entity<UserInsightSnapshot>()
                .HasIndex(x => new { x.Username, x.SnapshotType, x.SnapshotDate })
                .IsUnique()
                .HasDatabaseName("IX_UserInsightSnapshot_User_Type_Date");
        }
    }
}
