using Microsoft.EntityFrameworkCore;
using ShopDelivery.Ai;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Products;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Receipts;

public static class ReceiptEndpoints
{
    public static void MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/receipts/scan", async (
                IFormFile file,
                ReceiptExtractor? extractor,
                CancellationToken ct) =>
            {
                if (extractor is null)
                    return Results.Problem("Receipt scanning is not available: Document Intelligence is not configured.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                if (file.Length == 0)
                    return Results.BadRequest("Empty file.");
                await using var stream = file.OpenReadStream();
                return Results.Ok(await extractor.ExtractAsync(stream, ct));
            })
            .DisableAntiforgery()
            .RequireAuthorization()
            .WithName("ScanReceipt");

        app.MapPost("/api/receipts/review", async (
                IFormFile file,
                HttpRequest request,
                ReceiptExtractor? extractor,
                ProductMatcher matcher,
                CancellationToken ct) =>
            {
                if (extractor is null)
                    return Results.Problem("Receipt scanning is not available: Document Intelligence is not configured.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                if (file.Length == 0)
                    return Results.BadRequest("Empty file.");

                await using var stream = file.OpenReadStream();
                var scan = await extractor.ExtractAsync(stream, ct);
                var storeName = scan.MerchantName ?? "Unknown store";
                var lines = new List<ReviewLine>();
                foreach (var scannedLine in scan.Lines)
                {
                    var result = await matcher.TopMatchesAsync(
                        scannedLine.Description,
                        storeName,
                        request,
                        ct);
                    lines.Add(new ReviewLine(
                        scannedLine.Description,
                        scannedLine.Price ?? 0m,
                        scannedLine.Quantity ?? 1,
                        result.MatchedId,
                        result.Candidates));
                }

                return Results.Ok(new ScanReviewResponse(
                    storeName,
                    scan.PurchasedOn is { } date
                        ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                        : DateTimeOffset.UtcNow,
                    scan.Total ?? 0m,
                    lines));
            })
            .DisableAntiforgery()
            .RequireAuthorization()
            .WithName("ReviewReceipt");

        app.MapPost("/api/receipts/confirm", async (
                ConfirmRequest request,
                HttpContext httpContext,
                CustomerIdentity customerIdentity,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                if (request.Lines.Count == 0)
                    return Results.BadRequest("No lines to confirm.");

                var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
                var store = await ResolveStoreAsync(request.StoreName, db, ct);
                var receipt = new Receipt
                {
                    CustomerId = customerId,
                    StoreId = store.Id,
                    PurchasedAt = request.PurchasedAt,
                    Total = request.Total,
                };
                db.Receipts.Add(receipt);

                foreach (var line in request.Lines)
                {
                    var product = line.ProductId is { } productId
                        ? await db.Products.FirstOrDefaultAsync(
                            candidate => candidate.Id == productId
                                         && candidate.Status != ProductStatus.Rejected
                                         && candidate.Status != ProductStatus.Merged,
                            ct)
                        : null;
                    if (line.ProductId is not null && product is null)
                        return Results.BadRequest($"Product {line.ProductId} is unavailable.");

                    if (product is null)
                    {
                        product = await CreateProvisionalProductAsync(line, customerId, db, ct);
                    }

                    var storeProduct = await LearnOrReviewAliasAsync(
                        store,
                        product,
                        line,
                        customerId,
                        db,
                        ct);
                    receipt.Lines.Add(new PriceObservation
                    {
                        Product = product,
                        StoreProduct = storeProduct,
                        RawText = line.RawText.Trim(),
                        Price = line.Price,
                        Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                    });
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new { receipt.Id });
            })
            .RequireAuthorization()
            .WithName("ConfirmReceipt");
    }

    private static async Task<Store> ResolveStoreAsync(
        string rawName,
        ShopDbContext db,
        CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Unknown store" : rawName.Trim();
        var normalized = CatalogTextNormalizer.Normalize(name);
        var store = await db.Stores.FirstOrDefaultAsync(
            candidate => candidate.NormalizedName == normalized && candidate.BranchIdentifier == "",
            ct);
        if (store is not null)
            return store;

        store = new Store { Name = name, NormalizedName = normalized };
        db.Stores.Add(store);
        await db.SaveChangesAsync(ct);
        return store;
    }

    private static async Task<Product> CreateProvisionalProductAsync(
        ConfirmLine line,
        string customerId,
        ShopDbContext db,
        CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(line.ProductName)
            ? CatalogTextNormalizer.DisplayName(line.RawText)
            : line.ProductName.Trim();
        var normalized = CatalogTextNormalizer.Normalize(name);
        var product = await db.Products.FirstOrDefaultAsync(
            candidate => candidate.NormalizedName == normalized
                         && candidate.BrandId == null
                         && (candidate.Status == ProductStatus.Provisional
                             || candidate.Status == ProductStatus.ReviewRequired),
            ct);
        if (product is null)
        {
            product = new Product
            {
                Name = name,
                NormalizedName = normalized,
                Status = ProductStatus.ReviewRequired,
            };
            db.Products.Add(product);
        }

        var alreadyPending = product.Id > 0 && await db.ProductReviewItems.AnyAsync(
            item => item.ProposedProductId == product.Id && item.Status == ProductReviewStatus.Pending,
            ct);
        if (!alreadyPending)
        {
            db.ProductReviewItems.Add(new ProductReviewItem
            {
                ProposedProduct = product,
                CandidateProductId = line.RejectedProductId,
                RawName = line.RawText.Trim(),
                NormalizedName = CatalogTextNormalizer.Normalize(line.RawText),
                SourceType = "Receipt",
                SubmittedByCustomerIdHash = customerId,
            });
        }

        return product;
    }

    private static async Task<StoreProduct?> LearnOrReviewAliasAsync(
        Store store,
        Product selectedProduct,
        ConfirmLine line,
        string customerId,
        ShopDbContext db,
        CancellationToken ct)
    {
        var aliasName = string.IsNullOrWhiteSpace(line.RawText) ? selectedProduct.Name : line.RawText.Trim();
        var normalizedAlias = CatalogTextNormalizer.Normalize(aliasName);
        var existing = await db.StoreProducts.FirstOrDefaultAsync(
            alias => alias.StoreId == store.Id && alias.NormalizedName == normalizedAlias,
            ct);

        if (existing is null)
        {
            var alias = new StoreProduct
            {
                StoreId = store.Id,
                Product = selectedProduct,
                Name = aliasName,
                NormalizedName = normalizedAlias,
                Status = selectedProduct.Status == ProductStatus.Confirmed
                    ? StoreAliasStatus.Confirmed
                    : StoreAliasStatus.Provisional,
                ConfirmationCount = 1,
            };
            db.StoreProducts.Add(alias);
            alias.History.Add(new StoreProductMatchHistory
            {
                NewProduct = selectedProduct,
                Decision = MatchDecisionKind.Created,
                CustomerIdHash = customerId,
            });
            return alias;
        }

        existing.LastSeenAt = DateTimeOffset.UtcNow;
        if (existing.ProductId == selectedProduct.Id)
        {
            existing.ConfirmationCount++;
            existing.History.Add(new StoreProductMatchHistory
            {
                PreviousProductId = selectedProduct.Id,
                NewProductId = selectedProduct.Id,
                Decision = MatchDecisionKind.Confirmed,
                CustomerIdHash = customerId,
            });
            return existing;
        }

        existing.RejectionCount++;
        existing.Status = StoreAliasStatus.Disputed;
        existing.History.Add(new StoreProductMatchHistory
        {
            PreviousProductId = existing.ProductId,
            NewProduct = selectedProduct,
            Decision = MatchDecisionKind.Rejected,
            CustomerIdHash = customerId,
            Note = "Customer selected a different product; administrator review required.",
        });
        db.ProductReviewItems.Add(new ProductReviewItem
        {
            ProposedProduct = selectedProduct,
            CandidateProductId = existing.ProductId,
            RawName = aliasName,
            NormalizedName = normalizedAlias,
            SourceType = "AliasCorrection",
            SourceReference = $"store-alias:{existing.Id}",
            SubmittedByCustomerIdHash = customerId,
        });
        return null;
    }
}
