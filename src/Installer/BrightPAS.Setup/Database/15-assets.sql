IF OBJECT_ID(N'[assets].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'assets') IS NULL EXEC(N'CREATE SCHEMA [assets];');
    CREATE TABLE [assets].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    IF SCHEMA_ID(N'assets') IS NULL EXEC(N'CREATE SCHEMA [assets];');
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Settings] (
        [Id] int NOT NULL,
        [ScopeStorageHours] int NOT NULL,
        [MaintenanceWarningDays] int NOT NULL,
        [ReleaseNeedsSecondPerson] bit NOT NULL,
        [NextLoad] int NOT NULL,
        [NextNc] int NOT NULL,
        [NextPo] int NOT NULL,
        [NextLoan] int NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Suppliers] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [ContactName] nvarchar(100) NULL,
        [Email] nvarchar(200) NULL,
        [Phone] nvarchar(40) NULL,
        [XeroContactName] nvarchar(150) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[TraySets] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Specialty] nvarchar(60) NULL,
        [ShelfLifeDays] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_TraySets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Equipment] (
        [Id] uniqueidentifier NOT NULL,
        [AssetNumber] nvarchar(40) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Category] nvarchar(20) NOT NULL,
        [Make] nvarchar(80) NULL,
        [Model] nvarchar(80) NULL,
        [SerialNumber] nvarchar(80) NULL,
        [LocationId] uniqueidentifier NULL,
        [Owner] nvarchar(100) NULL,
        [SupplierId] uniqueidentifier NULL,
        [PurchasedOn] date NULL,
        [Value] decimal(12,2) NULL,
        [WarrantyUntil] date NULL,
        [Risk] nvarchar(10) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OutOfServiceReason] nvarchar(300) NULL,
        [LockedOutBy] nvarchar(100) NULL,
        [LockedOutAt] datetimeoffset NULL,
        [NetworkAddress] nvarchar(100) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Equipment] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Equipment_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [assets].[Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[PurchaseOrders] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [SupplierId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [ApprovedAt] datetimeoffset NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [assets].[Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[StockItems] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Unit] nvarchar(20) NOT NULL,
        [IsImplant] bit NOT NULL,
        [SupplierId] uniqueidentifier NULL,
        [SupplierCode] nvarchar(60) NULL,
        [ReorderLevel] int NOT NULL,
        [ReorderQuantity] int NOT NULL,
        [UnitCost] decimal(12,2) NOT NULL,
        [AccountCode] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_StockItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockItems_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [assets].[Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[TrayItems] (
        [Id] uniqueidentifier NOT NULL,
        [TraySetId] uniqueidentifier NOT NULL,
        [Sort] int NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_TrayItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TrayItems_TraySets_TraySetId] FOREIGN KEY ([TraySetId]) REFERENCES [assets].[TraySets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Cycles] (
        [Id] uniqueidentifier NOT NULL,
        [LoadNumber] nvarchar(30) NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [MachineCycleNumber] nvarchar(40) NULL,
        [Program] nvarchar(80) NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [EndedAt] datetimeoffset NULL,
        [MaxTemperatureC] decimal(6,2) NULL,
        [HoldSeconds] int NULL,
        [PressureKpa] decimal(8,2) NULL,
        [A0] int NULL,
        [MachinePassed] bit NOT NULL,
        [ChemicalIndicator] nvarchar(10) NOT NULL,
        [BiologicalIndicator] nvarchar(10) NOT NULL,
        [BowieDickTest] bit NOT NULL,
        [Release] nvarchar(10) NOT NULL,
        [ReleasedBy] nvarchar(100) NULL,
        [ReleasedAt] datetimeoffset NULL,
        [ReleaseNote] nvarchar(500) NULL,
        [Operator] nvarchar(100) NOT NULL,
        [Source] nvarchar(200) NOT NULL,
        [RawLog] nvarchar(max) NULL,
        CONSTRAINT [PK_Cycles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cycles_Equipment_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [assets].[Equipment] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[MaintenancePlans] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [IntervalMonths] int NOT NULL,
        [NextDue] date NOT NULL,
        [DoneBy] nvarchar(100) NULL,
        CONSTRAINT [PK_MaintenancePlans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaintenancePlans_Equipment_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [assets].[Equipment] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[MaintenanceRecords] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [DoneOn] date NOT NULL,
        [DoneBy] nvarchar(100) NOT NULL,
        [Passed] bit NOT NULL,
        [TagNumber] nvarchar(40) NULL,
        [Notes] nvarchar(1000) NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_MaintenanceRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaintenanceRecords_Equipment_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [assets].[Equipment] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[SupplierInvoices] (
        [Id] uniqueidentifier NOT NULL,
        [PurchaseOrderId] uniqueidentifier NOT NULL,
        [InvoiceNumber] nvarchar(60) NOT NULL,
        [InvoiceDate] date NOT NULL,
        [DueDate] date NOT NULL,
        [AmountExGst] decimal(12,2) NOT NULL,
        [Matched] bit NOT NULL,
        [MatchNote] nvarchar(500) NULL,
        [ExportedToXeroAt] datetimeoffset NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SupplierInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupplierInvoices_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [assets].[PurchaseOrders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[PurchaseOrderLines] (
        [Id] uniqueidentifier NOT NULL,
        [PurchaseOrderId] uniqueidentifier NOT NULL,
        [StockItemId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        [UnitCost] decimal(12,2) NOT NULL,
        [QuantityReceived] int NOT NULL,
        CONSTRAINT [PK_PurchaseOrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [assets].[PurchaseOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PurchaseOrderLines_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [assets].[StockItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[StockLots] (
        [Id] uniqueidentifier NOT NULL,
        [StockItemId] uniqueidentifier NOT NULL,
        [Lot] nvarchar(60) NOT NULL,
        [Expiry] date NULL,
        [OnHand] int NOT NULL,
        [ReceivedOn] date NOT NULL,
        CONSTRAINT [PK_StockLots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockLots_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [assets].[StockItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[LoanSets] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(30) NOT NULL,
        [SupplierId] uniqueidentifier NULL,
        [Description] nvarchar(300) NOT NULL,
        [ItemCount] int NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [Status] nvarchar(20) NOT NULL,
        [ReceivedAt] datetimeoffset NULL,
        [CheckedBy] nvarchar(100) NULL,
        [CheckNotes] nvarchar(500) NULL,
        [CycleId] uniqueidentifier NULL,
        [ReturnedAt] datetimeoffset NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_LoanSets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LoanSets_Cycles_CycleId] FOREIGN KEY ([CycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanSets_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [assets].[Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[ScopeEvents] (
        [Id] uniqueidentifier NOT NULL,
        [ScopeId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [At] datetimeoffset NOT NULL,
        [By] nvarchar(100) NOT NULL,
        [Passed] bit NULL,
        [CycleId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_ScopeEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ScopeEvents_Cycles_CycleId] FOREIGN KEY ([CycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ScopeEvents_Equipment_ScopeId] FOREIGN KEY ([ScopeId]) REFERENCES [assets].[Equipment] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Trays] (
        [Id] uniqueidentifier NOT NULL,
        [Barcode] nvarchar(40) NOT NULL,
        [TraySetId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [LastCycleId] uniqueidentifier NULL,
        [SterileUntil] date NULL,
        [PackedBy] nvarchar(100) NULL,
        [PackedAt] datetimeoffset NULL,
        [Notes] nvarchar(500) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Trays] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Trays_Cycles_LastCycleId] FOREIGN KEY ([LastCycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Trays_TraySets_TraySetId] FOREIGN KEY ([TraySetId]) REFERENCES [assets].[TraySets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Implants] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [StockItemId] uniqueidentifier NOT NULL,
        [LotId] uniqueidentifier NULL,
        [Lot] nvarchar(60) NOT NULL,
        [SerialNumber] nvarchar(60) NULL,
        [Manufacturer] nvarchar(100) NULL,
        [Site] nvarchar(100) NULL,
        [ImplantedOn] date NOT NULL,
        [RecordedBy] nvarchar(100) NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Implants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Implants_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [assets].[StockItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Implants_StockLots_LotId] FOREIGN KEY ([LotId]) REFERENCES [assets].[StockLots] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[StockMovements] (
        [Id] uniqueidentifier NOT NULL,
        [StockItemId] uniqueidentifier NOT NULL,
        [LotId] uniqueidentifier NULL,
        [Quantity] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [PurchaseOrderId] uniqueidentifier NULL,
        [Reason] nvarchar(300) NULL,
        [At] datetimeoffset NOT NULL,
        [By] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_StockMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockMovements_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [assets].[PurchaseOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockMovements_StockItems_StockItemId] FOREIGN KEY ([StockItemId]) REFERENCES [assets].[StockItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockMovements_StockLots_LotId] FOREIGN KEY ([LotId]) REFERENCES [assets].[StockLots] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[CycleItems] (
        [Id] uniqueidentifier NOT NULL,
        [CycleId] uniqueidentifier NOT NULL,
        [TrayInstanceId] uniqueidentifier NULL,
        [Description] nvarchar(200) NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_CycleItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CycleItems_Cycles_CycleId] FOREIGN KEY ([CycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CycleItems_Trays_TrayInstanceId] FOREIGN KEY ([TrayInstanceId]) REFERENCES [assets].[Trays] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[Nonconformances] (
        [Id] uniqueidentifier NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [RaisedOn] date NOT NULL,
        [RaisedBy] nvarchar(100) NOT NULL,
        [Category] nvarchar(60) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [CycleId] uniqueidentifier NULL,
        [TrayInstanceId] uniqueidentifier NULL,
        [EquipmentId] uniqueidentifier NULL,
        [ImmediateAction] nvarchar(1000) NULL,
        [RootCause] nvarchar(1000) NULL,
        [CorrectiveAction] nvarchar(1000) NULL,
        [PreventiveAction] nvarchar(1000) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ClosedBy] nvarchar(100) NULL,
        [ClosedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Nonconformances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Nonconformances_Cycles_CycleId] FOREIGN KEY ([CycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Nonconformances_Equipment_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [assets].[Equipment] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Nonconformances_Trays_TrayInstanceId] FOREIGN KEY ([TrayInstanceId]) REFERENCES [assets].[Trays] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE TABLE [assets].[TrayIssues] (
        [Id] uniqueidentifier NOT NULL,
        [TrayInstanceId] uniqueidentifier NOT NULL,
        [CycleId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [PatientId] uniqueidentifier NULL,
        [IssuedAt] datetimeoffset NOT NULL,
        [IssuedBy] nvarchar(100) NOT NULL,
        [ReturnedAt] datetimeoffset NULL,
        CONSTRAINT [PK_TrayIssues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TrayIssues_Cycles_CycleId] FOREIGN KEY ([CycleId]) REFERENCES [assets].[Cycles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TrayIssues_Trays_TrayInstanceId] FOREIGN KEY ([TrayInstanceId]) REFERENCES [assets].[Trays] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_CycleItems_CycleId] ON [assets].[CycleItems] ([CycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_CycleItems_TrayInstanceId] ON [assets].[CycleItems] ([TrayInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cycles_LoadNumber] ON [assets].[Cycles] ([LoadNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Cycles_MachineId_StartedAt] ON [assets].[Cycles] ([MachineId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Cycles_Release] ON [assets].[Cycles] ([Release]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Equipment_AssetNumber] ON [assets].[Equipment] ([AssetNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Equipment_Category] ON [assets].[Equipment] ([Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Equipment_LocationId] ON [assets].[Equipment] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Equipment_SerialNumber] ON [assets].[Equipment] ([SerialNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Equipment_SupplierId] ON [assets].[Equipment] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Implants_BookingId] ON [assets].[Implants] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Implants_LotId] ON [assets].[Implants] ([LotId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Implants_PatientId] ON [assets].[Implants] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Implants_StockItemId_Lot] ON [assets].[Implants] ([StockItemId], [Lot]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_LoanSets_BookingId] ON [assets].[LoanSets] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_LoanSets_CycleId] ON [assets].[LoanSets] ([CycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LoanSets_Number] ON [assets].[LoanSets] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_LoanSets_Status] ON [assets].[LoanSets] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_LoanSets_SupplierId] ON [assets].[LoanSets] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MaintenancePlans_EquipmentId_Kind] ON [assets].[MaintenancePlans] ([EquipmentId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_MaintenancePlans_NextDue] ON [assets].[MaintenancePlans] ([NextDue]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_MaintenanceRecords_EquipmentId_DoneOn] ON [assets].[MaintenanceRecords] ([EquipmentId], [DoneOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Nonconformances_CycleId] ON [assets].[Nonconformances] ([CycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Nonconformances_EquipmentId] ON [assets].[Nonconformances] ([EquipmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Nonconformances_Number] ON [assets].[Nonconformances] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Nonconformances_Status] ON [assets].[Nonconformances] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Nonconformances_TrayInstanceId] ON [assets].[Nonconformances] ([TrayInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_PurchaseOrderId] ON [assets].[PurchaseOrderLines] ([PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_StockItemId] ON [assets].[PurchaseOrderLines] ([StockItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseOrders_Number] ON [assets].[PurchaseOrders] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_Status] ON [assets].[PurchaseOrders] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_SupplierId] ON [assets].[PurchaseOrders] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_ScopeEvents_BookingId] ON [assets].[ScopeEvents] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_ScopeEvents_CycleId] ON [assets].[ScopeEvents] ([CycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_ScopeEvents_PatientId] ON [assets].[ScopeEvents] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_ScopeEvents_ScopeId_At] ON [assets].[ScopeEvents] ([ScopeId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockItems_Code] ON [assets].[StockItems] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockItems_SupplierId] ON [assets].[StockItems] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockLots_Expiry] ON [assets].[StockLots] ([Expiry]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockLots_StockItemId_Lot] ON [assets].[StockLots] ([StockItemId], [Lot]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockMovements_BookingId] ON [assets].[StockMovements] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockMovements_LotId] ON [assets].[StockMovements] ([LotId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockMovements_PatientId] ON [assets].[StockMovements] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockMovements_PurchaseOrderId] ON [assets].[StockMovements] ([PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_StockMovements_StockItemId_At] ON [assets].[StockMovements] ([StockItemId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_SupplierInvoices_ExportedToXeroAt] ON [assets].[SupplierInvoices] ([ExportedToXeroAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_SupplierInvoices_PurchaseOrderId] ON [assets].[SupplierInvoices] ([PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Suppliers_Name] ON [assets].[Suppliers] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_TrayIssues_BookingId] ON [assets].[TrayIssues] ([BookingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_TrayIssues_CycleId] ON [assets].[TrayIssues] ([CycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_TrayIssues_PatientId] ON [assets].[TrayIssues] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_TrayIssues_TrayInstanceId] ON [assets].[TrayIssues] ([TrayInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_TrayItems_TraySetId_Sort] ON [assets].[TrayItems] ([TraySetId], [Sort]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Trays_Barcode] ON [assets].[Trays] ([Barcode]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Trays_LastCycleId] ON [assets].[Trays] ([LastCycleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Trays_Status] ON [assets].[Trays] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE INDEX [IX_Trays_TraySetId] ON [assets].[Trays] ([TraySetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TraySets_Code] ON [assets].[TraySets] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [assets].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113104718_InitialAssets'
)
BEGIN
    INSERT INTO [assets].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113104718_InitialAssets', N'10.0.14');
END;

COMMIT;
GO

