using Microsoft.EntityFrameworkCore;

namespace ShopDelivery.Api.Data;

public static class DatabaseSchemaInitializer
{
    public static async Task EnsureAsync(ShopDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsInMemory())
        {
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        await db.Database.MigrateAsync(ct);
    }
}
