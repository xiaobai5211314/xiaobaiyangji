using Microsoft.EntityFrameworkCore;

namespace 小白养基.Models
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
        public DbSet<StockHolding> StockHoldings { get; set; }
        public DbSet<StockWatchItem> StockWatchItems { get; set; }
        public DbSet<StockOcrImportBatch> StockOcrImportBatches { get; set; }
        public DbSet<StockOcrImportItem> StockOcrImportItems { get; set; }
        public DbSet<StockQuoteSnapshot> StockQuoteSnapshots { get; set; }
        public DbSet<StockKLineCache> StockKLineCaches { get; set; }
        public DbSet<MarketDataCache> MarketDataCaches { get; set; }
        public DbSet<FundTradeOrder> FundTradeOrders { get; set; }

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
                .HasIndex(u => u.WechatOpenId)
                .IsUnique()
                .HasDatabaseName("IX_User_WechatOpenId");

            modelBuilder.Entity<User>()
                .Property(u => u.Username)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.PasswordHash)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.AvatarDataUrl)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .Property(u => u.WechatOpenId)
                .HasMaxLength(128)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .Property(u => u.WechatUnionId)
                .HasMaxLength(128)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .Property(u => u.Nickname)
                .HasMaxLength(120)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .Property(u => u.CreatedAt)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.LastLoginAt)
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

            modelBuilder.Entity<StockHolding>()
                .HasIndex(x => new { x.Username, x.Market, x.StockCode })
                .IsUnique()
                .HasDatabaseName("IX_StockHolding_User_Market_Code");

            modelBuilder.Entity<StockHolding>()
                .HasIndex(x => x.Username)
                .HasDatabaseName("IX_StockHolding_User");

            modelBuilder.Entity<StockWatchItem>()
                .HasIndex(x => new { x.Username, x.Market, x.StockCode })
                .IsUnique()
                .HasDatabaseName("IX_StockWatch_User_Market_Code");

            modelBuilder.Entity<StockOcrImportBatch>()
                .HasIndex(x => new { x.Username, x.CreatedAt })
                .HasDatabaseName("IX_StockOcrBatch_User_Created");

            modelBuilder.Entity<StockOcrImportItem>()
                .HasOne(x => x.Batch)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StockQuoteSnapshot>()
                .HasIndex(x => new { x.StockCode, x.QuoteTime })
                .HasDatabaseName("IX_StockQuote_Code_Time");

            modelBuilder.Entity<StockKLineCache>()
                .HasIndex(x => new { x.StockCode, x.Period })
                .IsUnique()
                .HasDatabaseName("IX_StockKLine_Code_Period");

            modelBuilder.Entity<MarketDataCache>()
                .HasIndex(x => x.CacheKey)
                .IsUnique()
                .HasDatabaseName("IX_MarketDataCache_Key");

            modelBuilder.Entity<MarketDataCache>()
                .HasIndex(x => new { x.DataType, x.UpdatedAt })
                .HasDatabaseName("IX_MarketDataCache_Type_Updated");

            modelBuilder.Entity<FundTradeOrder>()
                .HasIndex(x => new { x.Username, x.FundCode, x.Status })
                .HasDatabaseName("IX_FundTradeOrder_User_Code_Status");

            modelBuilder.Entity<FundTradeOrder>()
                .HasIndex(x => new { x.Username, x.TradeDate })
                .HasDatabaseName("IX_FundTradeOrder_User_Date");
        }
    }
}
