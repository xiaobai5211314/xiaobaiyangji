using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace 小白养基.Migrations
{
    public partial class InitialOptimizedSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS Users (
    Username varchar(255) NOT NULL,
    PasswordHash longtext NOT NULL,
    AvatarDataUrl longtext NULL,
    PRIMARY KEY (Username)
);

CREATE TABLE IF NOT EXISTS MyFunds (
    Id int NOT NULL AUTO_INCREMENT,
    Username varchar(50) NOT NULL,
    FundCode varchar(20) NOT NULL,
    FundName longtext NOT NULL,
    HoldAmount double NOT NULL,
    HoldShares double NOT NULL,
    LastSettledDate varchar(20) NULL,
    LastSettledProfit double NOT NULL DEFAULT 0,
    LastSettledRate double NOT NULL DEFAULT 0,
    CostAmount double NOT NULL,
    RealizedProfit double NOT NULL DEFAULT 0,
    LastTradeDate varchar(20) NULL,
    LastAddAmount double NOT NULL DEFAULT 0,
    PRIMARY KEY (Id)
);

CREATE TABLE IF NOT EXISTS FundRecords (
    Id int NOT NULL AUTO_INCREMENT,
    FundCode longtext NOT NULL,
    FundName longtext NOT NULL,
    EstimatedRate double NOT NULL,
    FetchTime datetime(6) NOT NULL,
    ActualRate double NOT NULL DEFAULT 0,
    DiffRate double NOT NULL DEFAULT 0,
    PRIMARY KEY (Id)
);

CREATE TABLE IF NOT EXISTS DailyArchives (
    Id int NOT NULL AUTO_INCREMENT,
    Username longtext NOT NULL,
    FundCode longtext NOT NULL,
    FundName longtext NOT NULL,
    RecordDate datetime(6) NOT NULL,
    Assets double NOT NULL,
    DailyProfit double NOT NULL,
    DailyRate double NOT NULL,
    TotalProfit double NOT NULL,
    TotalRate double NOT NULL,
    PRIMARY KEY (Id)
);

CREATE TABLE IF NOT EXISTS OcrCorrections (
    Id int NOT NULL AUTO_INCREMENT,
    Username varchar(50) NOT NULL,
    OcrName varchar(160) NOT NULL,
    FundCode varchar(20) NOT NULL,
    FundName varchar(160) NOT NULL,
    UpdatedAt datetime(6) NOT NULL,
    PRIMARY KEY (Id)
);
");

            migrationBuilder.Sql("CREATE UNIQUE INDEX IX_User_Username ON Users (Username);");
            migrationBuilder.Sql("CREATE INDEX IX_MyFundConfig_Username ON MyFunds (Username);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IX_MyFundConfig_User_Code ON MyFunds (Username, FundCode);");
            migrationBuilder.Sql("CREATE INDEX IX_FundRecord_Code_Time ON FundRecords (FundCode(20), FetchTime);");
            migrationBuilder.Sql("CREATE INDEX IX_FundRecord_Time ON FundRecords (FetchTime);");
            migrationBuilder.Sql("CREATE INDEX IX_DailyArchive_User_Date_Code ON DailyArchives (Username(50), RecordDate, FundCode(20));");
            migrationBuilder.Sql("CREATE INDEX IX_DailyArchive_User_Code_Date ON DailyArchives (Username(50), FundCode(20), RecordDate);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IX_OcrCorrection_User_OcrName ON OcrCorrections (Username, OcrName);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS OcrCorrections;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DailyArchives;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS FundRecords;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS MyFunds;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS Users;");
        }
    }
}
