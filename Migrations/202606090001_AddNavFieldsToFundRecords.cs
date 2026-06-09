using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202606090001_AddNavFieldsToFundRecords")]
    public partial class AddNavFieldsToFundRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "FundRecords", "NavDate", "VARCHAR(20) NULL");
            AddColumnIfMissing(migrationBuilder, "FundRecords", "Nav", "DOUBLE NULL");
            AddColumnIfMissing(migrationBuilder, "FundRecords", "Source", "VARCHAR(50) NOT NULL DEFAULT ''");
            AddColumnIfMissing(migrationBuilder, "FundRecords", "IsOfficial", "TINYINT(1) NOT NULL DEFAULT 0");

            // 添加索引（如果不存在）
            migrationBuilder.Sql(@"
SET @idx_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'FundRecords'
      AND INDEX_NAME = 'IX_FundRecord_Code_Official_NavDate'
);
SET @idx_sql := IF(@idx_exists = 0, 'CREATE INDEX IX_FundRecord_Code_Official_NavDate ON FundRecords (FundCode, IsOfficial, NavDate)', 'SELECT 1');
PREPARE idx_stmt FROM @idx_sql;
EXECUTE idx_stmt;
DEALLOCATE PREPARE idx_stmt;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @idx_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'FundRecords'
      AND INDEX_NAME = 'IX_FundRecord_Code_Official_NavDate'
);
SET @idx_sql := IF(@idx_exists > 0, 'DROP INDEX IX_FundRecord_Code_Official_NavDate ON FundRecords', 'SELECT 1');
PREPARE idx_stmt FROM @idx_sql;
EXECUTE idx_stmt;
DEALLOCATE PREPARE idx_stmt;
");

            DropColumnIfExists(migrationBuilder, "FundRecords", "IsOfficial");
            DropColumnIfExists(migrationBuilder, "FundRecords", "Source");
            DropColumnIfExists(migrationBuilder, "FundRecords", "Nav");
            DropColumnIfExists(migrationBuilder, "FundRecords", "NavDate");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string tableName, string columnName, string definition)
        {
            migrationBuilder.Sql($@"
SET @col_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = '{tableName}'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @col_{columnName}_sql := IF(@col_{columnName}_exists = 0, 'ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE col_{columnName}_stmt FROM @col_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE col_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE col_{columnName}_stmt;");
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string tableName, string columnName)
        {
            migrationBuilder.Sql($@"
SET @col_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = '{tableName}'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @col_{columnName}_sql := IF(@col_{columnName}_exists > 0, 'ALTER TABLE {tableName} DROP COLUMN {columnName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE col_{columnName}_stmt FROM @col_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE col_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE col_{columnName}_stmt;");
        }
    }
}
