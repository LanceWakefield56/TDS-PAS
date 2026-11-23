IF OBJECT_ID(N'[research].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'research') IS NULL EXEC(N'CREATE SCHEMA [research];');
    CREATE TABLE [research].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    IF SCHEMA_ID(N'research') IS NULL EXEC(N'CREATE SCHEMA [research];');
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE TABLE [research].[Extracts] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Dataset] nvarchar(30) NOT NULL,
        [Level] nvarchar(16) NOT NULL,
        [FromDate] date NOT NULL,
        [ToDate] date NOT NULL,
        [Purpose] nvarchar(20) NOT NULL,
        [Recipient] nvarchar(200) NOT NULL,
        [EthicsReference] nvarchar(100) NULL,
        [MaoriDataGovernance] nvarchar(1000) NULL,
        [SmallCell] int NOT NULL,
        [RemoveSmallGroups] bit NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Rows] int NULL,
        [MinGroupSize] int NULL,
        [AtRiskRows] int NULL,
        [SuppressedCells] int NULL,
        [RequestedBy] nvarchar(100) NOT NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [DecidedBy] nvarchar(100) NULL,
        [DecidedAt] datetimeoffset NULL,
        [DecisionNote] nvarchar(2000) NULL,
        [ReleasedBy] nvarchar(100) NULL,
        [ReleasedAt] datetimeoffset NULL,
        [Downloads] int NOT NULL,
        CONSTRAINT [PK_Extracts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE TABLE [research].[Settings] (
        [Id] int NOT NULL,
        [NextNumber] int NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ResearchSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE TABLE [research].[ExtractColumns] (
        [ExtractId] uniqueidentifier NOT NULL,
        [Column] nvarchar(40) NOT NULL,
        [Position] int NOT NULL,
        CONSTRAINT [PK_ExtractColumns] PRIMARY KEY ([ExtractId], [Column]),
        CONSTRAINT [FK_ExtractColumns_Extracts_ExtractId] FOREIGN KEY ([ExtractId]) REFERENCES [research].[Extracts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE TABLE [research].[StudyCodes] (
        [ExtractId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Code] nvarchar(12) NOT NULL,
        CONSTRAINT [PK_StudyCodes] PRIMARY KEY ([ExtractId], [PatientId]),
        CONSTRAINT [FK_StudyCodes_Extracts_ExtractId] FOREIGN KEY ([ExtractId]) REFERENCES [research].[Extracts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextNumber') AND [object_id] = OBJECT_ID(N'[research].[Settings]'))
        SET IDENTITY_INSERT [research].[Settings] ON;
    EXEC(N'INSERT INTO [research].[Settings] ([Id], [NextNumber])
    VALUES (1, 1)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextNumber') AND [object_id] = OBJECT_ID(N'[research].[Settings]'))
        SET IDENTITY_INSERT [research].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Extracts_Number] ON [research].[Extracts] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE INDEX [IX_Extracts_Status_RequestedAt] ON [research].[Extracts] ([Status], [RequestedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StudyCodes_ExtractId_Code] ON [research].[StudyCodes] ([ExtractId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    CREATE INDEX [IX_StudyCodes_PatientId] ON [research].[StudyCodes] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [research].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123180755_InitialResearch'
)
BEGIN
    INSERT INTO [research].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123180755_InitialResearch', N'10.0.14');
END;

COMMIT;
GO

