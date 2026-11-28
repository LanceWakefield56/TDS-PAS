IF OBJECT_ID(N'[payments].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'payments') IS NULL EXEC(N'CREATE SCHEMA [payments];');
    CREATE TABLE [payments].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    IF SCHEMA_ID(N'payments') IS NULL EXEC(N'CREATE SCHEMA [payments];');
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Links] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [InvoiceId] uniqueidentifier NULL,
        [StatementId] uniqueidentifier NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [LastOpenedAt] datetimeoffset NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [CancelledAt] datetimeoffset NULL,
        [CancelledBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Links] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Runs] (
        [Id] uniqueidentifier NOT NULL,
        [StatementDate] date NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [Count] int NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [SentAt] datetimeoffset NULL,
        [SentBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Runs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Settings] (
        [Id] int NOT NULL,
        [Provider] nvarchar(20) NOT NULL,
        [PublicUrl] nvarchar(200) NULL,
        [LinkDays] int NOT NULL,
        [MinimumPayment] decimal(12,2) NOT NULL,
        [WindcaveUrl] nvarchar(200) NOT NULL,
        [WindcaveUser] nvarchar(100) NULL,
        [WindcaveKeyProtected] nvarchar(1000) NULL,
        [StatementMinimum] decimal(12,2) NOT NULL,
        [StatementMessage] nvarchar(300) NULL,
        [NextStatementNumber] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Payments] (
        [Id] uniqueidentifier NOT NULL,
        [PayLinkId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Provider] nvarchar(20) NOT NULL,
        [SessionId] nvarchar(100) NULL,
        [MerchantReference] nvarchar(64) NOT NULL,
        [CardType] nvarchar(30) NULL,
        [Last4] nvarchar(4) NULL,
        [AuthCode] nvarchar(20) NULL,
        [ResponseText] nvarchar(300) NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        [NeedsAttention] bit NOT NULL,
        [AttentionReason] nvarchar(500) NULL,
        [ResolvedAt] datetimeoffset NULL,
        [ResolvedBy] nvarchar(100) NULL,
        [ResolvedNote] nvarchar(500) NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_Links_PayLinkId] FOREIGN KEY ([PayLinkId]) REFERENCES [payments].[Links] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Statements] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [StatementDate] date NOT NULL,
        [TotalDue] decimal(12,2) NOT NULL,
        [Overdue] decimal(12,2) NOT NULL,
        [Delivery] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Snapshot] nvarchar(max) NOT NULL,
        [SentAt] datetimeoffset NULL,
        [Note] nvarchar(300) NULL,
        CONSTRAINT [PK_Statements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Statements_Runs_RunId] FOREIGN KEY ([RunId]) REFERENCES [payments].[Runs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE TABLE [payments].[Allocations] (
        [Id] uniqueidentifier NOT NULL,
        [OnlinePaymentId] uniqueidentifier NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        [InvoiceNumber] nvarchar(30) NULL,
        [Amount] decimal(12,2) NOT NULL,
        CONSTRAINT [PK_Allocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Allocations_Payments_OnlinePaymentId] FOREIGN KEY ([OnlinePaymentId]) REFERENCES [payments].[Payments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Allocations_InvoiceId] ON [payments].[Allocations] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Allocations_OnlinePaymentId] ON [payments].[Allocations] ([OnlinePaymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Links_InvoiceId] ON [payments].[Links] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Links_PatientId] ON [payments].[Links] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Links_StatementId] ON [payments].[Links] ([StatementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Links_TokenHash] ON [payments].[Links] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Payments_MerchantReference] ON [payments].[Payments] ([MerchantReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Payments_PatientId] ON [payments].[Payments] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Payments_PayLinkId] ON [payments].[Payments] ([PayLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Payments_SessionId] ON [payments].[Payments] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Payments_Status_StartedAt] ON [payments].[Payments] ([Status], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Runs_StatementDate] ON [payments].[Runs] ([StatementDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Statements_Number] ON [payments].[Statements] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Statements_PatientId] ON [payments].[Statements] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    CREATE INDEX [IX_Statements_RunId] ON [payments].[Statements] ([RunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128013718_InitialPayments'
)
BEGIN
    INSERT INTO [payments].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128013718_InitialPayments', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [AutoSendStatements] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [AutoStatementDay] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [IncludeShortfalls] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [LastAutomationOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [NextPlanNumber] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [NextRefundNumber] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [OverdueFirstDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [OverdueSecondDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Settings] ADD [PlanReminderDays] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Payments] ADD [Refunded] decimal(12,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Payments] ADD [TransactionId] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Links] ADD [PlanId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    ALTER TABLE [payments].[Allocations] ADD [Refunded] decimal(12,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE TABLE [payments].[Overdue] (
        [Id] uniqueidentifier NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [InvoiceNumber] nvarchar(30) NULL,
        [Stage] int NOT NULL,
        [Balance] decimal(12,2) NOT NULL,
        [SentAt] datetimeoffset NOT NULL,
        [Messages] int NOT NULL,
        CONSTRAINT [PK_Overdue] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE TABLE [payments].[Plans] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [Instalment] decimal(12,2) NOT NULL,
        [Frequency] nvarchar(20) NOT NULL,
        [FirstDue] date NOT NULL,
        [PaidAtStart] decimal(12,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [EndedAt] datetimeoffset NULL,
        [EndedBy] nvarchar(100) NULL,
        [EndReason] nvarchar(300) NULL,
        [LastReminderFor] date NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE TABLE [payments].[Refunds] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [OnlinePaymentId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [FromUnapplied] decimal(12,2) NOT NULL,
        [Reason] nvarchar(300) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ProviderReference] nvarchar(100) NULL,
        [Error] nvarchar(300) NULL,
        [AppliedTo] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Refunds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Refunds_Payments_OnlinePaymentId] FOREIGN KEY ([OnlinePaymentId]) REFERENCES [payments].[Payments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE TABLE [payments].[PlanInvoices] (
        [Id] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PlanInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanInvoices_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [payments].[Plans] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Links_PlanId] ON [payments].[Links] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Overdue_InvoiceId_Stage] ON [payments].[Overdue] ([InvoiceId], [Stage]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Overdue_PatientId] ON [payments].[Overdue] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Overdue_SentAt] ON [payments].[Overdue] ([SentAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_PlanInvoices_InvoiceId] ON [payments].[PlanInvoices] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlanInvoices_PlanId_InvoiceId] ON [payments].[PlanInvoices] ([PlanId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Plans_Number] ON [payments].[Plans] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Plans_PatientId] ON [payments].[Plans] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Refunds_Number] ON [payments].[Refunds] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Refunds_OnlinePaymentId] ON [payments].[Refunds] ([OnlinePaymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    CREATE INDEX [IX_Refunds_PatientId] ON [payments].[Refunds] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128063437_PaymentsExtras'
)
BEGIN
    INSERT INTO [payments].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128063437_PaymentsExtras', N'10.0.14');
END;

COMMIT;
GO

