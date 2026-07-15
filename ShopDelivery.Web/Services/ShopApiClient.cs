using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using ShopDelivery.Shared;

namespace ShopDelivery.Web.Services;

public sealed class ShopApiClient(HttpClient http)
{
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

    private record ConfirmResult(int Id);
}