IF OBJECT_ID(N'[referrals].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'referrals') IS NULL EXEC(N'CREATE SCHEMA [referrals];');
    CREATE TABLE [referrals].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    IF SCHEMA_ID(N'referrals') IS NULL EXEC(N'CREATE SCHEMA [referrals];');
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE TABLE [referrals].[Referrals] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [ReceivedOn] date NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [ReferrerName] nvarchar(150) NOT NULL,
        [ReferrerOrganisation] nvarchar(150) NULL,
        [ReferrerHpi] nvarchar(20) NULL,
        [Specialist] nvarchar(150) NOT NULL,
        [Service] nvarchar(20) NOT NULL,
        [Funder] nvarchar(20) NOT NULL,
        [FunderReference] nvarchar(60) NULL,
        [Reason] nvarchar(2000) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TriagedAt] datetimeoffset NULL,
        [TriagedBy] nvarchar(100) NULL,
        [TriageNotes] nvarchar(2000) NULL,
        [DeclineReason] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Referrals] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE TABLE [referrals].[Settings] (
        [Id] int NOT NULL,
        [UrgentDays] int NOT NULL,
        [SemiUrgentDays] int NOT NULL,
        [RoutineDays] int NOT NULL,
        [NextReferralNumber] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE TABLE [referrals].[Waitlist] (
        [Id] uniqueidentifier NOT NULL,
        [ReferralId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Procedure] nvarchar(300) NOT NULL,
        [Laterality] nvarchar(20) NOT NULL,
        [Anaesthetic] nvarchar(20) NOT NULL,
        [DurationMinutes] int NOT NULL,
        [Service] nvarchar(20) NOT NULL,
        [Specialist] nvarchar(150) NOT NULL,
        [Funder] nvarchar(20) NOT NULL,
        [Priority] nvarchar(20) NOT NULL,
        [AddedOn] date NOT NULL,
        [TargetDate] date NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [HoldReason] nvarchar(300) NULL,
        [HoldUntil] date NULL,
        [BookingId] uniqueidentifier NULL,
        [BookedFor] date NULL,
        [RemovedReason] nvarchar(300) NULL,
        [Notes] nvarchar(1000) NULL,
        [StatusAt] datetimeoffset NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Waitlist] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Referrals_Number] ON [referrals].[Referrals] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE INDEX [IX_Referrals_PatientId] ON [referrals].[Referrals] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE INDEX [IX_Referrals_Status_ReceivedOn] ON [referrals].[Referrals] ([Status], [ReceivedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE INDEX [IX_Waitlist_BookingId] ON [referrals].[Waitlist] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE INDEX [IX_Waitlist_PatientId] ON [referrals].[Waitlist] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    CREATE INDEX [IX_Waitlist_Status_TargetDate] ON [referrals].[Waitlist] ([Status], [TargetDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101803_InitialReferrals'
)
BEGIN
    INSERT INTO [referrals].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107101803_InitialReferrals', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222728_NbrsCodes'
)
BEGIN
    ALTER TABLE [referrals].[Settings] ADD [NbrsAgencyCode] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222728_NbrsCodes'
)
BEGIN
    ALTER TABLE [referrals].[Settings] ADD [NbrsFacilityCode] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222728_NbrsCodes'
)
BEGIN
    ALTER TABLE [referrals].[Settings] ADD [NbrsSpecialtyEndoscopy] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222728_NbrsCodes'
)
BEGIN
    ALTER TABLE [referrals].[Settings] ADD [NbrsSpecialtySurgical] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [referrals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222728_NbrsCodes'
)
BEGIN
    INSERT INTO [referrals].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107222728_NbrsCodes', N'10.0.14');
END;

COMMIT;
GO

