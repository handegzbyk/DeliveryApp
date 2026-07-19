using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using ShopDelivery.Shared;

namespace ShopDelivery.Web.Services;

public sealed class ShopApiClient(HttpClient http)
{
    public async Task<ApiHealthResponse?> GetHealthAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<ApiHealthResponse>("api/health", ct);

    public async Task<IReadOnlyList<ProductSummary>> GetProductsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ProductSummary>>("api/products", ct) ?? [];

    public async Task<ProductSummary> ImportProductAsync(string query, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/products/import", new ProductImportRequest(query), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductSummary>(cancellationToken: ct))!;
    }

    public async Task<ProductSeedResponse> SeedProductCatalogAsync(CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/products/seed", new ProductSeedRequest(), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductSeedResponse>(cancellationToken: ct))!;
    }

    public async Task<ScannedReceipt?> ScanReceiptAsync(
        Stream image, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(image);
        content.Add(fileContent, "file", fileName);

        var response = await http.PostAsync("api/receipts/scan", content, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ScannedReceipt>(ct);
    }

    public async Task<ScanReviewResponse> ReviewAsync(IBrowserFile file, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024, ct); // 10 MB cap
        content.Add(new StreamContent(stream), "file", file.Name);

        var resp = await http.PostAsync("api/receipts/review", content, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ScanReviewResponse>(cancellationToken: ct))!;
    }

    public async Task<int> ConfirmAsync(ConfirmRequest request, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/receipts/confirm", request, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ConfirmResult>(cancellationToken: ct);
        return result!.Id;
    }

    public async Task<BasketExpenseResponse> GetReceiptExpenseAsync(int receiptId, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<BasketExpenseResponse>($"api/receipts/{receiptId}/expense", ct))!;

    public async Task<BasketExpenseResponse?> GetLatestExpenseAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/expenses/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BasketExpenseResponse>(cancellationToken: ct);
    }

    private record ConfirmResult(int Id);
}

public record ApiHealthResponse(string Status, DateTimeOffset At);
