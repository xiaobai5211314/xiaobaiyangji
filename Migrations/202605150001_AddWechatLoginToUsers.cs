using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using 小白养基.Models;

#nullable disable

namespace 小白养基.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("202605150001_AddWechatLoginToUsers")]
    public partial class AddWechatLoginToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(migrationBuilder, "WechatOpenId", "VARCHAR(128) NULL");
            AddColumnIfMissing(migrationBuilder, "WechatUnionId", "VARCHAR(128) NULL");
            AddColumnIfMissing(migrationBuilder, "Nickname", "VARCHAR(120) NULL");
            AddColumnIfMissing(migrationBuilder, "CreatedAt", "DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
            AddColumnIfMissing(migrationBuilder, "LastLoginAt", "DATETIME(6) NULL");

            migrationBuilder.Sql(@"
SET @wechat_openid_index_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Users'
      AND INDEX_NAME = 'IX_User_WechatOpenId'
);
");
            migrationBuilder.Sql("SET @wechat_openid_index_sql := IF(@wechat_openid_index_exists = 0, 'CREATE UNIQUE INDEX IX_User_WechatOpenId ON Users (WechatOpenId)', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE wechat_openid_index_stmt FROM @wechat_openid_index_sql;");
            migrationBuilder.Sql("EXECUTE wechat_openid_index_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE wechat_openid_index_stmt;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @wechat_openid_index_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Users'
      AND INDEX_NAME = 'IX_User_WechatOpenId'
);
");
            migrationBuilder.Sql("SET @wechat_openid_index_sql := IF(@wechat_openid_index_exists > 0, 'DROP INDEX IX_User_WechatOpenId ON Users', 'SELECT 1');");
            migrationBuilder.Sql("PREPARE wechat_openid_index_stmt FROM @wechat_openid_index_sql;");
            migrationBuilder.Sql("EXECUTE wechat_openid_index_stmt;");
            migrationBuilder.Sql("DEALLOCATE PREPARE wechat_openid_index_stmt;");

            DropColumnIfExists(migrationBuilder, "LastLoginAt");
            DropColumnIfExists(migrationBuilder, "CreatedAt");
            DropColumnIfExists(migrationBuilder, "Nickname");
            DropColumnIfExists(migrationBuilder, "WechatUnionId");
            DropColumnIfExists(migrationBuilder, "WechatOpenId");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string columnName, string definition)
        {
            migrationBuilder.Sql($@"
SET @users_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Users'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @users_{columnName}_sql := IF(@users_{columnName}_exists = 0, 'ALTER TABLE Users ADD COLUMN {columnName} {definition}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE users_{columnName}_stmt FROM @users_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE users_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE users_{columnName}_stmt;");
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($@"
SET @users_{columnName}_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Users'
      AND COLUMN_NAME = '{columnName}'
);
");
            migrationBuilder.Sql($"SET @users_{columnName}_sql := IF(@users_{columnName}_exists > 0, 'ALTER TABLE Users DROP COLUMN {columnName}', 'SELECT 1');");
            migrationBuilder.Sql($"PREPARE users_{columnName}_stmt FROM @users_{columnName}_sql;");
            migrationBuilder.Sql($"EXECUTE users_{columnName}_stmt;");
            migrationBuilder.Sql($"DEALLOCATE PREPARE users_{columnName}_stmt;");
        }
    }
}
