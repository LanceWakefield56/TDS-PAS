using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Claims.Migrations
{
    /// <inheritdoc />
    public partial class DirectClaiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRefund",
                schema: "claims",
                table: "TerminalTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "claims",
                table: "TerminalTransactions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundOfId",
                schema: "claims",
                table: "TerminalTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Refunded",
                schema: "claims",
                table: "TerminalTransactions",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WindcaveKeyProtected",
                schema: "claims",
                table: "Settings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindcaveUrl",
                schema: "claims",
                table: "Settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindcaveUser",
                schema: "claims",
                table: "Settings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                schema: "claims",
                table: "PriorApprovals",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Api",
                schema: "claims",
                table: "Funders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyProtected",
                schema: "claims",
                table: "Funders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiUrl",
                schema: "claims",
                table: "Funders",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastApiAt",
                schema: "claims",
                table: "Funders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastApiError",
                schema: "claims",
                table: "Funders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                schema: "claims",
                table: "Claims",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCheckedAt",
                schema: "claims",
                table: "Claims",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "XeroContacts",
                schema: "claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    XeroContactId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    How = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroContacts", x => x.Id);
                });

            migrationBuilder.UpdateData(
                schema: "claims",
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "WindcaveKeyProtected", "WindcaveUrl", "WindcaveUser" },
                values: new object[] { null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTransactions_RefundOfId",
                schema: "claims",
                table: "TerminalTransactions",
                column: "RefundOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ExternalReference",
                schema: "claims",
                table: "Claims",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_XeroContacts_Key",
                schema: "claims",
                table: "XeroContacts",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TerminalTransactions_TerminalTransactions_RefundOfId",
                schema: "claims",
                table: "TerminalTransactions",
                column: "RefundOfId",
                principalSchema: "claims",
                principalTable: "TerminalTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TerminalTransactions_TerminalTransactions_RefundOfId",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropTable(
                name: "XeroContacts",
                schema: "claims");

            migrationBuilder.DropIndex(
                name: "IX_TerminalTransactions_RefundOfId",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Claims_ExternalReference",
                schema: "claims",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "IsRefund",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropColumn(
                name: "RefundOfId",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropColumn(
                name: "Refunded",
                schema: "claims",
                table: "TerminalTransactions");

            migrationBuilder.DropColumn(
                name: "WindcaveKeyProtected",
                schema: "claims",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WindcaveUrl",
                schema: "claims",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WindcaveUser",
                schema: "claims",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                schema: "claims",
                table: "PriorApprovals");

            migrationBuilder.DropColumn(
                name: "Api",
                schema: "claims",
                table: "Funders");

            migrationBuilder.DropColumn(
                name: "ApiKeyProtected",
                schema: "claims",
                table: "Funders");

            migrationBuilder.DropColumn(
                name: "ApiUrl",
                schema: "claims",
                table: "Funders");

            migrationBuilder.DropColumn(
                name: "LastApiAt",
                schema: "claims",
                table: "Funders");

            migrationBuilder.DropColumn(
                name: "LastApiError",
                schema: "claims",
                table: "Funders");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                schema: "claims",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                schema: "claims",
                table: "Claims");
        }
    }
}
