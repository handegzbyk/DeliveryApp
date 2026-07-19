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
    [OpenFoodFactsCode] nvarchar(64) NULL,
    [BrandId] int NULL,
    [Category] nvarchar(max) NULL,
    [ImageUrl] nvarchar(max) NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id])
);
GO

IF COL_LENGTH(N'Products', N'OpenFoodFactsCode') IS NULL
ALTER TABLE [Products] ADD [OpenFoodFactsCode] nvarchar(64) NULL;
GO

IF OBJECT_ID(N'[StoreProducts]', N'U') IS NULL
CREATE TABLE [StoreProducts] (
    [Id] int NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [ProductId] int NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [StoreProductCode] nvarchar(128) NULL,
    CONSTRAINT [PK_StoreProducts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StoreProducts_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_StoreProducts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]) ON DELETE CASCADE
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
    [StoreProductId] int NULL,
    [ReceiptId] int NOT NULL,
    [RawText] nvarchar(max) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    CONSTRAINT [PK_PriceObservations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PriceObservations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PriceObservations_StoreProducts_StoreProductId] FOREIGN KEY ([StoreProductId]) REFERENCES [StoreProducts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PriceObservations_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE CASCADE
);
GO

IF COL_LENGTH(N'PriceObservations', N'StoreProductId') IS NULL
ALTER TABLE [PriceObservations] ADD [StoreProductId] int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PriceObservations_StoreProducts_StoreProductId')
   AND COL_LENGTH(N'PriceObservations', N'StoreProductId') IS NOT NULL
ALTER TABLE [PriceObservations]
ADD CONSTRAINT [FK_PriceObservations_StoreProducts_StoreProductId]
FOREIGN KEY ([StoreProductId]) REFERENCES [StoreProducts] ([Id]) ON DELETE NO ACTION;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_ProductId')
CREATE INDEX [IX_PriceObservations_ProductId] ON [PriceObservations] ([ProductId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_StoreProductId')
CREATE INDEX [IX_PriceObservations_StoreProductId] ON [PriceObservations] ([StoreProductId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_ReceiptId')
CREATE INDEX [IX_PriceObservations_ReceiptId] ON [PriceObservations] ([ReceiptId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_OpenFoodFactsCode')
CREATE UNIQUE INDEX [IX_Products_OpenFoodFactsCode] ON [Products] ([OpenFoodFactsCode]) WHERE [OpenFoodFactsCode] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_BrandId')
CREATE INDEX [IX_Products_BrandId] ON [Products] ([BrandId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_StoreId')
CREATE INDEX [IX_Receipts_StoreId] ON [Receipts] ([StoreId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_ProductId')
CREATE INDEX [IX_StoreProducts_ProductId] ON [StoreProducts] ([ProductId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_Name')
CREATE UNIQUE INDEX [IX_StoreProducts_StoreId_Name] ON [StoreProducts] ([StoreId], [Name]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_StoreProductCode')
CREATE UNIQUE INDEX [IX_StoreProducts_StoreId_StoreProductCode]
ON [StoreProducts] ([StoreId], [StoreProductCode])
WHERE [StoreProductCode] IS NOT NULL;
GO
