using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 估值助手.Models;

#nullable disable

namespace 估值助手.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202605030002_AddOcrCorrectionsSafetyNet")]
    public partial class AddOcrCorrectionsSafetyNet : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS OcrCorrections (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    OcrName VARCHAR(160) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(160) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
");

            migrationBuilder.Sql(@"
SET @ocr_index_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'OcrCorrections'
      AND INDEX_NAME = 'IX_OcrCorrection_User_OcrName'
);
");
            migrationBuilder.Sql("SET @ocr_index_sql := IF(@ocr_index_exists = 0, 'CREATE UNIQUE INDEX IX_OcrCorrection_User_OcrName ON OcrCorrections (Username, OcrName)', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE ocr_index_stmt FROM @ocr_index_sql;");
            migrationBuilder.Sql("EXECUTE ocr_index_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE ocr_index_stmt;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 保留用户 OCR 纠错学习数据，不在回滚时删除表。
        }
    }
}
