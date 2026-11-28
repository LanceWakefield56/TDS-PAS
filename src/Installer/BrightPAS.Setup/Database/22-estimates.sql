IF OBJECT_ID(N'[est].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'est') IS NULL EXEC(N'CREATE SCHEMA [est];');
    CREATE TABLE [est].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    IF SCHEMA_ID(N'est') IS NULL EXEC(N'CREATE SCHEMA [est];');
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Settings] (
        [Id] int NOT NULL,
        [GstRate] decimal(6,4) NOT NULL,
        [Loading] decimal(6,4) NOT NULL,
        [Markup] decimal(6,4) NOT NULL,
        [ExtendedTimePer15] decimal(12,2) NOT NULL,
        [ValidityMonths] int NOT NULL,
        [DepositPercent] decimal(5,2) NOT NULL,
        [DepositDaysBefore] int NOT NULL,
        [BalanceDaysAfter] int NOT NULL,
        [SurgeonFeeLabel] nvarchar(60) NOT NULL,
        [BankAccount] nvarchar(30) NOT NULL,
        [Phone] nvarchar(30) NOT NULL,
        [Email] nvarchar(200) NOT NULL,
        [Address] nvarchar(200) NOT NULL,
        [Terms] nvarchar(4000) NOT NULL,
        [SelfFundingText] nvarchar(1000) NOT NULL,
        [InsuranceText] nvarchar(1000) NOT NULL,
        [AcceptanceText] nvarchar(500) NOT NULL,
        [SendMode] nvarchar(12) NOT NULL,
        [HoldMinutes] int NOT NULL,
        [RequireIssued] bit NOT NULL,
        [PatientPassword] nvarchar(10) NOT NULL,
        [InsurerSameAsPatient] bit NOT NULL,
        [EmailSubject] nvarchar(200) NOT NULL,
        [EmailBody] nvarchar(2000) NOT NULL,
        [EmailSubjectInsurer] nvarchar(200) NOT NULL,
        [EmailBodyInsurer] nvarchar(2000) NOT NULL,
        [HintDobShort] nvarchar(300) NOT NULL,
        [HintDobLong] nvarchar(300) NOT NULL,
        [HintNhi] nvarchar(300) NOT NULL,
        [HintTyped] nvarchar(300) NOT NULL,
        [FromName] nvarchar(100) NULL,
        [BccAddress] nvarchar(200) NULL,
        [InsurerAddresses] nvarchar(2000) NOT NULL,
        [IncisiveFolder] nvarchar(260) NULL,
        [IncisiveFileName] nvarchar(100) NOT NULL,
        [ArchiveIssued] bit NOT NULL,
        [StaffCostPerMinute] decimal(8,2) NOT NULL,
        [OverheadPerCase] decimal(10,2) NOT NULL,
        [TemplatesSeeded] bit NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EstSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Specialties] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [DefaultNotes] nvarchar(4000) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Specialties] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[StockItems] (
        [Id] uniqueidentifier NOT NULL,
        [Supplier] nvarchar(150) NOT NULL,
        [Reference] nvarchar(100) NULL,
        [Name] nvarchar(300) NOT NULL,
        [PackPrice] decimal(12,4) NULL,
        [UnitsPerPack] decimal(12,2) NULL,
        [UnitPrice] decimal(12,4) NULL,
        [InStockQty] decimal(12,2) NULL,
        [NeededInHouse] decimal(12,2) NULL,
        [WhereUsed] nvarchar(300) NULL,
        [Issues] nvarchar(500) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_StockItems] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Surgeons] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [ClinicianId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Surgeons] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Procedures] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [SpecialtyId] uniqueidentifier NOT NULL,
        [Group] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [PrintTitle] nvarchar(300) NOT NULL,
        [Minutes] int NOT NULL,
        [DefaultAnaesthetic] nvarchar(24) NOT NULL,
        [Facility] decimal(12,2) NULL,
        [FacilitySpecial] decimal(12,2) NULL,
        [Anaesthetist] decimal(12,2) NULL,
        [AnaesthetistSpecial] decimal(12,2) NULL,
        [ConsumablesLine] decimal(12,2) NULL,
        [IncludedAddOnCodes] nvarchar(500) NULL,
        [EffectiveFrom] date NULL,
        [IsActive] bit NOT NULL,
        [Source] nvarchar(500) NULL,
        CONSTRAINT [PK_Procedures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Procedures_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [est].[Specialties] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[AddOns] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [SpecialtyId] uniqueidentifier NOT NULL,
        [Group] nvarchar(100) NULL,
        [Name] nvarchar(200) NOT NULL,
        [Fee] decimal(12,2) NOT NULL,
        [Type] nvarchar(10) NOT NULL,
        [SurgeonId] uniqueidentifier NULL,
        [Source] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_AddOns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AddOns_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [est].[Specialties] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AddOns_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[SurgeonSpecialties] (
        [SurgeonId] uniqueidentifier NOT NULL,
        [SpecialtyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SurgeonSpecialties] PRIMARY KEY ([SurgeonId], [SpecialtyId]),
        CONSTRAINT [FK_SurgeonSpecialties_Specialties_SpecialtyId] FOREIGN KEY ([SpecialtyId]) REFERENCES [est].[Specialties] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SurgeonSpecialties_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Estimates] (
        [Id] uniqueidentifier NOT NULL,
        [Ref] nvarchar(40) NOT NULL,
        [EstimateDate] date NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [DateOfBirth] date NULL,
        [Nhi] nvarchar(10) NULL,
        [Phone] nvarchar(50) NULL,
        [Email] nvarchar(200) NULL,
        [Address] nvarchar(200) NULL,
        [Town] nvarchar(100) NULL,
        [Postcode] nvarchar(10) NULL,
        [Funding] nvarchar(12) NOT NULL,
        [Insurer] nvarchar(100) NULL,
        [PolicyNumber] nvarchar(100) NULL,
        [PriorApproval] nvarchar(100) NULL,
        [ProcedureId] uniqueidentifier NULL,
        [ProcedureCode] nvarchar(20) NULL,
        [SpecialtyName] nvarchar(100) NULL,
        [ProcedureName] nvarchar(200) NULL,
        [Title] nvarchar(400) NULL,
        [SurgeonId] uniqueidentifier NULL,
        [SurgeonName] nvarchar(150) NULL,
        [ProcedureDate] date NULL,
        [Anaesthetic] nvarchar(24) NOT NULL,
        [PlannedMinutes] int NULL,
        [UseSpecial] bit NOT NULL,
        [Multiplier] decimal(5,2) NOT NULL,
        [FacilityOverride] decimal(12,2) NULL,
        [SurgeonOverride] decimal(12,2) NULL,
        [AnaesthetistOverride] decimal(12,2) NULL,
        [InputsJson] nvarchar(max) NULL,
        [Notes] nvarchar(4000) NULL,
        [PreparedBy] nvarchar(100) NULL,
        [Subtotal] decimal(12,2) NOT NULL,
        [Deposit] decimal(12,2) NOT NULL,
        [Balance] decimal(12,2) NOT NULL,
        [ValidUntil] date NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [IssuedAt] datetimeoffset NULL,
        [EmailedAt] datetimeoffset NULL,
        [SentToIncisiveAt] datetimeoffset NULL,
        [CancelReason] nvarchar(300) NULL,
        CONSTRAINT [PK_Estimates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Estimates_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [est].[Procedures] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Estimates_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[PendingPrices] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [Field] nvarchar(30) NOT NULL,
        [NewValue] decimal(12,2) NULL,
        [EffectiveFrom] date NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [AppliedAt] datetimeoffset NULL,
        CONSTRAINT [PK_PendingPrices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PendingPrices_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [est].[Procedures] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[ProcedureConsumables] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [StockItemId] uniqueidentifier NULL,
        [Description] nvarchar(300) NOT NULL,
        [Quantity] decimal(12,2) NOT NULL,
        [UnitCost] decimal(12,4) NULL,
        CONSTRAINT [PK_ProcedureConsumables] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcedureConsumables_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [est].[Procedures] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProcedureConsumables_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [est].[StockItems] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[SurgeonFees] (
        [Id] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [SurgeonId] uniqueidentifier NULL,
        [Fee] decimal(12,2) NULL,
        [SpecialFee] decimal(12,2) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_SurgeonFees] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SurgeonFees_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [est].[Procedures] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SurgeonFees_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [ProcedureId] uniqueidentifier NOT NULL,
        [SurgeonId] uniqueidentifier NULL,
        [Anaesthetic] nvarchar(24) NULL,
        [PlannedMinutes] int NULL,
        [UseSpecial] bit NOT NULL,
        [FacilityOverride] decimal(12,2) NULL,
        [SurgeonOverride] decimal(12,2) NULL,
        [AnaesthetistOverride] decimal(12,2) NULL,
        [InputsJson] nvarchar(max) NULL,
        [Notes] nvarchar(4000) NULL,
        [Visible] bit NOT NULL,
        [Favourite] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [UsedCount] int NOT NULL,
        [LastUsedAt] datetimeoffset NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Templates_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [est].[Procedures] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Templates_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[ArchivedPdfs] (
        [Id] uniqueidentifier NOT NULL,
        [EstimateId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [Reason] nvarchar(100) NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [PdfCipher] varbinary(max) NOT NULL,
        CONSTRAINT [PK_ArchivedPdfs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ArchivedPdfs_Estimates_EstimateId] FOREIGN KEY ([EstimateId]) REFERENCES [est].[Estimates] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[EstimateLines] (
        [Id] uniqueidentifier NOT NULL,
        [EstimateId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Sequence] int NOT NULL,
        [Label] nvarchar(300) NOT NULL,
        [Amount] decimal(12,2) NULL,
        [DisplayText] nvarchar(200) NULL,
        [Note] nvarchar(300) NULL,
        CONSTRAINT [PK_EstimateLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EstimateLines_Estimates_EstimateId] FOREIGN KEY ([EstimateId]) REFERENCES [est].[Estimates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[Outbox] (
        [Id] uniqueidentifier NOT NULL,
        [EstimateId] uniqueidentifier NOT NULL,
        [EstimateRef] nvarchar(40) NOT NULL,
        [PatientName] nvarchar(200) NOT NULL,
        [Recipient] nvarchar(10) NOT NULL,
        [ToAddress] nvarchar(200) NOT NULL,
        [Subject] nvarchar(300) NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [AttachmentName] nvarchar(200) NOT NULL,
        [AttachmentCipher] varbinary(max) NOT NULL,
        [PasswordProtected] bit NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [SendAfter] datetimeoffset NOT NULL,
        [SentAt] datetimeoffset NULL,
        [StoppedAt] datetimeoffset NULL,
        [StoppedBy] nvarchar(150) NULL,
        [Error] nvarchar(1000) NULL,
        [Attempts] int NOT NULL,
        CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Outbox_Estimates_EstimateId] FOREIGN KEY ([EstimateId]) REFERENCES [est].[Estimates] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE TABLE [est].[TemplateSurgeons] (
        [TemplateId] uniqueidentifier NOT NULL,
        [SurgeonId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_TemplateSurgeons] PRIMARY KEY ([TemplateId], [SurgeonId]),
        CONSTRAINT [FK_TemplateSurgeons_Surgeons_SurgeonId] FOREIGN KEY ([SurgeonId]) REFERENCES [est].[Surgeons] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TemplateSurgeons_Templates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [est].[Templates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AcceptanceText', N'Address', N'ArchiveIssued', N'BalanceDaysAfter', N'BankAccount', N'BccAddress', N'DepositDaysBefore', N'DepositPercent', N'Email', N'EmailBody', N'EmailBodyInsurer', N'EmailSubject', N'EmailSubjectInsurer', N'ExtendedTimePer15', N'FromName', N'GstRate', N'HintDobLong', N'HintDobShort', N'HintNhi', N'HintTyped', N'HoldMinutes', N'IncisiveFileName', N'IncisiveFolder', N'InsuranceText', N'InsurerAddresses', N'InsurerSameAsPatient', N'Loading', N'Markup', N'OverheadPerCase', N'PatientPassword', N'Phone', N'RequireIssued', N'SelfFundingText', N'SendMode', N'StaffCostPerMinute', N'SurgeonFeeLabel', N'TemplatesSeeded', N'Terms', N'ValidityMonths') AND [object_id] = OBJECT_ID(N'[est].[Settings]'))
        SET IDENTITY_INSERT [est].[Settings] ON;
    EXEC(N'INSERT INTO [est].[Settings] ([Id], [AcceptanceText], [Address], [ArchiveIssued], [BalanceDaysAfter], [BankAccount], [BccAddress], [DepositDaysBefore], [DepositPercent], [Email], [EmailBody], [EmailBodyInsurer], [EmailSubject], [EmailSubjectInsurer], [ExtendedTimePer15], [FromName], [GstRate], [HintDobLong], [HintDobShort], [HintNhi], [HintTyped], [HoldMinutes], [IncisiveFileName], [IncisiveFolder], [InsuranceText], [InsurerAddresses], [InsurerSameAsPatient], [Loading], [Markup], [OverheadPerCase], [PatientPassword], [Phone], [RequireIssued], [SelfFundingText], [SendMode], [StaffCostPerMinute], [SurgeonFeeLabel], [TemplatesSeeded], [Terms], [ValidityMonths])
    VALUES (1, N''Email acceptance of this estimate is required prior to confirmation of your booking.'', N''341 Lower Queen Street, Richmond, Tasman 7020'', CAST(1 AS bit), 14, N''03-0703-0046726-000'', NULL, 14, 50.0, N''enquiries@tasmandaysurgery.co.nz'', CONCAT(CAST(N''Dear {first},'' AS nvarchar(max)), nchar(10), nchar(10), N''Please find attached your estimate for {procedure}.'', nchar(10), nchar(10), N''If you are self-funding, please reply to this email to accept the estimate. If you are insured, please forward it to your insurer and send us their approval.'', nchar(10), nchar(10), N''Kind regards'', nchar(10), N''{preparedby}'', nchar(10), N''Tasman Day Surgery'', nchar(10), N''{phone}''), CONCAT(CAST(N''Hello,'' AS nvarchar(max)), nchar(10), nchar(10), N''Please find attached our estimate for {first} {last} (policy {policy}) for {procedure}, for prior approval.'', nchar(10), nchar(10), N''Kind regards'', nchar(10), N''{preparedby}'', nchar(10), N''Tasman Day Surgery'', nchar(10), N''{phone}''), N''Your estimate from Tasman Day Surgery – {ref}'', N''Estimate for {first} {last} – {ref} – {insurer} {policy}'', 90.0, N''Tasman Day Surgery'', 0.15, N''This estimate is password protected. The password is your date of birth as 8 digits, DDMMYYYY (for example 14 March 1965 = 14031965).'', N''This estimate is password protected. The password is your date of birth as 6 digits, DDMMYY (for example 14 March 1965 = 140365).'', N''This estimate is password protected. The password is your NHI number in capital letters (it is on your hospital letters and prescriptions).'', N''This estimate is password protected. We will give you the password separately by phone or text message.'', 10, N''{NHI}_{LAST}_{FIRST}_Estimate_{DATE}'', NULL, N''If you are funding via insurance, please forward this estimate to your provider, and email back their confirmed acceptance of the estimate. If your policy includes a shortfall or excess, the outstanding amount must be paid in full prior to the procedure.'', CONCAT(CAST(N''Southern Cross = '' AS nvarchar(max)), nchar(10), N''nib = '', nchar(10), N''AIA = '', nchar(10), N''Partners Life = '', nchar(10), N''Accuro = '', nchar(10), N''UniMed = ''), CAST(0 AS bit), 0.15, 0.25, 350.0, N''DobShort'', N''0800 827 329'', CAST(1 AS bit), N''If you are self-funding, please indicate that you accept the estimate by return email. A payment of {deposit}% of the estimate is required at least two weeks prior to the procedure and the balance must be paid within {balancedays} days of the surgery.'', N''OutlookDraft'', 4.45, N''Consultant Fee'', CAST(0 AS bit), CONCAT(CAST(N''All estimates are subject to change without prior notice. This estimate is valid for a total of {validity} months (until {validuntil}).'' AS nvarchar(max)), nchar(10), N''While it is likely to be the total cost, actual fees may vary depending on the length of procedure and/or length of stay, whether special equipment or materials are used, pre-existing conditions or in certain unusual circumstances. This may include an anaesthetic safety pre-assessment if required.'', nchar(10), N''These fees do not include the initial diagnostic consultation or visits more than three months after surgery. Estimates for the facility and anaesthetist are approximate only and if there are any concerns, they can be clarified with Tasman Day Surgery {phone}.'', nchar(10), N''Occasionally unforeseen circumstances may result in unexpected costs not outlined in this estimate. Examples of this include but are not limited to costs related to emergencies such as cardiac arrest, an unexpected allergic reaction during your stay or an unexpected finding during your procedure.''), 3)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AcceptanceText', N'Address', N'ArchiveIssued', N'BalanceDaysAfter', N'BankAccount', N'BccAddress', N'DepositDaysBefore', N'DepositPercent', N'Email', N'EmailBody', N'EmailBodyInsurer', N'EmailSubject', N'EmailSubjectInsurer', N'ExtendedTimePer15', N'FromName', N'GstRate', N'HintDobLong', N'HintDobShort', N'HintNhi', N'HintTyped', N'HoldMinutes', N'IncisiveFileName', N'IncisiveFolder', N'InsuranceText', N'InsurerAddresses', N'InsurerSameAsPatient', N'Loading', N'Markup', N'OverheadPerCase', N'PatientPassword', N'Phone', N'RequireIssued', N'SelfFundingText', N'SendMode', N'StaffCostPerMinute', N'SurgeonFeeLabel', N'TemplatesSeeded', N'Terms', N'ValidityMonths') AND [object_id] = OBJECT_ID(N'[est].[Settings]'))
        SET IDENTITY_INSERT [est].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AddOns_Code] ON [est].[AddOns] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_AddOns_SpecialtyId] ON [est].[AddOns] ([SpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_AddOns_SurgeonId] ON [est].[AddOns] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_ArchivedPdfs_EstimateId] ON [est].[ArchivedPdfs] ([EstimateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EstimateLines_EstimateId_Sequence] ON [est].[EstimateLines] ([EstimateId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_Email] ON [est].[Estimates] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_EstimateDate] ON [est].[Estimates] ([EstimateDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_LastName_FirstName] ON [est].[Estimates] ([LastName], [FirstName]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_Nhi] ON [est].[Estimates] ([Nhi]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_PatientId] ON [est].[Estimates] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_ProcedureId] ON [est].[Estimates] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Estimates_Ref] ON [est].[Estimates] ([Ref]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Estimates_SurgeonId] ON [est].[Estimates] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Outbox_EstimateId] ON [est].[Outbox] ([EstimateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Outbox_Status_SendAfter] ON [est].[Outbox] ([Status], [SendAfter]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_PendingPrices_AppliedAt_EffectiveFrom] ON [est].[PendingPrices] ([AppliedAt], [EffectiveFrom]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_PendingPrices_ProcedureId] ON [est].[PendingPrices] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_ProcedureConsumables_ProcedureId] ON [est].[ProcedureConsumables] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_ProcedureConsumables_StockItemId] ON [est].[ProcedureConsumables] ([StockItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Procedures_Code] ON [est].[Procedures] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Procedures_SpecialtyId_Group] ON [est].[Procedures] ([SpecialtyId], [Group]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Specialties_Name] ON [est].[Specialties] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_StockItems_Supplier_Name] ON [est].[StockItems] ([Supplier], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SurgeonFees_ProcedureId_SurgeonId] ON [est].[SurgeonFees] ([ProcedureId], [SurgeonId]) WHERE [SurgeonId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_SurgeonFees_SurgeonId] ON [est].[SurgeonFees] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_SurgeonFees_AllSurgeons] ON [est].[SurgeonFees] ([ProcedureId]) WHERE [SurgeonId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Surgeons_ClinicianId] ON [est].[Surgeons] ([ClinicianId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Surgeons_Name] ON [est].[Surgeons] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_SurgeonSpecialties_SpecialtyId] ON [est].[SurgeonSpecialties] ([SpecialtyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Templates_ProcedureId] ON [est].[Templates] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_Templates_SurgeonId] ON [est].[Templates] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    CREATE INDEX [IX_TemplateSurgeons_SurgeonId] ON [est].[TemplateSurgeons] ([SurgeonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123002041_InitialEstimates'
)
BEGIN
    INSERT INTO [est].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123002041_InitialEstimates', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Settings] ADD [ExpiryWarningDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Settings] ADD [FollowUpDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [BookingId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [DeclineReason] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [DepositPaid] decimal(12,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [DepositPaidOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [FollowUp] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [FollowUpAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [FollowUpBy] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [InvoiceId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [InvoiceNumber] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    ALTER TABLE [est].[Estimates] ADD [NextActionOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    CREATE TABLE [est].[EstimateEvents] (
        [Id] uniqueidentifier NOT NULL,
        [EstimateId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [By] nvarchar(100) NOT NULL,
        [Note] nvarchar(500) NULL,
        CONSTRAINT [PK_EstimateEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EstimateEvents_Estimates_EstimateId] FOREIGN KEY ([EstimateId]) REFERENCES [est].[Estimates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    EXEC(N'UPDATE [est].[Settings] SET [ExpiryWarningDays] = 14, [FollowUpDays] = 7
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    CREATE INDEX [IX_Estimates_BookingId] ON [est].[Estimates] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    CREATE INDEX [IX_Estimates_FollowUp_ValidUntil] ON [est].[Estimates] ([FollowUp], [ValidUntil]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    CREATE INDEX [IX_Estimates_InvoiceId] ON [est].[Estimates] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    CREATE INDEX [IX_EstimateEvents_EstimateId_At] ON [est].[EstimateEvents] ([EstimateId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [est].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128064807_EstimateFollowUp'
)
BEGIN
    INSERT INTO [est].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128064807_EstimateFollowUp', N'10.0.14');
END;

COMMIT;
GO

