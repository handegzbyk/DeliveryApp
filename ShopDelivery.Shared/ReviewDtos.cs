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
    int? MatchedProductId,              // set = confidently matched (score ≥ threshold)
    List<ProductCandidate> Candidates,  // sliding gallery when unmatched
    List<BrandOption> BrandOptions);

public record ProductCandidate(
    int? ProductId,      // existing product; null = "create new"
    string Name,
    string? ImageUrl,
    double Score);

public record BrandOption(int? BrandId, string Name);

public record ConfirmRequest(
    string StoreName,
    DateTimeOffset PurchasedAt,
    decimal Total,
    List<ConfirmLine> Lines);

public record ConfirmLine(
    string RawText, decimal Price, int Quantity,
    int? ProductId, string ProductName, int? BrandId, string? NewBrandName,
    bool LearnStoreAlias = true);
