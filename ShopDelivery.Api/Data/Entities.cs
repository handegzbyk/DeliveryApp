namespace ShopDelivery.Api.Data;

public enum ProductStatus
{
    Confirmed,
    Provisional,
    ReviewRequired,
    Merged,
    Rejected,
}

public enum StoreAliasStatus
{
    Confirmed,
    Provisional,
    Disputed,
    Rejected,
}

public enum ProductReviewStatus
{
    Pending,
    ConfirmedNew,
    Corrected,
    Merged,
    Rejected,
}

public enum MatchDecisionKind
{
    Created,
    Confirmed,
    Rejected,
    Corrected,
    Merged,
}

public sealed class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class Product
{
    public int Id { get; set; }
    public string? CatalogKey { get; set; }
    public string? Gtin { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string? Category { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Confirmed;
    public string? SourceUrl { get; set; }
    public int? MergedIntoProductId { get; set; }
    public Product? MergedIntoProduct { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<StoreProduct> StoreProducts { get; set; } = new List<StoreProduct>();
    public ICollection<PriceObservation> PriceObservations { get; set; } = new List<PriceObservation>();
}

// Compressed image bytes are deliberately isolated from Product so ordinary catalog/matching
// queries cannot accidentally materialize the BLOBs.
public sealed class ImageAsset
{
    public long Id { get; set; }
    public string ContentHash { get; set; } = "";
    public string MimeType { get; set; } = "application/octet-stream";
    public int Width { get; set; }
    public int Height { get; set; }
    public int ByteLength { get; set; }
    public string StorageProvider { get; set; } = "Database";
    public byte[] Data { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ProductImage> Products { get; set; } = new List<ProductImage>();
}

public sealed class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public long ImageAssetId { get; set; }
    public ImageAsset ImageAsset { get; set; } = null!;
    public bool IsPrimary { get; set; } = true;
    public string? SourceUrl { get; set; }
}

public sealed class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string BranchIdentifier { get; set; } = "";
    public string? Location { get; set; }

    public ICollection<StoreProduct> Products { get; set; } = new List<StoreProduct>();
    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}

// A store/branch-specific printed or catalog name learned for one canonical product.
public sealed class StoreProduct
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? StoreProductCode { get; set; }
    public StoreAliasStatus Status { get; set; } = StoreAliasStatus.Confirmed;
    public int ConfirmationCount { get; set; }
    public int RejectionCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PriceObservation> PriceObservations { get; set; } = new List<PriceObservation>();
    public ICollection<StoreProductMatchHistory> History { get; set; } = new List<StoreProductMatchHistory>();
}

public sealed class StoreProductMatchHistory
{
    public long Id { get; set; }
    public int StoreProductId { get; set; }
    public StoreProduct StoreProduct { get; set; } = null!;
    public int? PreviousProductId { get; set; }
    public Product? PreviousProduct { get; set; }
    public int? NewProductId { get; set; }
    public Product? NewProduct { get; set; }
    public MatchDecisionKind Decision { get; set; }
    public string? CustomerIdHash { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Ambiguous scraped/manual/OCR submissions stay here until an administrator confirms or merges
// them. ProposedProduct is usable for the customer's receipt while it remains provisional.
public sealed class ProductReviewItem
{
    public long Id { get; set; }
    public int ProposedProductId { get; set; }
    public Product ProposedProduct { get; set; } = null!;
    public int? CandidateProductId { get; set; }
    public Product? CandidateProduct { get; set; }
    public string RawName { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string SourceType { get; set; } = "Receipt";
    public string? SourceReference { get; set; }
    public ProductReviewStatus Status { get; set; } = ProductReviewStatus.Pending;
    public string? SubmittedByCustomerIdHash { get; set; }
    public string? AdminNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class Receipt
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = "";
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public DateTimeOffset PurchasedAt { get; set; }
    public decimal Total { get; set; }
    public ICollection<PriceObservation> Lines { get; set; } = new List<PriceObservation>();
}

public sealed class CustomerBudget
{
    public string CustomerId { get; set; } = "";
    public decimal MonthlyBudget { get; set; } = 500m;
}

public sealed class PriceObservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? StoreProductId { get; set; }
    public StoreProduct? StoreProduct { get; set; }
    public int ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public string RawText { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}
