using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace 小白养基.Migrations
{
    public partial class AddFundEstimateQuoteStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstimateSource",
                table: "FundRecords",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "fundgz_1234567");

            migrationBuilder.AddColumn<bool>(
                name: "QuoteOk",
                table: "FundRecords",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFallback",
                table: "FundRecords",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStale",
                table: "FundRecords",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsActualNav",
                table: "FundRecords",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ActualSource",
                table: "FundRecords",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EstimateMessage",
                table: "FundRecords",
                type: "varchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawTime",
                table: "FundRecords",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EstimateSource", table: "FundRecords");
            migrationBuilder.DropColumn(name: "QuoteOk", table: "FundRecords");
            migrationBuilder.DropColumn(name: "IsFallback", table: "FundRecords");
            migrationBuilder.DropColumn(name: "IsStale", table: "FundRecords");
            migrationBuilder.DropColumn(name: "IsActualNav", table: "FundRecords");
            migrationBuilder.DropColumn(name: "ActualSource", table: "FundRecords");
            migrationBuilder.DropColumn(name: "EstimateMessage", table: "FundRecords");
            migrationBuilder.DropColumn(name: "RawTime", table: "FundRecords");
        }
    }
}
