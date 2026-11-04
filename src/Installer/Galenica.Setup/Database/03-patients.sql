IF OBJECT_ID(N'[patients].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'patients') IS NULL EXEC(N'CREATE SCHEMA [patients];');
    CREATE TABLE [patients].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    IF SCHEMA_ID(N'patients') IS NULL EXEC(N'CREATE SCHEMA [patients];');
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE TABLE [patients].[MrnCounter] (
        [Id] int NOT NULL,
        [NextValue] bigint NOT NULL,
        CONSTRAINT [PK_MrnCounter] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MrnCounter_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE TABLE [patients].[Patients] (
        [Id] uniqueidentifier NOT NULL,
        [Mrn] nvarchar(20) NOT NULL,
        [Nhi] nchar(7) NULL,
        [FamilyName] nvarchar(100) NOT NULL,
        [GivenNames] nvarchar(150) NOT NULL,
        [PreferredName] nvarchar(100) NULL,
        [FamilyNameSearch] nvarchar(100) NOT NULL,
        [GivenNamesSearch] nvarchar(150) NOT NULL,
        [DateOfBirth] date NOT NULL,
        [Sex] nvarchar(15) NOT NULL,
        [Gender] nvarchar(50) NULL,
        [MobilePhone] nvarchar(30) NULL,
        [HomePhone] nvarchar(30) NULL,
        [Email] nvarchar(200) NULL,
        [AddressLine1] nvarchar(200) NULL,
        [AddressLine2] nvarchar(200) NULL,
        [Suburb] nvarchar(100) NULL,
        [City] nvarchar(100) NULL,
        [Postcode] nvarchar(10) NULL,
        [GpPracticeHpiFacilityId] nvarchar(20) NULL,
        [IsTestPatient] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Patients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE TABLE [patients].[Alerts] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Severity] nvarchar(20) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Alerts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Alerts_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients].[Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE TABLE [patients].[PatientEthnicities] (
        [PatientId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Code] nvarchar(5) NOT NULL,
        CONSTRAINT [PK_PatientEthnicities] PRIMARY KEY ([PatientId], [Order]),
        CONSTRAINT [FK_PatientEthnicities_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients].[Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextValue') AND [object_id] = OBJECT_ID(N'[patients].[MrnCounter]'))
        SET IDENTITY_INSERT [patients].[MrnCounter] ON;
    EXEC(N'INSERT INTO [patients].[MrnCounter] ([Id], [NextValue])
    VALUES (1, CAST(1 AS bigint))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextValue') AND [object_id] = OBJECT_ID(N'[patients].[MrnCounter]'))
        SET IDENTITY_INSERT [patients].[MrnCounter] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE INDEX [IX_Alerts_PatientId] ON [patients].[Alerts] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE INDEX [IX_Patients_DateOfBirth] ON [patients].[Patients] ([DateOfBirth]);
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE INDEX [IX_Patients_FamilyNameSearch_GivenNamesSearch_DateOfBirth] ON [patients].[Patients] ([FamilyNameSearch], [GivenNamesSearch], [DateOfBirth]);
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Patients_Mrn] ON [patients].[Patients] ([Mrn]);
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Patients_Nhi] ON [patients].[Patients] ([Nhi]) WHERE [Nhi] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [patients].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102053131_InitialPatients'
)
BEGIN
    INSERT INTO [patients].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102053131_InitialPatients', N'10.0.14');
END;

COMMIT;
GO

