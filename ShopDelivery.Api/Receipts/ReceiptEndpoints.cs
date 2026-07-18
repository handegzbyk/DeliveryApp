using Microsoft.EntityFrameworkCore;   // ToListAsync, FirstOrDefaultAsync, FirstAsync
using ShopDelivery.Ai;
using ShopDelivery.Api.Data;
using ShopDelivery.Shared;       // ShopDbContext + entities (Store, Product, Brand, Receipt, PriceObservation)

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
        .DisableAntiforgery()
        .WithName("ScanReceipt");

       // NEW: build review model from OCR result + suggestions
        app.MapPost("/api/receipts/review", async (
                IFormFile file, ReceiptExtractor extractor, ProductMatcher matcher, ShopDbContext db, CancellationToken ct) =>
            {
                if (file.Length == 0) return Results.BadRequest("Empty file.");

                await using var stream = file.OpenReadStream();
                var scan = await extractor.ExtractAsync(stream, ct);
                var brands = await db.Brands.ToListAsync(ct);
                var brandOptions = brands.Select(b => new BrandOption(b.Id, b.Name))
                                        .Prepend(new BrandOption(null, "— no brand —")).ToList();

                var lines = new List<ReviewLine>();
                foreach (var l in scan.Lines)
                {
                   var (matchedId, candidates) = await matcher.TopMatchesAsync(l.Description, ct);
                    lines.Add(new ReviewLine(l.Description, l.Price ?? 0m, l.Quantity ?? 1,
                        matchedId, candidates, brandOptions));
                }

                return Results.Ok(new ScanReviewResponse(
                    scan.MerchantName ?? "Unknown store",
                    scan.PurchasedOn is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : DateTimeOffset.UtcNow,
                    scan.Total ?? 0m,
                    lines));
            })
            .DisableAntiforgery().WithName("ReviewReceipt");

        }
}