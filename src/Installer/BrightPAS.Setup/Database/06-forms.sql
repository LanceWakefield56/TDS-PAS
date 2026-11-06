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

