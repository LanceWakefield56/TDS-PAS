IF OBJECT_ID(N'[emr].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'emr') IS NULL EXEC(N'CREATE SCHEMA [emr];');
    CREATE TABLE [emr].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    IF SCHEMA_ID(N'emr') IS NULL EXEC(N'CREATE SCHEMA [emr];');
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[ControlledDrugs] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Strength] nvarchar(40) NOT NULL,
        [Form] nvarchar(30) NOT NULL,
        [Balance] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ControlledDrugs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Episodes] (
        [Id] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Stage] nvarchar(20) NOT NULL,
        [AdmittedAt] datetimeoffset NULL,
        [AdmittedBy] nvarchar(100) NULL,
        [SedationOrAnaesthesia] bit NOT NULL,
        [EscortName] nvarchar(100) NULL,
        [EscortPhone] nvarchar(40) NULL,
        [OvernightCarer] nvarchar(100) NULL,
        [EscortConfirmed] bit NOT NULL,
        [EscortOverrideReason] nvarchar(300) NULL,
        [DischargedAt] datetimeoffset NULL,
        [DischargedBy] nvarchar(100) NULL,
        [DischargeDestination] nvarchar(100) NULL,
        [FollowUpCallOn] date NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Episodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Assessments] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Score] int NOT NULL,
        [Risk] nvarchar(20) NOT NULL,
        [Actions] nvarchar(500) NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Assessments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Assessments_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[BloodUnits] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [UnitNumber] nvarchar(40) NOT NULL,
        [Product] nvarchar(60) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [ColdChainOk] bit NOT NULL,
        [CheckedBy1] nvarchar(100) NOT NULL,
        [CheckedBy2] nvarchar(100) NOT NULL,
        [StartedAt] datetimeoffset NULL,
        [StoppedAt] datetimeoffset NULL,
        [Reaction] bit NOT NULL,
        [ReactionDetails] nvarchar(500) NULL,
        [Returned] bit NOT NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_BloodUnits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BloodUnits_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[ControlledDrugRegister] (
        [Id] uniqueidentifier NOT NULL,
        [ControlledDrugId] uniqueidentifier NOT NULL,
        [Movement] nvarchar(20) NOT NULL,
        [Quantity] int NOT NULL,
        [BalanceAfter] int NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [EpisodeId] uniqueidentifier NULL,
        [Reference] nvarchar(100) NULL,
        [By] nvarchar(100) NOT NULL,
        [WitnessedBy] nvarchar(100) NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ControlledDrugRegister] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ControlledDrugRegister_ControlledDrugs_ControlledDrugId] FOREIGN KEY ([ControlledDrugId]) REFERENCES [emr].[ControlledDrugs] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ControlledDrugRegister_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Counts] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Stage] nvarchar(20) NOT NULL,
        [Item] nvarchar(40) NOT NULL,
        [Expected] int NOT NULL,
        [Counted] int NOT NULL,
        [CountedBy] nvarchar(100) NOT NULL,
        [CheckedBy] nvarchar(100) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Resolved] bit NOT NULL,
        [ResolutionNote] nvarchar(500) NULL,
        CONSTRAINT [PK_Counts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Counts_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[FluidBalance] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [IsInput] bit NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Millilitres] int NOT NULL,
        [By] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_FluidBalance] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FluidBalance_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[IntraopRecords] (
        [EpisodeId] uniqueidentifier NOT NULL,
        [InTheatreAt] datetimeoffset NULL,
        [AnaestheticStartAt] datetimeoffset NULL,
        [KnifeToSkinAt] datetimeoffset NULL,
        [ClosureAt] datetimeoffset NULL,
        [OutOfTheatreAt] datetimeoffset NULL,
        [Position] nvarchar(200) NULL,
        [PositioningAids] nvarchar(200) NULL,
        [SkinPrep] nvarchar(200) NULL,
        [DiathermyUnit] nvarchar(200) NULL,
        [DiathermyPadSite] nvarchar(200) NULL,
        [PadSiteSkinIntactAfter] bit NULL,
        [TourniquetSite] nvarchar(200) NULL,
        [TourniquetPressure] int NULL,
        [TourniquetOnAt] datetimeoffset NULL,
        [TourniquetOffAt] datetimeoffset NULL,
        [SmokeEvacuation] bit NOT NULL,
        [WoundClass] nvarchar(30) NULL,
        [Drains] nvarchar(200) NULL,
        [Dressings] nvarchar(200) NULL,
        [Notes] nvarchar(2000) NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IntraopRecords] PRIMARY KEY ([EpisodeId]),
        CONSTRAINT [FK_IntraopRecords_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[MedicationsGiven] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Medicine] nvarchar(150) NOT NULL,
        [Dose] nvarchar(60) NOT NULL,
        [Route] nvarchar(30) NOT NULL,
        [GivenAt] datetimeoffset NOT NULL,
        [GivenBy] nvarchar(100) NOT NULL,
        [WitnessedBy] nvarchar(100) NULL,
        [Barcode] nvarchar(60) NULL,
        [ControlledDrugId] uniqueidentifier NULL,
        [Notes] nvarchar(300) NULL,
        CONSTRAINT [PK_MedicationsGiven] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MedicationsGiven_ControlledDrugs_ControlledDrugId] FOREIGN KEY ([ControlledDrugId]) REFERENCES [emr].[ControlledDrugs] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MedicationsGiven_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Notes] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [Author] nvarchar(100) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Signed] bit NOT NULL,
        [AmendsNoteId] uniqueidentifier NULL,
        CONSTRAINT [PK_Notes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notes_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Notes_Notes_AmendsNoteId] FOREIGN KEY ([AmendsNoteId]) REFERENCES [emr].[Notes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Observations] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [At] datetimeoffset NOT NULL,
        [RespiratoryRate] int NULL,
        [SpO2] int NULL,
        [OnOxygen] bit NOT NULL,
        [OxygenLitres] decimal(4,1) NULL,
        [SystolicBp] int NULL,
        [DiastolicBp] int NULL,
        [HeartRate] int NULL,
        [TemperatureC] decimal(4,1) NULL,
        [Consciousness] nvarchar(1) NULL,
        [Pain] int NULL,
        [Sedation] int NULL,
        [Ews] int NULL,
        [Escalation] nvarchar(300) NULL,
        [Source] nvarchar(10) NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Observations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Observations_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[SafetyChecks] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Phase] nvarchar(10) NOT NULL,
        [CompletedAt] datetimeoffset NOT NULL,
        [LedBy] nvarchar(100) NOT NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_SafetyChecks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SafetyChecks_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[Specimens] (
        [Id] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Site] nvarchar(200) NOT NULL,
        [Test] nvarchar(60) NOT NULL,
        [Container] nvarchar(60) NULL,
        [CollectedAt] datetimeoffset NOT NULL,
        [CollectedBy] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [SentAt] datetimeoffset NULL,
        [SentBy] nvarchar(100) NULL,
        [Courier] nvarchar(100) NULL,
        [ReceivedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Specimens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Specimens_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [emr].[Episodes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[AssessmentAnswers] (
        [AssessmentId] uniqueidentifier NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [Value] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_AssessmentAnswers] PRIMARY KEY ([AssessmentId], [Key]),
        CONSTRAINT [FK_AssessmentAnswers_Assessments_AssessmentId] FOREIGN KEY ([AssessmentId]) REFERENCES [emr].[Assessments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[SafetyCheckItems] (
        [SafetyCheckId] uniqueidentifier NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [Confirmed] bit NOT NULL,
        CONSTRAINT [PK_SafetyCheckItems] PRIMARY KEY ([SafetyCheckId], [Key]),
        CONSTRAINT [FK_SafetyCheckItems_SafetyChecks_SafetyCheckId] FOREIGN KEY ([SafetyCheckId]) REFERENCES [emr].[SafetyChecks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE TABLE [emr].[SafetyCheckTeam] (
        [SafetyCheckId] uniqueidentifier NOT NULL,
        [Role] nvarchar(40) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_SafetyCheckTeam] PRIMARY KEY ([SafetyCheckId], [Role]),
        CONSTRAINT [FK_SafetyCheckTeam_SafetyChecks_SafetyCheckId] FOREIGN KEY ([SafetyCheckId]) REFERENCES [emr].[SafetyChecks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Assessments_EpisodeId_Kind] ON [emr].[Assessments] ([EpisodeId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_BloodUnits_EpisodeId] ON [emr].[BloodUnits] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_BloodUnits_UnitNumber] ON [emr].[BloodUnits] ([UnitNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_ControlledDrugRegister_ControlledDrugId_At] ON [emr].[ControlledDrugRegister] ([ControlledDrugId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_ControlledDrugRegister_EpisodeId] ON [emr].[ControlledDrugRegister] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_ControlledDrugRegister_PatientId] ON [emr].[ControlledDrugRegister] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ControlledDrugs_Name_Strength_Form] ON [emr].[ControlledDrugs] ([Name], [Strength], [Form]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Counts_EpisodeId_Stage] ON [emr].[Counts] ([EpisodeId], [Stage]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Episodes_BookingId] ON [emr].[Episodes] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Episodes_PatientId] ON [emr].[Episodes] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Episodes_Stage] ON [emr].[Episodes] ([Stage]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_FluidBalance_EpisodeId_At] ON [emr].[FluidBalance] ([EpisodeId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_MedicationsGiven_ControlledDrugId] ON [emr].[MedicationsGiven] ([ControlledDrugId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_MedicationsGiven_EpisodeId_GivenAt] ON [emr].[MedicationsGiven] ([EpisodeId], [GivenAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Notes_AmendsNoteId] ON [emr].[Notes] ([AmendsNoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Notes_EpisodeId_At] ON [emr].[Notes] ([EpisodeId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Observations_DeviceId] ON [emr].[Observations] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Observations_EpisodeId_At] ON [emr].[Observations] ([EpisodeId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SafetyChecks_EpisodeId_Phase] ON [emr].[SafetyChecks] ([EpisodeId], [Phase]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Specimens_EpisodeId_Number] ON [emr].[Specimens] ([EpisodeId], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE INDEX [IX_Specimens_Status] ON [emr].[Specimens] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Templates_Name] ON [emr].[Templates] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [emr].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113115251_InitialEmr'
)
BEGIN
    INSERT INTO [emr].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113115251_InitialEmr', N'10.0.14');
END;

COMMIT;
GO

