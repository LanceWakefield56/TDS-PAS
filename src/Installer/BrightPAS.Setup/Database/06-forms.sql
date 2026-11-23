IF OBJECT_ID(N'[forms].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'forms') IS NULL EXEC(N'CREATE SCHEMA [forms];');
    CREATE TABLE [forms].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    IF SCHEMA_ID(N'forms') IS NULL EXEC(N'CREATE SCHEMA [forms];');
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    CREATE TABLE [forms].[Requests] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [DefinitionVersion] nvarchar(20) NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [OpenedAt] datetimeoffset NULL,
        [SubmittedAt] datetimeoffset NULL,
        [AnswersJson] nvarchar(max) NULL,
        [FlagsJson] nvarchar(max) NULL,
        [SuggestedCategory] nvarchar(10) NULL,
        [TriageCategory] nvarchar(10) NULL,
        [TriageOutcome] nvarchar(100) NULL,
        [TriageNotes] nvarchar(2000) NULL,
        [TriagedBy] nvarchar(100) NULL,
        [TriagedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    CREATE INDEX [IX_Requests_PatientId] ON [forms].[Requests] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    CREATE INDEX [IX_Requests_Status_SubmittedAt] ON [forms].[Requests] ([Status], [SubmittedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Requests_TokenHash] ON [forms].[Requests] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112520_InitialForms'
)
BEGIN
    INSERT INTO [forms].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106112520_InitialForms', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    CREATE TABLE [forms].[Answers] (
        [RequestId] uniqueidentifier NOT NULL,
        [QuestionKey] nvarchar(60) NOT NULL,
        [Value] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_Answers] PRIMARY KEY ([RequestId], [QuestionKey]),
        CONSTRAINT [FK_Answers_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [forms].[Requests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    CREATE TABLE [forms].[Flags] (
        [RequestId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Level] nvarchar(10) NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_Flags] PRIMARY KEY ([RequestId], [Sequence]),
        CONSTRAINT [FK_Flags_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [forms].[Requests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Requests_BookingId] ON [forms].[Requests] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    EXEC(N'INSERT INTO [forms].[Answers] ([RequestId], [QuestionKey], [Value])
    SELECT r.[Id], LEFT(j.[key], 60), LEFT(j.[value], 2000) FROM [forms].[Requests] r CROSS APPLY OPENJSON(r.[AnswersJson]) j
    WHERE r.[AnswersJson] IS NOT NULL AND ISJSON(r.[AnswersJson]) = 1 AND LEN(j.[value]) > 0;
    INSERT INTO [forms].[Flags] ([RequestId], [Sequence], [Level], [Text])
    SELECT r.[Id], CAST(j.[key] AS int) + 1, LEFT(ISNULL(JSON_VALUE(j.[value], ''$.level''), ''''), 10), LEFT(ISNULL(JSON_VALUE(j.[value], ''$.text''), ''''), 300)
    FROM [forms].[Requests] r CROSS APPLY OPENJSON(r.[FlagsJson]) j WHERE r.[FlagsJson] IS NOT NULL AND ISJSON(r.[FlagsJson]) = 1;');
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[forms].[Requests]') AND [c].[name] = N'AnswersJson');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [forms].[Requests] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [forms].[Requests] DROP COLUMN [AnswersJson];
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[forms].[Requests]') AND [c].[name] = N'FlagsJson');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [forms].[Requests] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [forms].[Requests] DROP COLUMN [FlagsJson];
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040613_IndexesIntegrity'
)
BEGIN
    INSERT INTO [forms].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040613_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[Designs] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Category] nvarchar(24) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Designs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[DesignVersions] (
        [Id] uniqueidentifier NOT NULL,
        [DesignId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        [Header] nvarchar(500) NULL,
        [Footer] nvarchar(500) NULL,
        [ThirdParty] bit NOT NULL,
        [LicenceNote] nvarchar(500) NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [ApprovalNote] nvarchar(1000) NULL,
        [PublishedAt] datetimeoffset NULL,
        [RetiredAt] datetimeoffset NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_DesignVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DesignVersions_Designs_DesignId] FOREIGN KEY ([DesignId]) REFERENCES [forms].[Designs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[Entries] (
        [Id] uniqueidentifier NOT NULL,
        [VersionId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [Status] nvarchar(12) NOT NULL,
        [StartedBy] nvarchar(100) NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [CompletedBy] nvarchar(100) NULL,
        [CompletedAt] datetimeoffset NULL,
        [VoidReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Entries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Entries_DesignVersions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [forms].[DesignVersions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[Fields] (
        [VersionId] uniqueidentifier NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [Sequence] int NOT NULL,
        [Section] nvarchar(100) NULL,
        [Label] nvarchar(300) NOT NULL,
        [Type] nvarchar(12) NOT NULL,
        [Required] bit NOT NULL,
        [Min] decimal(12,3) NULL,
        [Max] decimal(12,3) NULL,
        [ShowIfKey] nvarchar(40) NULL,
        [ShowIfValue] nvarchar(100) NULL,
        [Calculation] nvarchar(200) NULL,
        [MapsTo] nvarchar(30) NULL,
        [Help] nvarchar(500) NULL,
        CONSTRAINT [PK_Fields] PRIMARY KEY ([VersionId], [Key]),
        CONSTRAINT [FK_Fields_DesignVersions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [forms].[DesignVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[EntryValues] (
        [EntryId] uniqueidentifier NOT NULL,
        [FieldKey] nvarchar(40) NOT NULL,
        [Value] nvarchar(4000) NOT NULL,
        CONSTRAINT [PK_EntryValues] PRIMARY KEY ([EntryId], [FieldKey]),
        CONSTRAINT [FK_EntryValues_Entries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [forms].[Entries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE TABLE [forms].[FieldOptions] (
        [VersionId] uniqueidentifier NOT NULL,
        [FieldKey] nvarchar(40) NOT NULL,
        [Sequence] int NOT NULL,
        [Value] nvarchar(200) NOT NULL,
        [Score] int NULL,
        CONSTRAINT [PK_FieldOptions] PRIMARY KEY ([VersionId], [FieldKey], [Sequence]),
        CONSTRAINT [FK_FieldOptions_Fields_VersionId_FieldKey] FOREIGN KEY ([VersionId], [FieldKey]) REFERENCES [forms].[Fields] ([VersionId], [Key]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Designs_Code] ON [forms].[Designs] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DesignVersions_DesignId_Version] ON [forms].[DesignVersions] ([DesignId], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_DesignVersions_OnePublished] ON [forms].[DesignVersions] ([DesignId]) WHERE [Status] = ''Published''');
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE INDEX [IX_Entries_BookingId] ON [forms].[Entries] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE INDEX [IX_Entries_PatientId] ON [forms].[Entries] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE INDEX [IX_Entries_VersionId] ON [forms].[Entries] ([VersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Fields_VersionId_Sequence] ON [forms].[Fields] ([VersionId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [forms].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123181518_FormDesigner'
)
BEGIN
    INSERT INTO [forms].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123181518_FormDesigner', N'10.0.14');
END;

COMMIT;
GO

