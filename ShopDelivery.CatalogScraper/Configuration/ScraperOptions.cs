using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShopDelivery.CatalogScraper.Configuration;

public sealed class ScraperOptions
{
    public string StoreName { get; init; } = "";
    public List<string> StartUrls { get; init; } = [];
    public List<string> SitemapUrls { get; init; } = [];
    public List<string> AllowedHosts { get; init; } = [];
    public List<string> AllowedImageHosts { get; init; } = [];
    public List<string> ProductUrlPatterns { get; init; } = [];
    public List<string> ExcludedUrlPatterns { get; init; } =
    [
        @"/(account|login|logout|cart|checkout)(/|$)",
        @"\.(css|js|pdf|zip|png|jpe?g|gif|webp|svg|ico)(\?|$)",
    ];

    public int MaxPages { get; init; } = 500;
    public int MaxSitemapUrls { get; init; } = 5_000;
    public int RequestDelayMilliseconds { get; init; } = 1_200;
    public int MaxPageBytes { get; init; } = 5_000_000;
    public int MaxSitemapBytes { get; init; } = 50_000_000;
    public bool RespectRobotsTxt { get; init; } = true;
    public bool DownloadImages { get; init; } = true;
    public bool ResumeExistingCatalog { get; init; } = true;
    public int MaxImageBytes { get; init; } = 10_000_000;
    public bool KeepQueryStrings { get; init; }
    public string OutputDirectory { get; init; } = "artifacts/catalogs";
    public string UserAgent { get; init; } =
        "ShopDeliveryCatalogScraper/1.0 (local master-data builder)";

    public static async Task<ScraperOptions> LoadAsync(string path, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = File.OpenRead(fullPath);
        var options = await JsonSerializer.DeserializeAsync<ScraperOptions>(
                          stream,
                          new JsonSerializerOptions
                          {
                              PropertyNameCaseInsensitive = true,
                              ReadCommentHandling = JsonCommentHandling.Skip,
                              AllowTrailingCommas = true,
                          },
                          ct)
                      ?? throw new InvalidOperationException("The scraper configuration is empty.");

        options.Validate();
        var configDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        if (!Path.IsPathRooted(options.OutputDirectory))
        {
            options = options.WithOutputDirectory(
                Path.GetFullPath(Path.Combine(configDirectory, options.OutputDirectory)));
        }

        return options;
    }

    public IReadOnlySet<string> GetAllowedHosts()
    {
        var configured = AllowedHosts
            .Select(NormalizeHost)
            .Where(host => host.Length > 0);
        var discovered = StartUrls
            .Concat(SitemapUrls)
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "")
            .Select(NormalizeHost)
            .Where(host => host.Length > 0);

        return configured.Concat(discovered).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> GetAllowedImageHosts()
    {
        IEnumerable<string> configured = AllowedImageHosts.Count > 0
            ? AllowedImageHosts
            : GetAllowedHosts();
        return configured
            .Select(NormalizeHost)
            .Where(host => host.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(StoreName))
            throw new InvalidOperationException("StoreName is required.");
        if (StartUrls.Count == 0 && SitemapUrls.Count == 0)
            throw new InvalidOperationException("At least one StartUrl or SitemapUrl is required.");
        if (MaxPages is < 1 or > 100_000)
            throw new InvalidOperationException("MaxPages must be between 1 and 100,000.");
        if (MaxSitemapUrls is < 1 or > 1_000_000)
            throw new InvalidOperationException("MaxSitemapUrls must be between 1 and 1,000,000.");
        if (RequestDelayMilliseconds is < 0 or > 120_000)
            throw new InvalidOperationException("RequestDelayMilliseconds must be between 0 and 120,000.");
        if (MaxPageBytes is < 10_000 or > 50_000_000)
            throw new InvalidOperationException("MaxPageBytes must be between 10 KB and 50 MB.");
        if (MaxSitemapBytes is < 10_000 or > 100_000_000)
            throw new InvalidOperationException("MaxSitemapBytes must be between 10 KB and 100 MB.");
        if (MaxImageBytes is < 1_024 or > 100_000_000)
            throw new InvalidOperationException("MaxImageBytes must be between 1 KB and 100 MB.");
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new InvalidOperationException("OutputDirectory is required.");
        if (string.IsNullOrWhiteSpace(UserAgent))
            throw new InvalidOperationException("UserAgent is required.");

        foreach (var rawUrl in StartUrls.Concat(SitemapUrls))
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps
                    && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new InvalidOperationException(
                    $"Only HTTPS URLs are allowed (HTTP is allowed for localhost fixtures): {rawUrl}");
            }
        }

        foreach (var pattern in ProductUrlPatterns.Concat(ExcludedUrlPatterns))
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private ScraperOptions WithOutputDirectory(string outputDirectory) => new()
    {
        StoreName = StoreName,
        StartUrls = StartUrls,
        SitemapUrls = SitemapUrls,
        AllowedHosts = AllowedHosts,
        AllowedImageHosts = AllowedImageHosts,
        ProductUrlPatterns = ProductUrlPatterns,
        ExcludedUrlPatterns = ExcludedUrlPatterns,
        MaxPages = MaxPages,
        MaxSitemapUrls = MaxSitemapUrls,
        RequestDelayMilliseconds = RequestDelayMilliseconds,
        MaxPageBytes = MaxPageBytes,
        MaxSitemapBytes = MaxSitemapBytes,
        RespectRobotsTxt = RespectRobotsTxt,
        DownloadImages = DownloadImages,
        ResumeExistingCatalog = ResumeExistingCatalog,
        MaxImageBytes = MaxImageBytes,
        KeepQueryStrings = KeepQueryStrings,
        OutputDirectory = outputDirectory,
        UserAgent = UserAgent,
    };

    private static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();
}
