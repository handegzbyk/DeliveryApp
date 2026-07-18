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
