public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // canonical name, e.g. "Milk 1L"
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string? Category { get; set; }

    public ICollection<PriceObservation> Prices { get; set; } = new List<PriceObservation>();
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // "Dollar Tree"
    public string? Location { get; set; }
}

public class Receipt
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public DateTimeOffset PurchasedAt { get; set; }
    public decimal Total { get; set; }

    public ICollection<PriceObservation> Lines { get; set; } = new List<PriceObservation>();
}

// One scanned line = one price point for a product at a store/date
public class PriceObservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;

    public string RawText { get; set; } = "";       // original OCR line, for audit
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}