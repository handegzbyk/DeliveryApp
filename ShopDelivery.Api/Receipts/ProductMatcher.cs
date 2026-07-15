
using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;    

namespace ShopDelivery.Api.Receipts;

public class ProductMatcher(ShopDbContext db)
{
    public async Task<(int? productId, string name)> BestMatchAsync(string rawText)
    {
        var normalized = Normalize(rawText);
        var products = await db.Products.ToListAsync();

        var best = products
            .Select(p => (p, score: Similarity(normalized, Normalize(p.Name))))
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        return best.score >= 0.6           // threshold
            ? (best.p.Id, best.p.Name)
            : (null, TitleCase(rawText));   // no match → suggest cleaned raw text
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();

    // Simple token-overlap similarity (swap for Levenshtein if you prefer)
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