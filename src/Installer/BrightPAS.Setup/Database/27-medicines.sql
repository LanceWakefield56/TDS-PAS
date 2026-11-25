IF OBJECT_ID(N'[meds].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'meds') IS NULL EXEC(N'CREATE SCHEMA [meds];');
    CREATE TABLE [meds].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    IF SCHEMA_ID(N'meds') IS NULL EXEC(N'CREATE SCHEMA [meds];');
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[AllergyLinks] (
        [AlertId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Term] nvarchar(160) NOT NULL,
        [LinkedBy] nvarchar(100) NOT NULL,
        [LinkedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AllergyLinks] PRIMARY KEY ([AlertId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[ImportProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        [NameColumn] nvarchar(100) NOT NULL,
        [NzmtColumn] nvarchar(100) NULL,
        [FormColumn] nvarchar(100) NULL,
        [RouteColumn] nvarchar(100) NULL,
        [IngredientColumn] nvarchar(100) NULL,
        [AmountColumn] nvarchar(100) NULL,
        [UnitColumn] nvarchar(100) NULL,
        [PerMlColumn] nvarchar(100) NULL,
        [GtinColumn] nvarchar(100) NULL,
        [DeactivateMissing] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_ImportProfiles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[ImportRuns] (
        [Id] uniqueidentifier NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Source] nvarchar(10) NOT NULL,
        [Rows] int NOT NULL,
        [Added] int NOT NULL,
        [Updated] int NOT NULL,
        [Deactivated] int NOT NULL,
        [Skipped] int NOT NULL,
        [Problems] nvarchar(4000) NULL,
        [ImportedAt] datetimeoffset NOT NULL,
        [ImportedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_ImportRuns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Ingredients] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [NzmtId] nvarchar(20) NULL,
        CONSTRAINT [PK_Ingredients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Medicines] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [NzmtId] nvarchar(20) NULL,
        [Form] nvarchar(60) NULL,
        [DefaultRoute] nvarchar(30) NULL,
        [Source] nvarchar(10) NOT NULL,
        [OnFormulary] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Medicines] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[NzfReleases] (
        [Id] uniqueidentifier NOT NULL,
        [Edition] nvarchar(6) NOT NULL,
        [ReleaseNumber] int NULL,
        [ReleaseDate] date NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [Monographs] int NOT NULL,
        [Doses] int NOT NULL,
        [IsCurrent] bit NOT NULL,
        [Problems] nvarchar(2000) NULL,
        [ImportedAt] datetimeoffset NOT NULL,
        [ImportedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_NzfReleases] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[NzfSettings] (
        [Id] int NOT NULL,
        [NzfUrl] nvarchar(500) NULL,
        [NzfcUrl] nvarchar(500) NULL,
        [AutoDownload] bit NOT NULL,
        [WatchFolder] nvarchar(500) NULL,
        [LastCheckedAt] datetimeoffset NULL,
        [LastProblem] nvarchar(1000) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_NzfSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_NzfSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[RuleSets] (
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
        CONSTRAINT [PK_RuleSets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Settings] (
        [Id] int NOT NULL,
        [Mode] nvarchar(8) NOT NULL,
        [CheckRegularMedicines] bit NOT NULL,
        [DuplicateWithinMinutes] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MedSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[IngredientAliases] (
        [Id] uniqueidentifier NOT NULL,
        [IngredientId] uniqueidentifier NOT NULL,
        [Alias] nvarchar(150) NOT NULL,
        CONSTRAINT [PK_IngredientAliases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IngredientAliases_Ingredients_IngredientId] FOREIGN KEY ([IngredientId]) REFERENCES [meds].[Ingredients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Checks] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NULL,
        [MedicationId] uniqueidentifier NULL,
        [MedicineId] uniqueidentifier NULL,
        [MedicineText] nvarchar(150) NOT NULL,
        [Dose] nvarchar(60) NOT NULL,
        [Route] nvarchar(30) NOT NULL,
        [Result] nvarchar(12) NOT NULL,
        [OverrideReason] nvarchar(500) NULL,
        [RuleSetVersion] int NULL,
        [Mode] nvarchar(8) NOT NULL,
        [CheckedAt] datetimeoffset NOT NULL,
        [CheckedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Checks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Checks_Medicines_MedicineId] FOREIGN KEY ([MedicineId]) REFERENCES [meds].[Medicines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[MedicineGtins] (
        [Gtin] nvarchar(14) NOT NULL,
        [MedicineId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MedicineGtins] PRIMARY KEY ([Gtin]),
        CONSTRAINT [FK_MedicineGtins_Medicines_MedicineId] FOREIGN KEY ([MedicineId]) REFERENCES [meds].[Medicines] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[MedicineIngredients] (
        [MedicineId] uniqueidentifier NOT NULL,
        [IngredientId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,4) NULL,
        [Unit] nvarchar(12) NULL,
        [PerMl] decimal(10,3) NULL,
        CONSTRAINT [PK_MedicineIngredients] PRIMARY KEY ([MedicineId], [IngredientId]),
        CONSTRAINT [FK_MedicineIngredients_Ingredients_IngredientId] FOREIGN KEY ([IngredientId]) REFERENCES [meds].[Ingredients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MedicineIngredients_Medicines_MedicineId] FOREIGN KEY ([MedicineId]) REFERENCES [meds].[Medicines] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[NzfMonographs] (
        [Id] uniqueidentifier NOT NULL,
        [ReleaseId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [BaseName] nvarchar(200) NOT NULL,
        [Synonyms] nvarchar(300) NULL,
        [Chapter] nvarchar(200) NULL,
        [Indications] nvarchar(max) NULL,
        [ContraIndications] nvarchar(max) NULL,
        [Cautions] nvarchar(max) NULL,
        [HepaticImpairment] nvarchar(max) NULL,
        [RenalImpairment] nvarchar(max) NULL,
        [Pregnancy] nvarchar(max) NULL,
        [BreastFeeding] nvarchar(max) NULL,
        [AdverseEffects] nvarchar(max) NULL,
        [PatientAdvice] nvarchar(max) NULL,
        [OtherSections] nvarchar(max) NULL,
        CONSTRAINT [PK_NzfMonographs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NzfMonographs_NzfReleases_ReleaseId] FOREIGN KEY ([ReleaseId]) REFERENCES [meds].[NzfReleases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[CrossReactions] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [FromGroup] nvarchar(20) NOT NULL,
        [ToGroup] nvarchar(20) NOT NULL,
        [Severity] nvarchar(6) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_CrossReactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CrossReactions_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [meds].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[DoseRules] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Ingredient] nvarchar(150) NOT NULL,
        [Route] nvarchar(30) NULL,
        [MinAgeYears] int NULL,
        [MaxAgeYears] int NULL,
        [MinWeightKg] decimal(6,2) NULL,
        [MaxWeightKg] decimal(6,2) NULL,
        [Unit] nvarchar(12) NOT NULL,
        [MaxSingle] decimal(12,4) NULL,
        [MaxDaily] decimal(12,4) NULL,
        [MaxSinglePerKg] decimal(10,4) NULL,
        [MaxDailyPerKg] decimal(10,4) NULL,
        [MinIntervalHours] decimal(6,2) NULL,
        [Severity] nvarchar(6) NOT NULL,
        [Reference] nvarchar(300) NULL,
        [Note] nvarchar(500) NULL,
        CONSTRAINT [PK_DoseRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoseRules_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [meds].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Groups] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [AllergyClass] bit NOT NULL,
        [SameGroupSeverity] nvarchar(6) NOT NULL,
        CONSTRAINT [PK_Groups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Groups_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [meds].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[Interactions] (
        [Id] uniqueidentifier NOT NULL,
        [RuleSetId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [TermA] nvarchar(150) NOT NULL,
        [TermB] nvarchar(150) NOT NULL,
        [Severity] nvarchar(6) NOT NULL,
        [Effect] nvarchar(300) NOT NULL,
        [Advice] nvarchar(500) NULL,
        [Reference] nvarchar(300) NULL,
        CONSTRAINT [PK_Interactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Interactions_RuleSets_RuleSetId] FOREIGN KEY ([RuleSetId]) REFERENCES [meds].[RuleSets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[CheckFindings] (
        [CheckId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Kind] nvarchar(12) NOT NULL,
        [Severity] nvarchar(6) NOT NULL,
        [Text] nvarchar(500) NOT NULL,
        [RuleRef] nvarchar(60) NULL,
        CONSTRAINT [PK_CheckFindings] PRIMARY KEY ([CheckId], [Sequence]),
        CONSTRAINT [FK_CheckFindings_Checks_CheckId] FOREIGN KEY ([CheckId]) REFERENCES [meds].[Checks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[NzfDoses] (
        [MonographId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Indication] nvarchar(300) NOT NULL,
        [Route] nvarchar(150) NOT NULL,
        [RouteCodes] nvarchar(60) NOT NULL,
        [Population] nvarchar(200) NOT NULL,
        [Dose] nvarchar(1000) NOT NULL,
        [MinAgeYears] int NULL,
        [MaxAgeYears] int NULL,
        [MinWeightKg] decimal(6,2) NULL,
        [MaxWeightKg] decimal(6,2) NULL,
        [Neonate] bit NOT NULL,
        [Unit] nvarchar(12) NULL,
        [MaxSingle] decimal(12,4) NULL,
        [MaxSinglePerKg] decimal(10,4) NULL,
        [MaxDaily] decimal(12,4) NULL,
        [MaxDailyPerKg] decimal(10,4) NULL,
        [IntervalHours] decimal(6,2) NULL,
        [Titration] bit NOT NULL,
        CONSTRAINT [PK_NzfDoses] PRIMARY KEY ([MonographId], [Sequence]),
        CONSTRAINT [FK_NzfDoses_NzfMonographs_MonographId] FOREIGN KEY ([MonographId]) REFERENCES [meds].[NzfMonographs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[NzfPreparations] (
        [MonographId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Form] nvarchar(150) NOT NULL,
        [Strength] nvarchar(150) NOT NULL,
        [Subsidy] nvarchar(150) NULL,
        CONSTRAINT [PK_NzfPreparations] PRIMARY KEY ([MonographId], [Sequence]),
        CONSTRAINT [FK_NzfPreparations_NzfMonographs_MonographId] FOREIGN KEY ([MonographId]) REFERENCES [meds].[NzfMonographs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE TABLE [meds].[GroupMembers] (
        [GroupId] uniqueidentifier NOT NULL,
        [Term] nvarchar(150) NOT NULL,
        [IsKeyword] bit NOT NULL,
        CONSTRAINT [PK_GroupMembers] PRIMARY KEY ([GroupId], [Term]),
        CONSTRAINT [FK_GroupMembers_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [meds].[Groups] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AutoDownload', N'LastCheckedAt', N'LastProblem', N'NzfUrl', N'NzfcUrl', N'UpdatedAt', N'UpdatedBy', N'WatchFolder') AND [object_id] = OBJECT_ID(N'[meds].[NzfSettings]'))
        SET IDENTITY_INSERT [meds].[NzfSettings] ON;
    EXEC(N'INSERT INTO [meds].[NzfSettings] ([Id], [AutoDownload], [LastCheckedAt], [LastProblem], [NzfUrl], [NzfcUrl], [UpdatedAt], [UpdatedBy], [WatchFolder])
    VALUES (1, CAST(0 AS bit), NULL, NULL, N''https://www.nzf.org.nz/home/downloadpdf'', NULL, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AutoDownload', N'LastCheckedAt', N'LastProblem', N'NzfUrl', N'NzfcUrl', N'UpdatedAt', N'UpdatedBy', N'WatchFolder') AND [object_id] = OBJECT_ID(N'[meds].[NzfSettings]'))
        SET IDENTITY_INSERT [meds].[NzfSettings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CheckRegularMedicines', N'DuplicateWithinMinutes', N'Mode', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[meds].[Settings]'))
        SET IDENTITY_INSERT [meds].[Settings] ON;
    EXEC(N'INSERT INTO [meds].[Settings] ([Id], [CheckRegularMedicines], [DuplicateWithinMinutes], [Mode], [UpdatedAt], [UpdatedBy])
    VALUES (1, CAST(1 AS bit), 60, N''Advise'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CheckRegularMedicines', N'DuplicateWithinMinutes', N'Mode', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[meds].[Settings]'))
        SET IDENTITY_INSERT [meds].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_AllergyLinks_PatientId] ON [meds].[AllergyLinks] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Checks_EpisodeId] ON [meds].[Checks] ([EpisodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Checks_MedicationId] ON [meds].[Checks] ([MedicationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Checks_MedicineId] ON [meds].[Checks] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Checks_PatientId_CheckedAt] ON [meds].[Checks] ([PatientId], [CheckedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Checks_Result_CheckedAt] ON [meds].[Checks] ([Result], [CheckedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CrossReactions_RuleSetId_FromGroup_ToGroup] ON [meds].[CrossReactions] ([RuleSetId], [FromGroup], [ToGroup]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DoseRules_RuleSetId_Code] ON [meds].[DoseRules] ([RuleSetId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Groups_RuleSetId_Code] ON [meds].[Groups] ([RuleSetId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ImportProfiles_Name] ON [meds].[ImportProfiles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_ImportRuns_ImportedAt] ON [meds].[ImportRuns] ([ImportedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IngredientAliases_Alias] ON [meds].[IngredientAliases] ([Alias]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_IngredientAliases_IngredientId] ON [meds].[IngredientAliases] ([IngredientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Ingredients_Name] ON [meds].[Ingredients] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Ingredients_NzmtId] ON [meds].[Ingredients] ([NzmtId]) WHERE [NzmtId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Interactions_RuleSetId_Code] ON [meds].[Interactions] ([RuleSetId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_MedicineGtins_MedicineId] ON [meds].[MedicineGtins] ([MedicineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_MedicineIngredients_IngredientId] ON [meds].[MedicineIngredients] ([IngredientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Medicines_Name] ON [meds].[Medicines] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Medicines_NzmtId] ON [meds].[Medicines] ([NzmtId]) WHERE [NzmtId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_Medicines_OnFormulary_IsActive] ON [meds].[Medicines] ([OnFormulary], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_NzfMonographs_ReleaseId_BaseName] ON [meds].[NzfMonographs] ([ReleaseId], [BaseName]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_NzfMonographs_ReleaseId_Name] ON [meds].[NzfMonographs] ([ReleaseId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE INDEX [IX_NzfReleases_ImportedAt] ON [meds].[NzfReleases] ([ImportedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_NzfReleases_OneCurrent] ON [meds].[NzfReleases] ([Edition]) WHERE [IsCurrent] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RuleSets_Version] ON [meds].[RuleSets] ([Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_MedRuleSets_OneApproved] ON [meds].[RuleSets] ([Status]) WHERE [Status] = ''Approved''');
END;

IF NOT EXISTS (
    SELECT * FROM [meds].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063029_InitialMedicines'
)
BEGIN
    INSERT INTO [meds].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125063029_InitialMedicines', N'10.0.14');
END;

COMMIT;
GO

