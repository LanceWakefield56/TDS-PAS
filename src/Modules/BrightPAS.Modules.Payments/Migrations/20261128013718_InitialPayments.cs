using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "Links",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublicUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LinkDays = table.Column<int>(type: "int", nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    WindcaveUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WindcaveUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WindcaveKeyProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatementMinimum = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StatementMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NextStatementNumber = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MerchantReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CardType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AuthCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResponseText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NeedsAttention = table.Column<bool>(type: "bit", nullable: false),
                    AttentionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResolvedNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Links_PayLinkId",
                        column: x => x.PayLinkId,
                        principalSchema: "payments",
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Statements",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalDue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Overdue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Delivery = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Statements_Runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "payments",
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Allocations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnlinePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allocations_Payments_OnlinePaymentId",
                        column: x => x.OnlinePaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_InvoiceId",
                schema: "payments",
                table: "Allocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_OnlinePaymentId",
                schema: "payments",
                table: "Allocations",
                column: "OnlinePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_InvoiceId",
                schema: "payments",
                table: "Links",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_PatientId",
                schema: "payments",
                table: "Links",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_StatementId",
                schema: "payments",
                table: "Links",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_TokenHash",
                schema: "payments",
                table: "Links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantReference",
                schema: "payments",
                table: "Payments",
                column: "MerchantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PatientId",
                schema: "payments",
                table: "Payments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayLinkId",
                schema: "payments",
                table: "Payments",
                column: "PayLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SessionId",
                schema: "payments",
                table: "Payments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_StartedAt",
                schema: "payments",
                table: "Payments",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_StatementDate",
                schema: "payments",
                table: "Runs",
                column: "StatementDate");

            migrationBuilder.CreateIndex(
                name: "IX_Statements_Number",
                schema: "payments",
                table: "Statements",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Statements_PatientId",
                schema: "payments",
                table: "Statements",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Statements_RunId",
                schema: "payments",
                table: "Statements",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allocations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Statements",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Runs",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Links",
                schema: "payments");
        }
    }
}
