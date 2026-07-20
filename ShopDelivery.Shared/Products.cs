namespace ShopDelivery.Shared;

public record ProductSummary(
    int Id,
    string Name,
    string? Gtin,
    string? BrandName,
    string? Category,
    string? ImageUrl,
    bool HasImage,
    string Status,
    decimal? LatestPrice);

public record ProductSearchResponse(
    string Query,
    bool ExactMatch,
    List<ProductCandidate> Candidates);

public record ProductProposalRequest(
    string Name,
    string? Gtin = null,
    string? BrandName = null,
    string? Category = null,
    bool CreateWhenUncertain = false);

public record ProductProposalResponse(
    bool Created,
    long? ReviewItemId,
    ProductSummary? Product,
    List<ProductCandidate> Candidates);

public record CatalogImportResponse(
    int CreatedProducts,
    int UpdatedProducts,
    int CreatedAliases,
    int ReviewItems,
    int StoredImages,
    long SourceImageBytes,
    long StoredImageBytes,
    int FailedImages);
