namespace ShopDelivery.Api.Enrichment;

public record ProductInfo(
    string? CanonicalName,
    string? BrandName,
    string? Category,
    string? ImageUrl);

public interface IProductEnricher
{
    Task<ProductInfo?> EnrichAsync(string query, CancellationToken ct);
}