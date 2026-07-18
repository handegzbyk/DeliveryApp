using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Receipts;

public class ProductMatcher(ShopDbContext db)
{
    public const double MatchThreshold = 0.8;
    private const int MaxCandidates = 8;

    public async Task<(int? matchedId, List<ProductCandidate> candidates)> TopMatchesAsync(
        string rawText, string? storeName, CancellationToken ct)
    {
        var normalizedRawText = Normalize(rawText);
        var candidatesByProduct = new Dictionary<int, ProductCandidate>();

        if (!string.IsNullOrWhiteSpace(storeName))
        {
            var normalizedStoreName = storeName.Trim().ToLower();
            var storeAliases = await db.StoreProducts
                .AsNoTracking()
                .Where(alias => alias.Store.Name.ToLower() == normalizedStoreName)
                .Select(alias => new
                {
                    alias.Name,
                    alias.ProductId,
                    ProductName = alias.Product.Name,
                    alias.Product.ImageUrl,
                })
                .ToListAsync(ct);

            foreach (var alias in storeAliases)
            {
                var score = Math.Min(1, Similarity(normalizedRawText, Normalize(alias.Name)) + 0.1);
                KeepBestCandidate(
                    candidatesByProduct,
                    alias.ProductId,
                    alias.ProductName,
                    alias.ImageUrl,
                    score);
            }
        }

        var products = await db.Products.AsNoTracking().ToListAsync(ct);
        foreach (var product in products)
        {
            KeepBestCandidate(
                candidatesByProduct,
                product.Id,
                product.Name,
                product.ImageUrl,
                Similarity(normalizedRawText, Normalize(product.Name)));
        }

        var ranked = candidatesByProduct.Values
            .OrderByDescending(candidate => candidate.Score)
            .Take(MaxCandidates)
            .ToList();

        var best = ranked.FirstOrDefault();

        // always offer "create new"
        ranked.Add(new ProductCandidate(null, TitleCase(rawText), null, 0));

        int? matchedId = best is { Score: >= MatchThreshold } ? best.ProductId : null;
        return (matchedId, ranked);
    }

    public Task<(int? matchedId, List<ProductCandidate> candidates)> TopMatchesAsync(
        string rawText, CancellationToken ct) =>
        TopMatchesAsync(rawText, null, ct);

    private static void KeepBestCandidate(
        Dictionary<int, ProductCandidate> candidatesByProduct,
        int productId,
        string productName,
        string? imageUrl,
        double score)
    {
        if (candidatesByProduct.TryGetValue(productId, out var existing) && existing.Score >= score)
            return;

        candidatesByProduct[productId] = new ProductCandidate(productId, productName, imageUrl, score);
    }

    private static string Normalize(string value) =>
        new string(value.ToLowerInvariant().Where(character => char.IsLetterOrDigit(character) || character == ' ').ToArray()).Trim();

    private static double Similarity(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;
        return (double)leftTokens.Intersect(rightTokens).Count() / leftTokens.Union(rightTokens).Count();
    }

    private static string TitleCase(string value) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
