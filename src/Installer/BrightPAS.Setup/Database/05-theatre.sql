IF OBJECT_ID(N'[theatre].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'theatre') IS NULL EXEC(N'CREATE SCHEMA [theatre];');
    CREATE TABLE [theatre].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    IF SCHEMA_ID(N'theatre') IS NULL EXEC(N'CREATE SCHEMA [theatre];');
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    CREATE TABLE [theatre].[Sessions] (
        [Id] uniqueidentifier NOT NULL,
        [LocationId] uniqueidentifier NOT NULL,
        [LocationName] nvarchar(100) NOT NULL,
        [Date] date NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Surgeon] nvarchar(150) NOT NULL,
        [Anaesthetist] nvarchar(150) NULL,
        [Notes] nvarchar(1000) NULL,
        [IsCancelled] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    CREATE TABLE [theatre].[Bookings] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Procedure] nvarchar(300) NOT NULL,
        [Laterality] nvarchar(20) NOT NULL,
        [DurationMinutes] int NOT NULL,
        [Anaesthetic] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StatusAt] datetimeoffset NOT NULL,
        [CancelReason] nvarchar(300) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Bookings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Bookings_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [theatre].[Sessions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    CREATE INDEX [IX_Bookings_PatientId] ON [theatre].[Bookings] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    CREATE INDEX [IX_Bookings_SessionId] ON [theatre].[Bookings] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    CREATE INDEX [IX_Sessions_Date_LocationId] ON [theatre].[Sessions] ([Date], [LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112514_InitialTheatre'
)
BEGIN
    INSERT INTO [theatre].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106112514_InitialTheatre', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202547_SessionTemplates'
)
BEGIN
    CREATE TABLE [theatre].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [LocationId] uniqueidentifier NOT NULL,
        [DayOfWeek] nvarchar(10) NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Surgeon] nvarchar(150) NOT NULL,
        [Anaesthetist] nvarchar(150) NULL,
        [EveryWeeks] int NOT NULL,
        [StartsOn] date NOT NULL,
        [EndsOn] date NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202547_SessionTemplates'
)
BEGIN
    INSERT INTO [theatre].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107202547_SessionTemplates', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [BilledExternally] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [Checklist] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [DepositNote] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [IsTentative] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [LoanItems] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [Payment] nvarchar(20) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [ScopeId] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [TheatreIn] time NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [TheatreOut] time NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [TraysNeeded] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    ALTER TABLE [theatre].[Bookings] ADD [TraysUsed] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    CREATE TABLE [theatre].[Appointments] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [StartTime] time NOT NULL,
        [DurationMinutes] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Clinician] nvarchar(150) NOT NULL,
        [LocationId] uniqueidentifier NULL,
        [NoCharge] bit NOT NULL,
        [IsTentative] bit NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CancelReason] nvarchar(300) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Appointments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    CREATE INDEX [IX_Appointments_Date] ON [theatre].[Appointments] ([Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    CREATE INDEX [IX_Appointments_PatientId] ON [theatre].[Appointments] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210617_BookingDetailsAppointments'
)
BEGIN
    INSERT INTO [theatre].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107210617_BookingDetailsAppointments', N'10.0.14');
END;

COMMIT;
GO

