using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202606120001_AddDailyArchiveSettlementMetadata")]
    public partial class AddDailyArchiveSettlementMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "Source", "VARCHAR(60) NOT NULL DEFAULT 'unknown'");
            AddColumnIfMissing(migrationBuilder, "IsFinal", "TINYINT(1) NOT NULL DEFAULT 0");
            AddColumnIfMissing(migrationBuilder, "UpdatedAt", "DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropColumnIfExists(migrationBuilder, "UpdatedAt");
            DropColumnIfExists(migrationBuilder, "IsFinal");
            DropColumnIfExists(migrationBuilder, "Source");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string columnName, string definition)
        {
            migrationBuilder.Sql($@"
SET @daily_archive_{columnName}_exists := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DailyArchives' AND COLUMN_NAME = '{columnName}'
);");
            migrationBuilder.Sql($"SET @daily_archive_{columnName}_sql := IF(@daily_archive_{columnName}_exists = 0, 'ALTER TABLE DailyArchives ADD COLUMN {columnName} {definition}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE daily_archive_{columnName}_stmt FROM @daily_archive_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE daily_archive_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE daily_archive_{columnName}_stmt;");
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($@"
SET @daily_archive_{columnName}_exists := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DailyArchives' AND COLUMN_NAME = '{columnName}'
);");
            migrationBuilder.Sql($"SET @daily_archive_{columnName}_sql := IF(@daily_archive_{columnName}_exists > 0, 'ALTER TABLE DailyArchives DROP COLUMN {columnName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE daily_archive_{columnName}_stmt FROM @daily_archive_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE daily_archive_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE daily_archive_{columnName}_stmt;");
        }
    }
}
