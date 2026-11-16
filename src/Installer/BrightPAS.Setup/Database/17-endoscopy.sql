IF OBJECT_ID(N'[endo].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'endo') IS NULL EXEC(N'CREATE SCHEMA [endo];');
    CREATE TABLE [endo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    IF SCHEMA_ID(N'endo') IS NULL EXEC(N'CREATE SCHEMA [endo];');
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[AdviceTexts] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [Title] nvarchar(100) NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_AdviceTexts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Procedures] (
        [Id] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [EndoscopistId] uniqueidentifier NULL,
        [EndoscopistName] nvarchar(100) NULL,
        [Assistants] nvarchar(200) NULL,
        [UpperScopeId] uniqueidentifier NULL,
        [LowerScopeId] uniqueidentifier NULL,
        [TowerId] uniqueidentifier NULL,
        [AccessionNumber] nvarchar(16) NOT NULL,
        [StudyInstanceUid] nvarchar(64) NOT NULL,
        [NbspScreening] bit NOT NULL,
        [NbspNumber] nvarchar(30) NULL,
        [OtherIndication] nvarchar(300) NULL,
        [ConsentConfirmed] bit NOT NULL,
        [Sedation] nvarchar(30) NOT NULL,
        [SedationNotes] nvarchar(200) NULL,
        [Co2] bit NOT NULL,
        [WaterAssisted] bit NOT NULL,
        [ScopeGuide] bit NOT NULL,
        [PositionChange] bit NOT NULL,
        [AbdominalPressure] bit NOT NULL,
        [UpperStartAt] datetimeoffset NULL,
        [UpperEndAt] datetimeoffset NULL,
        [UpperExtent] nvarchar(20) NULL,
        [Retroflexion] bit NULL,
        [UpperPhotoDocumentation] bit NULL,
        [InsertedAt] datetimeoffset NULL,
        [CaecumAt] datetimeoffset NULL,
        [WithdrawalStartAt] datetimeoffset NULL,
        [EndedAt] datetimeoffset NULL,
        [LowerExtent] nvarchar(20) NULL,
        [AppendicealOrifice] bit NULL,
        [IleocaecalValve] bit NULL,
        [TerminalIleum] bit NULL,
        [CaecumPhotographed] bit NULL,
        [BbpsRight] int NULL,
        [BbpsTransverse] int NULL,
        [BbpsLeft] int NULL,
        [RectalRetroflexion] bit NULL,
        [IncompleteReason] nvarchar(300) NULL,
        [Comfort] int NULL,
        [Complication] nvarchar(30) NOT NULL,
        [ComplicationDetails] nvarchar(1000) NULL,
        [Diagnosis] nvarchar(2000) NULL,
        [Recommendations] nvarchar(2000) NULL,
        [FollowUp] nvarchar(500) NULL,
        [SurveillanceMonths] int NULL,
        [PatientAdvice] nvarchar(2000) NULL,
        [MppsUid] nvarchar(64) NULL,
        [SignedBy] nvarchar(100) NULL,
        [SignedAt] datetimeoffset NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Procedures] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Findings] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Site] nvarchar(40) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Grade] nvarchar(60) NULL,
        [SizeMm] int NULL,
        [Description] nvarchar(1000) NULL,
        CONSTRAINT [PK_Findings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Findings_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Interventions] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Site] nvarchar(40) NOT NULL,
        [Count] int NULL,
        [Pot] nvarchar(20) NULL,
        [Purpose] nvarchar(100) NULL,
        [Details] nvarchar(500) NULL,
        [Result] nvarchar(100) NULL,
        CONSTRAINT [PK_Interventions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Interventions_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Polyps] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Site] nvarchar(40) NOT NULL,
        [DistanceCm] int NULL,
        [SizeMm] int NOT NULL,
        [Paris] nvarchar(10) NOT NULL,
        [Nice] nvarchar(10) NULL,
        [Jnet] nvarchar(10) NULL,
        [Technique] nvarchar(30) NOT NULL,
        [Lifted] bit NULL,
        [Resection] nvarchar(20) NOT NULL,
        [Retrieved] nvarchar(10) NOT NULL,
        [Pot] nvarchar(20) NULL,
        [Complete] nvarchar(10) NOT NULL,
        [Clips] int NOT NULL,
        [Tattooed] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [Histology] nvarchar(40) NULL,
        [Dysplasia] nvarchar(20) NULL,
        [HistologyNotes] nvarchar(500) NULL,
        [HistologyBy] nvarchar(100) NULL,
        [HistologyAt] datetimeoffset NULL,
        CONSTRAINT [PK_Polyps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Polyps_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[ProcedureIndications] (
        [ProcedureId] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        CONSTRAINT [PK_ProcedureIndications] PRIMARY KEY ([ProcedureId], [Code]),
        CONSTRAINT [FK_ProcedureIndications_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Tattoos] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [Site] nvarchar(40) NOT NULL,
        [DistanceCm] int NULL,
        [Position] nvarchar(20) NOT NULL,
        [Ink] nvarchar(60) NOT NULL,
        [VolumeMl] decimal(5,1) NULL,
        [Injections] int NULL,
        [PolypId] uniqueidentifier NULL,
        [Reason] nvarchar(300) NULL,
        CONSTRAINT [PK_Tattoos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tattoos_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE TABLE [endo].[Media] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(60) NOT NULL,
        [Bytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NULL,
        [Chunks] int NOT NULL,
        [CapturedAt] datetimeoffset NOT NULL,
        [CapturedBy] nvarchar(100) NOT NULL,
        [DurationSeconds] decimal(9,1) NULL,
        [Site] nvarchar(40) NULL,
        [Caption] nvarchar(200) NULL,
        [InReport] bit NOT NULL,
        [InPatientReport] bit NOT NULL,
        [Sort] int NOT NULL,
        [FindingId] uniqueidentifier NULL,
        [PolypId] uniqueidentifier NULL,
        [SopInstanceUid] nvarchar(64) NULL,
        [DicomPatientId] nvarchar(64) NULL,
        [DicomPatientName] nvarchar(100) NULL,
        [DicomAccession] nvarchar(16) NULL,
        [DeletedBy] nvarchar(100) NULL,
        [DeletedAt] datetimeoffset NULL,
        [DeleteReason] nvarchar(300) NULL,
        CONSTRAINT [PK_Media] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Media_Findings_FindingId] FOREIGN KEY ([FindingId]) REFERENCES [endo].[Findings] ([Id]),
        CONSTRAINT [FK_Media_Polyps_PolypId] FOREIGN KEY ([PolypId]) REFERENCES [endo].[Polyps] ([Id]),
        CONSTRAINT [FK_Media_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [endo].[Procedures] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdviceTexts_Key] ON [endo].[AdviceTexts] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Findings_ProcedureId_Number] ON [endo].[Findings] ([ProcedureId], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Interventions_ProcedureId] ON [endo].[Interventions] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Media_FindingId] ON [endo].[Media] ([FindingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Media_PatientId] ON [endo].[Media] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Media_PolypId] ON [endo].[Media] ([PolypId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Media_ProcedureId_Sort] ON [endo].[Media] ([ProcedureId], [Sort]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Media_SopInstanceUid] ON [endo].[Media] ([SopInstanceUid]) WHERE [SopInstanceUid] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Media_Status] ON [endo].[Media] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Polyps_ProcedureId_Number] ON [endo].[Polyps] ([ProcedureId], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Procedures_AccessionNumber] ON [endo].[Procedures] ([AccessionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Procedures_BookingId] ON [endo].[Procedures] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_EndoscopistId] ON [endo].[Procedures] ([EndoscopistId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_LowerScopeId] ON [endo].[Procedures] ([LowerScopeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_PatientId] ON [endo].[Procedures] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_Status_Kind] ON [endo].[Procedures] ([Status], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_TowerId] ON [endo].[Procedures] ([TowerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Procedures_UpperScopeId] ON [endo].[Procedures] ([UpperScopeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Tattoos_PolypId] ON [endo].[Tattoos] ([PolypId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    CREATE INDEX [IX_Tattoos_ProcedureId] ON [endo].[Tattoos] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113193941_InitialEndoscopy'
)
BEGIN
    INSERT INTO [endo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113193941_InitialEndoscopy', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Procedures] ADD [GpLetterId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Procedures] ADD [NbspExportedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD [AnnotatedFromId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD [OriginalContentType] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD [OriginalFileName] nvarchar(260) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    CREATE INDEX [IX_Procedures_GpLetterId] ON [endo].[Procedures] ([GpLetterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    CREATE INDEX [IX_Media_AnnotatedFromId] ON [endo].[Media] ([AnnotatedFromId]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD CONSTRAINT [FK_Media_Media_AnnotatedFromId] FOREIGN KEY ([AnnotatedFromId]) REFERENCES [endo].[Media] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042049_AnnotationsNbspGpLetter'
)
BEGIN
    INSERT INTO [endo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114042049_AnnotationsNbspGpLetter', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116101418_VideoArchive'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD [ArchivedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116101418_VideoArchive'
)
BEGIN
    ALTER TABLE [endo].[Media] ADD [ArchivedTo] nvarchar(400) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [endo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116101418_VideoArchive'
)
BEGIN
    INSERT INTO [endo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116101418_VideoArchive', N'10.0.14');
END;

COMMIT;
GO

