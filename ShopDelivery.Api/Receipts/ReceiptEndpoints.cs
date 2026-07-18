using Microsoft.EntityFrameworkCore;   // ToListAsync, FirstOrDefaultAsync, FirstAsync
using ShopDelivery.Ai;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;
using ShopDelivery.Shared;       // review DTOs (ConfirmRequest, ReviewLine, …)
// NOTE: the persisted entities (Store, Product, Brand, Receipt, PriceObservation) live in the
// global namespace (Data/Entities.cs), so they are referenced unqualified below.

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

        // NEW: persist a reviewed receipt, then enqueue new products for background enrichment.
        app.MapPost("/api/receipts/confirm", async (
                ConfirmRequest req, ShopDbContext db, IEnrichmentQueue queue, CancellationToken ct) =>
            {
                if (req.Lines is null || req.Lines.Count == 0)
                    return Results.BadRequest("No lines to confirm.");

                // Store: reuse by name (case-insensitive) or create.
                var storeName = string.IsNullOrWhiteSpace(req.StoreName) ? "Unknown store" : req.StoreName.Trim();
                var store = await db.Stores
                    .FirstOrDefaultAsync(s => s.Name.ToLower() == storeName.ToLower(), ct);
                store ??= new Store { Name = storeName };

                var receipt = new Receipt
                {
                    Store = store,
                    PurchasedAt = req.PurchasedAt,
                    Total = req.Total,
                };
                db.Receipts.Add(receipt);

                // Cache brands created within this request so repeated names don't duplicate.
                var newBrands = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase);
                var createdProducts = new List<Product>();

                foreach (var line in req.Lines)
                {
                    Product product;
                    if (line.ProductId is { } existingId)
                    {
                        // Existing product picked in review.
                        product = await db.Products.FirstAsync(p => p.Id == existingId, ct);
                    }
                    else
                    {
                        product = new Product
                        {
                            Name = string.IsNullOrWhiteSpace(line.ProductName)
                                ? line.RawText.Trim()
                                : line.ProductName.Trim(),
                            Brand = await ResolveBrandAsync(line, db, newBrands, ct),
                        };
                        db.Products.Add(product);
                        createdProducts.Add(product);
                    }

                    receipt.Lines.Add(new PriceObservation
                    {
                        Product = product,
                        RawText = line.RawText,
                        Price = line.Price,
                        Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                    });
                }

                await db.SaveChangesAsync(ct);

                // Fire-and-forget enrichment for the products we just created (ids now assigned).
                foreach (var p in createdProducts)
                    await queue.EnqueueAsync(p.Id, ct);

                return Results.Ok(new { receipt.Id });
            })
            .DisableAntiforgery().WithName("ConfirmReceipt");
        }

    // Resolve the brand for a new product: existing id, reused/new name, or none.
    private static async Task<Brand?> ResolveBrandAsync(
        ConfirmLine line, ShopDbContext db, Dictionary<string, Brand> newBrands, CancellationToken ct)
    {
        if (line.BrandId is { } brandId)
            return await db.Brands.FirstOrDefaultAsync(b => b.Id == brandId, ct);

        if (string.IsNullOrWhiteSpace(line.NewBrandName))
            return null;

        var name = line.NewBrandName.Trim();
        if (newBrands.TryGetValue(name, out var cached))
            return cached;

        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Name.ToLower() == name.ToLower(), ct)
                    ?? new Brand { Name = name };
        newBrands[name] = brand;
        return brand;
    }
}