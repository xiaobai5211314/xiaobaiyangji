using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace 估值助手.Migrations
{
    public partial class AddPersistentStockModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockHoldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    StockCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Market = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StockName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Shares = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    CostPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    LastPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    LastRate = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    LastMarketValue = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    LastProfit = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    LastProfitRate = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StockHoldings", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockWatchItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    StockCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Market = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StockName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StockWatchItems", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockOcrImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    RawText = table.Column<string>(type: "longtext", nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_StockOcrImportBatches", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockQuoteSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StockCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Market = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StockName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    LatestPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    ChangeAmount = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    ChangeRate = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    OpenPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    HighPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    LowPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    PreviousClose = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(20,4)", nullable: false),
                    QuoteTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StockQuoteSnapshots", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockKLineCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StockCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Market = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Period = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    RefreshedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StockKLineCaches", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockOcrImportItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    StockCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Market = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StockName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    RecognizedName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Shares = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    CostPrice = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    CostAmount = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    MarketValue = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    FloatingProfit = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    FloatingProfitRate = table.Column<decimal>(type: "decimal(20,4)", nullable: true),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOcrImportItems", x => x.Id);
                    table.ForeignKey("FK_StockOcrImportItems_StockOcrImportBatches_BatchId", x => x.BatchId, "StockOcrImportBatches", "Id", onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex("IX_StockHolding_User_Code", "StockHoldings", new[] { "Username", "StockCode" }, unique: true);
            migrationBuilder.CreateIndex("IX_StockHolding_User", "StockHoldings", "Username");
            migrationBuilder.CreateIndex("IX_StockWatch_User_Code", "StockWatchItems", new[] { "Username", "StockCode" }, unique: true);
            migrationBuilder.CreateIndex("IX_StockOcrBatch_User_Created", "StockOcrImportBatches", new[] { "Username", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_StockOcrImportItems_BatchId", "StockOcrImportItems", "BatchId");
            migrationBuilder.CreateIndex("IX_StockQuote_Code_Time", "StockQuoteSnapshots", new[] { "StockCode", "QuoteTime" });
            migrationBuilder.CreateIndex("IX_StockKLine_Code_Period", "StockKLineCaches", new[] { "StockCode", "Period" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockOcrImportItems");
            migrationBuilder.DropTable(name: "StockHoldings");
            migrationBuilder.DropTable(name: "StockWatchItems");
            migrationBuilder.DropTable(name: "StockQuoteSnapshots");
            migrationBuilder.DropTable(name: "StockKLineCaches");
            migrationBuilder.DropTable(name: "StockOcrImportBatches");
        }
    }
}
