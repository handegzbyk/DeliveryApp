using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;
using ShopDelivery.Shared;

using BrandEntity = global::Brand;
using ProductEntity = global::Product;

namespace ShopDelivery.Api.Products;

public sealed class ProductCatalogSeeder(
    ShopDbContext db,
    IProductEnricher enricher,
    ILogger<ProductCatalogSeeder> logger)
{
    private const int MaxCategoriesPerRun = 12;
    private const int MaxPagesPerCategory = 3;
    private const int MaxPageSize = 100;

    private static readonly List<string> DefaultCategories =
    [
        "en:cheeses",
        "en:sausages",
        "en:salty-snacks",
        "en:chips-and-fries",
        "en:bread-rolls",
        "en:beers",
        "en:tomatoes",
        "en:sandwiches",
        "en:hamburgers",
        "en:fruit-juices",
        "en:mineral-waters",
        "en:plant-based-foods",
    ];

    public async Task<ProductSeedResponse> SeedAsync(ProductSeedRequest request, CancellationToken ct)
    {
        var categories = NormalizeCategories(request.Categories);
        var country = string.IsNullOrWhiteSpace(request.Country) ? "Germany" : request.Country.Trim();
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var maxPages = Math.Clamp(request.MaxPagesPerCategory, 1, MaxPagesPerCategory);

        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var category in categories)
        {
            for (var page = 1; page <= maxPages; page++)
            {
                var products = await enricher.SearchCategoryAsync(category, country, page, pageSize, ct);
                if (products.Count == 0)
                    break;

                foreach (var product in products)
                {
                    var result = await UpsertProductAsync(product, ct);
                    created += result == UpsertResult.Created ? 1 : 0;
                    updated += result == UpsertResult.Updated ? 1 : 0;
                    skipped += result == UpsertResult.Skipped ? 1 : 0;
                }

                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Seeded category {Category} page {Page}: {ProductCount} OpenFoodFacts product(s)",
                    category,
                    page,
                    products.Count);
            }
        }

        return new ProductSeedResponse(created, updated, skipped, categories);
    }

    private async Task<UpsertResult> UpsertProductAsync(ProductInfo candidate, CancellationToken ct)
    {
        var productName = NormalizeOptional(candidate.CanonicalName);
        if (productName is null)
            return UpsertResult.Skipped;

        var externalId = NormalizeOptional(candidate.ExternalId);
        var product = await FindProductAsync(externalId, productName, ct);
        var brand = await ResolveBrandAsync(candidate.BrandName, ct);
        var category = NormalizeOptional(candidate.Category);
        var imageUrl = NormalizeOptional(candidate.ImageUrl);

        if (product is null)
        {
            db.Products.Add(new ProductEntity
            {
                Name = productName,
                OpenFoodFactsCode = externalId,
                Brand = brand,
                Category = category,
                ImageUrl = imageUrl,
            });
            return UpsertResult.Created;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(product.OpenFoodFactsCode) && externalId is not null)
        {
            product.OpenFoodFactsCode = externalId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(product.Category) && category is not null)
        {
            product.Category = category;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(product.ImageUrl) && imageUrl is not null)
        {
            product.ImageUrl = imageUrl;
            changed = true;
        }

        if (product.Brand is null && brand is not null)
        {
            product.Brand = brand;
            changed = true;
        }

        return changed ? UpsertResult.Updated : UpsertResult.Skipped;
    }

    private async Task<ProductEntity?> FindProductAsync(
        string? externalId,
        string productName,
        CancellationToken ct)
    {
        if (externalId is not null)
        {
            var byExternalId = await db.Products
                .Include(product => product.Brand)
                .FirstOrDefaultAsync(product => product.OpenFoodFactsCode == externalId, ct);

            if (byExternalId is not null)
                return byExternalId;
        }

        var normalizedName = productName.ToLower();
        return await db.Products
            .Include(product => product.Brand)
            .FirstOrDefaultAsync(product => product.Name.ToLower() == normalizedName, ct);
    }

    private async Task<BrandEntity?> ResolveBrandAsync(string? rawBrandName, CancellationToken ct)
    {
        var brandName = NormalizeBrandName(rawBrandName);
        if (brandName is null)
            return null;

        var normalizedBrandName = brandName.ToLower();
        return await db.Brands.FirstOrDefaultAsync(brand => brand.Name.ToLower() == normalizedBrandName, ct)
               ?? new BrandEntity { Name = brandName };
    }

    private static List<string> NormalizeCategories(List<string>? categories) =>
        (categories is { Count: > 0 } ? categories : DefaultCategories)
        .Select(NormalizeOptional)
        .Where(category => category is not null)
        .Select(category => category!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxCategoriesPerRun)
        .ToList();

    private static string? NormalizeBrandName(string? rawBrandName) =>
        rawBrandName?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private enum UpsertResult
    {
        Created,
        Updated,
        Skipped,
    }
}
