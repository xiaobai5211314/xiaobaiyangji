using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202607150001_AddHoldSharesProvenance")]
    public partial class AddHoldSharesProvenance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "HoldSharesAreConfirmed", "TINYINT(1) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "HoldSharesSource", "VARCHAR(40) NULL");
            AddColumnIfMissing(migrationBuilder, "CostAmountIsConfirmed", "TINYINT(1) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "CostAmountSource", "VARCHAR(40) NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropColumnIfExists(migrationBuilder, "CostAmountSource");
            DropColumnIfExists(migrationBuilder, "CostAmountIsConfirmed");
            DropColumnIfExists(migrationBuilder, "HoldSharesSource");
            DropColumnIfExists(migrationBuilder, "HoldSharesAreConfirmed");
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
