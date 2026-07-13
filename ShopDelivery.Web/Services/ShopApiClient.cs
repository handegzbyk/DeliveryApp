using System.Net.Http.Json;
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
}