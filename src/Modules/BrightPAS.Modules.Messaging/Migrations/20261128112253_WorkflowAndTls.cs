using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowAndTls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextTaskNumber",
                schema: "msg",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DueMinutes",
                schema: "msg",
                table: "Nodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalateToRole",
                schema: "msg",
                table: "Nodes",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskRole",
                schema: "msg",
                table: "Nodes",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskTitle",
                schema: "msg",
                table: "Nodes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaitMinutes",
                schema: "msg",
                table: "Nodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateThumbprint",
                schema: "msg",
                table: "Interfaces",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeerThumbprint",
                schema: "msg",
                table: "Interfaces",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseTls",
                schema: "msg",
                table: "Interfaces",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FlowWaits",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowWaits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowWaits_Flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "msg",
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FlowWaits_Messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "msg",
                        principalTable: "Messages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalateToRole = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalatedFrom = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "msg",
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tasks_Messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "msg",
                        principalTable: "Messages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlowWaits_FlowId",
                schema: "msg",
                table: "FlowWaits",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowWaits_MessageId",
                schema: "msg",
                table: "FlowWaits",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowWaits_Status_DueAt",
                schema: "msg",
                table: "FlowWaits",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DueAt",
                schema: "msg",
                table: "Tasks",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_FlowId",
                schema: "msg",
                table: "Tasks",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_MessageId",
                schema: "msg",
                table: "Tasks",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Number",
                schema: "msg",
                table: "Tasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PatientId",
                schema: "msg",
                table: "Tasks",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status_Role",
                schema: "msg",
                table: "Tasks",
                columns: new[] { "Status", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowWaits",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Tasks",
                schema: "msg");

            migrationBuilder.DropColumn(
                name: "NextTaskNumber",
                schema: "msg",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DueMinutes",
                schema: "msg",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "EscalateToRole",
                schema: "msg",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "TaskRole",
                schema: "msg",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "TaskTitle",
                schema: "msg",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "WaitMinutes",
                schema: "msg",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CertificateThumbprint",
                schema: "msg",
                table: "Interfaces");

            migrationBuilder.DropColumn(
                name: "PeerThumbprint",
                schema: "msg",
                table: "Interfaces");

            migrationBuilder.DropColumn(
                name: "UseTls",
                schema: "msg",
                table: "Interfaces");
        }
    }
}
