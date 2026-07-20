using System.Net;
using ShopDelivery.CatalogScraper.Configuration;
using ShopDelivery.CatalogScraper.Networking;
using ShopDelivery.CatalogScraper.Output;
using ShopDelivery.CatalogScraper.Scraping;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var configPath = GetOption(args, "--config");
if (configPath is null)
{
    Console.Error.WriteLine("Missing required option: --config <path>");
    PrintUsage();
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var options = await ScraperOptions.LoadAsync(configPath, cancellation.Token);
    using var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    };
    using var rawHttp = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(45),
    };
    rawHttp.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    rawHttp.DefaultRequestHeaders.Accept.ParseAdd(
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/*;q=0.8,*/*;q=0.5");

    var allowedRequestHosts = options.GetAllowedHosts()
        .Concat(options.GetAllowedImageHosts())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var politeHttp = new PoliteHttpClient(
        rawHttp,
        TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds),
        allowedRequestHosts);
    var robots = new RobotsPolicyProvider(options, politeHttp);
    var existingCatalog = options.ResumeExistingCatalog
        ? await CatalogOutputWriter.LoadExistingAsync(options.OutputDirectory, cancellation.Token)
        : null;
    if (existingCatalog is not null)
        Console.WriteLine($"RESUME  {existingCatalog.Products.Count} existing products will be kept and skipped.");
    var crawler = new CatalogCrawler(
        options,
        politeHttp,
        robots,
        new ProductPageExtractor(),
        existingCatalog?.Products.Select(product => product.SourceUrl));
    var crawl = await crawler.CrawlAsync(cancellation.Token);
    var catalog = new CatalogBuilder().Build(options.StoreName, crawl, existingCatalog);
    var writer = new CatalogOutputWriter(
        options,
        new ImageDownloader(options, politeHttp, robots));
    var result = await writer.WriteAsync(catalog, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"Visited pages:       {result.Statistics.PagesVisited}");
    Console.WriteLine($"Product candidates:  {result.Statistics.ProductCandidatesFound}");
    Console.WriteLine($"Unique products:     {result.Statistics.UniqueProducts}");
    Console.WriteLine($"Blocked by robots:   {result.Statistics.PagesRejectedByRobots}");
    Console.WriteLine($"Failed pages:        {result.Statistics.PagesFailed}");
    if (result.Products.Count == 0)
        Console.WriteLine("No products were found. Check ProductUrlPatterns and whether pages expose HTML/JSON-LD product data.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Scrape cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Scrape failed: {exception.Message}");
    return 1;
}

static string? GetOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            return arguments[index + 1];
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("ShopDelivery local catalog scraper");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project ShopDelivery.CatalogScraper -- --config <store-config.json>");
    Console.WriteLine();
    Console.WriteLine("The scraper only reads configured public hosts and writes JSON, CSV, and optional images locally.");
}
