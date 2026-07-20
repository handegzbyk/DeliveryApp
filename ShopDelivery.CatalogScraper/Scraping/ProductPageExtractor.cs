using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ShopDelivery.CatalogScraper.Models;

namespace ShopDelivery.CatalogScraper.Scraping;

public sealed record PageExtraction(
    IReadOnlyList<ProductCandidate> Products,
    IReadOnlyList<Uri> Links);

public sealed partial class ProductPageExtractor
{
    private readonly HtmlParser _parser = new();

    public async Task<PageExtraction> ExtractAsync(
        string html,
        Uri pageUri,
        bool assumeProductPage,
        CancellationToken ct)
    {
        var document = await _parser.ParseDocumentAsync(html, ct);
        var baseUri = ResolveBaseUri(document, pageUri);
        var canonicalUri = ResolveUri(
                               document.QuerySelector("link[rel~='canonical']")?.GetAttribute("href"),
                               baseUri)
                           ?? pageUri;
        var products = new List<ProductCandidate>();

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (string.IsNullOrWhiteSpace(script.TextContent))
                continue;

            try
            {
                using var json = JsonDocument.Parse(
                    script.TextContent,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                        MaxDepth = 128,
                    });

                foreach (var node in FindProductNodes(json.RootElement))
                {
                    var product = MapJsonLdProduct(node, canonicalUri, baseUri);
                    if (product is not null)
                        products.Add(product);
                }
            }
            catch (JsonException)
            {
                // Invalid JSON-LD is common on retail pages. HTML metadata remains a fallback.
            }
        }

        if (products.Count == 0)
        {
            var fallback = ExtractFromHtmlMetadata(document, canonicalUri, baseUri, assumeProductPage);
            if (fallback is not null)
                products.Add(fallback);
        }

        var links = document.QuerySelectorAll("a[href]")
            .Select(anchor => ResolveUri(anchor.GetAttribute("href"), baseUri))
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PageExtraction(
            products
                .DistinctBy(ProductIdentity, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            links);
    }

    private static IEnumerable<JsonElement> FindProductNodes(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    foreach (var match in FindProductNodes(child))
                        yield return match;
                }
                break;
            case JsonValueKind.Object:
                if (IsProductType(element))
                    yield return element;

                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("offers")
                        || property.NameEquals("review")
                        || property.NameEquals("aggregateRating"))
                    {
                        continue;
                    }

                    foreach (var match in FindProductNodes(property.Value))
                        yield return match;
                }
                break;
        }
    }

    private static bool IsProductType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
            return false;

        return type.ValueKind switch
        {
            JsonValueKind.String => IsProductTypeName(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String && IsProductTypeName(item.GetString())),
            _ => false,
        };
    }

    private static bool IsProductTypeName(string? value) =>
        value?.Split('/').LastOrDefault()?.Equals("Product", StringComparison.OrdinalIgnoreCase) == true;

    private static ProductCandidate? MapJsonLdProduct(JsonElement product, Uri pageUri, Uri baseUri)
    {
        var name = Clean(GetText(product, "name"));
        if (name is null)
            return null;

        var brand = Clean(GetNestedText(product, "brand", "name") ?? GetText(product, "brand"));
        var category = Clean(GetNestedText(product, "category", "name") ?? GetText(product, "category"));
        var image = ResolveUri(GetImage(product), baseUri)?.AbsoluteUri;
        var externalCode = FirstNotEmpty(
            GetText(product, "gtin14"),
            GetText(product, "gtin13"),
            GetText(product, "gtin12"),
            GetText(product, "gtin8"),
            GetText(product, "gtin"),
            GetText(product, "sku"),
            GetText(product, "productID"));
        var source = ResolveUri(GetText(product, "url"), baseUri) ?? pageUri;

        return new ProductCandidate(
            name,
            brand,
            category,
            image,
            Clean(externalCode),
            source.AbsoluteUri);
    }

    private static ProductCandidate? ExtractFromHtmlMetadata(
        IDocument document,
        Uri pageUri,
        Uri baseUri,
        bool assumeProductPage)
    {
        var openGraphType = Meta(document, "property", "og:type");
        var hasProductMarkup = document.QuerySelector(
            "[itemtype$='/Product'], [itemtype$='#Product'], [typeof~='gr:Product'], [typeof~='gr:SomeItems']") is not null;
        if (!assumeProductPage
            && !hasProductMarkup
            && !string.Equals(openGraphType, "product", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Clean(
            AttributeOrText(document.QuerySelector("[itemprop='name']"))
            ?? AttributeOrText(document.QuerySelector("[property='gr:name']"))
            ?? document.QuerySelector("h1")?.TextContent
            ?? Meta(document, "property", "og:title")
            ?? document.Title);
        if (name is null)
            return null;

        var brand = Clean(
            Meta(document, "property", "product:brand")
            ?? AttributeOrText(document.QuerySelector("[itemprop='brand']"))
            ?? AttributeOrText(document.QuerySelector("[property='gr:hasBrand']")));
        var category = Clean(
            Meta(document, "property", "product:category")
            ?? AttributeOrText(document.QuerySelector("[itemprop='category']"))
            ?? AttributeOrText(document.QuerySelector("[property='gr:category']")));
        var imageValue = AttributeOrText(document.QuerySelector("[itemprop='image']"))
                         ?? AttributeOrText(document.QuerySelector("[rel~='foaf:depiction']"))
                         ?? Meta(document, "property", "og:image");
        var image = ResolveUri(imageValue, baseUri)?.AbsoluteUri;
        var externalCode = FirstNotEmpty(
            AttributeOrText(document.QuerySelector("[property='gr:hasEAN_UCC-13']")),
            AttributeOrText(document.QuerySelector("[itemprop='gtin14']")),
            AttributeOrText(document.QuerySelector("[itemprop='gtin13']")),
            AttributeOrText(document.QuerySelector("[itemprop='gtin12']")),
            AttributeOrText(document.QuerySelector("[itemprop='gtin8']")),
            AttributeOrText(document.QuerySelector("[itemprop='sku']")),
            AttributeOrText(document.QuerySelector("[property='gr:hasStockKeepingUnit']")),
            Meta(document, "property", "product:retailer_item_id"));

        return new ProductCandidate(
            name,
            brand,
            category,
            image,
            Clean(externalCode),
            pageUri.AbsoluteUri);
    }

    private static string? Meta(IDocument document, string attribute, string value) =>
        document.QuerySelector($"meta[{attribute}='{value}']")?.GetAttribute("content");

    private static string? AttributeOrText(IElement? element) =>
        element?.GetAttribute("content")
        ?? element?.GetAttribute("resource")
        ?? element?.GetAttribute("src")
        ?? element?.GetAttribute("href")
        ?? element?.TextContent;

    private static string? GetText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)),
            _ => null,
        };
    }

    private static string? GetNestedText(JsonElement element, string propertyName, string nestedName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Object)
            return GetText(value, nestedName);

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var nested = GetText(item, nestedName);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
        }

        return null;
    }

    private static string? GetImage(JsonElement product)
    {
        if (!product.TryGetProperty("image", out var image))
            return null;

        return ImageValue(image);
    }

    private static string? ImageValue(JsonElement image) => image.ValueKind switch
    {
        JsonValueKind.String => image.GetString(),
        JsonValueKind.Array => image.EnumerateArray()
            .Select(ImageValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
        JsonValueKind.Object => GetText(image, "contentUrl") ?? GetText(image, "url"),
        _ => null,
    };

    private static Uri ResolveBaseUri(IDocument document, Uri pageUri) =>
        ResolveUri(document.QuerySelector("base[href]")?.GetAttribute("href"), pageUri) ?? pageUri;

    private static Uri? ResolveUri(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(baseUri, value.Trim(), out var uri))
            return null;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    private static string ProductIdentity(ProductCandidate product) =>
        $"{product.BrandName}\n{product.Name}\n{product.ExternalCode}";

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Whitespace().Replace(value.Replace('\u00a0', ' '), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
