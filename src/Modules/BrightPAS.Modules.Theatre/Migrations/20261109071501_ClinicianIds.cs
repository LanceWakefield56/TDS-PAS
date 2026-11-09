using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Theatre.Migrations
{
    /// <inheritdoc />
    public partial class ClinicianIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {






            migrationBuilder.AddColumn<Guid>(
                name: "AnaesthetistId",
                schema: "theatre",
                table: "Templates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SurgeonId",
                schema: "theatre",
                table: "Templates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "AnaesthetistId",
                schema: "theatre",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SurgeonId",
                schema: "theatre",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicianId",
                schema: "theatre",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Templates_AnaesthetistId",
                schema: "theatre",
                table: "Templates",
                column: "AnaesthetistId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_SurgeonId",
                schema: "theatre",
                table: "Templates",
                column: "SurgeonId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_AnaesthetistId",
                schema: "theatre",
                table: "Sessions",
                column: "AnaesthetistId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SurgeonId_Date",
                schema: "theatre",
                table: "Sessions",
                columns: new[] { "SurgeonId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicianId_Date",
                schema: "theatre",
                table: "Appointments",
                columns: new[] { "ClinicianId", "Date" });

            // Dynamic SQL so the batch compiles before the new columns exist. Map the names typed on existing records to clinician-register ids before the name columns are dropped (3NF, 0.10.2).
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [core].[Clinicians] ([Id], [Name], [Type], [Colour], [IsActive])
SELECT NEWID(), x.[Name], N''Other'', 0, 0 FROM (SELECT DISTINCT LEFT(LTRIM(RTRIM(n.[Name])), 150) AS [Name] FROM (SELECT [Surgeon] AS [Name] FROM [theatre].[Sessions] UNION ALL SELECT [Anaesthetist] FROM [theatre].[Sessions] UNION ALL SELECT [Surgeon] FROM [theatre].[Templates] UNION ALL SELECT [Anaesthetist] FROM [theatre].[Templates] UNION ALL SELECT [Clinician] FROM [theatre].[Appointments]) n WHERE n.[Name] IS NOT NULL AND LTRIM(RTRIM(n.[Name])) <> N'''') x
WHERE NOT EXISTS (SELECT 1 FROM [core].[Clinicians] c WHERE c.[Name] = x.[Name]);
UPDATE s SET [SurgeonId] = c.[Id] FROM [theatre].[Sessions] s JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(s.[Surgeon]));
UPDATE s SET [AnaesthetistId] = c.[Id] FROM [theatre].[Sessions] s JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(s.[Anaesthetist]));
UPDATE t SET [SurgeonId] = c.[Id] FROM [theatre].[Templates] t JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(t.[Surgeon]));
UPDATE t SET [AnaesthetistId] = c.[Id] FROM [theatre].[Templates] t JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(t.[Anaesthetist]));
UPDATE a SET [ClinicianId] = c.[Id] FROM [theatre].[Appointments] a JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(a.[Clinician]));');");

            migrationBuilder.DropColumn(
                name: "Anaesthetist",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Surgeon",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Anaesthetist",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LocationName",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Surgeon",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Clinician",
                schema: "theatre",
                table: "Appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Templates_AnaesthetistId",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Templates_SurgeonId",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_AnaesthetistId",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_SurgeonId_Date",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicianId_Date",
                schema: "theatre",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "AnaesthetistId",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "SurgeonId",
                schema: "theatre",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "AnaesthetistId",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SurgeonId",
                schema: "theatre",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ClinicianId",
                schema: "theatre",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "Anaesthetist",
                schema: "theatre",
                table: "Templates",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Surgeon",
                schema: "theatre",
                table: "Templates",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Anaesthetist",
                schema: "theatre",
                table: "Sessions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                schema: "theatre",
                table: "Sessions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Surgeon",
                schema: "theatre",
                table: "Sessions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Clinician",
                schema: "theatre",
                table: "Appointments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }
    }
}
