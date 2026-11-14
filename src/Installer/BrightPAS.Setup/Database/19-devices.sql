IF OBJECT_ID(N'[devices].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'devices') IS NULL EXEC(N'CREATE SCHEMA [devices];');
    CREATE TABLE [devices].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    IF SCHEMA_ID(N'devices') IS NULL EXEC(N'CREATE SCHEMA [devices];');
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[AdtSends] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Event] nvarchar(3) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [By] nvarchar(100) NOT NULL,
        [MessageId] uniqueidentifier NULL,
        [Error] nvarchar(300) NULL,
        CONSTRAINT [PK_AdtSends] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[AnaestheticRecords] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [CaseNumber] nvarchar(64) NOT NULL,
        [ControlId] nvarchar(64) NOT NULL,
        [IsCorrection] bit NOT NULL,
        [SupersededById] uniqueidentifier NULL,
        [Nhi] nvarchar(10) NULL,
        [Anaesthetist] nvarchar(100) NULL,
        [StartAt] datetimeoffset NULL,
        [EndAt] datetimeoffset NULL,
        [Asa] nvarchar(10) NULL,
        [Technique] nvarchar(100) NULL,
        [Airway] nvarchar(100) NULL,
        [Comments] nvarchar(2000) NULL,
        [DocumentId] uniqueidentifier NULL,
        [NoteId] uniqueidentifier NULL,
        [MessageId] uniqueidentifier NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AnaestheticRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnaestheticRecords_AnaestheticRecords_SupersededById] FOREIGN KEY ([SupersededById]) REFERENCES [devices].[AnaestheticRecords] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[Monitors] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentId] uniqueidentifier NOT NULL,
        [Code] nvarchar(60) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastSeenAt] datetimeoffset NULL,
        [LastNhi] nvarchar(10) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Monitors] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[Settings] (
        [Id] int NOT NULL,
        [TheatreChartMinutes] int NOT NULL,
        [RecoveryChartMinutes] int NOT NULL,
        [WardChartMinutes] int NOT NULL,
        [StoreEverySeconds] int NOT NULL,
        [OfflineAfterSeconds] int NOT NULL,
        [UnassociatedKeepHours] int NOT NULL,
        [KeepMonitorMessagesHours] int NOT NULL,
        [AutoConnectByNhi] bit NOT NULL,
        [SaferSleepEnabled] bit NOT NULL,
        [SaferSleepOutCode] nvarchar(40) NOT NULL,
        [SaferSleepApplication] nvarchar(40) NOT NULL,
        [SaferSleepFacility] nvarchar(40) NOT NULL,
        [SendOn] nvarchar(20) NOT NULL,
        [FileDrugsToEmr] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[AnaestheticDrugs] (
        [Id] uniqueidentifier NOT NULL,
        [RecordId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Dose] decimal(10,3) NULL,
        [Unit] nvarchar(20) NULL,
        [Route] nvarchar(30) NULL,
        [Rate] nvarchar(60) NULL,
        [Code] nvarchar(60) NULL,
        CONSTRAINT [PK_AnaestheticDrugs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnaestheticDrugs_AnaestheticRecords_RecordId] FOREIGN KEY ([RecordId]) REFERENCES [devices].[AnaestheticRecords] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[AnaestheticEvents] (
        [Id] uniqueidentifier NOT NULL,
        [RecordId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_AnaestheticEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnaestheticEvents_AnaestheticRecords_RecordId] FOREIGN KEY ([RecordId]) REFERENCES [devices].[AnaestheticRecords] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[AnaestheticVitals] (
        [Id] uniqueidentifier NOT NULL,
        [RecordId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Code] nvarchar(12) NOT NULL,
        [Value] decimal(9,2) NOT NULL,
        [Unit] nvarchar(20) NULL,
        CONSTRAINT [PK_AnaestheticVitals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnaestheticVitals_AnaestheticRecords_RecordId] FOREIGN KEY ([RecordId]) REFERENCES [devices].[AnaestheticRecords] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[Associations] (
        [Id] uniqueidentifier NOT NULL,
        [MonitorId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [StartedBy] nvarchar(100) NOT NULL,
        [Method] nvarchar(20) NOT NULL,
        [EndedAt] datetimeoffset NULL,
        [EndedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Associations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Associations_Monitors_MonitorId] FOREIGN KEY ([MonitorId]) REFERENCES [devices].[Monitors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[Captures] (
        [Id] uniqueidentifier NOT NULL,
        [AssociationId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [MonitorId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [HeartRate] int NULL,
        [SpO2] int NULL,
        [SystolicBp] int NULL,
        [DiastolicBp] int NULL,
        [MeanBp] int NULL,
        [RespiratoryRate] int NULL,
        [TemperatureC] decimal(4,1) NULL,
        [EtCo2] decimal(5,1) NULL,
        [Status] nvarchar(12) NOT NULL,
        [ObservationId] uniqueidentifier NULL,
        [DecidedBy] nvarchar(100) NULL,
        [DecidedAt] datetimeoffset NULL,
        [RejectReason] nvarchar(200) NULL,
        CONSTRAINT [PK_Captures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Captures_Associations_AssociationId] FOREIGN KEY ([AssociationId]) REFERENCES [devices].[Associations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Captures_Monitors_MonitorId] FOREIGN KEY ([MonitorId]) REFERENCES [devices].[Monitors] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE TABLE [devices].[Readings] (
        [Id] bigint NOT NULL IDENTITY,
        [MonitorId] uniqueidentifier NOT NULL,
        [AssociationId] uniqueidentifier NULL,
        [At] datetimeoffset NOT NULL,
        [Code] nvarchar(12) NOT NULL,
        [Value] decimal(9,2) NOT NULL,
        [Unit] nvarchar(20) NULL,
        CONSTRAINT [PK_Readings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Readings_Associations_AssociationId] FOREIGN KEY ([AssociationId]) REFERENCES [devices].[Associations] ([Id]),
        CONSTRAINT [FK_Readings_Monitors_MonitorId] FOREIGN KEY ([MonitorId]) REFERENCES [devices].[Monitors] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AdtSends_EpisodeId_Event] ON [devices].[AdtSends] ([EpisodeId], [Event]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AdtSends_MessageId] ON [devices].[AdtSends] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticDrugs_RecordId_At] ON [devices].[AnaestheticDrugs] ([RecordId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticEvents_RecordId_At] ON [devices].[AnaestheticEvents] ([RecordId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_CaseNumber] ON [devices].[AnaestheticRecords] ([CaseNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AnaestheticRecords_ControlId] ON [devices].[AnaestheticRecords] ([ControlId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_DocumentId] ON [devices].[AnaestheticRecords] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_EpisodeId] ON [devices].[AnaestheticRecords] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_MessageId] ON [devices].[AnaestheticRecords] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_NoteId] ON [devices].[AnaestheticRecords] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_PatientId] ON [devices].[AnaestheticRecords] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticRecords_SupersededById] ON [devices].[AnaestheticRecords] ([SupersededById]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_AnaestheticVitals_RecordId_At] ON [devices].[AnaestheticVitals] ([RecordId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Associations_EpisodeId_StartedAt] ON [devices].[Associations] ([EpisodeId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Associations_MonitorId_StartedAt] ON [devices].[Associations] ([MonitorId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Associations_PatientId] ON [devices].[Associations] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Associations_OpenEpisode] ON [devices].[Associations] ([EpisodeId]) WHERE [EndedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Associations_OpenMonitor] ON [devices].[Associations] ([MonitorId]) WHERE [EndedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Captures_AssociationId_At] ON [devices].[Captures] ([AssociationId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Captures_EpisodeId_At] ON [devices].[Captures] ([EpisodeId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Captures_MonitorId] ON [devices].[Captures] ([MonitorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Captures_ObservationId] ON [devices].[Captures] ([ObservationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Monitors_Code] ON [devices].[Monitors] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Monitors_EquipmentId] ON [devices].[Monitors] ([EquipmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Readings_AssociationId_At] ON [devices].[Readings] ([AssociationId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    CREATE INDEX [IX_Readings_MonitorId_At] ON [devices].[Readings] ([MonitorId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [devices].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114092737_InitialDevices'
)
BEGIN
    INSERT INTO [devices].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114092737_InitialDevices', N'10.0.14');
END;

COMMIT;
GO

