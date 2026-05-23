using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202605030003_AddPersistentUiAndValuationCalibration")]
    public partial class AddPersistentUiAndValuationCalibration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS FundValuationEstimates (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(160) NOT NULL,
    TradeDate DATETIME(6) NOT NULL,
    EstimatedRate DOUBLE NOT NULL,
    EstimatedAssets DOUBLE NOT NULL,
    Source VARCHAR(40) NOT NULL,
    EstimatedAt DATETIME(6) NOT NULL,
    RawPayloadJson LONGTEXT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS FundValuationCalibrations (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(160) NOT NULL,
    TradeDate DATETIME(6) NOT NULL,
    EstimatedRate DOUBLE NOT NULL,
    ActualRate DOUBLE NOT NULL,
    ErrorRate DOUBLE NOT NULL,
    EstimatedAssets DOUBLE NOT NULL,
    ActualAssets DOUBLE NOT NULL,
    CorrectionOffset DOUBLE NOT NULL,
    SampleCount INT NOT NULL,
    Confidence VARCHAR(20) NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS UserUiStates (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    StateKey VARCHAR(80) NOT NULL,
    StateJson LONGTEXT NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS UserInsightSnapshots (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    SnapshotType VARCHAR(40) NOT NULL,
    SnapshotDate DATETIME(6) NOT NULL,
    PayloadJson LONGTEXT NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
");

            CreateIndexIfMissing(migrationBuilder, "FundValuationEstimates", "IX_FundValuationEstimate_User_Code_Date", "CREATE UNIQUE INDEX IX_FundValuationEstimate_User_Code_Date ON FundValuationEstimates (Username, FundCode, TradeDate)");
            CreateIndexIfMissing(migrationBuilder, "FundValuationEstimates", "IX_FundValuationEstimate_User_Date", "CREATE INDEX IX_FundValuationEstimate_User_Date ON FundValuationEstimates (Username, TradeDate)");
            CreateIndexIfMissing(migrationBuilder, "FundValuationCalibrations", "IX_FundValuationCalibration_User_Code_Date", "CREATE UNIQUE INDEX IX_FundValuationCalibration_User_Code_Date ON FundValuationCalibrations (Username, FundCode, TradeDate)");
            CreateIndexIfMissing(migrationBuilder, "FundValuationCalibrations", "IX_FundValuationCalibration_User_Code", "CREATE INDEX IX_FundValuationCalibration_User_Code ON FundValuationCalibrations (Username, FundCode)");
            CreateIndexIfMissing(migrationBuilder, "UserUiStates", "IX_UserUiState_User_Key", "CREATE UNIQUE INDEX IX_UserUiState_User_Key ON UserUiStates (Username, StateKey)");
            CreateIndexIfMissing(migrationBuilder, "UserInsightSnapshots", "IX_UserInsightSnapshot_User_Type_Date", "CREATE UNIQUE INDEX IX_UserInsightSnapshot_User_Type_Date ON UserInsightSnapshots (Username, SnapshotType, SnapshotDate)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 这些表包含用户历史校准与偏好数据，不在回滚中删除，避免误删用户数据。
        }

        private static void CreateIndexIfMissing(MigrationBuilder migrationBuilder, string tableName, string indexName, string createSql)
        {
            migrationBuilder.Sql($@"
SET @guzhi_idx_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = '{tableName}'
      AND INDEX_NAME = '{indexName}'
);
");
            migrationBuilder.Sql($"SET @guzhi_idx_sql := IF(@guzhi_idx_exists = 0, '{createSql}', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE guzhi_idx_stmt FROM @guzhi_idx_sql;");
            migrationBuilder.Sql("EXECUTE guzhi_idx_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE guzhi_idx_stmt;");
        }
    }
}
