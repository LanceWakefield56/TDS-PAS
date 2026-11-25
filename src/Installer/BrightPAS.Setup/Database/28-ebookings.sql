IF OBJECT_ID(N'[ebookings].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'ebookings') IS NULL EXEC(N'CREATE SCHEMA [ebookings];');
    CREATE TABLE [ebookings].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    IF SCHEMA_ID(N'ebookings') IS NULL EXEC(N'CREATE SCHEMA [ebookings];');
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE TABLE [ebookings].[Requests] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [ExternalRef] nvarchar(60) NULL,
        [SpecialistId] uniqueidentifier NOT NULL,
        [RoomsName] nvarchar(150) NULL,
        [ContactName] nvarchar(150) NOT NULL,
        [ContactPhone] nvarchar(30) NULL,
        [ContactEmail] nvarchar(200) NULL,
        [Nhi] nvarchar(7) NULL,
        [FamilyName] nvarchar(100) NOT NULL,
        [GivenNames] nvarchar(100) NOT NULL,
        [DateOfBirth] date NOT NULL,
        [Sex] nvarchar(20) NOT NULL,
        [MobilePhone] nvarchar(30) NULL,
        [Email] nvarchar(200) NULL,
        [AddressLine1] nvarchar(150) NULL,
        [Suburb] nvarchar(100) NULL,
        [City] nvarchar(100) NULL,
        [Postcode] nvarchar(10) NULL,
        [InterpreterLanguage] nvarchar(60) NULL,
        [PatientId] uniqueidentifier NULL,
        [Service] nvarchar(20) NOT NULL,
        [ProcedureItemId] uniqueidentifier NULL,
        [Procedure] nvarchar(300) NOT NULL,
        [Laterality] nvarchar(20) NOT NULL,
        [Anaesthetic] nvarchar(20) NOT NULL,
        [DurationMinutes] int NOT NULL,
        [Priority] nvarchar(20) NOT NULL,
        [Funder] nvarchar(20) NOT NULL,
        [FunderReference] nvarchar(60) NULL,
        [InsurerName] nvarchar(100) NULL,
        [EarliestDate] date NULL,
        [LatestDate] date NULL,
        [Availability] nvarchar(500) NULL,
        [ClinicalNotes] nvarchar(2000) NULL,
        [WeightKg] decimal(5,1) NULL,
        [HeightCm] decimal(5,1) NULL,
        [Asa] int NULL,
        [Diabetic] bit NOT NULL,
        [SleepApnoea] bit NOT NULL,
        [Anticoagulated] bit NOT NULL,
        [AnticoagulantDetails] nvarchar(300) NULL,
        [Allergies] nvarchar(500) NULL,
        [Comorbidities] nvarchar(1000) NULL,
        [ResponsibleAdultArranged] bit NOT NULL,
        [ConsentSigned] bit NOT NULL,
        [ConsentSignedOn] date NULL,
        [SubmittedAt] datetimeoffset NOT NULL,
        [SubmittedBy] nvarchar(100) NOT NULL,
        [StatusAt] datetimeoffset NOT NULL,
        [DecidedBy] nvarchar(100) NULL,
        [DeclineReason] nvarchar(500) NULL,
        [ReferralId] uniqueidentifier NULL,
        [WaitlistEntryId] uniqueidentifier NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE TABLE [ebookings].[Settings] (
        [Id] int NOT NULL,
        [NextNumber] int NOT NULL,
        [WatchFolder] nvarchar(300) NULL,
        [EmailRooms] bit NOT NULL,
        [MaxFileMegabytes] int NOT NULL,
        [Instructions] nvarchar(1000) NULL,
        [LastCheckedAt] datetimeoffset NULL,
        [LastProblem] nvarchar(500) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE TABLE [ebookings].[Files] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Category] nvarchar(20) NOT NULL,
        [FileName] nvarchar(150) NOT NULL,
        [ContentType] nvarchar(50) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [AddedAt] datetimeoffset NOT NULL,
        [AddedBy] nvarchar(100) NOT NULL,
        [PatientDocumentId] uniqueidentifier NULL,
        CONSTRAINT [PK_Files] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Files_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ebookings].[Requests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE TABLE [ebookings].[Messages] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [FromTds] bit NOT NULL,
        [Author] nvarchar(100) NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ebookings].[Requests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'EmailRooms', N'Instructions', N'LastCheckedAt', N'LastProblem', N'MaxFileMegabytes', N'NextNumber', N'UpdatedAt', N'UpdatedBy', N'WatchFolder') AND [object_id] = OBJECT_ID(N'[ebookings].[Settings]'))
        SET IDENTITY_INSERT [ebookings].[Settings] ON;
    EXEC(N'INSERT INTO [ebookings].[Settings] ([Id], [EmailRooms], [Instructions], [LastCheckedAt], [LastProblem], [MaxFileMegabytes], [NextNumber], [UpdatedAt], [UpdatedBy], [WatchFolder])
    VALUES (1, CAST(0 AS bit), NULL, NULL, NULL, 10, 1, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'EmailRooms', N'Instructions', N'LastCheckedAt', N'LastProblem', N'MaxFileMegabytes', N'NextNumber', N'UpdatedAt', N'UpdatedBy', N'WatchFolder') AND [object_id] = OBJECT_ID(N'[ebookings].[Settings]'))
        SET IDENTITY_INSERT [ebookings].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Files_PatientDocumentId] ON [ebookings].[Files] ([PatientDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Files_RequestId] ON [ebookings].[Files] ([RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Messages_RequestId_At] ON [ebookings].[Messages] ([RequestId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_ExternalRef] ON [ebookings].[Requests] ([ExternalRef]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_Nhi] ON [ebookings].[Requests] ([Nhi]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Requests_Number] ON [ebookings].[Requests] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_PatientId] ON [ebookings].[Requests] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_ProcedureItemId] ON [ebookings].[Requests] ([ProcedureItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_ReferralId] ON [ebookings].[Requests] ([ReferralId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_SpecialistId] ON [ebookings].[Requests] ([SpecialistId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_Status_SubmittedAt] ON [ebookings].[Requests] ([Status], [SubmittedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    CREATE INDEX [IX_Requests_WaitlistEntryId] ON [ebookings].[Requests] ([WaitlistEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [ebookings].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085651_InitialEBookings'
)
BEGIN
    INSERT INTO [ebookings].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125085651_InitialEBookings', N'10.0.14');
END;

COMMIT;
GO

