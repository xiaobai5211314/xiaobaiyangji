using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202606050001_AddMarketDataCache")]
    public partial class AddMarketDataCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @table_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
);
");
            migrationBuilder.Sql("SET @create_sql := IF(@table_exists = 0, 'CREATE TABLE MarketDataCaches (Id INT NOT NULL AUTO_INCREMENT, CacheKey VARCHAR(200) NOT NULL, DataType VARCHAR(50) NOT NULL, PayloadJson LONGTEXT NOT NULL, UpdatedAt DATETIME(6) NOT NULL, ExpiresAt DATETIME(6) NOT NULL, Source VARCHAR(100) NOT NULL, LastError LONGTEXT NULL, HitCount INT NOT NULL DEFAULT 0, IsStale TINYINT(1) NOT NULL DEFAULT 0, PRIMARY KEY (Id))', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE create_stmt FROM @create_sql;");
            migrationBuilder.Sql("EXECUTE create_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE create_stmt;");

            migrationBuilder.Sql(@"
SET @uniq_index_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
      AND INDEX_NAME = 'IX_MarketDataCache_Key'
);
");
            migrationBuilder.Sql("SET @uniq_sql := IF(@uniq_index_exists = 0, 'CREATE UNIQUE INDEX IX_MarketDataCache_Key ON MarketDataCaches (CacheKey)', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE uniq_stmt FROM @uniq_sql;");
            migrationBuilder.Sql("EXECUTE uniq_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE uniq_stmt;");

            migrationBuilder.Sql(@"
SET @type_index_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
      AND INDEX_NAME = 'IX_MarketDataCache_Type_Updated'
);
");
            migrationBuilder.Sql("SET @type_sql := IF(@type_index_exists = 0, 'CREATE INDEX IX_MarketDataCache_Type_Updated ON MarketDataCaches (DataType, UpdatedAt)', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE type_stmt FROM @type_sql;");
            migrationBuilder.Sql("EXECUTE type_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE type_stmt;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @table_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
);
");
            migrationBuilder.Sql("SET @drop_sql := IF(@table_exists > 0, 'DROP TABLE MarketDataCaches', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE drop_stmt FROM @drop_sql;");
            migrationBuilder.Sql("EXECUTE drop_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE drop_stmt;");
        }
    }
}
