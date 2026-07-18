using System.Net.Http.Json;

namespace ShopDelivery.Api.Enrichment;

public class OpenFoodFactsEnricher(HttpClient http) : IProductEnricher
{
    public async Task<ProductInfo?> EnrichAsync(string query, CancellationToken ct)
    {
        var search = await http.GetFromJsonAsync<OffSearchResponse>(
            $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&json=1&page_size=1", ct);
        var first = search?.Products?.FirstOrDefault();
        return first is null ? null : Map(first);

    }

    private static ProductInfo Map(OffProduct p) => new(
        p.ProductName, p.Brands, p.Categories, p.ImageUrl);

    private record OffProductResponse(OffProduct? Product);
    private record OffSearchResponse(List<OffProduct>? Products);
    private record OffProduct(
        string? ProductName, string? Brands, string? Categories, string? ImageUrl);
}