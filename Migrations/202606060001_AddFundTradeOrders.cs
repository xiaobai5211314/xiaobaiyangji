using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202606060001_AddFundTradeOrders")]
    public partial class AddFundTradeOrders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FundTradeOrders 表
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS FundTradeOrders (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(50) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(255) NOT NULL DEFAULT '',
    Direction VARCHAR(10) NOT NULL DEFAULT 'Buy',
    Amount DOUBLE NOT NULL DEFAULT 0,
    TradeTime VARCHAR(20) NULL,
    TradeDate VARCHAR(20) NULL,
    CutoffDate VARCHAR(20) NULL,
    Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    ConfirmDate VARCHAR(20) NULL,
    FirstProfitDate VARCHAR(20) NULL,
    Source VARCHAR(40) NOT NULL DEFAULT 'manual',
    RawText TEXT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX IX_FundTradeOrder_User_Code_Status (Username, FundCode, Status),
    INDEX IX_FundTradeOrder_User_Date (Username, TradeDate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");

            // MyFunds 新增 OCR 昨日收益字段
            AddColumnIfMissing(migrationBuilder, "OcrYesterdayIncome", "DOUBLE NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "OcrYesterdayDate", "VARCHAR(20) NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropColumnIfExists(migrationBuilder, "OcrYesterdayDate");
            DropColumnIfExists(migrationBuilder, "OcrYesterdayIncome");
            migrationBuilder.Sql("DROP TABLE IF EXISTS FundTradeOrders;");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string columnName, string definition)
        {
            migrationBuilder.Sql($@"
SET @myfund_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MyFunds'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @myfund_{columnName}_sql := IF(@myfund_{columnName}_exists = 0, 'ALTER TABLE MyFunds ADD COLUMN {columnName} {definition}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE myfund_{columnName}_stmt FROM @myfund_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE myfund_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE myfund_{columnName}_stmt;");
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($@"
SET @myfund_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MyFunds'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @myfund_{columnName}_sql := IF(@myfund_{columnName}_exists > 0, 'ALTER TABLE MyFunds DROP COLUMN {columnName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE myfund_{columnName}_stmt FROM @myfund_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE myfund_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE myfund_{columnName}_stmt;");
        }
    }
}
