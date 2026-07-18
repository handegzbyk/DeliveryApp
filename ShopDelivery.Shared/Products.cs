namespace ShopDelivery.Shared;

public record ProductSummary(
    int Id,
    string Name,
    string? OpenFoodFactsCode,
    string? BrandName,
    string? Category,
    string? ImageUrl,
    decimal? LatestPrice);

public record ProductImportRequest(
    string? Query,
    string? StoreName = null,
    string? StoreProductName = null,
    string? StoreProductCode = null);

public record ProductSeedRequest(
    List<string>? Categories = null,
    string Country = "Germany",
    int PageSize = 25,
    int MaxPagesPerCategory = 1);

public record ProductSeedResponse(
    int Created,
    int Updated,
    int Skipped,
    List<string> Categories);
