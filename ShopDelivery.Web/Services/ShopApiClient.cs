using System.Net.Http.Json;
using ShopDelivery.Shared;

namespace ShopDelivery.Web.Services;

public sealed class ShopApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<IReadOnlyList<Product>>("api/products", ct) ?? [];

    public async Task<ChatReply> AskAssistantAsync(string message, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/assistant", new ChatRequest(message), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatReply>(ct))!;
    }
}