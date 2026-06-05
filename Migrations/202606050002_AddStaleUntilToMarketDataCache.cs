using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202606050002_AddStaleUntilToMarketDataCache")]
    public partial class AddStaleUntilToMarketDataCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @col_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
      AND COLUMN_NAME = 'StaleUntil'
);
");
            migrationBuilder.Sql("SET @col_sql := IF(@col_exists = 0, 'ALTER TABLE MarketDataCaches ADD COLUMN StaleUntil DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE col_stmt FROM @col_sql;");
            migrationBuilder.Sql("EXECUTE col_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE col_stmt;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @col_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MarketDataCaches'
      AND COLUMN_NAME = 'StaleUntil'
);
");
            migrationBuilder.Sql("SET @col_sql := IF(@col_exists > 0, 'ALTER TABLE MarketDataCaches DROP COLUMN StaleUntil', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE col_stmt FROM @col_sql;");
            migrationBuilder.Sql("EXECUTE col_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE col_stmt;");
        }
    }
}
