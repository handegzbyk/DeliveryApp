using Microsoft.EntityFrameworkCore;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Data;

public sealed class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Milk", Description = "Fresh dairy milk", Price = 3.49m, ImageUrl = "https://example.com/milk.jpg" },
            new Product { Id = 2, Name = "Bread", Description = "Fresh baked bread", Price = 2.49m, ImageUrl = "https://example.com/bread.jpg" },
            new Product { Id = 3, Name = "Eggs", Description = "Organic eggs", Price = 4.29m, ImageUrl = "https://example.com/eggs.jpg" }
        );

        base.OnModelCreating(modelBuilder);
    }
}
