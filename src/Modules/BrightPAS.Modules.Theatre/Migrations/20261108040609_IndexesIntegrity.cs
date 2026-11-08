using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Theatre.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "BookingChecks",
                schema: "theatre",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingChecks", x => new { x.BookingId, x.ItemKey });
                    table.ForeignKey(
                        name: "FK_BookingChecks_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "theatre",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_LocationId",
                schema: "theatre",
                table: "Templates",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_LocationId",
                schema: "theatre",
                table: "Sessions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ScopeId",
                schema: "theatre",
                table: "Bookings",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_LocationId",
                schema: "theatre",
                table: "Appointments",
                column: "LocationId");

            // Move existing data into the normalised table before dropping the old column (0.10.1).
            migrationBuilder.Sql(@"INSERT INTO [theatre].[BookingChecks] ([BookingId], [ItemKey], [CheckedAt], [CheckedBy])
SELECT DISTINCT b.[Id], LEFT(LTRIM(RTRIM(v.[value])), 30), SYSDATETIMEOFFSET(), N'migrated' FROM [theatre].[Bookings] b CROSS APPLY STRING_SPLIT(b.[Checklist], ',') v
WHERE b.[Checklist] IS NOT NULL AND LEN(LTRIM(RTRIM(v.[value]))) > 0;");

            migrationBuilder.DropColumn(
                name: "Checklist",
                schema: "theatre",
                table: "Bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingChecks",
                schema: "theatre");

            migrationBuilder.DropIndex(
                name: "IX_Templates_LocationId",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_LocationId",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ScopeId",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_LocationId",
                schema: "theatre",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "Checklist",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }
    }
}
