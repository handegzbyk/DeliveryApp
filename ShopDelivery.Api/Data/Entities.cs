public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? OpenFoodFactsCode { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public string? Category { get; set; }   // filled by enrichment
    public string? ImageUrl { get; set; }    // filled by enrichment

    public ICollection<StoreProduct> StoreProducts { get; set; } = new List<StoreProduct>();
    public ICollection<PriceObservation> PriceObservations { get; set; } = new List<PriceObservation>();
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // "Dollar Tree"
    public string? Location { get; set; }

    public ICollection<StoreProduct> Products { get; set; } = new List<StoreProduct>();
}

// Store-specific naming/SKU for one canonical product.
// Example: different stores can print the same OpenFoodFacts product under different receipt names.
public class StoreProduct
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Name { get; set; } = "";          // receipt/catalog name used by the store
    public string? StoreProductCode { get; set; }   // store SKU/item id when available

    public ICollection<PriceObservation> PriceObservations { get; set; } = new List<PriceObservation>();
}

public class Receipt
{
    public int Id { get; set; }
    // Stable, pseudonymous id derived server-side from the authenticated issuer + subject.
    public string CustomerId { get; set; } = "";
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public DateTimeOffset PurchasedAt { get; set; }
    public decimal Total { get; set; }

    public ICollection<PriceObservation> Lines { get; set; } = new List<PriceObservation>();
}

public class CustomerBudget
{
    public string CustomerId { get; set; } = "";
    public decimal MonthlyBudget { get; set; } = 500m;
}

// One scanned line = one price point for a product at a store/date
public class PriceObservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? StoreProductId { get; set; }
    public StoreProduct? StoreProduct { get; set; }
    public int ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;

    public string RawText { get; set; } = "";       // original OCR line, for audit
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}
