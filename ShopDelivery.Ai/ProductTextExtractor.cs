using Azure;
using Azure.AI.DocumentIntelligence;

namespace ShopDelivery.Ai;

public sealed class ProductTextExtractor(DocumentIntelligenceClient client)
{
    public async Task<string> ExtractAsync(Stream image, CancellationToken ct = default)
    {
        var binary = await BinaryData.FromStreamAsync(image, ct);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            binary,
            cancellationToken: ct);
        return operation.Value.Content?.Trim() ?? "";
    }
}
