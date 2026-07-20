using Microsoft.EntityFrameworkCore;

namespace ShopDelivery.Api.Data;

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<StoreProductMatchHistory> StoreProductMatchHistory => Set<StoreProductMatchHistory>();
    public DbSet<ProductReviewItem> ProductReviewItems => Set<ProductReviewItem>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();
    public DbSet<CustomerBudget> CustomerBudgets => Set<CustomerBudget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.NormalizedName).HasMaxLength(200);
            entity.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(x => x.CatalogKey).HasMaxLength(64);
            entity.Property(x => x.Gtin).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(450);
            entity.Property(x => x.NormalizedName).HasMaxLength(450);
            entity.Property(x => x.Category).HasMaxLength(256);
            entity.Property(x => x.SourceUrl).HasMaxLength(2_048);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.CatalogKey).IsUnique().HasFilter("[CatalogKey] IS NOT NULL");
            entity.HasIndex(x => x.Gtin).IsUnique().HasFilter("[Gtin] IS NOT NULL");
            entity.HasIndex(x => new { x.NormalizedName, x.BrandId });
            entity.HasOne(x => x.MergedIntoProduct)
                .WithMany()
                .HasForeignKey(x => x.MergedIntoProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImageAsset>(entity =>
        {
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.MimeType).HasMaxLength(128);
            entity.Property(x => x.StorageProvider).HasMaxLength(32);
            entity.HasIndex(x => x.ContentHash).IsUnique();
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.Property(x => x.SourceUrl).HasMaxLength(2_048);
            entity.HasIndex(x => new { x.ProductId, x.ImageAssetId }).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.IsPrimary });
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.NormalizedName).HasMaxLength(200);
            entity.Property(x => x.BranchIdentifier).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(500);
            entity.HasIndex(x => new { x.NormalizedName, x.BranchIdentifier }).IsUnique();
        });

        modelBuilder.Entity<StoreProduct>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(450);
            entity.Property(x => x.NormalizedName).HasMaxLength(450);
            entity.Property(x => x.StoreProductCode).HasMaxLength(128);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => new { x.StoreId, x.NormalizedName }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.StoreProductCode })
                .IsUnique()
                .HasFilter("[StoreProductCode] IS NOT NULL");
            entity.HasOne(x => x.Product).WithMany(x => x.StoreProducts)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreProductMatchHistory>(entity =>
        {
            entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.CustomerIdHash).HasMaxLength(64);
            entity.Property(x => x.Note).HasMaxLength(1_000);
            entity.HasIndex(x => new { x.StoreProductId, x.CreatedAt });
            entity.HasOne(x => x.PreviousProduct).WithMany().HasForeignKey(x => x.PreviousProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.NewProduct).WithMany().HasForeignKey(x => x.NewProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductReviewItem>(entity =>
        {
            entity.Property(x => x.RawName).HasMaxLength(450);
            entity.Property(x => x.NormalizedName).HasMaxLength(450);
            entity.Property(x => x.SourceType).HasMaxLength(64);
            entity.Property(x => x.SourceReference).HasMaxLength(2_048);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.SubmittedByCustomerIdHash).HasMaxLength(64);
            entity.Property(x => x.AdminNote).HasMaxLength(1_000);
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasOne(x => x.ProposedProduct).WithMany().HasForeignKey(x => x.ProposedProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CandidateProduct).WithMany().HasForeignKey(x => x.CandidateProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.Property(x => x.CustomerId).HasMaxLength(64);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.CustomerId, x.PurchasedAt });
        });

        modelBuilder.Entity<CustomerBudget>(entity =>
        {
            entity.HasKey(x => x.CustomerId);
            entity.Property(x => x.CustomerId).HasMaxLength(64);
            entity.Property(x => x.MonthlyBudget).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PriceObservation>(entity =>
        {
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.HasOne(x => x.Product).WithMany(x => x.PriceObservations)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StoreProduct).WithMany(x => x.PriceObservations)
                .HasForeignKey(x => x.StoreProductId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
