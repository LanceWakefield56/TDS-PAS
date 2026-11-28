IF OBJECT_ID(N'[consent].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'consent') IS NULL EXEC(N'CREATE SCHEMA [consent];');
    CREATE TABLE [consent].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    IF SCHEMA_ID(N'consent') IS NULL EXEC(N'CREATE SCHEMA [consent];');
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Settings] (
        [Id] int NOT NULL,
        [NextNumber] int NOT NULL,
        [PublicUrl] nvarchar(300) NULL,
        [LinkDays] int NOT NULL,
        [RequireBeforeConfirm] bit NOT NULL,
        [PrepareDaysAhead] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Sheets] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Version] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [ProcedureWords] nvarchar(300) NULL,
        [About] nvarchar(4000) NOT NULL,
        [Benefits] nvarchar(4000) NOT NULL,
        [Alternatives] nvarchar(4000) NOT NULL,
        [NoTreatment] nvarchar(4000) NOT NULL,
        [Anaesthetic] nvarchar(4000) NULL,
        [Preparation] nvarchar(4000) NULL,
        [Recovery] nvarchar(4000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [ApprovedAt] datetimeoffset NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [ApprovalNote] nvarchar(500) NULL,
        [RetiredAt] datetimeoffset NULL,
        CONSTRAINT [PK_Sheets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Consents] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [SheetId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Method] nvarchar(20) NOT NULL,
        [Procedure] nvarchar(300) NOT NULL,
        [Laterality] nvarchar(20) NOT NULL,
        [ClinicianId] uniqueidentifier NULL,
        [ClinicianName] nvarchar(150) NULL,
        [Snapshot] nvarchar(max) NOT NULL,
        [ExtraRisks] nvarchar(2000) NULL,
        [NoteToPatient] nvarchar(1000) NULL,
        [TokenHash] nvarchar(64) NULL,
        [LinkSentAt] datetimeoffset NULL,
        [LinkExpiresAt] datetimeoffset NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [OpenedAt] datetimeoffset NULL,
        [SectionsRead] nvarchar(200) NULL,
        [Signer] nvarchar(20) NULL,
        [SignerName] nvarchar(150) NULL,
        [SignerRelationship] nvarchar(100) NULL,
        [Interpreter] nvarchar(150) NULL,
        [SignatureImage] varbinary(max) NULL,
        [SignedAt] datetimeoffset NULL,
        [SignedBy] nvarchar(100) NULL,
        [DocumentId] uniqueidentifier NULL,
        [ConfirmedAt] datetimeoffset NULL,
        [ConfirmedBy] nvarchar(100) NULL,
        [ConfirmNote] nvarchar(1000) NULL,
        [WithdrawnAt] datetimeoffset NULL,
        [WithdrawnBy] nvarchar(100) NULL,
        [WithdrawnReason] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Consents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Consents_Sheets_SheetId] FOREIGN KEY ([SheetId]) REFERENCES [consent].[Sheets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Links] (
        [Id] uniqueidentifier NOT NULL,
        [SheetId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_Links] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Links_Sheets_SheetId] FOREIGN KEY ([SheetId]) REFERENCES [consent].[Sheets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Risks] (
        [Id] uniqueidentifier NOT NULL,
        [SheetId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Text] nvarchar(500) NOT NULL,
        [Frequency] nvarchar(20) NOT NULL,
        [Serious] bit NOT NULL,
        CONSTRAINT [PK_Risks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Risks_Sheets_SheetId] FOREIGN KEY ([SheetId]) REFERENCES [consent].[Sheets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE TABLE [consent].[Questions] (
        [Id] uniqueidentifier NOT NULL,
        [ConsentId] uniqueidentifier NOT NULL,
        [Text] nvarchar(1000) NOT NULL,
        [AskedAt] datetimeoffset NOT NULL,
        [Answer] nvarchar(2000) NULL,
        [AnsweredAt] datetimeoffset NULL,
        [AnsweredBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Questions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Questions_Consents_ConsentId] FOREIGN KEY ([ConsentId]) REFERENCES [consent].[Consents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'LinkDays', N'NextNumber', N'PrepareDaysAhead', N'PublicUrl', N'RequireBeforeConfirm', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[consent].[Settings]'))
        SET IDENTITY_INSERT [consent].[Settings] ON;
    EXEC(N'INSERT INTO [consent].[Settings] ([Id], [LinkDays], [NextNumber], [PrepareDaysAhead], [PublicUrl], [RequireBeforeConfirm], [UpdatedAt], [UpdatedBy])
    VALUES (1, 14, 1, 28, NULL, CAST(0 AS bit), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'LinkDays', N'NextNumber', N'PrepareDaysAhead', N'PublicUrl', N'RequireBeforeConfirm', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[consent].[Settings]'))
        SET IDENTITY_INSERT [consent].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_BookingId] ON [consent].[Consents] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_ClinicianId] ON [consent].[Consents] ([ClinicianId]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_DocumentId] ON [consent].[Consents] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Consents_Number] ON [consent].[Consents] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_PatientId] ON [consent].[Consents] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_SheetId] ON [consent].[Consents] ([SheetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_Status] ON [consent].[Consents] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Consents_TokenHash] ON [consent].[Consents] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Links_SheetId_Order] ON [consent].[Links] ([SheetId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Questions_ConsentId_AskedAt] ON [consent].[Questions] ([ConsentId], [AskedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Risks_SheetId_Order] ON [consent].[Risks] ([SheetId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Sheets_Code_Version] ON [consent].[Sheets] ([Code], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    CREATE INDEX [IX_Sheets_Status] ON [consent].[Sheets] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [consent].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125222538_InitialConsent'
)
BEGIN
    INSERT INTO [consent].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125222538_InitialConsent', N'10.0.14');
END;

COMMIT;
GO

