using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ShopDelivery.CatalogScraper.Configuration;
using ShopDelivery.CatalogScraper.Models;
using ShopDelivery.CatalogScraper.Networking;

namespace ShopDelivery.CatalogScraper.Scraping;

public sealed class CatalogCrawler(
    ScraperOptions options,
    PoliteHttpClient http,
    RobotsPolicyProvider robots,
    ProductPageExtractor extractor,
    IEnumerable<string>? completedProductUrls = null)
{
    private readonly IReadOnlySet<string> _allowedHosts = options.GetAllowedHosts();
    private readonly List<Regex> _productPatterns = Compile(options.ProductUrlPatterns);
    private readonly List<Regex> _excludedPatterns = Compile(options.ExcludedUrlPatterns);
    private readonly HashSet<string> _scheduledPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Uri> _productPages = new();
    private readonly Queue<Uri> _discoveryPages = new();
    private readonly HashSet<string> _completedProductPages = new(
        completedProductUrls ?? [],
        StringComparer.OrdinalIgnoreCase);

    public async Task<CrawlResult> CrawlAsync(CancellationToken ct)
    {
        foreach (var startUrl in options.StartUrls)
            EnqueuePage(new Uri(startUrl), prioritize: MatchesProductPattern(startUrl));

        var sitemapUrls = await GetSitemapUrlsAsync(ct);
        var sitemapDiscoveries = await LoadSitemapsAsync(sitemapUrls, ct);

        var candidates = new List<ProductCandidate>();
        var visited = 0;
        var robotsRejected = 0;
        var failed = 0;

        while (visited < options.MaxPages && TryDequeue(out var pageUri))
        {
            ct.ThrowIfCancellationRequested();
            var policy = await robots.GetAsync(pageUri, ct);
            if (options.RespectRobotsTxt && !policy.IsAllowed(pageUri))
            {
                robotsRejected++;
                Console.WriteLine($"ROBOTS  {pageUri}");
                continue;
            }

            visited++;
            Console.WriteLine($"GET     [{visited}/{options.MaxPages}] {pageUri}");
            try
            {
                using var response = await http.GetAsync(
                    pageUri,
                    policy.CrawlDelay,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                if (!response.IsSuccessStatusCode)
                {
                    failed++;
                    Console.WriteLine($"SKIP    HTTP {(int)response.StatusCode} {pageUri}");
                    continue;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null
                    && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var html = await BoundedContentReader.ReadStringAsync(
                    response.Content,
                    options.MaxPageBytes,
                    ct);
                var extraction = await extractor.ExtractAsync(
                    html,
                    pageUri,
                    MatchesProductPattern(pageUri.AbsoluteUri),
                    ct);
                candidates.AddRange(extraction.Products);

                foreach (var link in extraction.Links)
                    EnqueuePage(link, prioritize: MatchesProductPattern(link.AbsoluteUri));
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                if (exception is TaskCanceledException && ct.IsCancellationRequested)
                    throw;
                failed++;
                Console.WriteLine($"FAILED  {pageUri}: {exception.Message}");
            }
        }

        return new CrawlResult(
            candidates,
            visited,
            robotsRejected,
            failed,
            sitemapDiscoveries);
    }

    private async Task<IReadOnlyList<Uri>> GetSitemapUrlsAsync(CancellationToken ct)
    {
        var sitemapUrls = options.SitemapUrls.Select(url => new Uri(url)).ToList();
        var origins = options.StartUrls
            .Select(url => new Uri(url).GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var rawOrigin in origins)
        {
            var origin = new Uri(rawOrigin);
            var policy = await robots.GetAsync(origin, ct);
            sitemapUrls.AddRange(policy.Sitemaps);
            var conventionalSitemap = new Uri(origin, "/sitemap.xml");
            if (options.SitemapUrls.Count == 0
                && policy.Sitemaps.Count == 0
                && policy.IsAllowed(conventionalSitemap))
            {
                sitemapUrls.Add(conventionalSitemap);
            }
        }

        return sitemapUrls
            .Where(IsAllowedHost)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<int> LoadSitemapsAsync(IReadOnlyList<Uri> initialSitemaps, CancellationToken ct)
    {
        var queue = new Queue<Uri>(initialSitemaps);
        var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discovered = 0;

        while (queue.Count > 0 && discovered < options.MaxSitemapUrls && visitedSitemaps.Count < 100)
        {
            var sitemapUri = queue.Dequeue();
            if (!visitedSitemaps.Add(sitemapUri.AbsoluteUri) || !IsAllowedHost(sitemapUri))
                continue;

            try
            {
                var policy = await robots.GetAsync(sitemapUri, ct);
                using var response = await http.GetAsync(
                    sitemapUri,
                    policy.CrawlDelay,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                var sitemapBytes = await BoundedContentReader.ReadBytesAsync(
                    response.Content,
                    options.MaxSitemapBytes,
                    ct);
                await using var content = new MemoryStream(sitemapBytes, writable: false);
                await using Stream xmlStream = IsGzip(response, sitemapUri)
                    ? new GZipStream(content, CompressionMode.Decompress)
                    : content;
                using var xmlReader = XmlReader.Create(
                    xmlStream,
                    new XmlReaderSettings
                    {
                        Async = true,
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = options.MaxSitemapBytes,
                    });
                var document = await XDocument.LoadAsync(xmlReader, LoadOptions.None, ct);
                var rootName = document.Root?.Name.LocalName;
                var locations = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "loc")
                    .Select(element => element.Value.Trim())
                    .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
                    .Select(value => new Uri(value));

                if (string.Equals(rootName, "sitemapindex", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var nested in locations.Where(IsAllowedHost))
                        queue.Enqueue(nested);
                }
                else
                {
                    foreach (var location in locations)
                    {
                        if (discovered >= options.MaxSitemapUrls)
                            break;
                        discovered++;
                        if (_productPatterns.Count == 0 || MatchesProductPattern(location.AbsoluteUri))
                            EnqueuePage(location, prioritize: true);
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or System.Xml.XmlException)
            {
                Console.WriteLine($"SITEMAP {sitemapUri}: {exception.Message}");
            }
        }

        return discovered;
    }

    private void EnqueuePage(Uri rawUri, bool prioritize)
    {
        var uri = NormalizePageUri(rawUri);
        if (uri is null
            || !IsAllowedHost(uri)
            || _excludedPatterns.Any(pattern => pattern.IsMatch(uri.PathAndQuery))
            || (prioritize && _completedProductPages.Contains(uri.AbsoluteUri))
            || !_scheduledPages.Add(uri.AbsoluteUri))
        {
            return;
        }

        if (prioritize)
            _productPages.Enqueue(uri);
        else
            _discoveryPages.Enqueue(uri);
    }

    private bool TryDequeue(out Uri uri)
    {
        if (_productPages.TryDequeue(out uri!))
            return true;
        return _discoveryPages.TryDequeue(out uri!);
    }

    private Uri? NormalizePageUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return null;
        }

        var builder = new UriBuilder(uri) { Fragment = "" };
        if (!options.KeepQueryStrings)
            builder.Query = "";
        return builder.Uri;
    }

    private bool IsAllowedHost(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && _allowedHosts.Contains(uri.Host.TrimEnd('.'));
    private bool MatchesProductPattern(string url) => _productPatterns.Any(pattern => pattern.IsMatch(url));

    private static bool IsGzip(HttpResponseMessage response, Uri uri) =>
        response.Content.Headers.ContentType?.MediaType?.Contains("gzip", StringComparison.OrdinalIgnoreCase) == true
        || uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    private static List<Regex> Compile(IEnumerable<string> patterns) => patterns
        .Select(pattern => new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .ToList();
}
