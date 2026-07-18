namespace ShopDelivery.Api.Enrichment;

public record ProductInfo(
    string? ExternalId,
    string? CanonicalName,
    string? BrandName,
    string? Category,
    string? ImageUrl);

public interface IProductEnricher
{
    Task<ProductInfo?> EnrichAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<ProductInfo>> SearchAsync(string query, int maxResults, CancellationToken ct);
    Task<IReadOnlyList<ProductInfo>> SearchCategoryAsync(
        string categoryTag,
        string country,
        int page,
        int pageSize,
        CancellationToken ct);
}
