IF OBJECT_ID(N'[imaging].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'imaging') IS NULL EXEC(N'CREATE SCHEMA [imaging];');
    CREATE TABLE [imaging].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    IF SCHEMA_ID(N'imaging') IS NULL EXEC(N'CREATE SCHEMA [imaging];');
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE TABLE [imaging].[Requests] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [AccessionNumber] nvarchar(16) NOT NULL,
        [StudyInstanceUid] nvarchar(64) NOT NULL,
        [Modality] nvarchar(4) NOT NULL,
        [Examination] nvarchar(200) NOT NULL,
        [BodyPart] nvarchar(64) NULL,
        [ClinicalNotes] nvarchar(2000) NULL,
        [WantedOn] date NULL,
        [Status] nvarchar(20) NOT NULL,
        [StudyId] uniqueidentifier NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [RequestedBy] nvarchar(100) NOT NULL,
        [CancelledAt] datetimeoffset NULL,
        [CancelledBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE TABLE [imaging].[Studies] (
        [Id] uniqueidentifier NOT NULL,
        [StudyInstanceUid] nvarchar(64) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [RequestId] uniqueidentifier NULL,
        [MatchedBy] nvarchar(30) NULL,
        [AccessionNumber] nvarchar(16) NULL,
        [StudyDate] date NULL,
        [StudyTime] nvarchar(16) NULL,
        [Description] nvarchar(200) NULL,
        [Modalities] nvarchar(60) NOT NULL,
        [BodyPart] nvarchar(64) NULL,
        [ReferringPhysician] nvarchar(200) NULL,
        [Institution] nvarchar(200) NULL,
        [FilePatientId] nvarchar(64) NULL,
        [FilePatientName] nvarchar(200) NULL,
        [FileBirthDate] date NULL,
        [FileSex] nvarchar(16) NULL,
        [Source] nvarchar(20) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [ReceivedBy] nvarchar(200) NOT NULL,
        [FiledAt] datetimeoffset NULL,
        [FiledBy] nvarchar(100) NULL,
        [ImageCount] int NOT NULL,
        [Bytes] bigint NOT NULL,
        [Report] nvarchar(max) NULL,
        [ReportAt] datetimeoffset NULL,
        [ReportBy] nvarchar(100) NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(100) NULL,
        [DeletedReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Studies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE TABLE [imaging].[Series] (
        [Id] uniqueidentifier NOT NULL,
        [StudyId] uniqueidentifier NOT NULL,
        [SeriesInstanceUid] nvarchar(64) NOT NULL,
        [Modality] nvarchar(16) NOT NULL,
        [SeriesNumber] int NULL,
        [Description] nvarchar(200) NULL,
        [BodyPart] nvarchar(64) NULL,
        [ImageCount] int NOT NULL,
        CONSTRAINT [PK_Series] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Series_Studies_StudyId] FOREIGN KEY ([StudyId]) REFERENCES [imaging].[Studies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE TABLE [imaging].[Images] (
        [Id] uniqueidentifier NOT NULL,
        [StudyId] uniqueidentifier NOT NULL,
        [SeriesId] uniqueidentifier NOT NULL,
        [SopInstanceUid] nvarchar(64) NOT NULL,
        [SopClassUid] nvarchar(64) NOT NULL,
        [InstanceNumber] int NULL,
        [Rows] int NOT NULL,
        [Columns] int NOT NULL,
        [Frames] int NOT NULL,
        [Photometric] nvarchar(20) NULL,
        [TransferSyntax] nvarchar(64) NOT NULL,
        [Viewable] bit NOT NULL,
        [WindowCentre] float NULL,
        [WindowWidth] float NULL,
        [PixelSpacing] nvarchar(40) NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Bytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Images] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Images_Series_SeriesId] FOREIGN KEY ([SeriesId]) REFERENCES [imaging].[Series] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Images_SeriesId] ON [imaging].[Images] ([SeriesId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Images_SopInstanceUid] ON [imaging].[Images] ([SopInstanceUid]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Images_StudyId] ON [imaging].[Images] ([StudyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Requests_AccessionNumber] ON [imaging].[Requests] ([AccessionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Requests_BookingId] ON [imaging].[Requests] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Requests_PatientId] ON [imaging].[Requests] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Requests_StudyInstanceUid] ON [imaging].[Requests] ([StudyInstanceUid]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Series_SeriesInstanceUid] ON [imaging].[Series] ([SeriesInstanceUid]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Series_StudyId] ON [imaging].[Series] ([StudyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Studies_AccessionNumber] ON [imaging].[Studies] ([AccessionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Studies_BookingId] ON [imaging].[Studies] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Studies_PatientId_Status] ON [imaging].[Studies] ([PatientId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE INDEX [IX_Studies_Status] ON [imaging].[Studies] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Studies_StudyInstanceUid] ON [imaging].[Studies] ([StudyInstanceUid]);
END;

IF NOT EXISTS (
    SELECT * FROM [imaging].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128044344_InitialImaging'
)
BEGIN
    INSERT INTO [imaging].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128044344_InitialImaging', N'10.0.14');
END;

COMMIT;
GO

