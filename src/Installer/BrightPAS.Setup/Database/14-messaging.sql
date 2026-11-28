IF OBJECT_ID(N'[msg].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'msg') IS NULL EXEC(N'CREATE SCHEMA [msg];');
    CREATE TABLE [msg].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    IF SCHEMA_ID(N'msg') IS NULL EXEC(N'CREATE SCHEMA [msg];');
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Flows] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(300) NULL,
        [IsActive] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Flows] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Interfaces] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Direction] nvarchar(10) NOT NULL,
        [Transport] nvarchar(15) NOT NULL,
        [Format] nvarchar(10) NOT NULL,
        [Handler] nvarchar(20) NOT NULL,
        [Host] nvarchar(255) NULL,
        [Port] int NULL,
        [FolderPath] nvarchar(260) NULL,
        [Url] nvarchar(500) NULL,
        [AeTitle] nvarchar(16) NULL,
        [SqlText] nvarchar(4000) NULL,
        [PollSeconds] int NULL,
        [WatermarkColumn] nvarchar(128) NULL,
        [Secret] varbinary(max) NULL,
        [SystemKey] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        [AlertAfterFailures] int NOT NULL,
        [SilentAlertHours] int NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Interfaces] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Mappings] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [SourceFormat] nvarchar(10) NOT NULL,
        [TargetFormat] nvarchar(10) NOT NULL,
        [Description] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Mappings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[McpClients] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [ExpiresAt] datetimeoffset NULL,
        [LastUsedAt] datetimeoffset NULL,
        CONSTRAINT [PK_McpClients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Settings] (
        [Id] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [AlertEmail] nvarchar(254) NULL,
        [RequireSecondApprover] bit NOT NULL,
        [AiEnabled] bit NOT NULL,
        [AiEndpoint] nvarchar(300) NULL,
        [AiModel] nvarchar(100) NULL,
        [AiKey] varbinary(max) NULL,
        [AiPrivacyReference] nvarchar(300) NULL,
        [AiApprovedBy] nvarchar(100) NULL,
        [AiApprovedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Tables] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Description] nvarchar(300) NULL,
        [WhenMissing] nvarchar(12) NOT NULL,
        [DefaultValue] nvarchar(200) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Tables] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Alerts] (
        [Id] uniqueidentifier NOT NULL,
        [InterfaceId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(12) NOT NULL,
        [Detail] nvarchar(500) NOT NULL,
        [RaisedAt] datetimeoffset NOT NULL,
        [NotifiedAt] datetimeoffset NULL,
        [ClearedAt] datetimeoffset NULL,
        [ClearedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Alerts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Alerts_Interfaces_InterfaceId] FOREIGN KEY ([InterfaceId]) REFERENCES [msg].[Interfaces] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[AllowedClients] (
        [Id] uniqueidentifier NOT NULL,
        [InterfaceId] uniqueidentifier NOT NULL,
        [Address] nvarchar(45) NOT NULL,
        [AeTitle] nvarchar(16) NULL,
        [Description] nvarchar(100) NULL,
        CONSTRAINT [PK_AllowedClients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AllowedClients_Interfaces_InterfaceId] FOREIGN KEY ([InterfaceId]) REFERENCES [msg].[Interfaces] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Messages] (
        [Id] uniqueidentifier NOT NULL,
        [InterfaceId] uniqueidentifier NOT NULL,
        [Direction] nvarchar(10) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [MessageType] nvarchar(40) NULL,
        [ControlId] nvarchar(64) NULL,
        [Reference] nvarchar(64) NULL,
        [PatientId] uniqueidentifier NULL,
        [Nhi] nvarchar(10) NULL,
        [Content] varbinary(max) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [SizeBytes] int NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAt] datetimeoffset NULL,
        [LastError] nvarchar(500) NULL,
        [AckCode] nvarchar(4) NULL,
        [AckText] nvarchar(300) NULL,
        [ParentId] uniqueidentifier NULL,
        [FlowId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Flows_FlowId] FOREIGN KEY ([FlowId]) REFERENCES [msg].[Flows] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Messages_Interfaces_InterfaceId] FOREIGN KEY ([InterfaceId]) REFERENCES [msg].[Interfaces] ([Id]),
        CONSTRAINT [FK_Messages_Messages_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [msg].[Messages] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[States] (
        [InterfaceId] uniqueidentifier NOT NULL,
        [Watermark] nvarchar(200) NULL,
        [LastPolledAt] datetimeoffset NULL,
        [LastRows] int NOT NULL,
        [LastError] nvarchar(500) NULL,
        CONSTRAINT [PK_States] PRIMARY KEY ([InterfaceId]),
        CONSTRAINT [FK_States_Interfaces_InterfaceId] FOREIGN KEY ([InterfaceId]) REFERENCES [msg].[Interfaces] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Nodes] (
        [Id] uniqueidentifier NOT NULL,
        [FlowId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Label] nvarchar(60) NOT NULL,
        [X] int NOT NULL,
        [Y] int NOT NULL,
        [InterfaceId] uniqueidentifier NULL,
        [MappingId] uniqueidentifier NULL,
        [Match] nvarchar(4) NOT NULL,
        CONSTRAINT [PK_Nodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Nodes_Flows_FlowId] FOREIGN KEY ([FlowId]) REFERENCES [msg].[Flows] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Nodes_Interfaces_InterfaceId] FOREIGN KEY ([InterfaceId]) REFERENCES [msg].[Interfaces] ([Id]),
        CONSTRAINT [FK_Nodes_Mappings_MappingId] FOREIGN KEY ([MappingId]) REFERENCES [msg].[Mappings] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Versions] (
        [Id] uniqueidentifier NOT NULL,
        [MappingId] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [StartFrom] nvarchar(12) NOT NULL,
        [Template] nvarchar(max) NULL,
        [SampleInput] nvarchar(max) NULL,
        [ChangeNote] nvarchar(300) NULL,
        [EditedBy] nvarchar(100) NOT NULL,
        [EditedAt] datetimeoffset NOT NULL,
        [SubmittedBy] nvarchar(100) NULL,
        [SubmittedAt] datetimeoffset NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [ApprovedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Versions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Versions_Mappings_MappingId] FOREIGN KEY ([MappingId]) REFERENCES [msg].[Mappings] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Entries] (
        [Id] uniqueidentifier NOT NULL,
        [TableId] uniqueidentifier NOT NULL,
        [FromCode] nvarchar(100) NOT NULL,
        [ToCode] nvarchar(200) NOT NULL,
        [Description] nvarchar(200) NULL,
        CONSTRAINT [PK_Entries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Entries_Tables_TableId] FOREIGN KEY ([TableId]) REFERENCES [msg].[Tables] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Events] (
        [Id] uniqueidentifier NOT NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Step] nvarchar(30) NOT NULL,
        [Detail] nvarchar(500) NULL,
        CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Events_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [msg].[Messages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Conditions] (
        [Id] uniqueidentifier NOT NULL,
        [NodeId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Source] nvarchar(100) NOT NULL,
        [Operator] nvarchar(12) NOT NULL,
        [Value] nvarchar(200) NULL,
        CONSTRAINT [PK_Conditions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Conditions_Nodes_NodeId] FOREIGN KEY ([NodeId]) REFERENCES [msg].[Nodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Edges] (
        [Id] uniqueidentifier NOT NULL,
        [FlowId] uniqueidentifier NOT NULL,
        [FromNodeId] uniqueidentifier NOT NULL,
        [ToNodeId] uniqueidentifier NOT NULL,
        [Branch] nvarchar(4) NOT NULL,
        CONSTRAINT [PK_Edges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Edges_Flows_FlowId] FOREIGN KEY ([FlowId]) REFERENCES [msg].[Flows] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Edges_Nodes_FromNodeId] FOREIGN KEY ([FromNodeId]) REFERENCES [msg].[Nodes] ([Id]),
        CONSTRAINT [FK_Edges_Nodes_ToNodeId] FOREIGN KEY ([ToNodeId]) REFERENCES [msg].[Nodes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Filters] (
        [Id] uniqueidentifier NOT NULL,
        [VersionId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Source] nvarchar(100) NOT NULL,
        [Operator] nvarchar(12) NOT NULL,
        [Value] nvarchar(200) NULL,
        CONSTRAINT [PK_Filters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Filters_Versions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [msg].[Versions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE TABLE [msg].[Rules] (
        [Id] uniqueidentifier NOT NULL,
        [VersionId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Target] nvarchar(100) NOT NULL,
        [Source] nvarchar(300) NULL,
        [Function] nvarchar(12) NOT NULL,
        [Argument] nvarchar(300) NULL,
        [TranslationTableId] uniqueidentifier NULL,
        [ConditionSource] nvarchar(100) NULL,
        [ConditionOperator] nvarchar(12) NULL,
        [ConditionValue] nvarchar(200) NULL,
        CONSTRAINT [PK_Rules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Rules_Tables_TranslationTableId] FOREIGN KEY ([TranslationTableId]) REFERENCES [msg].[Tables] ([Id]),
        CONSTRAINT [FK_Rules_Versions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [msg].[Versions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Alerts_InterfaceId_ClearedAt] ON [msg].[Alerts] ([InterfaceId], [ClearedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AllowedClients_InterfaceId_Address] ON [msg].[AllowedClients] ([InterfaceId], [Address]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Conditions_NodeId_Sequence] ON [msg].[Conditions] ([NodeId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Edges_FlowId] ON [msg].[Edges] ([FlowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Edges_FromNodeId_Branch] ON [msg].[Edges] ([FromNodeId], [Branch]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Edges_ToNodeId] ON [msg].[Edges] ([ToNodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Entries_TableId_FromCode] ON [msg].[Entries] ([TableId], [FromCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Events_MessageId_At] ON [msg].[Events] ([MessageId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Filters_VersionId_Sequence] ON [msg].[Filters] ([VersionId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Flows_Name] ON [msg].[Flows] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Interfaces_Code] ON [msg].[Interfaces] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Interfaces_SystemKey] ON [msg].[Interfaces] ([SystemKey]) WHERE [SystemKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Mappings_Name] ON [msg].[Mappings] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_McpClients_TokenHash] ON [msg].[McpClients] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_ControlId] ON [msg].[Messages] ([ControlId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_FlowId] ON [msg].[Messages] ([FlowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_InterfaceId_CreatedAt] ON [msg].[Messages] ([InterfaceId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_Nhi] ON [msg].[Messages] ([Nhi]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_ParentId] ON [msg].[Messages] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_PatientId] ON [msg].[Messages] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_Reference] ON [msg].[Messages] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_Status_NextAttemptAt] ON [msg].[Messages] ([Status], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Nodes_FlowId] ON [msg].[Nodes] ([FlowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Nodes_InterfaceId_Kind] ON [msg].[Nodes] ([InterfaceId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Nodes_MappingId] ON [msg].[Nodes] ([MappingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Rules_TranslationTableId] ON [msg].[Rules] ([TranslationTableId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Rules_VersionId_Sequence] ON [msg].[Rules] ([VersionId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tables_Name] ON [msg].[Tables] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Versions_MappingId_Number] ON [msg].[Versions] ([MappingId], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    CREATE INDEX [IX_Versions_MappingId_Status] ON [msg].[Versions] ([MappingId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109180651_InitialMessaging'
)
BEGIN
    INSERT INTO [msg].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109180651_InitialMessaging', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Settings] ADD [NextTaskNumber] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Nodes] ADD [DueMinutes] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Nodes] ADD [EscalateToRole] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Nodes] ADD [TaskRole] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Nodes] ADD [TaskTitle] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Nodes] ADD [WaitMinutes] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Interfaces] ADD [CertificateThumbprint] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Interfaces] ADD [PeerThumbprint] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    ALTER TABLE [msg].[Interfaces] ADD [UseTls] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE TABLE [msg].[FlowWaits] (
        [Id] uniqueidentifier NOT NULL,
        [FlowId] uniqueidentifier NOT NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [NodeKey] nvarchar(40) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [Format] nvarchar(10) NOT NULL,
        [DueAt] datetimeoffset NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ReleasedAt] datetimeoffset NULL,
        CONSTRAINT [PK_FlowWaits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FlowWaits_Flows_FlowId] FOREIGN KEY ([FlowId]) REFERENCES [msg].[Flows] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FlowWaits_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [msg].[Messages] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE TABLE [msg].[Tasks] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(10) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Detail] nvarchar(2000) NULL,
        [Role] nvarchar(60) NOT NULL,
        [AssignedTo] nvarchar(100) NULL,
        [Priority] nvarchar(10) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [DueAt] datetimeoffset NULL,
        [EscalateToRole] nvarchar(60) NULL,
        [EscalatedAt] datetimeoffset NULL,
        [EscalatedFrom] nvarchar(60) NULL,
        [PatientId] uniqueidentifier NULL,
        [MessageId] uniqueidentifier NULL,
        [FlowId] uniqueidentifier NULL,
        [Source] nvarchar(20) NOT NULL,
        [Outcome] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [DoneAt] datetimeoffset NULL,
        [DoneBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Tasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tasks_Flows_FlowId] FOREIGN KEY ([FlowId]) REFERENCES [msg].[Flows] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Tasks_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [msg].[Messages] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_FlowWaits_FlowId] ON [msg].[FlowWaits] ([FlowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_FlowWaits_MessageId] ON [msg].[FlowWaits] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_FlowWaits_Status_DueAt] ON [msg].[FlowWaits] ([Status], [DueAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_Tasks_DueAt] ON [msg].[Tasks] ([DueAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_Tasks_FlowId] ON [msg].[Tasks] ([FlowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_Tasks_MessageId] ON [msg].[Tasks] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tasks_Number] ON [msg].[Tasks] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_Tasks_PatientId] ON [msg].[Tasks] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    CREATE INDEX [IX_Tasks_Status_Role] ON [msg].[Tasks] ([Status], [Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [msg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128112253_WorkflowAndTls'
)
BEGIN
    INSERT INTO [msg].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128112253_WorkflowAndTls', N'10.0.14');
END;

COMMIT;
GO

