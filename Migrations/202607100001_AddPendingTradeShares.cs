using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202607100001_AddPendingTradeShares")]
    public partial class AddPendingTradeShares : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "PendingBuyShares", "DOUBLE NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "PendingSellShares", "DOUBLE NOT NULL DEFAULT 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropColumnIfExists(migrationBuilder, "PendingSellShares");
            DropColumnIfExists(migrationBuilder, "PendingBuyShares");
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
