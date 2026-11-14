IF OBJECT_ID(N'[letters].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'letters') IS NULL EXEC(N'CREATE SCHEMA [letters];');
    CREATE TABLE [letters].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    IF SCHEMA_ID(N'letters') IS NULL EXEC(N'CREATE SCHEMA [letters];');
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE TABLE [letters].[Letters] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [TemplateCode] nvarchar(30) NOT NULL,
        [TemplateVersion] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Recipient] nvarchar(20) NOT NULL,
        [RecipientName] nvarchar(200) NOT NULL,
        [RecipientAddress] nvarchar(500) NULL,
        [GpPracticeId] uniqueidentifier NULL,
        [Subject] nvarchar(200) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [ReferralId] uniqueidentifier NULL,
        [Status] nvarchar(20) NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [PrintCount] int NOT NULL,
        [MessageControlId] nvarchar(30) NULL,
        [AckCode] nvarchar(5) NULL,
        [AckText] nvarchar(500) NULL,
        [AckAt] datetimeoffset NULL,
        [VoidReason] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [Author] nvarchar(150) NULL,
        [FinalisedAt] datetimeoffset NULL,
        [FinalisedBy] nvarchar(100) NULL,
        [SentAt] datetimeoffset NULL,
        [SentBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Letters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE TABLE [letters].[Practices] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [HpiFacilityId] nvarchar(10) NULL,
        [HealthLinkEdi] nvarchar(20) NULL,
        [AddressLine1] nvarchar(150) NULL,
        [AddressLine2] nvarchar(150) NULL,
        [City] nvarchar(100) NULL,
        [Postcode] nvarchar(10) NULL,
        [Phone] nvarchar(30) NULL,
        [Email] nvarchar(200) NULL,
        [PreferredChannel] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Practices] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE TABLE [letters].[Settings] (
        [Id] int NOT NULL,
        [Mode] nvarchar(20) NOT NULL,
        [SenderEdi] nvarchar(20) NULL,
        [OutboxFolder] nvarchar(260) NULL,
        [InboxFolder] nvarchar(260) NULL,
        [NextControlNumber] int NOT NULL,
        [NextLetterNumber] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE TABLE [letters].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Recipient] nvarchar(20) NOT NULL,
        [Subject] nvarchar(200) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [Version] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE INDEX [IX_Letters_MessageControlId] ON [letters].[Letters] ([MessageControlId]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Letters_Number] ON [letters].[Letters] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE INDEX [IX_Letters_PatientId] ON [letters].[Letters] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE INDEX [IX_Letters_Status_CreatedAt] ON [letters].[Letters] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Practices_HpiFacilityId] ON [letters].[Practices] ([HpiFacilityId]) WHERE [HpiFacilityId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Templates_Code] ON [letters].[Templates] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202553_InitialLetters'
)
BEGIN
    INSERT INTO [letters].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107202553_InitialLetters', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040626_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Letters_BookingId] ON [letters].[Letters] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040626_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Letters_GpPracticeId] ON [letters].[Letters] ([GpPracticeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040626_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Letters_ReferralId] ON [letters].[Letters] ([ReferralId]);
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040626_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [letters].[Letters] ADD CONSTRAINT [FK_Letters_Practices_GpPracticeId] FOREIGN KEY ([GpPracticeId]) REFERENCES [letters].[Practices] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040626_IndexesIntegrity'
)
BEGIN
    INSERT INTO [letters].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040626_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114041953_LetterAttachments'
)
BEGIN
    CREATE TABLE [letters].[LetterAttachments] (
        [LetterId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(60) NOT NULL,
        [Content] varbinary(max) NOT NULL,
        CONSTRAINT [PK_LetterAttachments] PRIMARY KEY ([LetterId]),
        CONSTRAINT [FK_LetterAttachments_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [letters].[Letters] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [letters].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114041953_LetterAttachments'
)
BEGIN
    INSERT INTO [letters].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114041953_LetterAttachments', N'10.0.14');
END;

COMMIT;
GO

