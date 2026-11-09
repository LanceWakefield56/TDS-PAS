using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Forms.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.CreateTable(
                name: "Answers",
                schema: "forms",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Answers", x => new { x.RequestId, x.QuestionKey });
                    table.ForeignKey(
                        name: "FK_Answers_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "forms",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flags",
                schema: "forms",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => new { x.RequestId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_Flags_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "forms",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_BookingId",
                schema: "forms",
                table: "Requests",
                column: "BookingId");

            // Dynamic SQL so the batch compiles before the new columns exist. Move existing data into the normalised table before dropping the old column (0.10.1).
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [forms].[Answers] ([RequestId], [QuestionKey], [Value])
SELECT r.[Id], LEFT(j.[key], 60), LEFT(j.[value], 2000) FROM [forms].[Requests] r CROSS APPLY OPENJSON(r.[AnswersJson]) j
WHERE r.[AnswersJson] IS NOT NULL AND ISJSON(r.[AnswersJson]) = 1 AND LEN(j.[value]) > 0;
INSERT INTO [forms].[Flags] ([RequestId], [Sequence], [Level], [Text])
SELECT r.[Id], CAST(j.[key] AS int) + 1, LEFT(ISNULL(JSON_VALUE(j.[value], ''$.level''), ''''), 10), LEFT(ISNULL(JSON_VALUE(j.[value], ''$.text''), ''''), 300)
FROM [forms].[Requests] r CROSS APPLY OPENJSON(r.[FlagsJson]) j WHERE r.[FlagsJson] IS NOT NULL AND ISJSON(r.[FlagsJson]) = 1;');");

            migrationBuilder.DropColumn(
                name: "AnswersJson",
                schema: "forms",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "FlagsJson",
                schema: "forms",
                table: "Requests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Answers",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "Flags",
                schema: "forms");

            migrationBuilder.DropIndex(
                name: "IX_Requests_BookingId",
                schema: "forms",
                table: "Requests");

            migrationBuilder.AddColumn<string>(
                name: "AnswersJson",
                schema: "forms",
                table: "Requests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlagsJson",
                schema: "forms",
                table: "Requests",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
