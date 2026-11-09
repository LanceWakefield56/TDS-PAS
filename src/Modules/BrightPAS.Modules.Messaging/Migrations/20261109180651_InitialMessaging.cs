using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class InitialMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "msg");

            migrationBuilder.CreateTable(
                name: "Flows",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Interfaces",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Transport = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    FolderPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AeTitle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SqlText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PollSeconds = table.Column<int>(type: "int", nullable: true),
                    WatermarkColumn = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Secret = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SystemKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AlertAfterFailures = table.Column<int>(type: "int", nullable: false),
                    SilentAlertHours = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interfaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mappings",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpClients",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    AlertEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    RequireSecondApprover = table.Column<bool>(type: "bit", nullable: false),
                    AiEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AiEndpoint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AiModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AiKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    AiPrivacyReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AiApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AiApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tables",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    WhenMissing = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InterfaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClearedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClearedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Interfaces_InterfaceId",
                        column: x => x.InterfaceId,
                        principalSchema: "msg",
                        principalTable: "Interfaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllowedClients",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InterfaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    AeTitle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllowedClients_Interfaces_InterfaceId",
                        column: x => x.InterfaceId,
                        principalSchema: "msg",
                        principalTable: "Interfaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InterfaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ControlId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Nhi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AckCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AckText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "msg",
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Messages_Interfaces_InterfaceId",
                        column: x => x.InterfaceId,
                        principalSchema: "msg",
                        principalTable: "Interfaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Messages_Messages_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "msg",
                        principalTable: "Messages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "States",
                schema: "msg",
                columns: table => new
                {
                    InterfaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Watermark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRows = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_States", x => x.InterfaceId);
                    table.ForeignKey(
                        name: "FK_States_Interfaces_InterfaceId",
                        column: x => x.InterfaceId,
                        principalSchema: "msg",
                        principalTable: "Interfaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    InterfaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Match = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nodes_Flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "msg",
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Nodes_Interfaces_InterfaceId",
                        column: x => x.InterfaceId,
                        principalSchema: "msg",
                        principalTable: "Interfaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nodes_Mappings_MappingId",
                        column: x => x.MappingId,
                        principalSchema: "msg",
                        principalTable: "Mappings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Versions",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartFrom = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Template = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SampleInput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EditedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Versions_Mappings_MappingId",
                        column: x => x.MappingId,
                        principalSchema: "msg",
                        principalTable: "Mappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ToCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entries_Tables_TableId",
                        column: x => x.TableId,
                        principalSchema: "msg",
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "msg",
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conditions",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conditions_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "msg",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Edges",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Edges_Flows_FlowId",
                        column: x => x.FlowId,
                        principalSchema: "msg",
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Edges_Nodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalSchema: "msg",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Edges_Nodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalSchema: "msg",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Filters",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Filters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Filters_Versions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "msg",
                        principalTable: "Versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Function = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Argument = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TranslationTableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConditionSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConditionOperator = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ConditionValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_Tables_TranslationTableId",
                        column: x => x.TranslationTableId,
                        principalSchema: "msg",
                        principalTable: "Tables",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Rules_Versions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "msg",
                        principalTable: "Versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_InterfaceId_ClearedAt",
                schema: "msg",
                table: "Alerts",
                columns: new[] { "InterfaceId", "ClearedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedClients_InterfaceId_Address",
                schema: "msg",
                table: "AllowedClients",
                columns: new[] { "InterfaceId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conditions_NodeId_Sequence",
                schema: "msg",
                table: "Conditions",
                columns: new[] { "NodeId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Edges_FlowId",
                schema: "msg",
                table: "Edges",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Edges_FromNodeId_Branch",
                schema: "msg",
                table: "Edges",
                columns: new[] { "FromNodeId", "Branch" });

            migrationBuilder.CreateIndex(
                name: "IX_Edges_ToNodeId",
                schema: "msg",
                table: "Edges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_TableId_FromCode",
                schema: "msg",
                table: "Entries",
                columns: new[] { "TableId", "FromCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_MessageId_At",
                schema: "msg",
                table: "Events",
                columns: new[] { "MessageId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Filters_VersionId_Sequence",
                schema: "msg",
                table: "Filters",
                columns: new[] { "VersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Flows_Name",
                schema: "msg",
                table: "Flows",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Interfaces_Code",
                schema: "msg",
                table: "Interfaces",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Interfaces_SystemKey",
                schema: "msg",
                table: "Interfaces",
                column: "SystemKey",
                unique: true,
                filter: "[SystemKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Mappings_Name",
                schema: "msg",
                table: "Mappings",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpClients_TokenHash",
                schema: "msg",
                table: "McpClients",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ControlId",
                schema: "msg",
                table: "Messages",
                column: "ControlId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_FlowId",
                schema: "msg",
                table: "Messages",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_InterfaceId_CreatedAt",
                schema: "msg",
                table: "Messages",
                columns: new[] { "InterfaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Nhi",
                schema: "msg",
                table: "Messages",
                column: "Nhi");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ParentId",
                schema: "msg",
                table: "Messages",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_PatientId",
                schema: "msg",
                table: "Messages",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Reference",
                schema: "msg",
                table: "Messages",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Status_NextAttemptAt",
                schema: "msg",
                table: "Messages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_FlowId",
                schema: "msg",
                table: "Nodes",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_InterfaceId_Kind",
                schema: "msg",
                table: "Nodes",
                columns: new[] { "InterfaceId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_MappingId",
                schema: "msg",
                table: "Nodes",
                column: "MappingId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_TranslationTableId",
                schema: "msg",
                table: "Rules",
                column: "TranslationTableId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_VersionId_Sequence",
                schema: "msg",
                table: "Rules",
                columns: new[] { "VersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Tables_Name",
                schema: "msg",
                table: "Tables",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Versions_MappingId_Number",
                schema: "msg",
                table: "Versions",
                columns: new[] { "MappingId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Versions_MappingId_Status",
                schema: "msg",
                table: "Versions",
                columns: new[] { "MappingId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "AllowedClients",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Conditions",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Edges",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Entries",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Events",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Filters",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "McpClients",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Rules",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "States",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Nodes",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Tables",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Versions",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Flows",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Interfaces",
                schema: "msg");

            migrationBuilder.DropTable(
                name: "Mappings",
                schema: "msg");
        }
    }
}
