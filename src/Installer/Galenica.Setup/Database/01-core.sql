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

