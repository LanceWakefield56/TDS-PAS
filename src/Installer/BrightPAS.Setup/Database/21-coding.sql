IF OBJECT_ID(N'[coding].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'coding') IS NULL EXEC(N'CREATE SCHEMA [coding];');
    CREATE TABLE [coding].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    IF SCHEMA_ID(N'coding') IS NULL EXEC(N'CREATE SCHEMA [coding];');
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Advice] (
        [Id] uniqueidentifier NOT NULL,
        [Reference] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [Edition] nvarchar(60) NOT NULL,
        [AppliesToNz] bit NOT NULL,
        [ReviewedOn] date NULL,
        [CodePrefixes] nvarchar(400) NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [Link] nvarchar(400) NULL,
        [IsActive] bit NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Advice] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Codes] (
        [Id] uniqueidentifier NOT NULL,
        [System] nvarchar(2) NOT NULL,
        [Code] nvarchar(8) NOT NULL,
        [Type] nvarchar(1) NOT NULL,
        [Description] nvarchar(250) NOT NULL,
        [Sex] nvarchar(1) NULL,
        [AgeFromYears] int NULL,
        [AgeToYears] int NULL,
        [NeverAssign] bit NOT NULL,
        [NotPrincipal] bit NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Codes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Episodes] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [AdmissionDate] date NOT NULL,
        [DischargeDate] date NOT NULL,
        [PurchaserCode] nvarchar(2) NOT NULL,
        [AdmissionSource] nvarchar(1) NOT NULL,
        [EventEndType] nvarchar(2) NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [NotReportableReason] nvarchar(300) NULL,
        [CodedBy] nvarchar(100) NULL,
        [CodedAt] datetimeoffset NULL,
        [CoderNote] nvarchar(2000) NULL,
        [SubmissionId] uniqueidentifier NULL,
        [HealthNzError] nvarchar(1000) NULL,
        [Version] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Episodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[NbrsBatches] (
        [Id] uniqueidentifier NOT NULL,
        [Environment] nvarchar(4) NOT NULL,
        [Number] int NOT NULL,
        [FileName] nvarchar(20) NOT NULL,
        [Records] int NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [Accepted] int NOT NULL,
        [Rejected] int NOT NULL,
        [Warnings] int NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [AcknowledgedAt] datetimeoffset NULL,
        CONSTRAINT [PK_NbrsBatches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[NbrsEntries] (
        [Id] uniqueidentifier NOT NULL,
        [LocalId] nvarchar(14) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [WaitlistEntryId] uniqueidentifier NULL,
        [FacilityCode] nvarchar(4) NOT NULL,
        [BookingSource] nvarchar(1) NOT NULL,
        [ReferralDate] date NULL,
        [FirstAssessmentDate] date NULL,
        [CpacDate] date NULL,
        [CpacScore] nvarchar(5) NULL,
        [CpacSystem] nvarchar(4) NULL,
        [PurchaserCode] nvarchar(2) NOT NULL,
        [HealthSpecialty] nvarchar(3) NOT NULL,
        [ClinicalCode] nvarchar(8) NOT NULL,
        [ClinicalCodeType] nvarchar(1) NOT NULL,
        [DomicileCode] nvarchar(4) NULL,
        [ProfessionalGroup] nvarchar(2) NULL,
        [ResponsibilityCode] nvarchar(10) NULL,
        [AssessorCode] nvarchar(10) NULL,
        [AssessorGroup] nvarchar(2) NULL,
        [StagedFlag] nvarchar(1) NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_NbrsEntries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[ProcedureMaps] (
        [Id] uniqueidentifier NOT NULL,
        [Term] nvarchar(60) NOT NULL,
        [Label] nvarchar(150) NOT NULL,
        [ProcedureCodes] nvarchar(200) NOT NULL,
        [DiagnosisCode] nvarchar(10) NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ProcedureMaps] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Registrations] (
        [ClinicianId] uniqueidentifier NOT NULL,
        [ProfessionalGroup] nvarchar(2) NOT NULL,
        [RegistrationNumber] nvarchar(10) NOT NULL,
        CONSTRAINT [PK_Registrations] PRIMARY KEY ([ClinicianId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Settings] (
        [Id] int NOT NULL,
        [CodingSystem] nvarchar(2) NOT NULL,
        [EditionName] nvarchar(80) NOT NULL,
        [HealthAgencyCode] nvarchar(4) NULL,
        [FacilityCode] nvarchar(4) NULL,
        [FileAcronym] nvarchar(3) NULL,
        [PurchaserSelfFunded] nvarchar(2) NULL,
        [PurchaserInsurer] nvarchar(2) NULL,
        [PurchaserAcc] nvarchar(2) NULL,
        [PurchaserHealthNz] nvarchar(2) NULL,
        [PurchaserNoCharge] nvarchar(2) NULL,
        [TdsReportsContractedEvents] bit NOT NULL,
        [ReportWithinDays] int NOT NULL,
        [NbrsAgencyCode] nvarchar(4) NULL,
        [NbrsEnvironment] nvarchar(4) NOT NULL,
        [NextNbrsBatchCmpl] int NOT NULL,
        [NextNbrsBatchProd] int NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Submissions] (
        [Id] uniqueidentifier NOT NULL,
        [FileName] nvarchar(20) NOT NULL,
        [PeriodTo] date NOT NULL,
        [Records] int NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [IsResubmission] bit NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SentBy] nvarchar(100) NULL,
        [SentAt] datetimeoffset NULL,
        [Notes] nvarchar(1000) NULL,
        CONSTRAINT [PK_Submissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[Items] (
        [Id] uniqueidentifier NOT NULL,
        [CodedEpisodeId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [DiagnosisType] nvarchar(1) NOT NULL,
        [Code] nvarchar(8) NOT NULL,
        [Description] nvarchar(50) NOT NULL,
        [Date] date NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Items_Episodes_CodedEpisodeId] FOREIGN KEY ([CodedEpisodeId]) REFERENCES [coding].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[NbrsEvents] (
        [Id] uniqueidentifier NOT NULL,
        [EntryId] uniqueidentifier NOT NULL,
        [Action] nvarchar(1) NOT NULL,
        [StatusCode] nvarchar(2) NULL,
        [StatusDate] date NULL,
        [CertaintyDate] date NULL,
        [BookingMadeDate] date NULL,
        [BookedForDate] date NULL,
        [DeferredBy] nvarchar(1) NULL,
        [ExitDate] date NULL,
        [ExitCategory] nvarchar(2) NULL,
        [ContractAgency] nvarchar(4) NULL,
        [TreatmentFacility] nvarchar(4) NULL,
        [ClientSystemId] nvarchar(14) NULL,
        [EventLocalId] int NOT NULL,
        [BatchId] uniqueidentifier NULL,
        [LineNumber] int NULL,
        [Result] nvarchar(10) NOT NULL,
        [Messages] nvarchar(2000) NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_NbrsEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NbrsEvents_NbrsBatches_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [coding].[NbrsBatches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NbrsEvents_NbrsEntries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [coding].[NbrsEntries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE TABLE [coding].[SubmissionEvents] (
        [SubmissionId] uniqueidentifier NOT NULL,
        [CodedEpisodeId] uniqueidentifier NOT NULL,
        [RecordId] nvarchar(14) NOT NULL,
        CONSTRAINT [PK_SubmissionEvents] PRIMARY KEY ([SubmissionId], [CodedEpisodeId]),
        CONSTRAINT [FK_SubmissionEvents_Episodes_CodedEpisodeId] FOREIGN KEY ([CodedEpisodeId]) REFERENCES [coding].[Episodes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SubmissionEvents_Submissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [coding].[Submissions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Advice_Reference] ON [coding].[Advice] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Codes_Description] ON [coding].[Codes] ([Description]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Codes_System_Code_Type] ON [coding].[Codes] ([System], [Code], [Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Episodes_EpisodeId] ON [coding].[Episodes] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Episodes_PatientId] ON [coding].[Episodes] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Episodes_Status_DischargeDate] ON [coding].[Episodes] ([Status], [DischargeDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Episodes_SubmissionId] ON [coding].[Episodes] ([SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Items_CodedEpisodeId_Sequence] ON [coding].[Items] ([CodedEpisodeId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NbrsBatches_Environment_Number] ON [coding].[NbrsBatches] ([Environment], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NbrsEntries_FacilityCode_LocalId] ON [coding].[NbrsEntries] ([FacilityCode], [LocalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_NbrsEntries_PatientId] ON [coding].[NbrsEntries] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_NbrsEntries_WaitlistEntryId] ON [coding].[NbrsEntries] ([WaitlistEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_NbrsEvents_BatchId] ON [coding].[NbrsEvents] ([BatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_NbrsEvents_EntryId] ON [coding].[NbrsEvents] ([EntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_NbrsEvents_Result_CreatedAt] ON [coding].[NbrsEvents] ([Result], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProcedureMaps_Term] ON [coding].[ProcedureMaps] ([Term]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_SubmissionEvents_CodedEpisodeId] ON [coding].[SubmissionEvents] ([CodedEpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    CREATE INDEX [IX_Submissions_CreatedAt] ON [coding].[Submissions] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [coding].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116100928_InitialCoding'
)
BEGIN
    INSERT INTO [coding].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116100928_InitialCoding', N'10.0.14');
END;

COMMIT;
GO

