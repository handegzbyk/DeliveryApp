using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ShopDelivery.Api.Enrichment;

public class OpenFoodFactsEnricher(HttpClient http, ILogger<OpenFoodFactsEnricher> logger) : IProductEnricher
{
    private const int MaxAttempts = 3;

    public async Task<ProductInfo?> EnrichAsync(string query, CancellationToken ct)
    {
        var products = await SearchAsync(query, 1, ct);
        return products.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ProductInfo>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var pageSize = Math.Clamp(maxResults, 1, 20);
        var url = $"/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&json=1&page_size={pageSize}";
        return await SearchUrlAsync(url, query, ct);
    }

    public Task<IReadOnlyList<ProductInfo>> SearchCategoryAsync(
        string categoryTag,
        string country,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(categoryTag))
            return Task.FromResult<IReadOnlyList<ProductInfo>>([]);

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedCountry = string.IsNullOrWhiteSpace(country) ? "Germany" : country.Trim();
        var url = "/api/v2/search"
                  + $"?categories_tags={Uri.EscapeDataString(categoryTag.Trim())}"
                  + $"&countries_tags_en={Uri.EscapeDataString(normalizedCountry)}"
                  + "&fields=code,product_name,product_name_de,generic_name,brands,categories,image_url"
                  + $"&page={normalizedPage}"
                  + $"&page_size={normalizedPageSize}"
                  + "&sort_by=popularity_key";

        return SearchUrlAsync(url, categoryTag, ct);
    }

    private async Task<IReadOnlyList<ProductInfo>> SearchUrlAsync(
        string url,
        string queryDescription,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var search = await response.Content.ReadFromJsonAsync<OffSearchResponse>(ct);
                return search?.Products?
                    .Select(Map)
                    .Where(product => !string.IsNullOrWhiteSpace(product.CanonicalName))
                    .ToList() ?? [];
            }

            // Retry transient failures (rate-limit / temporary outage) with a short backoff.
            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                logger.LogInformation(
                    "OpenFoodFacts {Status} for '{Query}' (attempt {Attempt}/{Max}); retrying",
                    (int)response.StatusCode,
                    queryDescription,
                    attempt,
                    MaxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
                continue;
            }

            // Non-transient or out of attempts — leave the product un-enriched; it can be retried later.
            logger.LogInformation(
                "OpenFoodFacts gave up on '{Query}' after {Attempt} attempt(s): {Status}",
                queryDescription,
                attempt,
                (int)response.StatusCode);
            return [];
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout          // 408
             or HttpStatusCode.TooManyRequests         // 429
             or HttpStatusCode.InternalServerError     // 500
             or HttpStatusCode.BadGateway              // 502
             or HttpStatusCode.ServiceUnavailable      // 503
             or HttpStatusCode.GatewayTimeout;         // 504

    private static ProductInfo Map(OffProduct product) => new(
        product.Code,
        FirstNonEmpty(product.ProductNameDe, product.ProductName, product.GenericName),
        product.Brands,
        product.Categories,
        product.ImageUrl);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private record OffSearchResponse(
        [property: JsonPropertyName("products")] List<OffProduct>? Products);

    // OpenFoodFacts serializes fields in snake_case; map them explicitly.
    private record OffProduct(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("product_name")] string? ProductName,
        [property: JsonPropertyName("product_name_de")] string? ProductNameDe,
        [property: JsonPropertyName("generic_name")] string? GenericName,
        [property: JsonPropertyName("brands")] string? Brands,
        [property: JsonPropertyName("categories")] string? Categories,
        [property: JsonPropertyName("image_url")] string? ImageUrl);
}
