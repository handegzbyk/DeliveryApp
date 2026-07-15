namespace ShopDelivery.Shared;
public record ScanReviewResponse(
    string StoreName,
    DateTimeOffset PurchasedAt,
    decimal Total,
    List<ReviewLine> Lines);

public record ReviewLine(
    string RawText,
    decimal Price,
    int Quantity,
    int? SuggestedProductId,            // best existing match, or null
    string SuggestedName,               // pre-filled editable name
    List<BrandOption> BrandOptions);    // for the slider/carousel

public record BrandOption(int? BrandId, string Name);

// What the UI posts back on Confirm
public record ConfirmRequest(
    string StoreName,
    DateTimeOffset PurchasedAt,
    decimal Total,
    List<ConfirmLine> Lines);

public record ConfirmLine(
    string RawText,
    decimal Price,
    int Quantity,
    int? ProductId,          // set = match existing; null = create new
    string ProductName,      // used when creating new
    int? BrandId,
    string? NewBrandName);   // if user typed a brand not in the list