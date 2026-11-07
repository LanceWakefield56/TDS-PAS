using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Reminders.Migrations
{
    /// <inheritdoc />
    public partial class InitialReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reminders");

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppointmentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SecretLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IgnoreQuietHours = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DedupeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptOuts",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptOuts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Preferences",
                schema: "reminders",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowSms = table.Column<bool>(type: "bit", nullable: false),
                    AllowEmail = table.Column<bool>(type: "bit", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preferences", x => x.PatientId);
                });

            migrationBuilder.CreateTable(
                name: "Replies",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplyToProviderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchedMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Meaning = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActionTaken = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HandledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HandledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Replies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SmsProvider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailProvider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SendBookingConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    SendDayBeforeReminder = table.Column<bool>(type: "bit", nullable: false),
                    SendEmailCopies = table.Column<bool>(type: "bit", nullable: false),
                    DayBeforeHour = table.Column<int>(type: "int", nullable: false),
                    ArrivalLeadMinutes = table.Column<int>(type: "int", nullable: false),
                    QuietStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    QuietEnd = table.Column<TimeOnly>(type: "time", nullable: false),
                    ClinicName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClinicPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ConfirmationsFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModicaBaseUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ModicaApplication = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModicaPasswordProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ModicaMask = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    GenericUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GenericAuthHeader = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GenericAuthValueProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GenericBodyTemplate = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SmtpHost = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SmtpPort = table.Column<int>(type: "int", nullable: false),
                    SmtpStartTls = table.Column<bool>(type: "bit", nullable: false),
                    SmtpUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SmtpPasswordProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WebhookKeyProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Address_CreatedAt",
                schema: "reminders",
                table: "Messages",
                columns: new[] { "Address", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_DedupeKey",
                schema: "reminders",
                table: "Messages",
                column: "DedupeKey",
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_PatientId",
                schema: "reminders",
                table: "Messages",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProviderMessageId",
                schema: "reminders",
                table: "Messages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Status_NextAttemptAt",
                schema: "reminders",
                table: "Messages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OptOuts_Channel_Address",
                schema: "reminders",
                table: "OptOuts",
                columns: new[] { "Channel", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Replies_ProviderMessageId",
                schema: "reminders",
                table: "Replies",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Replies_Status_ReceivedAt",
                schema: "reminders",
                table: "Replies",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Kind_Channel_Language",
                schema: "reminders",
                table: "Templates",
                columns: new[] { "Kind", "Channel", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Messages",
                schema: "reminders");

            migrationBuilder.DropTable(
                name: "OptOuts",
                schema: "reminders");

            migrationBuilder.DropTable(
                name: "Preferences",
                schema: "reminders");

            migrationBuilder.DropTable(
                name: "Replies",
                schema: "reminders");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "reminders");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "reminders");
        }
    }
}
