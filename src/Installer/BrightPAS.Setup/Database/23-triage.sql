IF OBJECT_ID(N'[triage].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'triage') IS NULL EXEC(N'CREATE SCHEMA [triage];');
    CREATE TABLE [triage].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    IF SCHEMA_ID(N'triage') IS NULL EXEC(N'CREATE SCHEMA [triage];');
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[RuleSets] (
        [Id] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [IsExample] bit NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ApprovedBy] nvarchar(150) NULL,
        [ApprovedRole] nvarchar(100) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [ApprovalNote] nvarchar(1000) NULL,
        [RetiredAt] datetimeoffset NULL,
        CONSTRAINT [PK_RuleSets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[Settings] (
        [Id] int NOT NULL,
        [HoldBookingUntilSignedOff] bit NOT NULL,
        [HoldProcedures] nvarchar(500) NULL,
        [RetriageAfterDays] int NOT NULL,
        [SignOffDaysBefore] int NOT NULL,
        [LastScanAt] datetimeoffset NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TriageSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[Assessments] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [FormRequestId] uniqueidentifier NULL,
        [PreviousId] uniqueidentifier NULL,
        [Trigger] nvarchar(16) NOT NULL,
        [TriggerDetail] nvarchar(300) NULL,
        [Status] nvarchar(12) NOT NULL,
        [Procedure] nvarchar(200) NOT NULL,
        [Anaesthetic] nvarchar(20) NULL,
        [ProcedureDate] date NULL,
        [RuleSetId] uniqueidentifier NULL,
        [RuleSetVersion] int NULL,
        [RulesApproved] bit NOT NULL,
        [FactsJson] nvarchar(max) NOT NULL,
        [StaffInputsJson] nvarchar(4000) NULL,
        [Bmi] decimal(5,1) NULL,
        [StopBang] int NULL,
        [StopBangKnown] int NULL,
        [Frailty] int NULL,
        [Vte] int NULL,
        [Rcri] int NULL,
        [SuggestedAsa] int NULL,
        [ConfirmedAsa] int NULL,
        [SuggestedOutcome] nvarchar(24) NOT NULL,
        [FinalOutcome] nvarchar(24) NULL,
        [Precautions] nvarchar(1000) NULL,
        [Missing] nvarchar(1000) NULL,
        [ChangeSummary] nvarchar(2000) NULL,
        [Worse] bit NOT NULL,
        [Notes] nvarchar(4000) NULL,
        [OverrideReason] nvarchar(1000) NULL,
        [SignedOffBy] nvarchar(150) NULL,
        [SignedOffRole] nvarchar(100) NULL,
        [SignedOffAt] datetimeoffset NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Assessments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Assessments_Assessments_PreviousId] FOREIGN KEY ([PreviousId]) REFERENCES [triage].[Assessments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Assessments_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [triage].[RuleSets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[MedicineRules] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Keywords] nvarchar(500) NOT NULL,
        [Instruction] nvarchar(1000) NOT NULL,
        [Outcome] nvarchar(24) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_MedicineRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MedicineRules_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [triage].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[SuitabilityRules] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Fact] nvarchar(40) NOT NULL,
        [Operator] nvarchar(10) NOT NULL,
        [Threshold] decimal(8,2) NULL,
        [TextValue] nvarchar(100) NULL,
        [ProcedureContains] nvarchar(300) NULL,
        [Anaesthetic] nvarchar(20) NULL,
        [Outcome] nvarchar(24) NOT NULL,
        [Precaution] nvarchar(300) NULL,
        [Explanation] nvarchar(1000) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_SuitabilityRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SuitabilityRules_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [triage].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE TABLE [triage].[Findings] (
        [AssessmentId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [Outcome] nvarchar(24) NOT NULL,
        [Text] nvarchar(500) NOT NULL,
        [Precaution] nvarchar(300) NULL,
        CONSTRAINT [PK_Findings] PRIMARY KEY ([AssessmentId], [Sequence]),
        CONSTRAINT [FK_Findings_Assessments_AssessmentId] FOREIGN KEY ([AssessmentId]) REFERENCES [triage].[Assessments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'HoldBookingUntilSignedOff', N'HoldProcedures', N'LastScanAt', N'RetriageAfterDays', N'SignOffDaysBefore') AND [object_id] = OBJECT_ID(N'[triage].[Settings]'))
        SET IDENTITY_INSERT [triage].[Settings] ON;
    EXEC(N'INSERT INTO [triage].[Settings] ([Id], [HoldBookingUntilSignedOff], [HoldProcedures], [LastScanAt], [RetriageAfterDays], [SignOffDaysBefore])
    VALUES (1, CAST(0 AS bit), NULL, NULL, 90, 7)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'HoldBookingUntilSignedOff', N'HoldProcedures', N'LastScanAt', N'RetriageAfterDays', N'SignOffDaysBefore') AND [object_id] = OBJECT_ID(N'[triage].[Settings]'))
        SET IDENTITY_INSERT [triage].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_BookingId] ON [triage].[Assessments] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_FormRequestId] ON [triage].[Assessments] ([FormRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_PatientId_Status] ON [triage].[Assessments] ([PatientId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_PreviousId] ON [triage].[Assessments] ([PreviousId]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_RuleSetId] ON [triage].[Assessments] ([RuleSetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_Assessments_Status_ProcedureDate] ON [triage].[Assessments] ([Status], [ProcedureDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE INDEX [IX_MedicineRules_RuleSetId] ON [triage].[MedicineRules] ([RuleSetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RuleSets_Version] ON [triage].[RuleSets] ([Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RuleSets_OneApproved] ON [triage].[RuleSets] ([Status]) WHERE [Status] = ''Approved''');
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SuitabilityRules_RuleSetId_Code] ON [triage].[SuitabilityRules] ([RuleSetId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [triage].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123062318_InitialTriage'
)
BEGIN
    INSERT INTO [triage].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123062318_InitialTriage', N'10.0.14');
END;

COMMIT;
GO

