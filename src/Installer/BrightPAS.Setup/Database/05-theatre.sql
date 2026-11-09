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

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    CREATE TABLE [theatre].[BookingChecks] (
        [BookingId] uniqueidentifier NOT NULL,
        [ItemKey] nvarchar(30) NOT NULL,
        [CheckedAt] datetimeoffset NOT NULL,
        [CheckedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_BookingChecks] PRIMARY KEY ([BookingId], [ItemKey]),
        CONSTRAINT [FK_BookingChecks_Bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [theatre].[Bookings] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Templates_LocationId] ON [theatre].[Templates] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Sessions_LocationId] ON [theatre].[Sessions] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Bookings_ScopeId] ON [theatre].[Bookings] ([ScopeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Appointments_LocationId] ON [theatre].[Appointments] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    EXEC(N'INSERT INTO [theatre].[BookingChecks] ([BookingId], [ItemKey], [CheckedAt], [CheckedBy])
    SELECT DISTINCT b.[Id], LEFT(LTRIM(RTRIM(v.[value])), 30), SYSDATETIMEOFFSET(), N''migrated'' FROM [theatre].[Bookings] b CROSS APPLY STRING_SPLIT(b.[Checklist], '','') v
    WHERE b.[Checklist] IS NOT NULL AND LEN(LTRIM(RTRIM(v.[value]))) > 0;');
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Bookings]') AND [c].[name] = N'Checklist');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Bookings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [theatre].[Bookings] DROP COLUMN [Checklist];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040609_IndexesIntegrity'
)
BEGIN
    INSERT INTO [theatre].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040609_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    ALTER TABLE [theatre].[Templates] ADD [AnaesthetistId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    ALTER TABLE [theatre].[Templates] ADD [SurgeonId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    ALTER TABLE [theatre].[Sessions] ADD [AnaesthetistId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    ALTER TABLE [theatre].[Sessions] ADD [SurgeonId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    ALTER TABLE [theatre].[Appointments] ADD [ClinicianId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    CREATE INDEX [IX_Templates_AnaesthetistId] ON [theatre].[Templates] ([AnaesthetistId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    CREATE INDEX [IX_Templates_SurgeonId] ON [theatre].[Templates] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    CREATE INDEX [IX_Sessions_AnaesthetistId] ON [theatre].[Sessions] ([AnaesthetistId]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    CREATE INDEX [IX_Sessions_SurgeonId_Date] ON [theatre].[Sessions] ([SurgeonId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    CREATE INDEX [IX_Appointments_ClinicianId_Date] ON [theatre].[Appointments] ([ClinicianId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    EXEC(N'INSERT INTO [core].[Clinicians] ([Id], [Name], [Type], [Colour], [IsActive])
    SELECT NEWID(), x.[Name], N''Other'', 0, 0 FROM (SELECT DISTINCT LEFT(LTRIM(RTRIM(n.[Name])), 150) AS [Name] FROM (SELECT [Surgeon] AS [Name] FROM [theatre].[Sessions] UNION ALL SELECT [Anaesthetist] FROM [theatre].[Sessions] UNION ALL SELECT [Surgeon] FROM [theatre].[Templates] UNION ALL SELECT [Anaesthetist] FROM [theatre].[Templates] UNION ALL SELECT [Clinician] FROM [theatre].[Appointments]) n WHERE n.[Name] IS NOT NULL AND LTRIM(RTRIM(n.[Name])) <> N'''') x
    WHERE NOT EXISTS (SELECT 1 FROM [core].[Clinicians] c WHERE c.[Name] = x.[Name]);
    UPDATE s SET [SurgeonId] = c.[Id] FROM [theatre].[Sessions] s JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(s.[Surgeon]));
    UPDATE s SET [AnaesthetistId] = c.[Id] FROM [theatre].[Sessions] s JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(s.[Anaesthetist]));
    UPDATE t SET [SurgeonId] = c.[Id] FROM [theatre].[Templates] t JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(t.[Surgeon]));
    UPDATE t SET [AnaesthetistId] = c.[Id] FROM [theatre].[Templates] t JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(t.[Anaesthetist]));
    UPDATE a SET [ClinicianId] = c.[Id] FROM [theatre].[Appointments] a JOIN [core].[Clinicians] c ON c.[Name] = LTRIM(RTRIM(a.[Clinician]));');
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Templates]') AND [c].[name] = N'Anaesthetist');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Templates] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [theatre].[Templates] DROP COLUMN [Anaesthetist];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Templates]') AND [c].[name] = N'Surgeon');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Templates] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [theatre].[Templates] DROP COLUMN [Surgeon];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Sessions]') AND [c].[name] = N'Anaesthetist');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Sessions] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [theatre].[Sessions] DROP COLUMN [Anaesthetist];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Sessions]') AND [c].[name] = N'LocationName');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Sessions] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [theatre].[Sessions] DROP COLUMN [LocationName];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Sessions]') AND [c].[name] = N'Surgeon');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Sessions] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [theatre].[Sessions] DROP COLUMN [Surgeon];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[theatre].[Appointments]') AND [c].[name] = N'Clinician');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [theatre].[Appointments] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [theatre].[Appointments] DROP COLUMN [Clinician];
END;

IF NOT EXISTS (
    SELECT * FROM [theatre].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109071501_ClinicianIds'
)
BEGIN
    INSERT INTO [theatre].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109071501_ClinicianIds', N'10.0.14');
END;

COMMIT;
GO

