using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Patients.Migrations
{
    /// <inheritdoc />
    public partial class DocumentText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTexts",
                schema: "patients",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CharCount = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTexts", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentTexts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "patients",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_Status",
                schema: "patients",
                table: "DocumentTexts",
                column: "Status");
        
            // Existing documents become searchable too: queue them for text extraction.
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [patients].[DocumentTexts] ([DocumentId], [Status], [Attempts], [CharCount]) SELECT d.[Id], N''Pending'', 0, 0 FROM [patients].[Documents] d WHERE d.[IsDeleted] = 0 AND NOT EXISTS (SELECT 1 FROM [patients].[DocumentTexts] t WHERE t.[DocumentId] = d.[Id]);');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTexts",
                schema: "patients");
        }
    }
}
