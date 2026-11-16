IF OBJECT_ID(N'[quality].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'quality') IS NULL EXEC(N'CREATE SCHEMA [quality];');
    CREATE TABLE [quality].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    IF SCHEMA_ID(N'quality') IS NULL EXEC(N'CREATE SCHEMA [quality];');
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Compliments] (
        [Id] uniqueidentifier NOT NULL,
        [ReceivedOn] date NOT NULL,
        [From] nvarchar(150) NOT NULL,
        [StaffNamed] nvarchar(300) NULL,
        [Text] nvarchar(2000) NOT NULL,
        [SharedAt] datetimeoffset NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Compliments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[CorrectiveActions] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [Source] nvarchar(15) NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(40) NULL,
        [Title] nvarchar(200) NOT NULL,
        [Detail] nvarchar(4000) NULL,
        [Owner] nvarchar(100) NOT NULL,
        [DueOn] date NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Evidence] nvarchar(4000) NULL,
        [DoneAt] datetimeoffset NULL,
        [DoneBy] nvarchar(100) NULL,
        [VerifiedAt] datetimeoffset NULL,
        [VerifiedBy] nvarchar(100) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_CorrectiveActions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Incidents] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [ReportedAt] datetimeoffset NOT NULL,
        [ReportedBy] nvarchar(100) NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [Location] nvarchar(100) NOT NULL,
        [Category] nvarchar(30) NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [ImmediateAction] nvarchar(2000) NULL,
        [PatientId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [EquipmentId] uniqueidentifier NULL,
        [StaffInvolved] nvarchar(300) NULL,
        [Harm] nvarchar(12) NOT NULL,
        [Sac] int NULL,
        [AlwaysReport] bit NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [Investigator] nvarchar(100) NULL,
        [TriagedBy] nvarchar(100) NULL,
        [TriagedAt] datetimeoffset NULL,
        [EscalatedAt] datetimeoffset NULL,
        [Findings] nvarchar(max) NULL,
        [ContributingFactors] nvarchar(4000) NULL,
        [LearningSummary] nvarchar(4000) NULL,
        [ClosedBy] nvarchar(100) NULL,
        [ClosedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Incidents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Indicators] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Definition] nvarchar(2000) NOT NULL,
        [Numerator] nvarchar(500) NOT NULL,
        [Denominator] nvarchar(500) NOT NULL,
        [Calculator] nvarchar(30) NULL,
        [Scale] int NOT NULL,
        [Direction] nvarchar(15) NOT NULL,
        [Target] decimal(9,2) NULL,
        [AlertAt] decimal(9,2) NULL,
        [Owner] nvarchar(100) NOT NULL,
        [Source] nvarchar(200) NULL,
        [IsActive] bit NOT NULL,
        [Sort] int NOT NULL,
        CONSTRAINT [PK_Indicators] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Policies] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Version] nvarchar(20) NOT NULL,
        [Owner] nvarchar(100) NOT NULL,
        [Link] nvarchar(500) NULL,
        [ReviewBy] date NOT NULL,
        [RequiresAcknowledgement] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Policies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Risks] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [IsHazard] bit NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Category] nvarchar(25) NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Owner] nvarchar(100) NOT NULL,
        [Likelihood] int NOT NULL,
        [Consequence] int NOT NULL,
        [Controls] nvarchar(4000) NULL,
        [ResidualLikelihood] int NULL,
        [ResidualConsequence] int NULL,
        [Location] nvarchar(100) NULL,
        [EquipmentId] uniqueidentifier NULL,
        [ReviewBy] date NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Risks] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Sequences] (
        [Key] nvarchar(20) NOT NULL,
        [Next] int NOT NULL,
        CONSTRAINT [PK_Sequences] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Settings] (
        [Id] int NOT NULL,
        [EscalateTo] nvarchar(300) NOT NULL,
        [ExtraNonWorkingDays] nvarchar(1000) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Complaints] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [ReceivedOn] date NOT NULL,
        [ReceivedVia] nvarchar(40) NOT NULL,
        [Complainant] nvarchar(150) NOT NULL,
        [Contact] nvarchar(200) NULL,
        [PatientId] uniqueidentifier NULL,
        [Summary] nvarchar(4000) NOT NULL,
        [Category] nvarchar(60) NOT NULL,
        [Status] nvarchar(15) NOT NULL,
        [AcknowledgeBy] date NOT NULL,
        [AcknowledgedOn] date NULL,
        [DecideBy] date NULL,
        [ExtensionNotifiedOn] date NULL,
        [ExtensionReason] nvarchar(500) NULL,
        [RespondedOn] date NULL,
        [Outcome] nvarchar(15) NULL,
        [Response] nvarchar(4000) NULL,
        [HdcReferral] bit NOT NULL,
        [AdvocacyInvolved] bit NOT NULL,
        [IncidentId] uniqueidentifier NULL,
        [Owner] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Complaints] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Complaints_Incidents_IncidentId] FOREIGN KEY ([IncidentId]) REFERENCES [quality].[Incidents] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Disclosures] (
        [Id] uniqueidentifier NOT NULL,
        [IncidentId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [By] nvarchar(100) NOT NULL,
        [PersonTold] nvarchar(150) NOT NULL,
        [Method] nvarchar(60) NOT NULL,
        [Notes] nvarchar(2000) NULL,
        CONSTRAINT [PK_Disclosures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Disclosures_Incidents_IncidentId] FOREIGN KEY ([IncidentId]) REFERENCES [quality].[Incidents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [IncidentId] uniqueidentifier NOT NULL,
        [Agency] nvarchar(25) NOT NULL,
        [NotifiedAt] datetimeoffset NULL,
        [NotifiedBy] nvarchar(100) NULL,
        [Reference] nvarchar(100) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Incidents_IncidentId] FOREIGN KEY ([IncidentId]) REFERENCES [quality].[Incidents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[IndicatorValues] (
        [Id] uniqueidentifier NOT NULL,
        [IndicatorId] uniqueidentifier NOT NULL,
        [Month] date NOT NULL,
        [NumeratorValue] decimal(12,2) NOT NULL,
        [DenominatorValue] decimal(12,2) NULL,
        [EnteredBy] nvarchar(100) NOT NULL,
        [EnteredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IndicatorValues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IndicatorValues_Indicators_IndicatorId] FOREIGN KEY ([IndicatorId]) REFERENCES [quality].[Indicators] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE TABLE [quality].[PolicyAcknowledgements] (
        [Id] uniqueidentifier NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [Version] nvarchar(20) NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PolicyAcknowledgements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PolicyAcknowledgements_Policies_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [quality].[Policies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Complaints_IncidentId] ON [quality].[Complaints] ([IncidentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Complaints_Number] ON [quality].[Complaints] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Complaints_PatientId] ON [quality].[Complaints] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Complaints_ReceivedOn] ON [quality].[Complaints] ([ReceivedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Complaints_Status_AcknowledgeBy] ON [quality].[Complaints] ([Status], [AcknowledgeBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Compliments_ReceivedOn] ON [quality].[Compliments] ([ReceivedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CorrectiveActions_Number] ON [quality].[CorrectiveActions] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_CorrectiveActions_Source_SourceId] ON [quality].[CorrectiveActions] ([Source], [SourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_CorrectiveActions_Status_DueOn] ON [quality].[CorrectiveActions] ([Status], [DueOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Disclosures_IncidentId_At] ON [quality].[Disclosures] ([IncidentId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Incidents_BookingId] ON [quality].[Incidents] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Incidents_EquipmentId] ON [quality].[Incidents] ([EquipmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Incidents_Number] ON [quality].[Incidents] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Incidents_OccurredAt] ON [quality].[Incidents] ([OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Incidents_PatientId] ON [quality].[Incidents] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Incidents_Status_ReportedAt] ON [quality].[Incidents] ([Status], [ReportedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Indicators_Code] ON [quality].[Indicators] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IndicatorValues_IndicatorId_Month] ON [quality].[IndicatorValues] ([IndicatorId], [Month]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Notifications_IncidentId_Agency] ON [quality].[Notifications] ([IncidentId], [Agency]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Policies_Code] ON [quality].[Policies] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Policies_ReviewBy] ON [quality].[Policies] ([ReviewBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PolicyAcknowledgements_DocumentId_Version_UserName] ON [quality].[PolicyAcknowledgements] ([DocumentId], [Version], [UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Risks_EquipmentId] ON [quality].[Risks] ([EquipmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Risks_IsHazard_Status] ON [quality].[Risks] ([IsHazard], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Risks_Number] ON [quality].[Risks] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    CREATE INDEX [IX_Risks_ReviewBy] ON [quality].[Risks] ([ReviewBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [quality].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116013721_InitialQuality'
)
BEGIN
    INSERT INTO [quality].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116013721_InitialQuality', N'10.0.14');
END;

COMMIT;
GO

