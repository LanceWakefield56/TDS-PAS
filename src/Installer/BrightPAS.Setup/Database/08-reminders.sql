IF OBJECT_ID(N'[reminders].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'reminders') IS NULL EXEC(N'CREATE SCHEMA [reminders];');
    CREATE TABLE [reminders].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    IF SCHEMA_ID(N'reminders') IS NULL EXEC(N'CREATE SCHEMA [reminders];');
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[Messages] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [AppointmentDate] date NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Channel] nvarchar(10) NOT NULL,
        [Address] nvarchar(200) NOT NULL,
        [Subject] nvarchar(200) NULL,
        [Body] nvarchar(4000) NOT NULL,
        [SecretLink] nvarchar(500) NULL,
        [Status] nvarchar(20) NOT NULL,
        [IgnoreQuietHours] bit NOT NULL,
        [Provider] nvarchar(30) NULL,
        [ProviderMessageId] nvarchar(100) NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAt] datetimeoffset NOT NULL,
        [LastError] nvarchar(1000) NULL,
        [DedupeKey] nvarchar(100) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [SentAt] datetimeoffset NULL,
        [DeliveredAt] datetimeoffset NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[OptOuts] (
        [Id] uniqueidentifier NOT NULL,
        [Channel] nvarchar(10) NOT NULL,
        [Address] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [Source] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_OptOuts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[Preferences] (
        [PatientId] uniqueidentifier NOT NULL,
        [AllowSms] bit NOT NULL,
        [AllowEmail] bit NOT NULL,
        [Language] nvarchar(5) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Preferences] PRIMARY KEY ([PatientId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[Replies] (
        [Id] uniqueidentifier NOT NULL,
        [From] nvarchar(200) NOT NULL,
        [Body] nvarchar(2000) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [Provider] nvarchar(30) NOT NULL,
        [ProviderMessageId] nvarchar(100) NULL,
        [ReplyToProviderId] nvarchar(100) NULL,
        [MatchedMessageId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [Meaning] nvarchar(20) NOT NULL,
        [ActionTaken] nvarchar(300) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [HandledBy] nvarchar(100) NULL,
        [HandledAt] datetimeoffset NULL,
        [Note] nvarchar(1000) NULL,
        CONSTRAINT [PK_Replies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[Settings] (
        [Id] int NOT NULL,
        [SmsProvider] nvarchar(20) NOT NULL,
        [EmailProvider] nvarchar(20) NOT NULL,
        [SendBookingConfirmation] bit NOT NULL,
        [SendDayBeforeReminder] bit NOT NULL,
        [SendEmailCopies] bit NOT NULL,
        [DayBeforeHour] int NOT NULL,
        [ArrivalLeadMinutes] int NOT NULL,
        [QuietStart] time NOT NULL,
        [QuietEnd] time NOT NULL,
        [ClinicName] nvarchar(100) NOT NULL,
        [ClinicPhone] nvarchar(30) NOT NULL,
        [ConfirmationsFrom] datetimeoffset NOT NULL,
        [ModicaBaseUrl] nvarchar(200) NOT NULL,
        [ModicaApplication] nvarchar(100) NULL,
        [ModicaPasswordProtected] nvarchar(1000) NULL,
        [ModicaMask] nvarchar(20) NULL,
        [GenericUrl] nvarchar(300) NULL,
        [GenericAuthHeader] nvarchar(100) NULL,
        [GenericAuthValueProtected] nvarchar(1000) NULL,
        [GenericBodyTemplate] nvarchar(2000) NOT NULL,
        [SmtpHost] nvarchar(200) NULL,
        [SmtpPort] int NOT NULL,
        [SmtpStartTls] bit NOT NULL,
        [SmtpUser] nvarchar(200) NULL,
        [SmtpPasswordProtected] nvarchar(1000) NULL,
        [FromAddress] nvarchar(200) NULL,
        [FromName] nvarchar(100) NOT NULL,
        [WebhookKeyProtected] nvarchar(1000) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE TABLE [reminders].[Templates] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Channel] nvarchar(10) NOT NULL,
        [Language] nvarchar(5) NOT NULL,
        [Subject] nvarchar(200) NULL,
        [Body] nvarchar(4000) NOT NULL,
        [IsActive] bit NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Templates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Messages_Address_CreatedAt] ON [reminders].[Messages] ([Address], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Messages_DedupeKey] ON [reminders].[Messages] ([DedupeKey]) WHERE [DedupeKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Messages_PatientId] ON [reminders].[Messages] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Messages_ProviderMessageId] ON [reminders].[Messages] ([ProviderMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Messages_Status_NextAttemptAt] ON [reminders].[Messages] ([Status], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OptOuts_Channel_Address] ON [reminders].[OptOuts] ([Channel], [Address]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Replies_ProviderMessageId] ON [reminders].[Replies] ([ProviderMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE INDEX [IX_Replies_Status_ReceivedAt] ON [reminders].[Replies] ([Status], [ReceivedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Templates_Kind_Channel_Language] ON [reminders].[Templates] ([Kind], [Channel], [Language]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107050316_InitialReminders'
)
BEGIN
    INSERT INTO [reminders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107050316_InitialReminders', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Replies_BookingId] ON [reminders].[Replies] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Replies_MatchedMessageId] ON [reminders].[Replies] ([MatchedMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Replies_PatientId] ON [reminders].[Replies] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    CREATE INDEX [IX_Messages_BookingId] ON [reminders].[Messages] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    ALTER TABLE [reminders].[Replies] ADD CONSTRAINT [FK_Replies_Messages_MatchedMessageId] FOREIGN KEY ([MatchedMessageId]) REFERENCES [reminders].[Messages] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108040638_IndexesIntegrity'
)
BEGIN
    INSERT INTO [reminders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108040638_IndexesIntegrity', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111032244_TestMessagesWithoutPatient'
)
BEGIN
    EXEC(N'IF OBJECT_ID(N''[reminders].[FK_reminders_Messages_Patient]'', N''F'') IS NOT NULL ALTER TABLE [reminders].[Messages] DROP CONSTRAINT [FK_reminders_Messages_Patient];')
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111032244_TestMessagesWithoutPatient'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[reminders].[Messages]') AND [c].[name] = N'PatientId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [reminders].[Messages] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [reminders].[Messages] ALTER COLUMN [PatientId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111032244_TestMessagesWithoutPatient'
)
BEGIN
    EXEC(N'UPDATE [reminders].[Messages] SET [PatientId] = NULL WHERE [PatientId] = ''00000000-0000-0000-0000-000000000000'';')
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111032244_TestMessagesWithoutPatient'
)
BEGIN
    INSERT INTO [reminders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111032244_TestMessagesWithoutPatient', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Settings] ADD [BulkHoldSeconds] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Settings] ADD [HoldSeconds] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Settings] ADD [ReviewKinds] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [CorrectionForId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [RecallReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [RecalledAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [RecalledBy] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [ReleasedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [ReleasedBy] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [RequiresRelease] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD [WrongRecipientAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    CREATE INDEX [IX_Messages_CorrectionForId] ON [reminders].[Messages] ([CorrectionForId]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    ALTER TABLE [reminders].[Messages] ADD CONSTRAINT [FK_Messages_Messages_CorrectionForId] FOREIGN KEY ([CorrectionForId]) REFERENCES [reminders].[Messages] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [reminders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123175821_SendSafety'
)
BEGIN
    INSERT INTO [reminders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123175821_SendSafety', N'10.0.14');
END;

COMMIT;
GO

