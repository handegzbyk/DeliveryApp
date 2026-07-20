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
    int? MatchedProductId,
    List<ProductCandidate> Candidates);

public record ProductCandidate(
    int? ProductId,
    string Name,
    string? ImageUrl,
    double Score,
    string MatchReason = "Name match",
    bool RequiresAdminReview = false);

public record ConfirmRequest(
    string StoreName,
    DateTimeOffset PurchasedAt,
    decimal Total,
    List<ConfirmLine> Lines);

public record ConfirmLine(
    string RawText,
    decimal Price,
    int Quantity,
    int? ProductId,
    string ProductName,
    bool CreateProvisional = false,
    int? RejectedProductId = null);

public record AdminReviewItem(
    long Id,
    int ProposedProductId,
    string ProposedProductName,
    string? ProposedGtin,
    string? ProposedBrandName,
    string? ProposedCategory,
    bool HasImage,
    int? CandidateProductId,
    string? CandidateProductName,
    string RawName,
    string SourceType,
    string? SourceReference,
    DateTimeOffset CreatedAt);

public record AdminReviewDecision(
    string Action,
    int? TargetProductId = null,
    string? CorrectedName = null,
    string? CorrectedGtin = null,
    string? BrandName = null,
    string? Category = null,
    string? Note = null);
