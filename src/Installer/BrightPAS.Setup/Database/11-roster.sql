IF OBJECT_ID(N'[roster].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'roster') IS NULL EXEC(N'CREATE SCHEMA [roster];');
    CREATE TABLE [roster].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    IF SCHEMA_ID(N'roster') IS NULL EXEC(N'CREATE SCHEMA [roster];');
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE TABLE [roster].[Leave] (
        [Id] uniqueidentifier NOT NULL,
        [StaffId] uniqueidentifier NOT NULL,
        [From] date NOT NULL,
        [To] date NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Note] nvarchar(300) NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [RequestedBy] nvarchar(100) NOT NULL,
        [DecidedAt] datetimeoffset NULL,
        [DecidedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Leave] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE TABLE [roster].[Shifts] (
        [Id] uniqueidentifier NOT NULL,
        [StaffId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [AllDay] bit NOT NULL,
        [Start] time NULL,
        [End] time NULL,
        [BreakMinutes] int NOT NULL,
        [Area] nvarchar(50) NULL,
        [IsTentative] bit NOT NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Shifts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE TABLE [roster].[Staff] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Role] nvarchar(30) NOT NULL,
        [UserName] nvarchar(100) NULL,
        [Colour] int NOT NULL,
        [ContractedHours] decimal(5,2) NULL,
        [Phone] nvarchar(30) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Staff] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE TABLE [roster].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [StaffId] uniqueidentifier NOT NULL,
        [DayOfWeek] nvarchar(10) NOT NULL,
        [AllDay] bit NOT NULL,
        [Start] time NULL,
        [End] time NULL,
        [BreakMinutes] int NOT NULL,
        [Area] nvarchar(50) NULL,
        [EveryWeeks] int NOT NULL,
        [StartsOn] date NOT NULL,
        [EndsOn] date NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE INDEX [IX_Leave_StaffId_From] ON [roster].[Leave] ([StaffId], [From]);
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE INDEX [IX_Shifts_Date_StaffId] ON [roster].[Shifts] ([Date], [StaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Staff_Name] ON [roster].[Staff] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210623_InitialRoster'
)
BEGIN
    INSERT INTO [roster].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107210623_InitialRoster', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222722_SwapsPayroll'
)
BEGIN
    ALTER TABLE [roster].[Staff] ADD [EarningsRate] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222722_SwapsPayroll'
)
BEGIN
    ALTER TABLE [roster].[Staff] ADD [PayrollCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222722_SwapsPayroll'
)
BEGIN
    CREATE TABLE [roster].[Swaps] (
        [Id] uniqueidentifier NOT NULL,
        [ShiftId] uniqueidentifier NOT NULL,
        [FromStaffId] uniqueidentifier NOT NULL,
        [ToStaffId] uniqueidentifier NULL,
        [OfferedShiftId] uniqueidentifier NULL,
        [Status] nvarchar(15) NOT NULL,
        [Note] nvarchar(300) NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [RequestedBy] nvarchar(100) NOT NULL,
        [AcceptedAt] datetimeoffset NULL,
        [AcceptedBy] nvarchar(100) NULL,
        [DecidedAt] datetimeoffset NULL,
        [DecidedBy] nvarchar(100) NULL,
        [DecisionNote] nvarchar(300) NULL,
        CONSTRAINT [PK_Swaps] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222722_SwapsPayroll'
)
BEGIN
    CREATE INDEX [IX_Swaps_Status_ShiftId] ON [roster].[Swaps] ([Status], [ShiftId]);
END;

IF NOT EXISTS (
    SELECT * FROM [roster].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107222722_SwapsPayroll'
)
BEGIN
    INSERT INTO [roster].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107222722_SwapsPayroll', N'10.0.14');
END;

COMMIT;
GO

