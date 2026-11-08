IF OBJECT_ID(N'[labs].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'labs') IS NULL EXEC(N'CREATE SCHEMA [labs];');
    CREATE TABLE [labs].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    IF SCHEMA_ID(N'labs') IS NULL EXEC(N'CREATE SCHEMA [labs];');
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE TABLE [labs].[Messages] (
        [Id] uniqueidentifier NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [ControlId] nvarchar(50) NULL,
        [SendingFacility] nvarchar(100) NULL,
        [MessageType] nvarchar(20) NULL,
        [Raw] nvarchar(max) NOT NULL,
        [Outcome] nvarchar(20) NOT NULL,
        [Error] nvarchar(1000) NULL,
        [ReceivedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE TABLE [labs].[Reports] (
        [Id] uniqueidentifier NOT NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [MatchStatus] nvarchar(20) NOT NULL,
        [MatchNote] nvarchar(300) NULL,
        [MatchedBy] nvarchar(100) NULL,
        [PatientNhi] nvarchar(7) NULL,
        [PatientMrn] nvarchar(20) NULL,
        [PatientFamilyName] nvarchar(100) NOT NULL,
        [PatientGivenNames] nvarchar(150) NOT NULL,
        [PatientDateOfBirth] date NULL,
        [Laboratory] nvarchar(100) NOT NULL,
        [PlacerOrderNumber] nvarchar(50) NULL,
        [AccessionNumber] nvarchar(50) NULL,
        [TestCode] nvarchar(50) NOT NULL,
        [TestName] nvarchar(200) NOT NULL,
        [RequestedBy] nvarchar(150) NULL,
        [RequestedByCode] nvarchar(30) NULL,
        [CollectedAt] datetimeoffset NULL,
        [ReportedAt] datetimeoffset NULL,
        [ResultStatus] nvarchar(20) NOT NULL,
        [IsAbnormal] bit NOT NULL,
        [Comment] nvarchar(4000) NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [SupersededById] uniqueidentifier NULL,
        [AcknowledgedAt] datetimeoffset NULL,
        [AcknowledgedBy] nvarchar(100) NULL,
        [AcknowledgementComment] nvarchar(1000) NULL,
        CONSTRAINT [PK_Reports] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE TABLE [labs].[Observations] (
        [Id] uniqueidentifier NOT NULL,
        [ReportId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ValueType] nvarchar(5) NULL,
        [Value] nvarchar(4000) NOT NULL,
        [Units] nvarchar(50) NULL,
        [ReferenceRange] nvarchar(60) NULL,
        [Flag] nvarchar(5) NULL,
        [Comment] nvarchar(2000) NULL,
        CONSTRAINT [PK_Observations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Observations_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [labs].[Reports] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Messages_ReceivedAt] ON [labs].[Messages] ([ReceivedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Messages_SendingFacility_ControlId] ON [labs].[Messages] ([SendingFacility], [ControlId]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Observations_ReportId] ON [labs].[Observations] ([ReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Reports_AcknowledgedAt_ReceivedAt] ON [labs].[Reports] ([AcknowledgedAt], [ReceivedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Reports_Laboratory_AccessionNumber_TestCode] ON [labs].[Reports] ([Laboratory], [AccessionNumber], [TestCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    CREATE INDEX [IX_Reports_PatientId] ON [labs].[Reports] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112509_InitialLabs'
)
BEGIN
    INSERT INTO [labs].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106112509_InitialLabs', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Reports_MessageId] ON [labs].[Reports] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Reports_SupersededById] ON [labs].[Reports] ([SupersededById]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Messages_ControlId] ON [labs].[Messages] ([ControlId]);
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [labs].[Reports] ADD CONSTRAINT [FK_Reports_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [labs].[Messages] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [labs].[Reports] ADD CONSTRAINT [FK_Reports_Reports_SupersededById] FOREIGN KEY ([SupersededById]) REFERENCES [labs].[Reports] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [labs].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040621_IndexesIntegrity'
)
BEGIN
    INSERT INTO [labs].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040621_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

