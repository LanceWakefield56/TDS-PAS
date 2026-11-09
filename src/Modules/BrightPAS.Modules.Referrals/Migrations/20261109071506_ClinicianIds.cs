using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Referrals.Migrations
{
    /// <inheritdoc />
    public partial class ClinicianIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.AddColumn<Guid>(
                name: "SpecialistId",
                schema: "referrals",
                table: "Waitlist",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SpecialistId",
                schema: "referrals",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_SpecialistId",
                schema: "referrals",
                table: "Waitlist",
                column: "SpecialistId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_SpecialistId",
                schema: "referrals",
                table: "Referrals",
                column: "SpecialistId");

            // Dynamic SQL so the batch compiles before the new columns exist. Map the names typed on existing records to clinician-register ids before the name columns are dropped (3NF, 0.10.2).
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [core].[Clinicians] ([Id], [Name], [Type], [Colour], [IsActive])
SELECT NEWID(), x.[Name], N''Other'', 0, 0 FROM (SELECT DISTINCT LEFT(LTRIM(RTRIM(n.[Name])), 150) AS [Name] FROM (SELECT [Specialist] AS [Name] FROM [referrals].[Referrals] UNION ALL SELECT [Specialist] FROM [referrals].[Waitlist]) n WHERE n.[Name] IS NOT NULL AND LTRIM(RTRIM(n.[Name])) <> N'''') x
WHERE NOT EXISTS (SELECT 1 FROM [core].[Clinicians] c WHERE c.[Name] = x.[Name]);
UPDATE r SET [SpecialistId] = c.[Id] FROM [referrals].[Referrals] r JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(r.[Specialist]));
UPDATE w SET [SpecialistId] = c.[Id] FROM [referrals].[Waitlist] w JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(w.[Specialist]));');");

            migrationBuilder.DropColumn(
                name: "Specialist",
                schema: "referrals",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "Specialist",
                schema: "referrals",
                table: "Referrals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Waitlist_SpecialistId",
                schema: "referrals",
                table: "Waitlist");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_SpecialistId",
                schema: "referrals",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "SpecialistId",
                schema: "referrals",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "SpecialistId",
                schema: "referrals",
                table: "Referrals");

            migrationBuilder.AddColumn<string>(
                name: "Specialist",
                schema: "referrals",
                table: "Waitlist",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Specialist",
                schema: "referrals",
                table: "Referrals",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }
    }
}
