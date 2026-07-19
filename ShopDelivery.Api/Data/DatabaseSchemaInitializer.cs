using System.Data;
using Microsoft.EntityFrameworkCore;

namespace ShopDelivery.Api.Data;

public static class DatabaseSchemaInitializer
{
    public static async Task EnsureAsync(ShopDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        var providerName = db.Database.ProviderName ?? "";
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await ApplySqlServerUpdatesAsync(db, ct);
        }
        else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await ApplySqliteUpdatesAsync(db, ct);
        }
    }

    private static async Task ApplySqlServerUpdatesAsync(ShopDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
IF COL_LENGTH(N'Products', N'OpenFoodFactsCode') IS NULL
    ALTER TABLE [Products] ADD [OpenFoodFactsCode] nvarchar(64) NULL;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'[StoreProducts]', N'U') IS NULL
BEGIN
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
END;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF COL_LENGTH(N'PriceObservations', N'StoreProductId') IS NULL
    ALTER TABLE [PriceObservations] ADD [StoreProductId] int NULL;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PriceObservations_StoreProducts_StoreProductId')
   AND COL_LENGTH(N'PriceObservations', N'StoreProductId') IS NOT NULL
BEGIN
    ALTER TABLE [PriceObservations]
    ADD CONSTRAINT [FK_PriceObservations_StoreProducts_StoreProductId]
    FOREIGN KEY ([StoreProductId]) REFERENCES [StoreProducts] ([Id]) ON DELETE NO ACTION;
END;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (
    SELECT [OpenFoodFactsCode]
    FROM [Products]
    WHERE [OpenFoodFactsCode] IS NOT NULL
    GROUP BY [OpenFoodFactsCode]
    HAVING COUNT(*) > 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_OpenFoodFactsCode' AND is_unique = 0)
        DROP INDEX [IX_Products_OpenFoodFactsCode] ON [Products];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_OpenFoodFactsCode')
        CREATE UNIQUE INDEX [IX_Products_OpenFoodFactsCode] ON [Products] ([OpenFoodFactsCode]) WHERE [OpenFoodFactsCode] IS NOT NULL;
END;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_ProductId')
    CREATE INDEX [IX_StoreProducts_ProductId] ON [StoreProducts] ([ProductId]);
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (
    SELECT [StoreId], [Name]
    FROM [StoreProducts]
    GROUP BY [StoreId], [Name]
    HAVING COUNT(*) > 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_Name' AND is_unique = 0)
        DROP INDEX [IX_StoreProducts_StoreId_Name] ON [StoreProducts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_Name')
        CREATE UNIQUE INDEX [IX_StoreProducts_StoreId_Name] ON [StoreProducts] ([StoreId], [Name]);
END;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (
    SELECT [StoreId], [StoreProductCode]
    FROM [StoreProducts]
    WHERE [StoreProductCode] IS NOT NULL
    GROUP BY [StoreId], [StoreProductCode]
    HAVING COUNT(*) > 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_StoreProductCode' AND is_unique = 0)
        DROP INDEX [IX_StoreProducts_StoreId_StoreProductCode] ON [StoreProducts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreProducts_StoreId_StoreProductCode')
        CREATE UNIQUE INDEX [IX_StoreProducts_StoreId_StoreProductCode]
        ON [StoreProducts] ([StoreId], [StoreProductCode])
        WHERE [StoreProductCode] IS NOT NULL;
END;
""", ct);

        await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PriceObservations_StoreProductId')
    CREATE INDEX [IX_PriceObservations_StoreProductId] ON [PriceObservations] ([StoreProductId]);
""", ct);
    }

    private static async Task ApplySqliteUpdatesAsync(ShopDbContext db, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(db, "Products", "OpenFoodFactsCode", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Products ADD COLUMN OpenFoodFactsCode TEXT NULL;",
                ct);
        }

        await db.Database.ExecuteSqlRawAsync("""
CREATE TABLE IF NOT EXISTS StoreProducts (
    Id INTEGER NOT NULL CONSTRAINT PK_StoreProducts PRIMARY KEY AUTOINCREMENT,
    StoreId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    StoreProductCode TEXT NULL,
    CONSTRAINT FK_StoreProducts_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE CASCADE,
    CONSTRAINT FK_StoreProducts_Stores_StoreId FOREIGN KEY (StoreId) REFERENCES Stores (Id) ON DELETE CASCADE
);
""", ct);

        if (!await ColumnExistsAsync(db, "PriceObservations", "StoreProductId", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE PriceObservations ADD COLUMN StoreProductId INTEGER NULL;",
                ct);
        }

        await EnsureSqliteUniqueIndexAsync(
            db,
            "IX_Products_OpenFoodFactsCode",
            "Products",
            "OpenFoodFactsCode",
            "OpenFoodFactsCode IS NOT NULL",
            "SELECT 1 FROM Products WHERE OpenFoodFactsCode IS NOT NULL GROUP BY OpenFoodFactsCode HAVING COUNT(*) > 1 LIMIT 1;",
            ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StoreProducts_ProductId ON StoreProducts (ProductId);",
            ct);
        await EnsureSqliteUniqueIndexAsync(
            db,
            "IX_StoreProducts_StoreId_Name",
            "StoreProducts",
            "StoreId, Name",
            null,
            "SELECT 1 FROM StoreProducts GROUP BY StoreId, Name HAVING COUNT(*) > 1 LIMIT 1;",
            ct);
        await EnsureSqliteUniqueIndexAsync(
            db,
            "IX_StoreProducts_StoreId_StoreProductCode",
            "StoreProducts",
            "StoreId, StoreProductCode",
            "StoreProductCode IS NOT NULL",
            "SELECT 1 FROM StoreProducts WHERE StoreProductCode IS NOT NULL GROUP BY StoreId, StoreProductCode HAVING COUNT(*) > 1 LIMIT 1;",
            ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_PriceObservations_StoreProductId ON PriceObservations (StoreProductId);",
            ct);
    }

    private static async Task EnsureSqliteUniqueIndexAsync(
        ShopDbContext db,
        string indexName,
        string tableName,
        string columns,
        string? filter,
        string duplicateQuery,
        CancellationToken ct)
    {
        if (await QueryReturnsRowsAsync(db, duplicateQuery, ct))
            return;

        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS {indexName};", ct);
        var filterClause = filter is null ? "" : $" WHERE {filter}";
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {tableName} ({columns}){filterClause};",
            ct);
    }

    private static async Task<bool> QueryReturnsRowsAsync(
        ShopDbContext db,
        string commandText,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            return await command.ExecuteScalarAsync(ct) is not null;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        ShopDbContext db,
        string tableName,
        string columnName,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName.Replace("'", "''")}');";

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
