using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Patients.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transfers_BookingId",
                schema: "patients",
                table: "Transfers",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_GpPracticeHpiFacilityId",
                schema: "patients",
                table: "Patients",
                column: "GpPracticeHpiFacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_MergedIntoId",
                schema: "patients",
                table: "Patients",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_Merges_DuplicateId",
                schema: "patients",
                table: "Merges",
                column: "DuplicateId");

            migrationBuilder.CreateIndex(
                name: "IX_Merges_SurvivorId",
                schema: "patients",
                table: "Merges",
                column: "SurvivorId");

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_PatientId",
                schema: "patients",
                table: "Corrections",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Corrections_Patients_PatientId",
                schema: "patients",
                table: "Corrections",
                column: "PatientId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Patients_PatientId",
                schema: "patients",
                table: "Documents",
                column: "PatientId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Merges_Patients_DuplicateId",
                schema: "patients",
                table: "Merges",
                column: "DuplicateId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Merges_Patients_SurvivorId",
                schema: "patients",
                table: "Merges",
                column: "SurvivorId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Patients_MergedIntoId",
                schema: "patients",
                table: "Patients",
                column: "MergedIntoId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Patients_PatientId",
                schema: "patients",
                table: "Transfers",
                column: "PatientId",
                principalSchema: "patients",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Corrections_Patients_PatientId",
                schema: "patients",
                table: "Corrections");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Patients_PatientId",
                schema: "patients",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Merges_Patients_DuplicateId",
                schema: "patients",
                table: "Merges");

            migrationBuilder.DropForeignKey(
                name: "FK_Merges_Patients_SurvivorId",
                schema: "patients",
                table: "Merges");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Patients_MergedIntoId",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Patients_PatientId",
                schema: "patients",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_BookingId",
                schema: "patients",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Patients_GpPracticeHpiFacilityId",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_MergedIntoId",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Merges_DuplicateId",
                schema: "patients",
                table: "Merges");

            migrationBuilder.DropIndex(
                name: "IX_Merges_SurvivorId",
                schema: "patients",
                table: "Merges");

            migrationBuilder.DropIndex(
                name: "IX_Corrections_PatientId",
                schema: "patients",
                table: "Corrections");
        }
    }
}
