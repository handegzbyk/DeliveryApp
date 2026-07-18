using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;

using BrandEntity = global::Brand;
using ProductEntity = global::Product;

namespace ShopDelivery.Api.Products;

public sealed class ProductCandidateSeeder(
    ShopDbContext db,
    IProductEnricher enricher,
    ILogger<ProductCandidateSeeder> logger)
{
    private const int SearchResultsPerQuery = 6;
    private const int MaxQueriesPerReceiptLine = 4;

    private static readonly HashSet<string> StorePrefixTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "edeka",
        "g&g",
        "gg",
        "gut",
        "günstig",
        "bio",
        "naturals",
    };

    public async Task SeedFromReceiptLineAsync(string rawText, CancellationToken ct)
    {
        var queries = BuildSearchQueries(rawText);
        foreach (var query in queries)
        {
            var candidates = await enricher.SearchAsync(query, SearchResultsPerQuery, ct);
            foreach (var candidate in candidates)
            {
                await UpsertProductAsync(candidate, ct);
            }

            if (candidates.Count > 0)
            {
                logger.LogInformation(
                    "Seeded {CandidateCount} OpenFoodFacts candidate(s) for receipt line '{RawText}' using query '{Query}'",
                    candidates.Count,
                    rawText,
                    query);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertProductAsync(ProductInfo candidate, CancellationToken ct)
    {
        var externalId = NormalizeOptional(candidate.ExternalId);
        var productName = NormalizeOptional(candidate.CanonicalName);
        if (productName is null)
            return;

        var product = await FindProductAsync(externalId, productName, ct);
        var brand = await ResolveBrandAsync(candidate.BrandName, ct);

        if (product is null)
        {
            db.Products.Add(new ProductEntity
            {
                Name = productName,
                OpenFoodFactsCode = externalId,
                Brand = brand,
                Category = NormalizeOptional(candidate.Category),
                ImageUrl = NormalizeOptional(candidate.ImageUrl),
            });
            return;
        }

        product.OpenFoodFactsCode ??= externalId;
        product.Brand ??= brand;
        product.Category ??= NormalizeOptional(candidate.Category);
        product.ImageUrl ??= NormalizeOptional(candidate.ImageUrl);
    }

    private async Task<ProductEntity?> FindProductAsync(
        string? externalId,
        string productName,
        CancellationToken ct)
    {
        if (externalId is not null)
        {
            var productByExternalId = await db.Products.FirstOrDefaultAsync(
                product => product.OpenFoodFactsCode == externalId,
                ct);

            if (productByExternalId is not null)
                return productByExternalId;
        }

        var normalizedProductName = productName.ToLower();
        return await db.Products.FirstOrDefaultAsync(
            product => product.Name.ToLower() == normalizedProductName,
            ct);
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

    private static IReadOnlyList<string> BuildSearchQueries(string rawText)
    {
        var queries = new List<string>();
        AddQuery(queries, rawText);

        var cleaned = Regex.Replace(rawText, @"[^\p{L}\p{N}&]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        AddQuery(queries, cleaned);
        AddQuery(queries, cleaned.Replace("&", " "));

        var tokens = cleaned.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var withoutStorePrefixes = tokens.SkipWhile(token => StorePrefixTokens.Contains(token)).ToArray();
        AddQuery(queries, string.Join(' ', withoutStorePrefixes));

        if (tokens.Length > 1)
            AddQuery(queries, string.Join(' ', tokens.Skip(1)));

        if (tokens.Length > 2)
            AddQuery(queries, string.Join(' ', tokens.TakeLast(2)));

        return queries
            .Where(query => query.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxQueriesPerReceiptLine)
            .ToList();
    }

    private static void AddQuery(List<string> queries, string? query)
    {
        var normalized = NormalizeOptional(query);
        if (normalized is not null)
            queries.Add(normalized);
    }

    private static string? NormalizeBrandName(string? rawBrandName) =>
        rawBrandName?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
