using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202607150002_AddPlatformHoldingAdjustment")]
    public partial class AddPlatformHoldingAdjustment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @platform_holding_adjustment_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MyFunds'
      AND COLUMN_NAME = 'PlatformHoldingAdjustment'
);
");
            migrationBuilder.Sql("SET @platform_holding_adjustment_sql := IF(@platform_holding_adjustment_exists = 0, 'ALTER TABLE MyFunds ADD COLUMN PlatformHoldingAdjustment DOUBLE NOT NULL DEFAULT 0', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE platform_holding_adjustment_stmt FROM @platform_holding_adjustment_sql;");
            migrationBuilder.Sql("EXECUTE platform_holding_adjustment_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE platform_holding_adjustment_stmt;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @platform_holding_adjustment_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MyFunds'
      AND COLUMN_NAME = 'PlatformHoldingAdjustment'
);
");
            migrationBuilder.Sql("SET @platform_holding_adjustment_sql := IF(@platform_holding_adjustment_exists > 0, 'ALTER TABLE MyFunds DROP COLUMN PlatformHoldingAdjustment', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE platform_holding_adjustment_stmt FROM @platform_holding_adjustment_sql;");
            migrationBuilder.Sql("EXECUTE platform_holding_adjustment_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE platform_holding_adjustment_stmt;");
        }
    }
}
