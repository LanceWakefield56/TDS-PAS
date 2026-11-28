using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoSendStatements",
                schema: "payments",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoStatementDay",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeShortfalls",
                schema: "payments",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastAutomationOn",
                schema: "payments",
                table: "Settings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextPlanNumber",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NextRefundNumber",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OverdueFirstDays",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OverdueSecondDays",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlanReminderDays",
                schema: "payments",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Refunded",
                schema: "payments",
                table: "Payments",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionId",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                schema: "payments",
                table: "Links",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Refunded",
                schema: "payments",
                table: "Allocations",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Overdue",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Messages = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Overdue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Instalment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstDue = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidAtStart = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LastReminderFor = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OnlinePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FromUnapplied = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AppliedTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_OnlinePaymentId",
                        column: x => x.OnlinePaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanInvoices",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanInvoices_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "payments",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Links_PlanId",
                schema: "payments",
                table: "Links",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Overdue_InvoiceId_Stage",
                schema: "payments",
                table: "Overdue",
                columns: new[] { "InvoiceId", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Overdue_PatientId",
                schema: "payments",
                table: "Overdue",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Overdue_SentAt",
                schema: "payments",
                table: "Overdue",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlanInvoices_InvoiceId",
                schema: "payments",
                table: "PlanInvoices",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanInvoices_PlanId_InvoiceId",
                schema: "payments",
                table: "PlanInvoices",
                columns: new[] { "PlanId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Number",
                schema: "payments",
                table: "Plans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_PatientId",
                schema: "payments",
                table: "Plans",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Number",
                schema: "payments",
                table: "Refunds",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OnlinePaymentId",
                schema: "payments",
                table: "Refunds",
                column: "OnlinePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PatientId",
                schema: "payments",
                table: "Refunds",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Overdue",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PlanInvoices",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "payments");

            migrationBuilder.DropIndex(
                name: "IX_Links_PlanId",
                schema: "payments",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "AutoSendStatements",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoStatementDay",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IncludeShortfalls",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LastAutomationOn",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NextPlanNumber",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NextRefundNumber",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OverdueFirstDays",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OverdueSecondDays",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PlanReminderDays",
                schema: "payments",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Refunded",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PlanId",
                schema: "payments",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "Refunded",
                schema: "payments",
                table: "Allocations");
        }
    }
}
