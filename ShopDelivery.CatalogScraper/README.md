# ShopDelivery.CatalogScraper

Local command-line project for building reviewed ShopDelivery master-product data from public store
pages. It extracts Schema.org `Product` JSON-LD first and falls back to Open Graph or HTML product
metadata. The crawler can discover pages from `robots.txt`, XML sitemaps, and links.

It does not read or write customer data, log in to store accounts, bypass access controls, or write
directly to ShopDelivery's database. Review the generated catalog before importing it into master
data.

## Configure a store

Copy `stores/store.example.json` to a file ending in `.local.json` and set:

- `StoreName`: shared ShopDelivery store name.
- `StartUrls`: public category or product pages from which link discovery starts.
- `SitemapUrls`: optional explicit sitemap URLs. Otherwise the crawler uses `robots.txt` and then
  `/sitemap.xml`.
- `AllowedHosts`: exact hosts the crawler may visit. Redirects or links to other hosts are ignored.
- `AllowedImageHosts`: exact store/CDN hosts from which local image copies may be downloaded.
- `ProductUrlPatterns`: regular expressions used to prioritize and recognize product pages. Sitemap
  entries that do not match are ignored when patterns are configured.
- `MaxPages`, `MaxSitemapUrls`, `MaxPageBytes`, `MaxSitemapBytes`, and
  `RequestDelayMilliseconds`: crawl safety limits.
- `RespectRobotsTxt`: keep this `true`. A failed or denied `robots.txt` blocks that origin.
- `DownloadImages`: store optional local review copies under the output `images` directory.
- `ResumeExistingCatalog`: keep this `true` for large catalogs. Existing product source pages and
  downloaded images are skipped, while new products and aliases are merged into `catalog.json`.
- `KeepQueryStrings`: normally `false` to avoid crawler traps. Enable only when pagination needs it.
- `OutputDirectory`: resolved relative to the configuration file.
- `UserAgent`: use an honest name and your real contact address.

Only HTTPS targets are accepted. HTTP is permitted solely for loopback fixture testing.

## Run locally

From the repository root:

```bash
dotnet run --project ShopDelivery.CatalogScraper -- \
  --config ShopDelivery.CatalogScraper/stores/my-store.local.json
```

Press `Ctrl+C` to stop cleanly. Generated output is ignored by Git.

### EDEKA24

`stores/edeka.example.json` contains a conservative EDEKA24 configuration. Copy it to the ignored
local configuration once, then run it from the repository root:

```bash
cp ShopDelivery.CatalogScraper/stores/edeka.example.json \
  ShopDelivery.CatalogScraper/stores/edeka.local.json
dotnet run --project ShopDelivery.CatalogScraper -- \
  --config ShopDelivery.CatalogScraper/stores/edeka.local.json
```

The configuration starts from the public top-level catalog category pages and discovers product
links from them. It deliberately has no explicit sitemap because the sitemap URL currently advertised by
EDEKA24 is not usable over the scraper's HTTPS-only transport. EDEKA24 publishes a 20-second crawl
delay, so even a limited run takes time. Keep that delay intact; reduce `MaxPages` when testing and
increase it gradually for broader catalog coverage. `MaxPages` is a per-run safety limit; repeat
the command to continue from the existing output until a run adds no new products.

EDEKA24 product pages publish GoodRelations/RDFa fields. The extractor reads the exact product name,
category, EAN, and 400-by-400 image URL from those fields. A brand is left empty when the page does
not publish a separate brand value; it is not guessed from the first word of the product name.

## Output

The configured directory receives:

- `catalog.json`: versioned master products, store catalog aliases, source URLs, and crawl statistics.
- `catalog.csv`: the same essential fields for manual review and spreadsheet cleanup.
- `images/`: optional downloaded product images named with the stable `ProductKey`.

Local image copies are restricted to common raster formats; active formats such as SVG are kept as
source URLs but are not downloaded.

Products are deduplicated using normalized **brand + product name**, producing a deterministic key.
GTIN/SKU is retained when published by the store, but is not trusted as the only identity because
many store sites expose internal SKUs.

`ImageUrl` retains the source URL used by the app; `LocalImagePath` is only a review/archive copy.
Before using images in production, confirm the store's terms and image redistribution rights or
copy approved assets into storage you control.

## Limits and responsible use

- Check each site's terms and `robots.txt` before running a large crawl.
- Keep the request delay conservative and the page limit small during configuration.
- The project extracts server-rendered HTML and structured data. It intentionally does not automate
  browsers, evade bot controls, solve CAPTCHAs, or scrape authenticated pages.
- Store website names are useful catalog aliases, but they are not automatically treated as
  customer-confirmed receipt aliases.
