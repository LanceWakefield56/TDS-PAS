IF OBJECT_ID(N'[core].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
    CREATE TABLE [core].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN

    CREATE TABLE [core].[AuditEvents] (
        [Sequence] bigint NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [UserId] nvarchar(100) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [PatientId] nvarchar(100) NULL,
        [Reason] nvarchar(500) NULL,
        [Workstation] nvarchar(100) NULL,
        [PreviousHash] nvarchar(64) NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Sequence])
    ) WITH (LEDGER = ON (APPEND_ONLY = ON));
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE TABLE [core].[Organisations] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [HpiOrganisationId] nvarchar(20) NULL,
        CONSTRAINT [PK_Organisations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE TABLE [core].[Sites] (
        [Id] uniqueidentifier NOT NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [HpiFacilityId] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Sites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sites_Organisations_OrganisationId] FOREIGN KEY ([OrganisationId]) REFERENCES [core].[Organisations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE TABLE [core].[Facilities] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Facilities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Facilities_Sites_SiteId] FOREIGN KEY ([SiteId]) REFERENCES [core].[Sites] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE TABLE [core].[Locations] (
        [Id] uniqueidentifier NOT NULL,
        [FacilityId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Locations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Locations_Facilities_FacilityId] FOREIGN KEY ([FacilityId]) REFERENCES [core].[Facilities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_OccurredAt] ON [core].[AuditEvents] ([OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_PatientId] ON [core].[AuditEvents] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE INDEX [IX_Facilities_SiteId] ON [core].[Facilities] ([SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Locations_FacilityId_Code] ON [core].[Locations] ([FacilityId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    CREATE INDEX [IX_Sites_OrganisationId] ON [core].[Sites] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102035447_InitialCore'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102035447_InitialCore', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102052429_AddSetupState'
)
BEGIN
    CREATE TABLE [core].[SetupState] (
        [Id] int NOT NULL,
        [BootstrapCodeHash] nvarchar(200) NULL,
        [BootstrapCodeCreatedAt] datetimeoffset NULL,
        [BootstrapCodeUsedAt] datetimeoffset NULL,
        [InstalledVersion] nvarchar(50) NULL,
        [InstalledAt] datetimeoffset NULL,
        CONSTRAINT [PK_SetupState] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SetupState_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102052429_AddSetupState'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102052429_AddSetupState', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106111225_AddLicenceState'
)
BEGIN
    CREATE TABLE [core].[LicenceState] (
        [Id] int NOT NULL,
        [InstallationId] uniqueidentifier NOT NULL,
        [LicenceText] nvarchar(max) NULL,
        [LicenceInstalledAt] datetimeoffset NULL,
        [LicenceInstalledBy] nvarchar(100) NULL,
        [ActivationText] nvarchar(max) NULL,
        [ActivationCheckedAt] datetimeoffset NULL,
        [ActivationError] nvarchar(500) NULL,
        [LastKnownModules] nvarchar(500) NULL,
        [LastSeenAt] datetimeoffset NULL,
        CONSTRAINT [PK_LicenceState] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LicenceState_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106111225_AddLicenceState'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106111225_AddLicenceState', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101753_ModuleSetup'
)
BEGIN
    CREATE TABLE [core].[ModuleSetup] (
        [ModuleId] nvarchar(40) NOT NULL,
        [SetUpAt] datetimeoffset NOT NULL,
        [SetUpBy] nvarchar(100) NOT NULL,
        [SkippedChecks] nvarchar(1000) NULL,
        CONSTRAINT [PK_ModuleSetup] PRIMARY KEY ([ModuleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107101753_ModuleSetup'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107101753_ModuleSetup', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [AddressLine1] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [AddressLine2] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [City] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [Email] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [Phone] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [Postcode] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    ALTER TABLE [core].[Sites] ADD [Suburb] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    CREATE TABLE [core].[Clinicians] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Specialty] nvarchar(100) NULL,
        [HpiCpn] nvarchar(10) NULL,
        [Phone] nvarchar(30) NULL,
        [Email] nvarchar(200) NULL,
        [PracticeName] nvarchar(150) NULL,
        [UserName] nvarchar(100) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Clinicians] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    CREATE TABLE [core].[Procedures] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Service] nvarchar(20) NOT NULL,
        [DefaultMinutes] int NOT NULL,
        [DefaultAnaesthetic] nvarchar(20) NOT NULL,
        [SideRequired] bit NOT NULL,
        [FeeCode] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Procedures] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clinicians_Name] ON [core].[Clinicians] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Procedures_Code] ON [core].[Procedures] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202541_OrganisationAdmin'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107202541_OrganisationAdmin', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210610_ClinicianColour'
)
BEGIN
    ALTER TABLE [core].[Clinicians] ADD [Colour] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210610_ClinicianColour'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107210610_ClinicianColour', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222713_SystemSettings'
)
BEGIN
    CREATE TABLE [core].[Settings] (
        [Key] nvarchar(60) NOT NULL,
        [Value] nvarchar(500) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [core].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222713_SystemSettings'
)
BEGIN
    INSERT INTO [core].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107222713_SystemSettings', N'10.0.14');
END;

COMMIT;
GO

