# DeliveryApp

## Customer authentication and ownership

The app uses OpenID Connect in the Blazor client and validates bearer access tokens in the API.
The API derives a pseudonymous customer id from the validated token issuer and subject; customer
ids are never accepted from request bodies or query parameters.

- Shared globally: products, brands, stores, store-product aliases, and product matching.
- Customer-owned: receipts, receipt prices, monthly budget settings, and budget dashboards.

For local Development, both projects use the development-only `local-customer` identity. The API
also accepts `X-Development-Customer` in Development so isolation can be tested with two simulated
customers. This header is never enabled outside Development.

For a deployed environment, register a browser/OpenID Connect application and an API scope with
your identity provider. Add these redirect URIs to the browser application:

- `https://<web-host>/authentication/login-callback`
- `https://<web-host>/authentication/logout-callback`

Set the public browser values in `ShopDelivery.Web/wwwroot/appsettings.json`:

```json
"Authentication": {
  "Authority": "https://<identity-provider>/<tenant>",
  "ClientId": "<browser-client-id>",
  "ApiScope": "<api-scope>"
}
```

Set the API validation values before provisioning:

```bash
azd env set AUTHENTICATION_AUTHORITY "https://<identity-provider>/<tenant>"
azd env set AUTHENTICATION_AUDIENCE "<api-audience>"
```

Production startup intentionally fails if authentication is not configured; it never falls back
to a client-supplied customer id.

`Authority` is the OpenID Connect issuer base URL. `ClientId` identifies the browser application.
`ApiScope` is the full delegated scope requested by the browser, while API `Audience` is the token
`aud` value configured for the API registration. For example, with Microsoft Entra ID:

```text
Authority: https://login.microsoftonline.com/<tenant-id>/v2.0
ClientId:  <single-page-application-client-id>
ApiScope:  api://<api-application-client-id>/access_as_user
Audience:  api://<api-application-client-id>
```

For Entra application roles, keep `Authentication:RoleClaim` set to `roles` in both projects and
assign the `admin` app role only to catalog administrators. If another provider emits a different
role claim, configure the same claim name in the web client and API.

The deployed API receives `AUTHENTICATION_AUTHORITY` and `AUTHENTICATION_AUDIENCE` through the azd
parameters and maps them to `Authentication__Authority` and `Authentication__Audience`. The static
Blazor values are public settings, not secrets; place the matching Authority, ClientId, and
ApiScope in the published `wwwroot/appsettings.json` before deployment.

## Product catalog flow

The EDEKA24 scrape is the initial global master catalog. It is not a permanent EDEKA-only model:
later store scrapes use the same importer, and receipt/manual proposals search the existing catalog
before creating a review-required product.

1. Scraped products are matched by GTIN, stable catalog key, then normalized name and brand.
2. Receipt matching first checks a confirmed store alias, then ranks confirmed master products.
3. A customer confirms a candidate or chooses “Not this — review as new.” The receipt and price are
   saved immediately to that customer's budget.
4. Uncertain new products receive a generic image and enter the administrator queue. An admin can
   fix name, barcode, brand, category, add/replace the image, confirm, merge, or reject.
5. Shared aliases are never silently overwritten. Disagreements produce history and admin review.

Open Food Facts is not called during receipt scanning or matching.

## Import reviewed catalog artifacts

The API owns database import so the scraper stays a local, reviewable collection tool:

```bash
dotnet run --project ShopDelivery.Api -- \
  --import-catalog ShopDelivery.CatalogScraper/artifacts/catalogs/edeka24/catalog.json \
  --sqlite ShopDelivery.Api/shopdelivery.db
```

Images are stored in an isolated BLOB table, SHA-256 deduplicated, and kept as the smaller of the
compressed source or lossless WebP. Product queries return only an image endpoint; the BLOB is
streamed on demand with long-lived browser caching. See
`ShopDelivery.Api/Data/README-database.md` for migration and provider details.

## Local store catalog scraper

`ShopDelivery.CatalogScraper` builds reviewable global master-product data from configured public
store pages. It honors `robots.txt`, throttles requests, limits crawling to allowlisted hosts, reads
Schema.org/HTML product names and images, and writes deduplicated JSON/CSV plus optional local image
copies. It never reads customer data or writes directly to the database.

See `ShopDelivery.CatalogScraper/README.md` and copy
`ShopDelivery.CatalogScraper/stores/store.example.json` to a `.local.json` configuration before
running it locally. Existing output is resumed by default: completed product pages and downloaded
images are skipped, while newly found products are merged into the same catalog atomically.
