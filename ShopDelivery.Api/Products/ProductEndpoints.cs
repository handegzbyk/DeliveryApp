using Microsoft.EntityFrameworkCore;
using ShopDelivery.Ai;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Receipts;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Products;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/product-images/{imageId:long}", async (
                long imageId,
                HttpRequest request,
                HttpResponse response,
                IProductImageStore imageStore,
                CancellationToken ct) =>
            {
                if (!await imageStore.WriteResponseAsync(imageId, request, response, ct))
                    response.StatusCode = StatusCodes.Status404NotFound;
            })
            .AllowAnonymous()
            .WithName("GetProductImage");

        var products = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();

        products.MapGet("", async (
            HttpContext httpContext,
            CustomerIdentity customerIdentity,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
            var rows = await db.Products
                .AsNoTracking()
                .Where(product => product.Status == ProductStatus.Confirmed)
                .OrderBy(product => product.Name)
                .Select(product => new ProductRow(
                    product.Id,
                    product.Name,
                    product.Gtin,
                    product.Brand != null ? product.Brand.Name : null,
                    product.Category,
                    product.Status,
                    product.Images.Where(image => image.IsPrimary)
                        .Select(image => (long?)image.ImageAssetId)
                        .FirstOrDefault()))
                .ToListAsync(ct);
            var latestPrices = await LatestPricesAsync(customerId, db, ct);
            return Results.Ok(rows.Select(row => Map(row, httpContext.Request, latestPrices.GetValueOrDefault(row.Id))));
        });

        products.MapGet("/search", async (
            string q,
            HttpRequest request,
            ProductMatcher matcher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest("Search text is required.");
            return Results.Ok(await matcher.SearchAsync(q, request, ct));
        });

        products.MapPost("/search-image", async (
                IFormFile file,
                HttpRequest request,
                ProductTextExtractor? extractor,
                ProductMatcher matcher,
                CancellationToken ct) =>
            {
                if (extractor is null)
                    return Results.Problem("Image search is not available: Document Intelligence is not configured.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                if (file.Length == 0 || file.Length > 10_000_000)
                    return Results.BadRequest("Image must be between 1 byte and 10 MB.");
                await using var stream = file.OpenReadStream();
                var text = await extractor.ExtractAsync(stream, ct);
                if (string.IsNullOrWhiteSpace(text))
                    return Results.NotFound("No searchable text was found in the image.");
                return Results.Ok(await matcher.SearchAsync(text, request, ct));
            })
            .DisableAntiforgery();

        products.MapPost("/proposals", async (
            ProductProposalRequest proposal,
            HttpContext httpContext,
            CustomerIdentity customerIdentity,
            ProductMatcher matcher,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(proposal.Name))
                return Results.BadRequest("Product name is required.");

            var gtin = CatalogTextNormalizer.NormalizeGtin(proposal.Gtin);
            if (gtin is not null)
            {
                var byGtin = await db.Products.FirstOrDefaultAsync(
                    product => product.Gtin == gtin && product.Status == ProductStatus.Confirmed,
                    ct);
                if (byGtin is not null)
                {
                    var summary = await SummaryAsync(byGtin.Id, httpContext.Request, db, ct);
                    return Results.Ok(new ProductProposalResponse(false, null, summary, []));
                }
            }

            var search = await matcher.SearchAsync(proposal.Name, httpContext.Request, ct);
            if (search.ExactMatch || !proposal.CreateWhenUncertain)
                return Results.Ok(new ProductProposalResponse(false, null, null, search.Candidates));

            var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
            var product = new Product
            {
                Gtin = gtin,
                Name = proposal.Name.Trim(),
                NormalizedName = CatalogTextNormalizer.Normalize(proposal.Name),
                Category = NormalizeOptional(proposal.Category),
                Status = ProductStatus.ReviewRequired,
            };
            if (!string.IsNullOrWhiteSpace(proposal.BrandName))
                product.Brand = await ResolveBrandAsync(proposal.BrandName, db, ct);
            var review = new ProductReviewItem
            {
                ProposedProduct = product,
                CandidateProductId = search.Candidates.FirstOrDefault()?.ProductId,
                RawName = proposal.Name.Trim(),
                NormalizedName = product.NormalizedName,
                SourceType = "Manual",
                SubmittedByCustomerIdHash = customerId,
            };
            db.ProductReviewItems.Add(review);
            await db.SaveChangesAsync(ct);
            return Results.Created(
                $"/api/admin/product-reviews/{review.Id}",
                new ProductProposalResponse(
                    true,
                    review.Id,
                    await SummaryAsync(product.Id, httpContext.Request, db, ct),
                    search.Candidates));
        });

        var admin = app.MapGroup("/api/admin")
            .WithTags("Catalog administration")
            .RequireAuthorization("CatalogAdmin");

        admin.MapPost("/products/{productId:int}/images", async (
                int productId,
                IFormFile file,
                HttpRequest request,
                IProductImageStore imageStore,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                if (file.Length == 0 || file.Length > 10_000_000)
                    return Results.BadRequest("Image must be between 1 byte and 10 MB.");
                var product = await db.Products
                    .Include(item => item.Images)
                    .FirstOrDefaultAsync(item => item.Id == productId
                                                 && item.Status != ProductStatus.Merged
                                                 && item.Status != ProductStatus.Rejected,
                        ct);
                if (product is null)
                    return Results.NotFound();

                await using var stream = file.OpenReadStream();
                var stored = await imageStore.SaveAsync(stream, ct);
                var imageId = stored.PendingEntity?.Id ?? stored.Id;
                var existingLink = stored.PendingEntity is null
                    ? product.Images.FirstOrDefault(link => link.ImageAssetId == stored.Id)
                    : null;
                foreach (var current in product.Images)
                    current.IsPrimary = false;
                if (existingLink is not null)
                {
                    existingLink.IsPrimary = true;
                    imageId = existingLink.ImageAssetId;
                }
                else
                {
                    var link = new ProductImage { Product = product, IsPrimary = true };
                    if (stored.PendingEntity is not null)
                        link.ImageAsset = stored.PendingEntity;
                    else
                        link.ImageAssetId = stored.Id;
                    product.Images.Add(link);
                }
                await db.SaveChangesAsync(ct);
                imageId = existingLink?.ImageAssetId
                          ?? product.Images.First(link => link.IsPrimary).ImageAssetId;

                return Results.Ok(new
                {
                    imageId,
                    imageUrl = ProductImageUrls.For(request, imageId),
                });
            })
            .DisableAntiforgery();

        admin.MapGet("/product-reviews", async (ShopDbContext db, CancellationToken ct) =>
        {
            var query = db.ProductReviewItems
                .AsNoTracking()
                .Where(item => item.Status == ProductReviewStatus.Pending)
                .Select(item => new AdminReviewItem(
                    item.Id,
                    item.ProposedProductId,
                    item.ProposedProduct.Name,
                    item.ProposedProduct.Gtin,
                    item.ProposedProduct.Brand != null ? item.ProposedProduct.Brand.Name : null,
                    item.ProposedProduct.Category,
                    item.ProposedProduct.Images.Any(),
                    item.CandidateProductId,
                    item.CandidateProduct != null ? item.CandidateProduct.Name : null,
                    item.RawName,
                    item.SourceType,
                    item.SourceReference,
                    item.CreatedAt));
            var reviews = db.Database.IsSqlite()
                ? (await query.ToListAsync(ct)).OrderBy(item => item.CreatedAt).ToList()
                : await query.OrderBy(item => item.CreatedAt).ToListAsync(ct);
            return Results.Ok(reviews);
        });

        admin.MapPost("/product-reviews/{reviewId:long}/decision", async (
            long reviewId,
            AdminReviewDecision decision,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var review = await db.ProductReviewItems
                .Include(item => item.ProposedProduct)
                .FirstOrDefaultAsync(item => item.Id == reviewId, ct);
            if (review is null || review.Status != ProductReviewStatus.Pending)
                return Results.NotFound();

            var action = decision.Action.Trim().ToLowerInvariant();
            var aliasReview = review.SourceType is "AliasCorrection" or "CatalogAliasConflict";
            switch (action)
            {
                case "confirm":
                    try
                    {
                        await ApplyCorrectionsAsync(review.ProposedProduct, decision, db, ct);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.Conflict(exception.Message);
                    }
                    if (aliasReview)
                    {
                        if (!await ApplyAliasDecisionAsync(review, acceptCorrection: true, MatchDecisionKind.Corrected, db, ct))
                            return Results.Conflict("The store alias for this review no longer exists.");
                        review.Status = ProductReviewStatus.Corrected;
                    }
                    else
                    {
                        review.ProposedProduct.Status = ProductStatus.Confirmed;
                        await db.StoreProducts
                            .Where(alias => alias.ProductId == review.ProposedProductId
                                            && alias.Status == StoreAliasStatus.Provisional)
                            .ExecuteUpdateAsync(
                                update => update.SetProperty(alias => alias.Status, StoreAliasStatus.Confirmed),
                                ct);
                        review.Status = ProductReviewStatus.ConfirmedNew;
                    }
                    break;
                case "merge":
                    if (decision.TargetProductId is not { } targetId || targetId == review.ProposedProductId)
                        return Results.BadRequest("A different target product is required for merge.");
                    var target = await db.Products.FirstOrDefaultAsync(
                        product => product.Id == targetId && product.Status == ProductStatus.Confirmed,
                        ct);
                    if (target is null)
                        return Results.BadRequest("Target product is unavailable.");
                    if (aliasReview
                        && !await ApplyAliasDecisionAsync(review, acceptCorrection: false, MatchDecisionKind.Merged, db, ct))
                    {
                        return Results.Conflict("The store alias for this review no longer exists.");
                    }
                    await MergeAsync(review.ProposedProduct, target, db, ct);
                    review.Status = ProductReviewStatus.Merged;
                    break;
                case "reject":
                    if (aliasReview)
                    {
                        if (!await ApplyAliasDecisionAsync(review, acceptCorrection: false, MatchDecisionKind.Rejected, db, ct))
                            return Results.Conflict("The store alias for this review no longer exists.");
                    }
                    else
                    {
                        review.ProposedProduct.Status = ProductStatus.Rejected;
                        await db.StoreProducts
                            .Where(alias => alias.ProductId == review.ProposedProductId)
                            .ExecuteUpdateAsync(
                                update => update.SetProperty(alias => alias.Status, StoreAliasStatus.Rejected),
                                ct);
                    }
                    review.Status = ProductReviewStatus.Rejected;
                    break;
                default:
                    return Results.BadRequest("Action must be confirm, merge, or reject.");
            }

            review.AdminNote = NormalizeOptional(decision.Note);
            review.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<bool> ApplyAliasDecisionAsync(
        ProductReviewItem review,
        bool acceptCorrection,
        MatchDecisionKind decision,
        ShopDbContext db,
        CancellationToken ct)
    {
        var aliasId = ParseAliasId(review.SourceReference);
        var alias = aliasId is { } id
            ? await db.StoreProducts.FirstOrDefaultAsync(item => item.Id == id, ct)
            : await db.StoreProducts.FirstOrDefaultAsync(
                item => item.NormalizedName == review.NormalizedName
                        && item.ProductId == review.CandidateProductId,
                ct);
        if (alias is null)
            return false;

        var previousProductId = alias.ProductId;
        var newProductId = acceptCorrection ? review.ProposedProductId : previousProductId;
        alias.ProductId = newProductId;
        alias.Status = StoreAliasStatus.Confirmed;
        alias.LastSeenAt = DateTimeOffset.UtcNow;
        db.StoreProductMatchHistory.Add(new StoreProductMatchHistory
        {
            StoreProductId = alias.Id,
            PreviousProductId = previousProductId,
            NewProductId = newProductId,
            Decision = decision,
            Note = acceptCorrection
                ? "Administrator accepted the customer/catalog correction."
                : "Administrator retained the existing mapping.",
        });
        return true;
    }

    private static long? ParseAliasId(string? sourceReference)
    {
        const string prefix = "store-alias:";
        return sourceReference?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
               && long.TryParse(sourceReference[prefix.Length..], out var id)
            ? id
            : null;
    }

    private static async Task ApplyCorrectionsAsync(
        Product product,
        AdminReviewDecision decision,
        ShopDbContext db,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(decision.CorrectedName))
        {
            product.Name = decision.CorrectedName.Trim();
            product.NormalizedName = CatalogTextNormalizer.Normalize(product.Name);
        }

        var gtin = CatalogTextNormalizer.NormalizeGtin(decision.CorrectedGtin);
        if (gtin is not null && await db.Products.AnyAsync(other => other.Id != product.Id && other.Gtin == gtin, ct))
            throw new InvalidOperationException("GTIN already belongs to another product.");
        product.Gtin = gtin ?? product.Gtin;
        product.Category = NormalizeOptional(decision.Category) ?? product.Category;
        if (!string.IsNullOrWhiteSpace(decision.BrandName))
            product.Brand = await ResolveBrandAsync(decision.BrandName, db, ct);
        product.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task MergeAsync(
        Product proposed,
        Product target,
        ShopDbContext db,
        CancellationToken ct)
    {
        await db.PriceObservations
            .Where(observation => observation.ProductId == proposed.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(observation => observation.ProductId, target.Id), ct);
        var aliases = await db.StoreProducts
            .AsNoTracking()
            .Where(alias => alias.ProductId == proposed.Id)
            .Select(alias => alias.Id)
            .ToListAsync(ct);
        foreach (var aliasId in aliases)
        {
            db.StoreProductMatchHistory.Add(new StoreProductMatchHistory
            {
                StoreProductId = aliasId,
                PreviousProductId = proposed.Id,
                NewProductId = target.Id,
                Decision = MatchDecisionKind.Merged,
                Note = "Administrator merged duplicate master products.",
            });
        }
        await db.StoreProducts
            .Where(alias => alias.ProductId == proposed.Id)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(alias => alias.ProductId, target.Id)
                    .SetProperty(alias => alias.Status, StoreAliasStatus.Confirmed),
                ct);

        var targetHasPrimary = await db.ProductImages.AnyAsync(
            image => image.ProductId == target.Id && image.IsPrimary,
            ct);
        var proposedImages = await db.ProductImages.Where(image => image.ProductId == proposed.Id).ToListAsync(ct);
        foreach (var image in proposedImages)
        {
            if (await db.ProductImages.AnyAsync(
                    targetImage => targetImage.ProductId == target.Id
                                   && targetImage.ImageAssetId == image.ImageAssetId,
                    ct))
            {
                db.ProductImages.Remove(image);
                continue;
            }
            image.ProductId = target.Id;
            if (targetHasPrimary)
                image.IsPrimary = false;
            else if (image.IsPrimary)
                targetHasPrimary = true;
        }

        proposed.Status = ProductStatus.Merged;
        proposed.MergedIntoProductId = target.Id;
        proposed.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task<Dictionary<int, decimal?>> LatestPricesAsync(
        string customerId,
        ShopDbContext db,
        CancellationToken ct)
    {
        var rows = await db.PriceObservations
            .AsNoTracking()
            .Where(observation => observation.Receipt.CustomerId == customerId)
            .Select(observation => new
            {
                observation.ProductId,
                observation.Price,
                observation.Receipt.PurchasedAt,
                observation.Id,
            })
            .ToListAsync(ct);
        return rows.GroupBy(row => row.ProductId).ToDictionary(
            group => group.Key,
            group => (decimal?)group.OrderByDescending(row => row.PurchasedAt)
                .ThenByDescending(row => row.Id)
                .First().Price);
    }

    private static async Task<ProductSummary?> SummaryAsync(
        int productId,
        HttpRequest request,
        ShopDbContext db,
        CancellationToken ct)
    {
        var row = await db.Products.AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => new ProductRow(
                product.Id,
                product.Name,
                product.Gtin,
                product.Brand != null ? product.Brand.Name : null,
                product.Category,
                product.Status,
                product.Images.Where(image => image.IsPrimary)
                    .Select(image => (long?)image.ImageAssetId).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row, request, null);
    }

    private static ProductSummary Map(ProductRow row, HttpRequest request, decimal? latestPrice) => new(
        row.Id,
        row.Name,
        row.Gtin,
        row.BrandName,
        row.Category,
        ProductImageUrls.For(request, row.ImageAssetId),
        row.ImageAssetId is not null,
        row.Status.ToString(),
        latestPrice);

    private static async Task<Brand> ResolveBrandAsync(
        string rawName,
        ShopDbContext db,
        CancellationToken ct)
    {
        var name = rawName.Trim();
        var normalized = CatalogTextNormalizer.Normalize(name);
        return await db.Brands.FirstOrDefaultAsync(brand => brand.NormalizedName == normalized, ct)
               ?? new Brand { Name = name, NormalizedName = normalized };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ProductRow(
        int Id,
        string Name,
        string? Gtin,
        string? BrandName,
        string? Category,
        ProductStatus Status,
        long? ImageAssetId);
}
