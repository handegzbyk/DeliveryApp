using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Receipts;

public class ProductMatcher(ShopDbContext db)
{
    public const double MatchThreshold = 0.8;
    private const int MaxCandidates = 8;

    public async Task<(int? matchedId, List<ProductCandidate> candidates)> TopMatchesAsync(
        string rawText, CancellationToken ct)
    {
        var products = await db.Products.ToListAsync(ct);

        var norm = Normalize(rawText);
        var ranked = products
            .Select(p => new ProductCandidate(p.Id, p.Name, p.ImageUrl, Similarity(norm, Normalize(p.Name))))
            .OrderByDescending(c => c.Score)
            .Take(MaxCandidates)
            .ToList();

        var best = ranked.FirstOrDefault();

        // always offer "create new"
        ranked.Add(new ProductCandidate(null, TitleCase(rawText), null, 0));

        int? matchedId = best is { Score: >= MatchThreshold } ? best.ProductId : null;
        return (matchedId, ranked);
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();

    private static double Similarity(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0) return 0;
        return (double)ta.Intersect(tb).Count() / ta.Union(tb).Count();
    }

    private static string TitleCase(string s) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}