IF OBJECT_ID(N'[followup].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'followup') IS NULL EXEC(N'CREATE SCHEMA [followup];');
    CREATE TABLE [followup].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    IF SCHEMA_ID(N'followup') IS NULL EXEC(N'CREATE SCHEMA [followup];');
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[Plans] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [ProcedureContains] nvarchar(500) NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [MaxAttempts] int NOT NULL,
        [SortOrder] int NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[Settings] (
        [Id] int NOT NULL,
        [DutyMobile] nvarchar(30) NULL,
        [PublicUrl] nvarchar(200) NULL,
        [SendCheckInLinks] bit NOT NULL,
        [LinkDays] int NOT NULL,
        [GpLetter] bit NOT NULL,
        [StartFrom] date NULL,
        [LastScanAt] datetimeoffset NULL,
        [NextSsiNumber] int NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_FollowUpSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[Contacts] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NULL,
        [Procedure] nvarchar(200) NOT NULL,
        [OperationDate] date NOT NULL,
        [Kind] nvarchar(12) NOT NULL,
        [Method] nvarchar(12) NOT NULL,
        [Label] nvarchar(100) NOT NULL,
        [DueOn] date NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Attempts] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [AssignedTo] nvarchar(100) NULL,
        [Concern] bit NOT NULL,
        [ConcernText] nvarchar(1000) NULL,
        [SuspectedSsi] bit NOT NULL,
        [Escalation] nvarchar(20) NOT NULL,
        [EscalationNote] nvarchar(2000) NULL,
        [EscalationClosedAt] datetimeoffset NULL,
        [EscalationClosedBy] nvarchar(100) NULL,
        [EscalationOutcome] nvarchar(2000) NULL,
        [IncidentId] uniqueidentifier NULL,
        [DoneBy] nvarchar(100) NULL,
        [DoneAt] datetimeoffset NULL,
        [DoneHow] nvarchar(40) NULL,
        [Notes] nvarchar(4000) NULL,
        [TokenHash] nvarchar(64) NULL,
        [LinkSentAt] datetimeoffset NULL,
        [LinkExpiresAt] datetimeoffset NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [AnsweredAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Contacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Contacts_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [followup].[Plans] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[PlanSteps] (
        [Id] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Kind] nvarchar(12) NOT NULL,
        [Method] nvarchar(12) NOT NULL,
        [Days] int NOT NULL,
        [DaysWithImplant] int NULL,
        [WorkingDays] bit NOT NULL,
        [OnlyIfHighRisk] bit NOT NULL,
        [AssignTo] nvarchar(100) NULL,
        [Label] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_PlanSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanSteps_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [followup].[Plans] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[ContactAnswers] (
        [ContactId] uniqueidentifier NOT NULL,
        [QuestionKey] nvarchar(40) NOT NULL,
        [Value] nvarchar(1000) NOT NULL,
        CONSTRAINT [PK_ContactAnswers] PRIMARY KEY ([ContactId], [QuestionKey]),
        CONSTRAINT [FK_ContactAnswers_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [followup].[Contacts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[SsiCases] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [ContactId] uniqueidentifier NULL,
        [Procedure] nvarchar(200) NOT NULL,
        [Surgeon] nvarchar(150) NULL,
        [OperationDate] date NOT NULL,
        [OnsetOn] date NOT NULL,
        [Signs] nvarchar(1000) NOT NULL,
        [Source] nvarchar(14) NOT NULL,
        [Antibiotics] bit NOT NULL,
        [ReturnToTheatre] bit NOT NULL,
        [Readmitted] bit NOT NULL,
        [Microbiology] nvarchar(500) NULL,
        [Class] nvarchar(12) NOT NULL,
        [ConfirmedBy] nvarchar(150) NULL,
        [ConfirmedAt] datetimeoffset NULL,
        [ConfirmNote] nvarchar(2000) NULL,
        [SurveillanceDays] int NOT NULL,
        [Implant] bit NOT NULL,
        [WoundClass] nvarchar(40) NULL,
        [Asa] int NULL,
        [IncidentId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_SsiCases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SsiCases_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [followup].[Contacts] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE TABLE [followup].[SsiRiskFactors] (
        [SsiCaseId] uniqueidentifier NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [Value] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_SsiRiskFactors] PRIMARY KEY ([SsiCaseId], [Name]),
        CONSTRAINT [FK_SsiRiskFactors_SsiCases_SsiCaseId] FOREIGN KEY ([SsiCaseId]) REFERENCES [followup].[SsiCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'DutyMobile', N'GpLetter', N'LastScanAt', N'LinkDays', N'NextSsiNumber', N'PublicUrl', N'SendCheckInLinks', N'StartFrom') AND [object_id] = OBJECT_ID(N'[followup].[Settings]'))
        SET IDENTITY_INSERT [followup].[Settings] ON;
    EXEC(N'INSERT INTO [followup].[Settings] ([Id], [DutyMobile], [GpLetter], [LastScanAt], [LinkDays], [NextSsiNumber], [PublicUrl], [SendCheckInLinks], [StartFrom])
    VALUES (1, NULL, CAST(1 AS bit), NULL, 5, 1, NULL, CAST(1 AS bit), NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'DutyMobile', N'GpLetter', N'LastScanAt', N'LinkDays', N'NextSsiNumber', N'PublicUrl', N'SendCheckInLinks', N'StartFrom') AND [object_id] = OBJECT_ID(N'[followup].[Settings]'))
        SET IDENTITY_INSERT [followup].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_Contacts_BookingId] ON [followup].[Contacts] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_Contacts_Concern_Status] ON [followup].[Contacts] ([Concern], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_Contacts_PatientId] ON [followup].[Contacts] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_Contacts_PlanId] ON [followup].[Contacts] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_Contacts_Status_DueOn] ON [followup].[Contacts] ([Status], [DueOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Contacts_TokenHash] ON [followup].[Contacts] ([TokenHash]) WHERE [TokenHash] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Plans_Name] ON [followup].[Plans] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Plans_OneDefault] ON [followup].[Plans] ([IsDefault]) WHERE [IsDefault] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlanSteps_PlanId_Sequence] ON [followup].[PlanSteps] ([PlanId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_SsiCases_BookingId] ON [followup].[SsiCases] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_SsiCases_Class_OperationDate] ON [followup].[SsiCases] ([Class], [OperationDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_SsiCases_ContactId] ON [followup].[SsiCases] ([ContactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SsiCases_Number] ON [followup].[SsiCases] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    CREATE INDEX [IX_SsiCases_PatientId] ON [followup].[SsiCases] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [followup].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123104055_InitialFollowUp'
)
BEGIN
    INSERT INTO [followup].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123104055_InitialFollowUp', N'10.0.14');
END;

COMMIT;
GO

