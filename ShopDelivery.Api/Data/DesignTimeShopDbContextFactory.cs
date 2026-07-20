using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopDelivery.Api.Data;

public sealed class DesignTimeShopDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ShopDbContext>();
        if (string.Equals(
                Environment.GetEnvironmentVariable("SHOPDELIVERY_EF_PROVIDER"),
                "SqlServer",
                StringComparison.OrdinalIgnoreCase))
        {
            builder.UseSqlServer(
                Environment.GetEnvironmentVariable("SHOPDELIVERY_EF_CONNECTION")
                ?? "Server=(localdb)\\mssqllocaldb;Database=ShopDeliveryDesign;Trusted_Connection=True;");
        }
        else
        {
            builder.UseSqlite(
                Environment.GetEnvironmentVariable("SHOPDELIVERY_EF_CONNECTION")
                ?? "Data Source=shopdelivery.db");
        }

        return new ShopDbContext(builder.Options);
    }
}
