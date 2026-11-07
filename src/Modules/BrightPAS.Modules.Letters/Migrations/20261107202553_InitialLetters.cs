using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Letters.Migrations
{
    /// <inheritdoc />
    public partial class InitialLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "letters");

            migrationBuilder.CreateTable(
                name: "Letters",
                schema: "letters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GpPracticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    MessageControlId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AckCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    AckText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FinalisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalisedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Letters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Practices",
                schema: "letters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HpiFacilityId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    HealthLinkEdi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Postcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreferredChannel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Practices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "letters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SenderEdi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OutboxFolder = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    InboxFolder = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    NextControlNumber = table.Column<int>(type: "int", nullable: false),
                    NextLetterNumber = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "letters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Letters_MessageControlId",
                schema: "letters",
                table: "Letters",
                column: "MessageControlId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_Number",
                schema: "letters",
                table: "Letters",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Letters_PatientId",
                schema: "letters",
                table: "Letters",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_Status_CreatedAt",
                schema: "letters",
                table: "Letters",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Practices_HpiFacilityId",
                schema: "letters",
                table: "Practices",
                column: "HpiFacilityId",
                unique: true,
                filter: "[HpiFacilityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Code",
                schema: "letters",
                table: "Templates",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Letters",
                schema: "letters");

            migrationBuilder.DropTable(
                name: "Practices",
                schema: "letters");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "letters");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "letters");
        }
    }
}
