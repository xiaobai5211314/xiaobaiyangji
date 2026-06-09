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
            // 与 202606060002 相同模式：SET 变量 + PREPARE + EXECUTE
            AddColumnIfMissing(migrationBuilder, "NavDate", "VARCHAR(20) NULL");
            AddColumnIfMissing(migrationBuilder, "Nav", "DOUBLE NULL");
            AddColumnIfMissing(migrationBuilder, "Source", "VARCHAR(50) NOT NULL DEFAULT ''");
            AddColumnIfMissing(migrationBuilder, "IsOfficial", "TINYINT(1) NOT NULL DEFAULT 0");
            AddIndexIfMissing(migrationBuilder, "IX_FundRecord_Code_Official_NavDate", "FundRecords", "FundCode, IsOfficial, NavDate");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIndexIfExists(migrationBuilder, "IX_FundRecord_Code_Official_NavDate", "FundRecords");
            DropColumnIfExists(migrationBuilder, "IsOfficial");
            DropColumnIfExists(migrationBuilder, "Source");
            DropColumnIfExists(migrationBuilder, "Nav");
            DropColumnIfExists(migrationBuilder, "NavDate");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string columnName, string definition)
        {
            // 完全复用 202606060002 已验证的模式
            migrationBuilder.Sql($@"
SET @col_exists_{columnName} := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'FundRecords' AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @col_sql_{columnName} := IF(@col_exists_{columnName} = 0, 'ALTER TABLE FundRecords ADD COLUMN {columnName} {definition}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE col_stmt_{columnName} FROM @col_sql_{columnName};");
            migrationBuilder.Sql($"EXECUTE col_stmt_{columnName};");
            migrationBuilder.Sql($"DEALLOCATE PREPARE col_stmt_{columnName};");
        }

        private static void AddIndexIfMissing(MigrationBuilder migrationBuilder, string indexName, string tableName, string columns)
        {
            migrationBuilder.Sql($@"
SET @idx_exists_{indexName} := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND INDEX_NAME = '{indexName}'
);
");
            migrationBuilder.Sql($"SET @idx_sql_{indexName} := IF(@idx_exists_{indexName} = 0, 'CREATE INDEX {indexName} ON {tableName} ({columns})', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE idx_stmt_{indexName} FROM @idx_sql_{indexName};");
            migrationBuilder.Sql($"EXECUTE idx_stmt_{indexName};");
            migrationBuilder.Sql($"DEALLOCATE PREPARE idx_stmt_{indexName};");
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($@"
SET @col_exists_{columnName} := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'FundRecords' AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @col_sql_{columnName} := IF(@col_exists_{columnName} > 0, 'ALTER TABLE FundRecords DROP COLUMN {columnName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE col_stmt_{columnName} FROM @col_sql_{columnName};");
            migrationBuilder.Sql($"EXECUTE col_stmt_{columnName};");
            migrationBuilder.Sql($"DEALLOCATE PREPARE col_stmt_{columnName};");
        }

        private static void DropIndexIfExists(MigrationBuilder migrationBuilder, string indexName, string tableName)
        {
            migrationBuilder.Sql($@"
SET @idx_exists_{indexName} := (
    SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND INDEX_NAME = '{indexName}'
);
");
            migrationBuilder.Sql($"SET @idx_sql_{indexName} := IF(@idx_exists_{indexName} > 0, 'DROP INDEX {indexName} ON {tableName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE idx_stmt_{indexName} FROM @idx_sql_{indexName};");
            migrationBuilder.Sql($"EXECUTE idx_stmt_{indexName};");
            migrationBuilder.Sql($"DEALLOCATE PREPARE idx_stmt_{indexName};");
        }
    }
}
