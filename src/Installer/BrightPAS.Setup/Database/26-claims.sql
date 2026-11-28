IF OBJECT_ID(N'[claims].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'claims') IS NULL EXEC(N'CREATE SCHEMA [claims];');
    CREATE TABLE [claims].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    IF SCHEMA_ID(N'claims') IS NULL EXEC(N'CREATE SCHEMA [claims];');
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[Funders] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [SubmitBy] nvarchar(10) NOT NULL,
        [PriorApprovalRequired] bit NOT NULL,
        [ClaimsEmail] nvarchar(200) NULL,
        [ProviderNumber] nvarchar(50) NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(1000) NULL,
        CONSTRAINT [PK_Funders] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[IdentitySettings] (
        [Id] int NOT NULL,
        [Mode] nvarchar(10) NOT NULL,
        [NhiBaseUrl] nvarchar(300) NULL,
        [HpiBaseUrl] nvarchar(300) NULL,
        [TokenUrl] nvarchar(300) NULL,
        [ClientId] nvarchar(100) NULL,
        [ClientSecretProtected] nvarchar(1000) NULL,
        [LastOkAt] datetimeoffset NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_IdentitySettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IdentitySettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[Settings] (
        [Id] int NOT NULL,
        [NextClaim] int NOT NULL,
        [NextApproval] int NOT NULL,
        [TerminalProvider] nvarchar(10) NOT NULL,
        [TerminalId] nvarchar(30) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ClaimsSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[TerminalTransactions] (
        [Id] uniqueidentifier NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Provider] nvarchar(10) NOT NULL,
        [TerminalId] nvarchar(30) NULL,
        [MerchantReference] nvarchar(40) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [AuthCode] nvarchar(20) NULL,
        [CardType] nvarchar(20) NULL,
        [Last4] nchar(4) NULL,
        [ResponseText] nvarchar(300) NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [StartedBy] nvarchar(100) NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        [PaymentRecorded] bit NOT NULL,
        CONSTRAINT [PK_TerminalTransactions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[XeroExports] (
        [Id] uniqueidentifier NOT NULL,
        [Mode] nvarchar(6) NOT NULL,
        [Invoices] int NOT NULL,
        [Payments] int NOT NULL,
        [FileName] nvarchar(200) NULL,
        [Errors] nvarchar(4000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_XeroExports] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[XeroSettings] (
        [Id] int NOT NULL,
        [Mode] nvarchar(6) NOT NULL,
        [SalesAccountCode] nvarchar(10) NOT NULL,
        [GstTaxType] nvarchar(20) NOT NULL,
        [NoGstTaxType] nvarchar(20) NOT NULL,
        [PaymentAccountCode] nvarchar(10) NULL,
        [TenantId] nvarchar(50) NULL,
        [ClientId] nvarchar(100) NULL,
        [ClientSecretProtected] nvarchar(1000) NULL,
        [RefreshTokenProtected] nvarchar(2000) NULL,
        [LastSyncAt] datetimeoffset NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_XeroSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_XeroSettings_SingleRow] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[ClaimBatches] (
        [Id] uniqueidentifier NOT NULL,
        [FunderId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [Claims] int NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_ClaimBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClaimBatches_Funders_FunderId] FOREIGN KEY ([FunderId]) REFERENCES [claims].[Funders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[PriorApprovals] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [FunderId] uniqueidentifier NOT NULL,
        [MemberNumber] nvarchar(50) NULL,
        [AccClaimNumber] nvarchar(20) NULL,
        [Procedure] nvarchar(200) NOT NULL,
        [AmountRequested] decimal(12,2) NOT NULL,
        [Status] nvarchar(14) NOT NULL,
        [ApprovalReference] nvarchar(50) NULL,
        [ApprovedAmount] decimal(12,2) NULL,
        [ValidUntil] date NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [DecidedAt] datetimeoffset NULL,
        [DecidedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_PriorApprovals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PriorApprovals_Funders_FunderId] FOREIGN KEY ([FunderId]) REFERENCES [claims].[Funders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[Remittances] (
        [Id] uniqueidentifier NOT NULL,
        [FunderId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [LineCount] int NOT NULL,
        [Matched] int NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [ImportedAt] datetimeoffset NOT NULL,
        [ImportedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Remittances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Remittances_Funders_FunderId] FOREIGN KEY ([FunderId]) REFERENCES [claims].[Funders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[XeroLinks] (
        [SourceId] uniqueidentifier NOT NULL,
        [SourceType] nvarchar(10) NOT NULL,
        [ExportId] uniqueidentifier NOT NULL,
        [XeroId] nvarchar(50) NULL,
        [ExportedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_XeroLinks] PRIMARY KEY ([SourceType], [SourceId]),
        CONSTRAINT [FK_XeroLinks_XeroExports_ExportId] FOREIGN KEY ([ExportId]) REFERENCES [claims].[XeroExports] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[Claims] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [FunderId] uniqueidentifier NOT NULL,
        [PriorApprovalId] uniqueidentifier NULL,
        [MemberNumber] nvarchar(50) NULL,
        [AccClaimNumber] nvarchar(20) NULL,
        [Status] nvarchar(12) NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Paid] decimal(12,2) NOT NULL,
        [BatchId] uniqueidentifier NULL,
        [SubmittedAt] datetimeoffset NULL,
        [SubmittedBy] nvarchar(100) NULL,
        [ResponseAt] datetimeoffset NULL,
        [ResponseText] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Claims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Claims_ClaimBatches_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [claims].[ClaimBatches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Claims_Funders_FunderId] FOREIGN KEY ([FunderId]) REFERENCES [claims].[Funders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Claims_PriorApprovals_PriorApprovalId] FOREIGN KEY ([PriorApprovalId]) REFERENCES [claims].[PriorApprovals] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[ClaimLines] (
        [ClaimId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        CONSTRAINT [PK_ClaimLines] PRIMARY KEY ([ClaimId], [Sequence]),
        CONSTRAINT [FK_ClaimLines_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [claims].[Claims] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE TABLE [claims].[RemittanceLines] (
        [RemittanceId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [ClaimReference] nvarchar(50) NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Outcome] nvarchar(20) NOT NULL,
        [Reason] nvarchar(300) NULL,
        [ClaimId] uniqueidentifier NULL,
        [Result] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_RemittanceLines] PRIMARY KEY ([RemittanceId], [Sequence]),
        CONSTRAINT [FK_RemittanceLines_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [claims].[Claims] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RemittanceLines_Remittances_RemittanceId] FOREIGN KEY ([RemittanceId]) REFERENCES [claims].[Remittances] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClientId', N'ClientSecretProtected', N'HpiBaseUrl', N'LastError', N'LastOkAt', N'Mode', N'NhiBaseUrl', N'TokenUrl') AND [object_id] = OBJECT_ID(N'[claims].[IdentitySettings]'))
        SET IDENTITY_INSERT [claims].[IdentitySettings] ON;
    EXEC(N'INSERT INTO [claims].[IdentitySettings] ([Id], [ClientId], [ClientSecretProtected], [HpiBaseUrl], [LastError], [LastOkAt], [Mode], [NhiBaseUrl], [TokenUrl])
    VALUES (1, NULL, NULL, NULL, NULL, NULL, N''Off'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClientId', N'ClientSecretProtected', N'HpiBaseUrl', N'LastError', N'LastOkAt', N'Mode', N'NhiBaseUrl', N'TokenUrl') AND [object_id] = OBJECT_ID(N'[claims].[IdentitySettings]'))
        SET IDENTITY_INSERT [claims].[IdentitySettings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextApproval', N'NextClaim', N'TerminalId', N'TerminalProvider') AND [object_id] = OBJECT_ID(N'[claims].[Settings]'))
        SET IDENTITY_INSERT [claims].[Settings] ON;
    EXEC(N'INSERT INTO [claims].[Settings] ([Id], [NextApproval], [NextClaim], [TerminalId], [TerminalProvider])
    VALUES (1, 1, 1, NULL, N''Manual'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'NextApproval', N'NextClaim', N'TerminalId', N'TerminalProvider') AND [object_id] = OBJECT_ID(N'[claims].[Settings]'))
        SET IDENTITY_INSERT [claims].[Settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClientId', N'ClientSecretProtected', N'GstTaxType', N'LastError', N'LastSyncAt', N'Mode', N'NoGstTaxType', N'PaymentAccountCode', N'RefreshTokenProtected', N'SalesAccountCode', N'TenantId') AND [object_id] = OBJECT_ID(N'[claims].[XeroSettings]'))
        SET IDENTITY_INSERT [claims].[XeroSettings] ON;
    EXEC(N'INSERT INTO [claims].[XeroSettings] ([Id], [ClientId], [ClientSecretProtected], [GstTaxType], [LastError], [LastSyncAt], [Mode], [NoGstTaxType], [PaymentAccountCode], [RefreshTokenProtected], [SalesAccountCode], [TenantId])
    VALUES (1, NULL, NULL, N''OUTPUT2'', NULL, NULL, N''Off'', N''NONE'', NULL, NULL, N''200'', NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClientId', N'ClientSecretProtected', N'GstTaxType', N'LastError', N'LastSyncAt', N'Mode', N'NoGstTaxType', N'PaymentAccountCode', N'RefreshTokenProtected', N'SalesAccountCode', N'TenantId') AND [object_id] = OBJECT_ID(N'[claims].[XeroSettings]'))
        SET IDENTITY_INSERT [claims].[XeroSettings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_ClaimBatches_FunderId] ON [claims].[ClaimBatches] ([FunderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Claims_BatchId] ON [claims].[Claims] ([BatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Claims_FunderId] ON [claims].[Claims] ([FunderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Claims_InvoiceId] ON [claims].[Claims] ([InvoiceId]) WHERE [Status] <> ''WrittenOff''');
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Claims_Number] ON [claims].[Claims] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Claims_PatientId] ON [claims].[Claims] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Claims_PriorApprovalId] ON [claims].[Claims] ([PriorApprovalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Claims_Status_SubmittedAt] ON [claims].[Claims] ([Status], [SubmittedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Funders_Code] ON [claims].[Funders] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_PriorApprovals_BookingId] ON [claims].[PriorApprovals] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_PriorApprovals_FunderId] ON [claims].[PriorApprovals] ([FunderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PriorApprovals_Number] ON [claims].[PriorApprovals] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_PriorApprovals_PatientId] ON [claims].[PriorApprovals] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_RemittanceLines_ClaimId] ON [claims].[RemittanceLines] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Remittances_FileHash] ON [claims].[Remittances] ([FileHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_Remittances_FunderId] ON [claims].[Remittances] ([FunderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_TerminalTransactions_InvoiceId] ON [claims].[TerminalTransactions] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TerminalTransactions_MerchantReference] ON [claims].[TerminalTransactions] ([MerchantReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_TerminalTransactions_StartedAt] ON [claims].[TerminalTransactions] ([StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    CREATE INDEX [IX_XeroLinks_ExportId] ON [claims].[XeroLinks] ([ExportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123185904_InitialClaims'
)
BEGIN
    INSERT INTO [claims].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123185904_InitialClaims', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[TerminalTransactions] ADD [IsRefund] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[TerminalTransactions] ADD [Reason] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[TerminalTransactions] ADD [RefundOfId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[TerminalTransactions] ADD [Refunded] decimal(12,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Settings] ADD [WindcaveKeyProtected] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Settings] ADD [WindcaveUrl] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Settings] ADD [WindcaveUser] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[PriorApprovals] ADD [ExternalReference] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Funders] ADD [Api] nvarchar(10) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Funders] ADD [ApiKeyProtected] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Funders] ADD [ApiUrl] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Funders] ADD [LastApiAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Funders] ADD [LastApiError] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Claims] ADD [ExternalReference] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[Claims] ADD [LastCheckedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    CREATE TABLE [claims].[XeroContacts] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(200) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [XeroContactId] nvarchar(50) NOT NULL,
        [How] nvarchar(10) NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_XeroContacts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    EXEC(N'UPDATE [claims].[Settings] SET [WindcaveKeyProtected] = NULL, [WindcaveUrl] = NULL, [WindcaveUser] = NULL
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    CREATE INDEX [IX_TerminalTransactions_RefundOfId] ON [claims].[TerminalTransactions] ([RefundOfId]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    CREATE INDEX [IX_Claims_ExternalReference] ON [claims].[Claims] ([ExternalReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    CREATE UNIQUE INDEX [IX_XeroContacts_Key] ON [claims].[XeroContacts] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    ALTER TABLE [claims].[TerminalTransactions] ADD CONSTRAINT [FK_TerminalTransactions_TerminalTransactions_RefundOfId] FOREIGN KEY ([RefundOfId]) REFERENCES [claims].[TerminalTransactions] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [claims].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128070743_DirectClaiming'
)
BEGIN
    INSERT INTO [claims].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128070743_DirectClaiming', N'10.0.14');
END;

COMMIT;
GO

