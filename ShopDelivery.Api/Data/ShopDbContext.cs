using Microsoft.EntityFrameworkCore;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Data;

public sealed class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

}
