-- ShopDelivery schema for Azure SQL Database
-- Generated from the EF Core model (ShopDbContext). Run this against a freshly
-- created, EMPTY database (see ShopDelivery.Api/Data/README-database.md).
-- Idempotent guards are included so it is safe to re-run.

IF OBJECT_ID(N'[Brands]', N'U') IS NULL
CREATE TABLE [Brands] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Brands] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID(N'[Stores]', N'U') IS NULL
CREATE TABLE [Stores] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Location] nvarchar(max) NULL,
    CONSTRAINT [PK_Stores] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID(N'[Products]', N'U') IS NULL
CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [BrandId] int NULL,
    [Category] nvarchar(max) NULL,
    [ImageUrl] nvarchar(max) NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id])
);
GO

IF OBJECT_ID(N'[Receipts]', N'U') IS NULL
CREATE TABLE [Receipts] (
    [Id] int NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [PurchasedAt] datetimeoffset NOT NULL,
    [Total] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_Receipts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Receipts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]) ON DELETE CASCADE
);
GO

IF OBJECT_ID(N'[PriceObservations]', N'U') IS NULL
CREATE TABLE [PriceObservations] (
    [Id] int NOT NULL IDENTITY,
    [ProductId] int NOT NULL,
    [ReceiptId] int NOT NULL,
    [RawText] nvarchar(max) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    CONSTRAINT [PK_PriceObservations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PriceObservations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PriceObservations_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE CASCADE
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_ProductId')
CREATE INDEX [IX_PriceObservations_ProductId] ON [PriceObservations] ([ProductId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_ReceiptId')
CREATE INDEX [IX_PriceObservations_ReceiptId] ON [PriceObservations] ([ReceiptId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_BrandId')
CREATE INDEX [IX_Products_BrandId] ON [Products] ([BrandId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_StoreId')
CREATE INDEX [IX_Receipts_StoreId] ON [Receipts] ([StoreId]);
GO
