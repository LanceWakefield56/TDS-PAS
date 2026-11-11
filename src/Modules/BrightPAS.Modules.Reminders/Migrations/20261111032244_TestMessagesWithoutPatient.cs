using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Reminders.Migrations
{
    /// <inheritdoc />
    public partial class TestMessagesWithoutPatient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.12.3: test messages (no patient) failed against the cross-schema foreign key. The key is dropped here and
            // re-created by 90-reporting.sql, which always runs last.
            migrationBuilder.Sql("EXEC(N'IF OBJECT_ID(N''[reminders].[FK_reminders_Messages_Patient]'', N''F'') IS NOT NULL ALTER TABLE [reminders].[Messages] DROP CONSTRAINT [FK_reminders_Messages_Patient];')");
            migrationBuilder.AlterColumn<Guid>(
                name: "PatientId",
                schema: "reminders",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
            migrationBuilder.Sql("EXEC(N'UPDATE [reminders].[Messages] SET [PatientId] = NULL WHERE [PatientId] = ''00000000-0000-0000-0000-000000000000'';')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PatientId",
                schema: "reminders",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
