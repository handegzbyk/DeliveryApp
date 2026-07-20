using ShopDelivery.CatalogScraper.Configuration;
using ShopDelivery.CatalogScraper.Networking;

namespace ShopDelivery.CatalogScraper.Scraping;

public sealed class RobotsPolicyProvider(ScraperOptions options, PoliteHttpClient http)
{
    private readonly Dictionary<string, RobotsPolicy> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RobotsPolicy> GetAsync(Uri uri, CancellationToken ct)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (_cache.TryGetValue(origin, out var cached))
            return cached;
        if (!options.RespectRobotsTxt)
            return _cache[origin] = RobotsPolicy.AllowAll;

        var robotsUri = new Uri(new Uri(origin), "/robots.txt");
        try
        {
            using var response = await http.GetAsync(
                robotsUri,
                null,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return _cache[origin] = RobotsPolicy.AllowAll;
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"ROBOTS  HTTP {(int)response.StatusCode}; blocking {origin}");
                return _cache[origin] = RobotsPolicy.DenyAll;
            }

            var content = await BoundedContentReader.ReadStringAsync(response.Content, 1_000_000, ct);
            return _cache[origin] = RobotsPolicy.Parse(content, new Uri(origin), options.UserAgent);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            Console.WriteLine(
                $"ROBOTS  unavailable; blocking {origin}: {exception.GetBaseException().Message}");
            return _cache[origin] = RobotsPolicy.DenyAll;
        }
    }
}
