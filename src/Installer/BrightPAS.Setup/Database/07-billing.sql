IF OBJECT_ID(N'[billing].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'billing') IS NULL EXEC(N'CREATE SCHEMA [billing];');
    CREATE TABLE [billing].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    IF SCHEMA_ID(N'billing') IS NULL EXEC(N'CREATE SCHEMA [billing];');
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE TABLE [billing].[Documents] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Number] nvarchar(20) NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [SourceEstimateId] uniqueidentifier NULL,
        [Payer] nvarchar(12) NOT NULL,
        [PayerName] nvarchar(100) NULL,
        [PayerReference] nvarchar(50) NULL,
        [Surgeon] nvarchar(150) NULL,
        [Anaesthetist] nvarchar(150) NULL,
        [Status] nvarchar(10) NOT NULL,
        [GstPercent] decimal(5,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [IssuedOn] date NULL,
        [DueOn] date NULL,
        [Notes] nvarchar(1000) NULL,
        [VoidReason] nvarchar(300) NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE TABLE [billing].[Fees] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [GstApplicable] bit NOT NULL,
        [Split] bit NOT NULL,
        [SurgeonPercent] decimal(5,2) NULL,
        [AnaesthetistPercent] decimal(5,2) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Fees] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE TABLE [billing].[Settings] (
        [Id] int NOT NULL,
        [SurgeonPercent] decimal(5,2) NOT NULL,
        [AnaesthetistPercent] decimal(5,2) NOT NULL,
        [GstPercent] decimal(5,2) NOT NULL,
        [EstimateValidDays] int NOT NULL,
        [PaymentTermsDays] int NOT NULL,
        [GstNumber] nvarchar(20) NULL,
        [BankAccount] nvarchar(30) NULL,
        [NextEstimateNumber] int NOT NULL,
        [NextInvoiceNumber] int NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Settings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE TABLE [billing].[Lines] (
        [Id] uniqueidentifier NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Quantity] decimal(9,2) NOT NULL,
        [UnitPrice] decimal(12,2) NOT NULL,
        [GstApplicable] bit NOT NULL,
        [SurgeonPercent] decimal(5,2) NOT NULL,
        [AnaesthetistPercent] decimal(5,2) NOT NULL,
        CONSTRAINT [PK_Lines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Lines_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [billing].[Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE TABLE [billing].[Payments] (
        [Id] uniqueidentifier NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [PaidOn] date NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Method] nvarchar(15) NOT NULL,
        [Reference] nvarchar(50) NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [billing].[Documents] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AnaesthetistPercent', N'BankAccount', N'EstimateValidDays', N'GstNumber', N'GstPercent', N'NextEstimateNumber', N'NextInvoiceNumber', N'PaymentTermsDays', N'SurgeonPercent') AND [object_id] = OBJECT_ID(N'[billing].[Settings]'))
        SET IDENTITY_INSERT [billing].[Settings] ON;
    EXEC(N'INSERT INTO [billing].[Settings] ([Id], [AnaesthetistPercent], [BankAccount], [EstimateValidDays], [GstNumber], [GstPercent], [NextEstimateNumber], [NextInvoiceNumber], [PaymentTermsDays], [SurgeonPercent])
    VALUES (1, 20.0, NULL, 30, NULL, 15.0, 1, 1, 20, 40.0)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AnaesthetistPercent', N'BankAccount', N'EstimateValidDays', N'GstNumber', N'GstPercent', N'NextEstimateNumber', N'NextInvoiceNumber', N'PaymentTermsDays', N'SurgeonPercent') AND [object_id] = OBJECT_ID(N'[billing].[Settings]'))
        SET IDENTITY_INSERT [billing].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Documents_Number] ON [billing].[Documents] ([Number]) WHERE [Number] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE INDEX [IX_Documents_PatientId] ON [billing].[Documents] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Fees_Code] ON [billing].[Fees] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE INDEX [IX_Lines_DocumentId] ON [billing].[Lines] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    CREATE INDEX [IX_Payments_DocumentId] ON [billing].[Payments] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112525_InitialBilling'
)
BEGIN
    INSERT INTO [billing].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106112525_InitialBilling', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040617_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Documents_BookingId] ON [billing].[Documents] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040617_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Documents_SourceEstimateId] ON [billing].[Documents] ([SourceEstimateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040617_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [billing].[Documents] ADD CONSTRAINT [FK_Documents_Documents_SourceEstimateId] FOREIGN KEY ([SourceEstimateId]) REFERENCES [billing].[Documents] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040617_IndexesIntegrity'
)
BEGIN
    INSERT INTO [billing].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040617_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070749_CreditNotes'
)
BEGIN
    ALTER TABLE [billing].[Settings] ADD [NextCreditNoteNumber] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070749_CreditNotes'
)
BEGIN
    EXEC(N'UPDATE [billing].[Settings] SET [NextCreditNoteNumber] = 1
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [billing].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070749_CreditNotes'
)
BEGIN
    INSERT INTO [billing].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128070749_CreditNotes', N'10.0.14');
END;

COMMIT;
GO

