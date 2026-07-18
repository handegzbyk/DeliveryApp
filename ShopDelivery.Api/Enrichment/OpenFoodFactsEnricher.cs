using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ShopDelivery.Api.Enrichment;

public class OpenFoodFactsEnricher(HttpClient http, ILogger<OpenFoodFactsEnricher> logger) : IProductEnricher
{
    private const int MaxAttempts = 3;

    public async Task<ProductInfo?> EnrichAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var url = $"/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&json=1&page_size=1";

        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var search = await response.Content.ReadFromJsonAsync<OffSearchResponse>(ct);
                var first = search?.Products?.FirstOrDefault();
                return first is null ? null : Map(first);
            }

            // Retry transient failures (rate-limit / temporary outage) with a short backoff.
            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                logger.LogInformation("OpenFoodFacts {Status} for '{Query}' (attempt {Attempt}/{Max}); retrying",
                    (int)response.StatusCode, query, attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
                continue;
            }

            // Non-transient or out of attempts — leave the product un-enriched; it can be retried later.
            logger.LogInformation("OpenFoodFacts gave up on '{Query}' after {Attempt} attempt(s): {Status}",
                query, attempt, (int)response.StatusCode);
            return null;
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout          // 408
             or HttpStatusCode.TooManyRequests         // 429
             or HttpStatusCode.InternalServerError     // 500
             or HttpStatusCode.BadGateway              // 502
             or HttpStatusCode.ServiceUnavailable      // 503
             or HttpStatusCode.GatewayTimeout;         // 504

    private static ProductInfo Map(OffProduct p) => new(
        p.ProductName, p.Brands, p.Categories, p.ImageUrl);

    private record OffSearchResponse(
        [property: JsonPropertyName("products")] List<OffProduct>? Products);

    // OpenFoodFacts serializes fields in snake_case; map them explicitly.
    private record OffProduct(
        [property: JsonPropertyName("product_name")] string? ProductName,
        [property: JsonPropertyName("brands")] string? Brands,
        [property: JsonPropertyName("categories")] string? Categories,
        [property: JsonPropertyName("image_url")] string? ImageUrl);
}