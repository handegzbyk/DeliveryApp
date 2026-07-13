namespace ShopDelivery.Shared;

// Raw structured output from OCR, before dedup/persistence.
public record ScannedReceipt(
    string? MerchantName,
    DateOnly? PurchasedOn,
    decimal? Total,
    IReadOnlyList<ScannedLine> Lines);

public record ScannedLine(
    string Description,
    int? Quantity,
    decimal? Price);