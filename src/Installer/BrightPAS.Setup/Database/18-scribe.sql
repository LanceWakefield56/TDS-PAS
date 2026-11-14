IF OBJECT_ID(N'[scribe].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'scribe') IS NULL EXEC(N'CREATE SCHEMA [scribe];');
    CREATE TABLE [scribe].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    IF SCHEMA_ID(N'scribe') IS NULL EXEC(N'CREATE SCHEMA [scribe];');
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[Consents] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Given] bit NOT NULL,
        [Standing] bit NOT NULL,
        [Product] nvarchar(20) NOT NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        [WithdrawnBy] nvarchar(100) NULL,
        [WithdrawnAt] datetimeoffset NULL,
        CONSTRAINT [PK_Consents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[DictionaryTerms] (
        [Id] uniqueidentifier NOT NULL,
        [Term] nvarchar(100) NOT NULL,
        [SoundsLike] nvarchar(300) NULL,
        [Category] nvarchar(30) NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DictionaryTerms] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[Settings] (
        [Id] int NOT NULL,
        [Provider] nvarchar(20) NOT NULL,
        [Speech] nvarchar(20) NOT NULL,
        [SpeechUrl] nvarchar(300) NULL,
        [SpeechModel] nvarchar(100) NULL,
        [SpeechKey] varbinary(max) NULL,
        [Draft] nvarchar(20) NOT NULL,
        [DraftUrl] nvarchar(300) NULL,
        [DraftModel] nvarchar(100) NULL,
        [DraftKey] varbinary(max) NULL,
        [CloudPrivacyReference] nvarchar(200) NULL,
        [CloudApprovedBy] nvarchar(100) NULL,
        [CloudApprovedAt] datetimeoffset NULL,
        [MedowPiaReference] nvarchar(200) NULL,
        [MedowSupplierAssessment] nvarchar(200) NULL,
        [MedowAgreementReference] nvarchar(200) NULL,
        [MedowLaunchUrl] nvarchar(300) NULL,
        [InboundTokenHash] nvarchar(64) NULL,
        [MedowApprovedBy] nvarchar(100) NULL,
        [MedowApprovedAt] datetimeoffset NULL,
        [AudioRetentionDays] int NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Context] nvarchar(20) NOT NULL,
        [Sections] nvarchar(4000) NOT NULL,
        [Version] int NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[Sessions] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [Context] nvarchar(20) NOT NULL,
        [TemplateId] uniqueidentifier NOT NULL,
        [TemplateVersion] int NOT NULL,
        [Provider] nvarchar(20) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Clinician] nvarchar(100) NOT NULL,
        [ConsentId] uniqueidentifier NULL,
        [AudioFolder] nvarchar(100) NULL,
        [AudioType] nvarchar(60) NULL,
        [AudioChunks] int NOT NULL,
        [AudioBytes] bigint NOT NULL,
        [DurationSeconds] decimal(9,1) NULL,
        [AudioDeletedAt] datetimeoffset NULL,
        [SpeechEngine] nvarchar(100) NULL,
        [DraftEngine] nvarchar(100) NULL,
        [DeIdentified] bit NOT NULL,
        [Draft] nvarchar(max) NULL,
        [SignedText] nvarchar(max) NULL,
        [SignedBy] nvarchar(100) NULL,
        [SignedAt] datetimeoffset NULL,
        [EditPercent] decimal(5,1) NULL,
        [NoteId] uniqueidentifier NULL,
        [DocumentId] uniqueidentifier NULL,
        [DiscardReason] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sessions_Consents_ConsentId] FOREIGN KEY ([ConsentId]) REFERENCES [scribe].[Consents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Sessions_Templates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [scribe].[Templates] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[DraftSentences] (
        [SessionId] uniqueidentifier NOT NULL,
        [Seq] int NOT NULL,
        [Section] nvarchar(100) NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        [Sources] nvarchar(200) NULL,
        CONSTRAINT [PK_DraftSentences] PRIMARY KEY ([SessionId], [Seq]),
        CONSTRAINT [FK_DraftSentences_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [scribe].[Sessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE TABLE [scribe].[TranscriptSegments] (
        [SessionId] uniqueidentifier NOT NULL,
        [Seq] int NOT NULL,
        [Speaker] nvarchar(30) NOT NULL,
        [StartSeconds] decimal(9,2) NOT NULL,
        [EndSeconds] decimal(9,2) NOT NULL,
        [Text] nvarchar(4000) NOT NULL,
        [Confidence] decimal(4,3) NULL,
        CONSTRAINT [PK_TranscriptSegments] PRIMARY KEY ([SessionId], [Seq]),
        CONSTRAINT [FK_TranscriptSegments_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [scribe].[Sessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Consents_PatientId_RecordedAt] ON [scribe].[Consents] ([PatientId], [RecordedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_DictionaryTerms_Category] ON [scribe].[DictionaryTerms] ([Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DictionaryTerms_Term] ON [scribe].[DictionaryTerms] ([Term]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_BookingId] ON [scribe].[Sessions] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_Clinician_Status] ON [scribe].[Sessions] ([Clinician], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_ConsentId] ON [scribe].[Sessions] ([ConsentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_DocumentId] ON [scribe].[Sessions] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_NoteId] ON [scribe].[Sessions] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_PatientId] ON [scribe].[Sessions] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Sessions_TemplateId] ON [scribe].[Sessions] ([TemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Templates_Code] ON [scribe].[Templates] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    CREATE INDEX [IX_Templates_Context_Status] ON [scribe].[Templates] ([Context], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [scribe].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114044412_InitialScribe'
)
BEGIN
    INSERT INTO [scribe].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114044412_InitialScribe', N'10.0.14');
END;

COMMIT;
GO

