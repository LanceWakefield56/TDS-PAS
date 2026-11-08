using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Labs.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Reports_MessageId",
                schema: "labs",
                table: "Reports",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_SupersededById",
                schema: "labs",
                table: "Reports",
                column: "SupersededById");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ControlId",
                schema: "labs",
                table: "Messages",
                column: "ControlId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Messages_MessageId",
                schema: "labs",
                table: "Reports",
                column: "MessageId",
                principalSchema: "labs",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Reports_SupersededById",
                schema: "labs",
                table: "Reports",
                column: "SupersededById",
                principalSchema: "labs",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Messages_MessageId",
                schema: "labs",
                table: "Reports");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Reports_SupersededById",
                schema: "labs",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_MessageId",
                schema: "labs",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_SupersededById",
                schema: "labs",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ControlId",
                schema: "labs",
                table: "Messages");
        }
    }
}
