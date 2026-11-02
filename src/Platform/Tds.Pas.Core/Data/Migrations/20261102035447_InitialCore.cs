using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tds.Pas.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            // Audit table is created as a SQL Server 2022 append-only LEDGER table (tamper-evident at the
            // database level, in addition to the application hash chain). EF cannot express this, so raw SQL is used.
            migrationBuilder.Sql(@"
CREATE TABLE [core].[AuditEvents] (
    [Sequence] bigint NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [UserId] nvarchar(100) NOT NULL,
    [Action] nvarchar(50) NOT NULL,
    [EntityType] nvarchar(100) NOT NULL,
    [EntityId] nvarchar(100) NOT NULL,
    [PatientId] nvarchar(100) NULL,
    [Reason] nvarchar(500) NULL,
    [Workstation] nvarchar(100) NULL,
    [PreviousHash] nvarchar(64) NOT NULL,
    [Hash] nvarchar(64) NOT NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Sequence])
) WITH (LEDGER = ON (APPEND_ONLY = ON));");

            migrationBuilder.CreateTable(
                name: "Organisations",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HpiOrganisationId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HpiFacilityId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "core",
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Facilities",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Facilities_Sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "core",
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "core",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                schema: "core",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_PatientId",
                schema: "core",
                table: "AuditEvents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_SiteId",
                schema: "core",
                table: "Facilities",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_FacilityId_Code",
                schema: "core",
                table: "Locations",
                columns: new[] { "FacilityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_OrganisationId",
                schema: "core",
                table: "Sites",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Locations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Facilities",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Sites",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Organisations",
                schema: "core");
        }
    }
}
