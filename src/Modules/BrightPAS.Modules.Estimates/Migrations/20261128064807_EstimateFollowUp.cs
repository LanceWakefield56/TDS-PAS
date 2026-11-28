using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Estimates.Migrations
{
    /// <inheritdoc />
    public partial class EstimateFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpiryWarningDays",
                schema: "est",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FollowUpDays",
                schema: "est",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                schema: "est",
                table: "Estimates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                schema: "est",
                table: "Estimates",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositPaid",
                schema: "est",
                table: "Estimates",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DepositPaidOn",
                schema: "est",
                table: "Estimates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUp",
                schema: "est",
                table: "Estimates",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpAt",
                schema: "est",
                table: "Estimates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpBy",
                schema: "est",
                table: "Estimates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceId",
                schema: "est",
                table: "Estimates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                schema: "est",
                table: "Estimates",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextActionOn",
                schema: "est",
                table: "Estimates",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EstimateEvents",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateEvents_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "est",
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "est",
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ExpiryWarningDays", "FollowUpDays" },
                values: new object[] { 14, 7 });

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_BookingId",
                schema: "est",
                table: "Estimates",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_FollowUp_ValidUntil",
                schema: "est",
                table: "Estimates",
                columns: new[] { "FollowUp", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_InvoiceId",
                schema: "est",
                table: "Estimates",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateEvents_EstimateId_At",
                schema: "est",
                table: "EstimateEvents",
                columns: new[] { "EstimateId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EstimateEvents",
                schema: "est");

            migrationBuilder.DropIndex(
                name: "IX_Estimates_BookingId",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropIndex(
                name: "IX_Estimates_FollowUp_ValidUntil",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropIndex(
                name: "IX_Estimates_InvoiceId",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "ExpiryWarningDays",
                schema: "est",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "FollowUpDays",
                schema: "est",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BookingId",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "DepositPaid",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "DepositPaidOn",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "FollowUp",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "FollowUpAt",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "FollowUpBy",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                schema: "est",
                table: "Estimates");

            migrationBuilder.DropColumn(
                name: "NextActionOn",
                schema: "est",
                table: "Estimates");
        }
    }
}
