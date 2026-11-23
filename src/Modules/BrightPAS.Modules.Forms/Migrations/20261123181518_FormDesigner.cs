using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Forms.Migrations
{
    /// <inheritdoc />
    public partial class FormDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Designs",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DesignVersions",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Header = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Footer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ThirdParty = table.Column<bool>(type: "bit", nullable: false),
                    LicenceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesignVersions_Designs_DesignId",
                        column: x => x.DesignId,
                        principalSchema: "forms",
                        principalTable: "Designs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entries_DesignVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "forms",
                        principalTable: "DesignVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fields",
                schema: "forms",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    Min = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    ShowIfKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ShowIfValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Calculation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MapsTo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Help = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fields", x => new { x.VersionId, x.Key });
                    table.ForeignKey(
                        name: "FK_Fields_DesignVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "forms",
                        principalTable: "DesignVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntryValues",
                schema: "forms",
                columns: table => new
                {
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryValues", x => new { x.EntryId, x.FieldKey });
                    table.ForeignKey(
                        name: "FK_EntryValues_Entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "forms",
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FieldOptions",
                schema: "forms",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldOptions", x => new { x.VersionId, x.FieldKey, x.Sequence });
                    table.ForeignKey(
                        name: "FK_FieldOptions_Fields_VersionId_FieldKey",
                        columns: x => new { x.VersionId, x.FieldKey },
                        principalSchema: "forms",
                        principalTable: "Fields",
                        principalColumns: new[] { "VersionId", "Key" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Designs_Code",
                schema: "forms",
                table: "Designs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesignVersions_DesignId_Version",
                schema: "forms",
                table: "DesignVersions",
                columns: new[] { "DesignId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DesignVersions_OnePublished",
                schema: "forms",
                table: "DesignVersions",
                column: "DesignId",
                unique: true,
                filter: "[Status] = 'Published'");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_BookingId",
                schema: "forms",
                table: "Entries",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_PatientId",
                schema: "forms",
                table: "Entries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_VersionId",
                schema: "forms",
                table: "Entries",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Fields_VersionId_Sequence",
                schema: "forms",
                table: "Fields",
                columns: new[] { "VersionId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryValues",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "FieldOptions",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "Entries",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "Fields",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "DesignVersions",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "Designs",
                schema: "forms");
        }
    }
}
