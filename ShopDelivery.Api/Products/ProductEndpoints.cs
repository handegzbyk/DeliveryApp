using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;
using ShopDelivery.Shared;

using BrandEntity = global::Brand;
using ProductEntity = global::Product;
using StoreEntity = global::Store;
using StoreProductEntity = global::StoreProduct;

namespace ShopDelivery.Api.Products;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();

        group.MapGet("", async (
            HttpContext httpContext,
            CustomerIdentity customerIdentity,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
            var productEntities = await db.Products
                .AsNoTracking()
                .Include(product => product.Brand)
                .OrderBy(product => product.Name)
                .ToListAsync(ct);
            var customerPrices = await db.PriceObservations
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
            var latestPrices = customerPrices
                .GroupBy(observation => observation.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => (decimal?)group
                        .OrderByDescending(observation => observation.PurchasedAt)
                        .ThenByDescending(observation => observation.Id)
                        .First().Price);
            var products = productEntities
                .Select(product => Map(
                    product,
                    latestPrices.GetValueOrDefault(product.Id)))
                .ToList();

            return Results.Ok(products);
        })
        .WithName("ListProducts");

        group.MapPost("/import", async (
            ProductImportRequest request,
            HttpContext httpContext,
            CustomerIdentity customerIdentity,
            ShopDbContext db,
            IProductEnricher enricher,
            CancellationToken ct) =>
        {
            var query = NormalizeOptional(request.Query);
            if (query is null)
                return Results.BadRequest("Product query is required.");

            var info = await enricher.EnrichAsync(query, ct);
            if (info is null)
                return Results.NotFound($"No product found for '{query}'.");

            var externalId = NormalizeOptional(info.ExternalId);
            var productName = FirstNonEmpty(info.CanonicalName, query)!;
            var brandName = NormalizeBrandName(info.BrandName);
            var product = await FindProductAsync(db, externalId, productName, brandName, ct);
            var brand = await ResolveBrandAsync(brandName, db, ct);
            var created = product is null;

            if (product is null)
            {
                product = new ProductEntity
                {
                    Name = productName,
                    OpenFoodFactsCode = externalId,
                    Brand = brand,
                    Category = NormalizeOptional(info.Category),
                    ImageUrl = NormalizeOptional(info.ImageUrl),
                };
                db.Products.Add(product);
            }
            else
            {
                product.OpenFoodFactsCode ??= externalId;
                product.Category ??= NormalizeOptional(info.Category);
                product.ImageUrl ??= NormalizeOptional(info.ImageUrl);
                product.Brand ??= brand;
            }

            if (!string.IsNullOrWhiteSpace(request.StoreName))
            {
                var store = await ResolveStoreAsync(request.StoreName, db, ct);
                var aliasName = FirstNonEmpty(request.StoreProductName, query)!;
                await UpsertStoreProductAsync(
                    db,
                    store,
                    product,
                    aliasName,
                    request.StoreProductCode,
                    ct);
            }

            await db.SaveChangesAsync(ct);

            var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
            var latestPrice = await LatestPriceAsync(db, product.Id, customerId, ct);
            var result = Map(product, latestPrice);

            return created
                ? Results.Created($"/api/products/{product.Id}", result)
                : Results.Ok(result);
        })
        .WithName("ImportProduct");

        group.MapPost("/seed", async (
            ProductSeedRequest request,
            ProductCatalogSeeder seeder,
            CancellationToken ct) =>
        {
            var result = await seeder.SeedAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("SeedProductCatalog");
    }

    private static async Task<ProductEntity?> FindProductAsync(
        ShopDbContext db,
        string? externalId,
        string productName,
        string? brandName,
        CancellationToken ct)
    {
        if (externalId is not null)
        {
            var productByExternalId = await db.Products
                .Include(product => product.Brand)
                .FirstOrDefaultAsync(product => product.OpenFoodFactsCode == externalId, ct);

            if (productByExternalId is not null)
                return productByExternalId;
        }

        var normalizedName = productName.ToLower();
        var products = db.Products
            .Include(product => product.Brand)
            .Where(product => product.Name.ToLower() == normalizedName);

        if (brandName is null)
            return await products.FirstOrDefaultAsync(product => product.BrandId == null, ct);

        var normalizedBrandName = brandName.ToLower();
        return await products.FirstOrDefaultAsync(
            product => product.Brand != null && product.Brand.Name.ToLower() == normalizedBrandName,
            ct);
    }

    private static async Task<BrandEntity?> ResolveBrandAsync(
        string? rawBrandName,
        ShopDbContext db,
        CancellationToken ct)
    {
        var brandName = NormalizeBrandName(rawBrandName);
        if (brandName is null)
            return null;

        var normalizedBrandName = brandName.ToLower();
        return await db.Brands.FirstOrDefaultAsync(brand => brand.Name.ToLower() == normalizedBrandName, ct)
               ?? new BrandEntity { Name = brandName };
    }

    private static async Task<StoreEntity> ResolveStoreAsync(
        string rawStoreName,
        ShopDbContext db,
        CancellationToken ct)
    {
        var storeName = NormalizeOptional(rawStoreName) ?? "Unknown store";
        var normalizedStoreName = storeName.ToLower();
        return await db.Stores.FirstOrDefaultAsync(store => store.Name.ToLower() == normalizedStoreName, ct)
               ?? new StoreEntity { Name = storeName };
    }

    private static async Task UpsertStoreProductAsync(
        ShopDbContext db,
        StoreEntity store,
        ProductEntity product,
        string aliasName,
        string? rawStoreProductCode,
        CancellationToken ct)
    {
        var storeProductCode = NormalizeOptional(rawStoreProductCode);
        var normalizedAliasName = aliasName.ToLower();
        StoreProductEntity? storeProduct = null;

        if (store.Id > 0 && storeProductCode is not null)
        {
            storeProduct = await db.StoreProducts.FirstOrDefaultAsync(
                alias => alias.StoreId == store.Id && alias.StoreProductCode == storeProductCode,
                ct);
        }

        if (store.Id > 0 && storeProduct is null)
        {
            storeProduct = await db.StoreProducts.FirstOrDefaultAsync(
                alias => alias.StoreId == store.Id && alias.Name.ToLower() == normalizedAliasName,
                ct);
        }

        if (storeProduct is null)
        {
            db.StoreProducts.Add(new StoreProductEntity
            {
                Store = store,
                Product = product,
                Name = aliasName,
                StoreProductCode = storeProductCode,
            });
            return;
        }

        storeProduct.Product = product;
        storeProduct.StoreProductCode ??= storeProductCode;
    }

    private static string? NormalizeBrandName(string? rawBrandName) =>
        rawBrandName?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(NormalizeOptional).FirstOrDefault(value => value is not null);

    private static async Task<decimal?> LatestPriceAsync(
        ShopDbContext db,
        int productId,
        string customerId,
        CancellationToken ct)
    {
        var prices = db.PriceObservations
            .AsNoTracking()
            .Where(observation => observation.ProductId == productId
                                  && observation.Receipt.CustomerId == customerId);

        if (!db.Database.IsSqlite())
        {
            return await prices
                .OrderByDescending(observation => observation.Receipt.PurchasedAt)
                .ThenByDescending(observation => observation.Id)
                .Select(observation => (decimal?)observation.Price)
                .FirstOrDefaultAsync(ct);
        }

        return (await prices
                .Select(observation => new
                {
                    observation.Price,
                    observation.Receipt.PurchasedAt,
                    observation.Id,
                })
                .ToListAsync(ct))
            .OrderByDescending(observation => observation.PurchasedAt)
            .ThenByDescending(observation => observation.Id)
            .Select(observation => (decimal?)observation.Price)
            .FirstOrDefault();
    }

    private static ProductSummary Map(ProductEntity product, decimal? latestPrice) => new(
        product.Id,
        product.Name,
        product.OpenFoodFactsCode,
        product.Brand?.Name,
        product.Category,
        product.ImageUrl,
        latestPrice);
}
