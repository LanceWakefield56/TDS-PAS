using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Rostering.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Templates_StaffId",
                schema: "roster",
                table: "Templates",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_FromStaffId",
                schema: "roster",
                table: "Swaps",
                column: "FromStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_OfferedShiftId",
                schema: "roster",
                table: "Swaps",
                column: "OfferedShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_ShiftId",
                schema: "roster",
                table: "Swaps",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_ToStaffId",
                schema: "roster",
                table: "Swaps",
                column: "ToStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_StaffId",
                schema: "roster",
                table: "Shifts",
                column: "StaffId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leave_Staff_StaffId",
                schema: "roster",
                table: "Leave",
                column: "StaffId",
                principalSchema: "roster",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Staff_StaffId",
                schema: "roster",
                table: "Shifts",
                column: "StaffId",
                principalSchema: "roster",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Swaps_Shifts_OfferedShiftId",
                schema: "roster",
                table: "Swaps",
                column: "OfferedShiftId",
                principalSchema: "roster",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Swaps_Shifts_ShiftId",
                schema: "roster",
                table: "Swaps",
                column: "ShiftId",
                principalSchema: "roster",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Swaps_Staff_FromStaffId",
                schema: "roster",
                table: "Swaps",
                column: "FromStaffId",
                principalSchema: "roster",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Swaps_Staff_ToStaffId",
                schema: "roster",
                table: "Swaps",
                column: "ToStaffId",
                principalSchema: "roster",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Templates_Staff_StaffId",
                schema: "roster",
                table: "Templates",
                column: "StaffId",
                principalSchema: "roster",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leave_Staff_StaffId",
                schema: "roster",
                table: "Leave");

            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Staff_StaffId",
                schema: "roster",
                table: "Shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Swaps_Shifts_OfferedShiftId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropForeignKey(
                name: "FK_Swaps_Shifts_ShiftId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropForeignKey(
                name: "FK_Swaps_Staff_FromStaffId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropForeignKey(
                name: "FK_Swaps_Staff_ToStaffId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropForeignKey(
                name: "FK_Templates_Staff_StaffId",
                schema: "roster",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Templates_StaffId",
                schema: "roster",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Swaps_FromStaffId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropIndex(
                name: "IX_Swaps_OfferedShiftId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropIndex(
                name: "IX_Swaps_ShiftId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropIndex(
                name: "IX_Swaps_ToStaffId",
                schema: "roster",
                table: "Swaps");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_StaffId",
                schema: "roster",
                table: "Shifts");
        }
    }
}
