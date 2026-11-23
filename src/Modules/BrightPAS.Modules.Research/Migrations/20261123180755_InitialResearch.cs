using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Research.Migrations
{
    /// <inheritdoc />
    public partial class InitialResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "research");

            migrationBuilder.CreateTable(
                name: "Extracts",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Dataset = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EthicsReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaoriDataGovernance = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SmallCell = table.Column<int>(type: "int", nullable: false),
                    RemoveSmallGroups = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: true),
                    MinGroupSize = table.Column<int>(type: "int", nullable: true),
                    AtRiskRows = table.Column<int>(type: "int", nullable: true),
                    SuppressedCells = table.Column<int>(type: "int", nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReleasedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Downloads = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Extracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "research",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_ResearchSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "ExtractColumns",
                schema: "research",
                columns: table => new
                {
                    ExtractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Column = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractColumns", x => new { x.ExtractId, x.Column });
                    table.ForeignKey(
                        name: "FK_ExtractColumns_Extracts_ExtractId",
                        column: x => x.ExtractId,
                        principalSchema: "research",
                        principalTable: "Extracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyCodes",
                schema: "research",
                columns: table => new
                {
                    ExtractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyCodes", x => new { x.ExtractId, x.PatientId });
                    table.ForeignKey(
                        name: "FK_StudyCodes_Extracts_ExtractId",
                        column: x => x.ExtractId,
                        principalSchema: "research",
                        principalTable: "Extracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "research",
                table: "Settings",
                columns: new[] { "Id", "NextNumber" },
                values: new object[] { 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Extracts_Number",
                schema: "research",
                table: "Extracts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Extracts_Status_RequestedAt",
                schema: "research",
                table: "Extracts",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyCodes_ExtractId_Code",
                schema: "research",
                table: "StudyCodes",
                columns: new[] { "ExtractId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyCodes_PatientId",
                schema: "research",
                table: "StudyCodes",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractColumns",
                schema: "research");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "research");

            migrationBuilder.DropTable(
                name: "StudyCodes",
                schema: "research");

            migrationBuilder.DropTable(
                name: "Extracts",
                schema: "research");
        }
    }
}
