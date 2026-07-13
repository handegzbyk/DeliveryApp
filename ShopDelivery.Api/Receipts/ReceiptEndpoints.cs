using ShopDelivery.Ai;

namespace ShopDelivery.Api.Receipts;

public static class ReceiptEndpoints
{
    public static void MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/receipts/scan", async (IFormFile file, ReceiptExtractor extractor, CancellationToken ct) =>
        {
            if (file.Length == 0)
                return Results.BadRequest("Empty file.");

            await using var stream = file.OpenReadStream();
            var scanned = await extractor.ExtractAsync(stream, ct);
            return Results.Ok(scanned);
        })
        .DisableAntiforgery()   // multipart upload from the WASM client
        .WithName("ScanReceipt");
    }
}