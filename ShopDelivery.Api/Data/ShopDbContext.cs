using Microsoft.EntityFrameworkCore;

namespace ShopDelivery.Api.Data;

public sealed class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Money columns: pin precision so SQL Server doesn't warn about / silently truncate decimals.
        modelBuilder.Entity<Receipt>().Property(receipt => receipt.Total).HasPrecision(18, 2);
        modelBuilder.Entity<PriceObservation>().Property(observation => observation.Price).HasPrecision(18, 2);

        modelBuilder.Entity<Product>().Property(product => product.OpenFoodFactsCode).HasMaxLength(64);
        modelBuilder.Entity<Product>().HasIndex(product => product.OpenFoodFactsCode);

        modelBuilder.Entity<StoreProduct>().Property(storeProduct => storeProduct.Name).HasMaxLength(450);
        modelBuilder.Entity<StoreProduct>().Property(storeProduct => storeProduct.StoreProductCode).HasMaxLength(128);
        modelBuilder.Entity<StoreProduct>().HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.Name });
        modelBuilder.Entity<StoreProduct>().HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.StoreProductCode });

        modelBuilder.Entity<PriceObservation>()
            .HasOne(observation => observation.StoreProduct)
            .WithMany(storeProduct => storeProduct.PriceObservations)
            .HasForeignKey(observation => observation.StoreProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
