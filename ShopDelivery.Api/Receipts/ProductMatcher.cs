using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Products;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Receipts;

public sealed partial class ProductMatcher(ShopDbContext db)
{
    public const double AutomaticMatchThreshold = 0.93;
    private const int MaxCandidates = 8;
    private const int MaxDatabaseCandidates = 250;

    public async Task<(int? MatchedId, List<ProductCandidate> Candidates)> TopMatchesAsync(
        string rawText,
        string? storeName,
        HttpRequest request,
        CancellationToken ct)
    {
        var normalized = CatalogTextNormalizer.Normalize(rawText);
        if (normalized.Length == 0)
            return (null, [NewCandidate(rawText)]);

        var candidates = new Dictionary<int, RankedCandidate>();
        var exactAliasProductId = await AddStoreAliasesAsync(
            storeName,
            normalized,
            candidates,
            ct);
        await AddMasterProductsAsync(normalized, candidates, ct);

        var ranked = candidates.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name)
            .Take(MaxCandidates)
            .Select(candidate => new ProductCandidate(
                candidate.ProductId,
                candidate.Name,
                ProductImageUrls.For(request, candidate.ImageAssetId),
                Math.Round(candidate.Score, 4),
                candidate.Reason))
            .ToList();
        ranked.Add(NewCandidate(rawText));

        var best = candidates.Values.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        var matchedId = exactAliasProductId
                        ?? (best is { Score: >= AutomaticMatchThreshold } ? best.ProductId : null);
        return (matchedId, ranked);
    }

    public async Task<ProductSearchResponse> SearchAsync(
        string query,
        HttpRequest request,
        CancellationToken ct)
    {
        var gtin = CatalogTextNormalizer.NormalizeGtin(query);
        if (gtin is not null)
        {
            var byGtin = await db.Products
                .AsNoTracking()
                .Where(product => product.Gtin == gtin && product.Status == ProductStatus.Confirmed)
                .Select(product => new CandidateRow(
                    product.Id,
                    product.Name,
                    product.NormalizedName,
                    product.Images.Where(image => image.IsPrimary)
                        .Select(image => (long?)image.ImageAssetId)
                        .FirstOrDefault()))
                .FirstOrDefaultAsync(ct);
            if (byGtin is not null)
            {
                return new ProductSearchResponse(
                    query,
                    true,
                    [new ProductCandidate(
                        byGtin.ProductId,
                        byGtin.Name,
                        ProductImageUrls.For(request, byGtin.ImageAssetId),
                        1,
                        "Exact barcode")]);
            }
        }

        var (matchedId, candidates) = await TopMatchesAsync(query, null, request, ct);
        candidates.RemoveAll(candidate => candidate.ProductId is null);
        return new ProductSearchResponse(query, matchedId is not null, candidates);
    }

    private async Task<int?> AddStoreAliasesAsync(
        string? storeName,
        string normalizedText,
        Dictionary<int, RankedCandidate> candidates,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storeName))
            return null;

        var normalizedStore = CatalogTextNormalizer.Normalize(storeName);
        var baseQuery = db.StoreProducts
            .AsNoTracking()
            .Where(alias => alias.Store.NormalizedName == normalizedStore
                            && alias.Status == StoreAliasStatus.Confirmed);
        var exactAlias = await baseQuery
            .Where(alias => alias.NormalizedName == normalizedText)
            .Select(alias => new CandidateRow(
                alias.ProductId,
                alias.Product.Name,
                alias.NormalizedName,
                alias.Product.Images.Where(image => image.IsPrimary)
                    .Select(image => (long?)image.ImageAssetId)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(ct);
        if (exactAlias is not null)
        {
            KeepBest(candidates, exactAlias, 1, "Confirmed store name");
            return exactAlias.ProductId;
        }

        var tokens = Tokens(normalizedText)
            .Where(token => token.Length >= 3)
            .OrderByDescending(token => token.Length)
            .Take(4)
            .ToArray();
        if (tokens.Length == 0)
            return null;
        Expression<Func<StoreProduct, bool>> predicate = alias => false;
        foreach (var token in tokens)
        {
            var captured = token;
            predicate = Or(predicate, alias => alias.NormalizedName.Contains(captured));
        }

        var aliases = await baseQuery
            .Where(predicate)
            .OrderByDescending(alias => alias.ConfirmationCount)
            .Take(MaxDatabaseCandidates)
            .Select(alias => new CandidateRow(
                alias.ProductId,
                alias.Product.Name,
                alias.NormalizedName,
                alias.Product.Images.Where(image => image.IsPrimary)
                    .Select(image => (long?)image.ImageAssetId)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        foreach (var alias in aliases)
        {
            var score = Similarity(normalizedText, alias.NormalizedName) * 0.96;
            KeepBest(candidates, alias, score, "Similar store name");
        }

        return null;
    }

    private async Task AddMasterProductsAsync(
        string normalizedText,
        Dictionary<int, RankedCandidate> candidates,
        CancellationToken ct)
    {
        var tokens = Tokens(normalizedText)
            .Where(token => token.Length >= 2)
            .OrderByDescending(token => token.Length)
            .Take(5)
            .ToArray();
        if (tokens.Length == 0)
            return;

        Expression<Func<Product, bool>> predicate = product => false;
        foreach (var token in tokens)
        {
            var captured = token;
            predicate = Or(predicate, product => product.NormalizedName.Contains(captured));
        }

        var rows = await db.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Confirmed)
            .Where(predicate)
            .OrderBy(product => product.Name)
            .Take(MaxDatabaseCandidates)
            .Select(product => new CandidateRow(
                product.Id,
                product.Name,
                product.NormalizedName,
                product.Images.Where(image => image.IsPrimary)
                    .Select(image => (long?)image.ImageAssetId)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var score = Similarity(normalizedText, row.NormalizedName);
            KeepBest(candidates, row, score, "Master catalog name");
        }
    }

    private static double Similarity(string left, string right)
    {
        if (left == right)
            return 1;

        var leftTokens = Tokens(left).ToHashSet(StringComparer.Ordinal);
        var rightTokens = Tokens(right).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;

        var intersection = leftTokens.Intersect(rightTokens).Count();
        var tokenScore = (double)intersection / leftTokens.Union(rightTokens).Count();
        var containment = (double)intersection / Math.Min(leftTokens.Count, rightTokens.Count);
        var quantityPenalty = QuantitySignature(left) is { } leftQuantity
                              && QuantitySignature(right) is { } rightQuantity
                              && leftQuantity != rightQuantity
            ? 0.18
            : 0;
        return Math.Clamp(tokenScore * 0.65 + containment * 0.35 - quantityPenalty, 0, 1);
    }

    private static string[] Tokens(string value) =>
        value.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string? QuantitySignature(string value)
    {
        var match = Quantity().Match(value);
        return match.Success ? match.Value.Replace(" ", "", StringComparison.Ordinal) : null;
    }

    private static void KeepBest(
        Dictionary<int, RankedCandidate> candidates,
        CandidateRow row,
        double score,
        string reason)
    {
        if (candidates.TryGetValue(row.ProductId, out var current) && current.Score >= score)
            return;
        candidates[row.ProductId] = new RankedCandidate(
            row.ProductId,
            row.Name,
            row.ImageAssetId,
            score,
            reason);
    }

    private static ProductCandidate NewCandidate(string rawText) => new(
        null,
        CatalogTextNormalizer.DisplayName(rawText),
        ProductImages.Generic,
        0,
        "Not in catalog — submit for admin review",
        true);

    private static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(T));
        var body = Expression.OrElse(
            Expression.Invoke(left, parameter),
            Expression.Invoke(right, parameter));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed record CandidateRow(
        int ProductId,
        string Name,
        string NormalizedName,
        long? ImageAssetId);

    private sealed record RankedCandidate(
        int ProductId,
        string Name,
        long? ImageAssetId,
        double Score,
        string Reason);

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*(?:kg|g|l|ml|cl|stk)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Quantity();
}
