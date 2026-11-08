IF OBJECT_ID(N'[cred].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'cred') IS NULL EXEC(N'CREATE SCHEMA [cred];');
    CREATE TABLE [cred].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    IF SCHEMA_ID(N'cred') IS NULL EXEC(N'CREATE SCHEMA [cred];');
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    CREATE TABLE [cred].[Credentials] (
        [Id] uniqueidentifier NOT NULL,
        [ClinicianId] uniqueidentifier NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Reference] nvarchar(80) NULL,
        [Issuer] nvarchar(120) NULL,
        [IssuedOn] date NULL,
        [ExpiresOn] date NULL,
        [Notes] nvarchar(500) NULL,
        [VerifiedBy] nvarchar(100) NOT NULL,
        [VerifiedAt] datetimeoffset NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Credentials] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    CREATE TABLE [cred].[Privileges] (
        [Id] uniqueidentifier NOT NULL,
        [ClinicianId] uniqueidentifier NOT NULL,
        [Service] nvarchar(20) NOT NULL,
        [ProcedureCode] nvarchar(30) NULL,
        [ApprovedOn] date NOT NULL,
        [ReviewDue] date NULL,
        [Conditions] nvarchar(300) NULL,
        [ApprovedBy] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Privileges] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    CREATE TABLE [cred].[Settings] (
        [Id] int NOT NULL,
        [RequiredTypes] nvarchar(200) NOT NULL,
        [WarningDays] int NOT NULL,
        [CheckPrivileges] bit NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    CREATE INDEX [IX_Credentials_ClinicianId_Type] ON [cred].[Credentials] ([ClinicianId], [Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    CREATE INDEX [IX_Privileges_ClinicianId] ON [cred].[Privileges] ([ClinicianId]);
END;

IF NOT EXISTS (
    SELECT * FROM [cred].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222732_InitialCredentialing'
)
BEGIN
    INSERT INTO [cred].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107222732_InitialCredentialing', N'10.0.14');
END;

COMMIT;
GO

