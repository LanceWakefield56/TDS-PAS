using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Credentialing.Migrations
{
    /// <inheritdoc />
    public partial class InitialCredentialing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cred");

            migrationBuilder.CreateTable(
                name: "Credentials",
                schema: "cred",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Issuer = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Privileges",
                schema: "cred",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Service = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcedureCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ApprovedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewDue = table.Column<DateOnly>(type: "date", nullable: true),
                    Conditions = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Privileges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "cred",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RequiredTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WarningDays = table.Column<int>(type: "int", nullable: false),
                    CheckPrivileges = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_ClinicianId_Type",
                schema: "cred",
                table: "Credentials",
                columns: new[] { "ClinicianId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Privileges_ClinicianId",
                schema: "cred",
                table: "Privileges",
                column: "ClinicianId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Credentials",
                schema: "cred");

            migrationBuilder.DropTable(
                name: "Privileges",
                schema: "cred");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "cred");
        }
    }
}
