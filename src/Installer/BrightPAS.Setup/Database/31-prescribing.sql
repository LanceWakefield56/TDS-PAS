IF OBJECT_ID(N'[rx].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'rx') IS NULL EXEC(N'CREATE SCHEMA [rx];');
    CREATE TABLE [rx].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    IF SCHEMA_ID(N'rx') IS NULL EXEC(N'CREATE SCHEMA [rx];');
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Orders] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [MedicineId] uniqueidentifier NULL,
        [Medicine] nvarchar(150) NOT NULL,
        [Dose] nvarchar(60) NOT NULL,
        [Route] nvarchar(30) NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Frequency] nvarchar(100) NULL,
        [Indication] nvarchar(150) NULL,
        [Notes] nvarchar(300) NULL,
        [Status] nvarchar(20) NOT NULL,
        [PrescribedAt] datetimeoffset NOT NULL,
        [PrescribedBy] nvarchar(100) NOT NULL,
        [StoppedAt] datetimeoffset NULL,
        [StoppedBy] nvarchar(100) NULL,
        [StopReason] nvarchar(300) NULL,
        [TimesGiven] int NOT NULL,
        [LastGivenAt] datetimeoffset NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Prescribers] (
        [Id] uniqueidentifier NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [HpiCpn] nvarchar(10) NOT NULL,
        [Registration] nvarchar(20) NULL,
        [Scope] nvarchar(40) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Prescribers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Scans] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NULL,
        [MedicationId] uniqueidentifier NULL,
        [Medicine] nvarchar(150) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [User] nvarchar(100) NOT NULL,
        [Mode] nvarchar(20) NOT NULL,
        [PatientScanned] bit NOT NULL,
        [PatientMatched] bit NULL,
        [PackScanned] bit NOT NULL,
        [Gtin] nvarchar(14) NULL,
        [Batch] nvarchar(20) NULL,
        [Expiry] date NULL,
        [Serial] nvarchar(20) NULL,
        [PackMatched] bit NULL,
        [Result] nvarchar(20) NOT NULL,
        [OverrideReason] nvarchar(500) NULL,
        [Findings] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_Scans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Settings] (
        [Id] int NOT NULL,
        [BcmaMode] nvarchar(20) NOT NULL,
        [WarnWithoutOrder] bit NOT NULL,
        [Provider] nvarchar(20) NOT NULL,
        [NzepsUrl] nvarchar(200) NULL,
        [NzepsClientId] nvarchar(100) NULL,
        [NzepsKeyProtected] nvarchar(1000) NULL,
        [FacilityHpi] nvarchar(20) NULL,
        [ExportFolder] nvarchar(260) NULL,
        [NextNumber] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Prescriptions] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [NzepsId] nvarchar(18) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NULL,
        [PrescriberId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [SignedAt] datetimeoffset NULL,
        [SentAt] datetimeoffset NULL,
        [SendReference] nvarchar(100) NULL,
        [SendError] nvarchar(500) NULL,
        [CancelledAt] datetimeoffset NULL,
        [CancelledBy] nvarchar(100) NULL,
        [CancelReason] nvarchar(300) NULL,
        [TimesPrinted] int NOT NULL,
        CONSTRAINT [PK_Prescriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Prescriptions_Prescribers_PrescriberId] FOREIGN KEY ([PrescriberId]) REFERENCES [rx].[Prescribers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE TABLE [rx].[Items] (
        [Id] uniqueidentifier NOT NULL,
        [PrescriptionId] uniqueidentifier NOT NULL,
        [MedicineId] uniqueidentifier NULL,
        [Medicine] nvarchar(150) NOT NULL,
        [NzmtId] nvarchar(20) NULL,
        [Directions] nvarchar(500) NOT NULL,
        [Quantity] decimal(9,2) NOT NULL,
        [Unit] nvarchar(30) NOT NULL,
        [Repeats] int NOT NULL,
        [Controlled] bit NOT NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Items_Prescriptions_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [rx].[Prescriptions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Items_MedicineId] ON [rx].[Items] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Items_PrescriptionId] ON [rx].[Items] ([PrescriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Orders_EpisodeId] ON [rx].[Orders] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Orders_MedicineId] ON [rx].[Orders] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Orders_PatientId] ON [rx].[Orders] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prescribers_UserName] ON [rx].[Prescribers] ([UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_EpisodeId] ON [rx].[Prescriptions] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prescriptions_Number] ON [rx].[Prescriptions] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prescriptions_NzepsId] ON [rx].[Prescriptions] ([NzepsId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_PatientId] ON [rx].[Prescriptions] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Prescriptions_PrescriberId] ON [rx].[Prescriptions] ([PrescriberId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Scans_At] ON [rx].[Scans] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Scans_EpisodeId] ON [rx].[Scans] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Scans_MedicationId] ON [rx].[Scans] ([MedicationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    CREATE INDEX [IX_Scans_PatientId] ON [rx].[Scans] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [rx].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128062217_InitialPrescribing'
)
BEGIN
    INSERT INTO [rx].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128062217_InitialPrescribing', N'10.0.14');
END;

COMMIT;
GO

