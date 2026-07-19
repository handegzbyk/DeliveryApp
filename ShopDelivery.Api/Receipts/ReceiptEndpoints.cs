using Microsoft.EntityFrameworkCore;   // ToListAsync, FirstOrDefaultAsync, FirstAsync
using ShopDelivery.Ai;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;
using ShopDelivery.Api.Products;
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
        .RequireAuthorization()
        .WithName("ScanReceipt");

        // NEW: build review model from OCR result + suggestions
        app.MapPost("/api/receipts/review", async (
                IFormFile file,
                ReceiptExtractor extractor,
                ProductMatcher matcher,
                ProductCandidateSeeder productSeeder,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                if (file.Length == 0) return Results.BadRequest("Empty file.");

                await using var stream = file.OpenReadStream();
                var scan = await extractor.ExtractAsync(stream, ct);
                var brands = await db.Brands.ToListAsync(ct);
                var brandOptions = brands.Select(brand => new BrandOption(brand.Id, brand.Name))
                                        .Prepend(new BrandOption(null, "— no brand —")).ToList();
                var storeName = scan.MerchantName ?? "Unknown store";

                var lines = new List<ReviewLine>();
                foreach (var scannedLine in scan.Lines)
                {
                    var (matchedId, candidates) = await matcher.TopMatchesAsync(scannedLine.Description, storeName, ct);
                    if (matchedId is null)
                    {
                        await productSeeder.SeedFromReceiptLineAsync(scannedLine.Description, ct);
                        (matchedId, candidates) = await matcher.TopMatchesAsync(scannedLine.Description, storeName, ct);
                    }

                    lines.Add(new ReviewLine(scannedLine.Description, scannedLine.Price ?? 0m, scannedLine.Quantity ?? 1,
                        matchedId, candidates, brandOptions));
                }

                return Results.Ok(new ScanReviewResponse(
                    storeName,
                    scan.PurchasedOn is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : DateTimeOffset.UtcNow,
                    scan.Total ?? 0m,
                    lines));
            })
            .DisableAntiforgery()
            .RequireAuthorization()
            .WithName("ReviewReceipt");

        // NEW: persist a reviewed receipt, then enqueue new products for background enrichment.
        app.MapPost("/api/receipts/confirm", async (
                ConfirmRequest req,
                HttpContext httpContext,
                CustomerIdentity customerIdentity,
                ShopDbContext db,
                IEnrichmentQueue queue,
                CancellationToken ct) =>
            {
                if (req.Lines is null || req.Lines.Count == 0)
                    return Results.BadRequest("No lines to confirm.");

                // Store: reuse by name (case-insensitive) or create.
                var storeName = string.IsNullOrWhiteSpace(req.StoreName) ? "Unknown store" : req.StoreName.Trim();
                var store = await db.Stores
                    .FirstOrDefaultAsync(store => store.Name.ToLower() == storeName.ToLower(), ct);
                store ??= new Store { Name = storeName };

                var receipt = new Receipt
                {
                    CustomerId = customerIdentity.GetRequiredCustomerId(httpContext.User),
                    Store = store,
                    PurchasedAt = req.PurchasedAt,
                    Total = req.Total,
                };
                db.Receipts.Add(receipt);

                // Cache brands created within this request so repeated names don't duplicate.
                var newBrands = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase);
                var storeProductAliases = new Dictionary<string, StoreProduct>(StringComparer.OrdinalIgnoreCase);
                var createdProducts = new List<Product>();
                var genericProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

                foreach (var line in req.Lines)
                {
                    Product product;
                    if (line.ProductId is { } existingId)
                    {
                        // Existing product picked in review.
                        product = await db.Products.FirstAsync(product => product.Id == existingId, ct);
                    }
                    else
                    {
                        var productName = string.IsNullOrWhiteSpace(line.ProductName)
                            ? line.RawText.Trim()
                            : line.ProductName.Trim();

                        if (!line.LearnStoreAlias)
                        {
                            product = await ResolveGenericProductAsync(productName, db, genericProducts, ct);
                        }
                        else
                        {
                            product = new Product
                            {
                                Name = productName,
                                Brand = await ResolveBrandAsync(line, db, newBrands, ct),
                            };
                            db.Products.Add(product);
                            createdProducts.Add(product);
                        }
                    }

                    var storeProduct = await ResolveStoreProductAsync(line, store, product, db, storeProductAliases, ct);
                    receipt.Lines.Add(new PriceObservation
                    {
                        Product = product,
                        StoreProduct = storeProduct,
                        RawText = line.RawText,
                        Price = line.Price,
                        Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                    });
                }

                await db.SaveChangesAsync(ct);

                // Fire-and-forget enrichment for the products we just created (ids now assigned).
                foreach (var createdProduct in createdProducts)
                    await queue.EnqueueAsync(createdProduct.Id, ct);

                return Results.Ok(new { receipt.Id });
            })
            .DisableAntiforgery()
            .RequireAuthorization()
            .WithName("ConfirmReceipt");
        }

    // Resolve the brand for a new product: existing id, reused/new name, or none.
    private static async Task<Brand?> ResolveBrandAsync(
        ConfirmLine line, ShopDbContext db, Dictionary<string, Brand> newBrands, CancellationToken ct)
    {
        if (line.BrandId is { } brandId)
            return await db.Brands.FirstOrDefaultAsync(brand => brand.Id == brandId, ct);

        if (string.IsNullOrWhiteSpace(line.NewBrandName))
            return null;

        var name = line.NewBrandName.Trim();
        if (newBrands.TryGetValue(name, out var cached))
            return cached;

        var brand = await db.Brands.FirstOrDefaultAsync(brand => brand.Name.ToLower() == name.ToLower(), ct)
                    ?? new Brand { Name = name };
        newBrands[name] = brand;
        return brand;
    }

    private static async Task<Product> ResolveGenericProductAsync(
        string productName,
        ShopDbContext db,
        Dictionary<string, Product> genericProducts,
        CancellationToken ct)
    {
        if (genericProducts.TryGetValue(productName, out var cached))
            return cached;

        var normalizedName = productName.ToLower();
        var product = await db.Products.FirstOrDefaultAsync(
            candidate => candidate.BrandId == null
                         && candidate.OpenFoodFactsCode == null
                         && candidate.ImageUrl == ProductImages.Generic
                         && candidate.Name.ToLower() == normalizedName,
            ct);

        if (product is null)
        {
            product = new Product
            {
                Name = productName,
                ImageUrl = ProductImages.Generic,
            };
            db.Products.Add(product);
        }

        genericProducts[productName] = product;
        return product;
    }

    // Learn the store-specific receipt/catalog name for the confirmed canonical product.
    private static async Task<StoreProduct?> ResolveStoreProductAsync(
        ConfirmLine line,
        Store store,
        Product product,
        ShopDbContext db,
        Dictionary<string, StoreProduct> storeProductAliases,
        CancellationToken ct)
    {
        // "Not this" still creates a product and price observation for the customer's budget,
        // but it must not replace the globally learned store alias with a generic fallback.
        if (!line.LearnStoreAlias)
            return null;

        var aliasName = string.IsNullOrWhiteSpace(line.RawText)
            ? (string.IsNullOrWhiteSpace(line.ProductName) ? product.Name : line.ProductName.Trim())
            : line.RawText.Trim();

        if (storeProductAliases.TryGetValue(aliasName, out var cached))
        {
            cached.Product = product;
            return cached;
        }

        StoreProduct? storeProduct = null;
        if (store.Id > 0)
        {
            var normalizedAliasName = aliasName.ToLower();
            storeProduct = await db.StoreProducts.FirstOrDefaultAsync(
                alias => alias.StoreId == store.Id && alias.Name.ToLower() == normalizedAliasName,
                ct);
        }

        if (storeProduct is null)
        {
            storeProduct = new StoreProduct
            {
                Store = store,
                Product = product,
                Name = aliasName,
            };
            db.StoreProducts.Add(storeProduct);
        }
        else
        {
            storeProduct.Product = product;
        }

        storeProductAliases[aliasName] = storeProduct;
        return storeProduct;
    }
}
