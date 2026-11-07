using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                schema: "core",
                table: "Sites",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                schema: "core",
                table: "Sites",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "core",
                table: "Sites",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "core",
                table: "Sites",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "core",
                table: "Sites",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                schema: "core",
                table: "Sites",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suburb",
                schema: "core",
                table: "Sites",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clinicians",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HpiCpn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PracticeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinicians", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Procedures",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultAnaesthetic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SideRequired = table.Column<bool>(type: "bit", nullable: false),
                    FeeCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Procedures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clinicians_Name",
                schema: "core",
                table: "Clinicians",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_Code",
                schema: "core",
                table: "Procedures",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clinicians",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Procedures",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Postcode",
                schema: "core",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Suburb",
                schema: "core",
                table: "Sites");
        }
    }
}
