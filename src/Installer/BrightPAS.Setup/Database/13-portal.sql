IF OBJECT_ID(N'[portal].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'portal') IS NULL EXEC(N'CREATE SCHEMA [portal];');
    CREATE TABLE [portal].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    IF SCHEMA_ID(N'portal') IS NULL EXEC(N'CREATE SCHEMA [portal];');
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE TABLE [portal].[Access] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokedBy] nvarchar(100) NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [LastUsedAt] datetimeoffset NULL,
        [UploadsToday] int NOT NULL,
        [UploadDay] date NULL,
        CONSTRAINT [PK_Access] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE TABLE [portal].[Delegates] (
        [Id] uniqueidentifier NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [ClinicianId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Delegates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE TABLE [portal].[Requests] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [AccessId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [Message] nvarchar(1000) NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [HandledAt] datetimeoffset NULL,
        [HandledBy] nvarchar(100) NULL,
        [Response] nvarchar(1000) NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE INDEX [IX_Access_PatientId] ON [portal].[Access] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Access_TokenHash] ON [portal].[Access] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Delegates_UserName_ClinicianId] ON [portal].[Delegates] ([UserName], [ClinicianId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE INDEX [IX_Requests_PatientId] ON [portal].[Requests] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    CREATE INDEX [IX_Requests_Status_CreatedAt] ON [portal].[Requests] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222736_InitialPortal'
)
BEGIN
    INSERT INTO [portal].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107222736_InitialPortal', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040630_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Requests_AccessId] ON [portal].[Requests] ([AccessId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040630_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Requests_BookingId] ON [portal].[Requests] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040630_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Delegates_ClinicianId] ON [portal].[Delegates] ([ClinicianId]);
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040630_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [portal].[Requests] ADD CONSTRAINT [FK_Requests_Access_AccessId] FOREIGN KEY ([AccessId]) REFERENCES [portal].[Access] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [portal].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040630_IndexesIntegrity'
)
BEGIN
    INSERT INTO [portal].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040630_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

