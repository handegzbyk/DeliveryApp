namespace ShopDelivery.CatalogScraper.Models;

public sealed record ProductCandidate(
    string Name,
    string? BrandName,
    string? Category,
    string? ImageUrl,
    string? ExternalCode,
    string SourceUrl);

public sealed record MasterProduct(
    string ProductKey,
    string Name,
    string? BrandName,
    string? Category,
    string? ExternalCode,
    string? ImageUrl,
    string? LocalImagePath,
    string SourceUrl);

public sealed record StoreProductAlias(
    string StoreName,
    string StoreProductName,
    string? StoreProductCode,
    string ProductKey,
    string SourceUrl);

public sealed record CrawlStatistics(
    int PagesVisited,
    int PagesRejectedByRobots,
    int PagesFailed,
    int SitemapUrlsDiscovered,
    int ProductCandidatesFound,
    int UniqueProducts);

public sealed record CatalogDocument(
    int SchemaVersion,
    string StoreName,
    DateTimeOffset GeneratedAt,
    CrawlStatistics Statistics,
    IReadOnlyList<MasterProduct> Products,
    IReadOnlyList<StoreProductAlias> StoreAliases);

public sealed record CrawlResult(
    IReadOnlyList<ProductCandidate> Candidates,
    int PagesVisited,
    int PagesRejectedByRobots,
    int PagesFailed,
    int SitemapUrlsDiscovered);
