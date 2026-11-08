using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EntityType_EntityId",
                schema: "core",
                table: "AuditEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserId_OccurredAt",
                schema: "core",
                table: "AuditEvents",
                columns: new[] { "UserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_EntityType_EntityId",
                schema: "core",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_UserId_OccurredAt",
                schema: "core",
                table: "AuditEvents");
        }
    }
}
