# Catalog database

EF Core migrations are the only schema source of truth. The API applies pending migrations at
startup. Do not maintain a second hand-written SQL schema.

## Ownership and workflow

- Global master data: `Brands`, `Products`, `ImageAssets`, `ProductImages`, `Stores`,
  `StoreProducts`, alias history, and the administrator review queue.
- Customer data: `Receipts`, `PriceObservations`, and `CustomerBudgets`. Every API query for these
  records derives the customer key from the validated token; request payloads cannot choose it.
- A scraped catalog is matched by GTIN, stable catalog key, then normalized name and brand.
- Uncertain receipt/manual products use `ReviewRequired` and remain usable for that customer's
  receipt with the generic image. An administrator can correct and confirm, merge, or reject them.
- A customer's “Not this” choice never silently changes an existing shared alias. It records
  history, marks the alias disputed, and creates an administrator review item.

## Images

Images are isolated in `ImageAssets`, deduplicated by SHA-256, and linked to products. Import keeps
the smaller of the already-compressed source and a lossless WebP representation. This does not
invent missing pixels: the browser decodes the selected compressed bytes at display time. Normal
product and matching queries select only the image id, never the BLOB. `/api/product-images/{id}`
streams the BLOB with an immutable cache header and ETag.

## Local SQLite

With no SQL Server connection configured, Development uses `ShopDelivery.Api/shopdelivery.db`.
To import a reviewed scraper artifact:

```bash
dotnet run --project ShopDelivery.Api -- \
  --import-catalog ShopDelivery.CatalogScraper/artifacts/catalogs/edeka24/catalog.json \
  --sqlite ShopDelivery.Api/shopdelivery.db
```

The import is idempotent. Running it again updates existing products and creates no duplicate
images or aliases.

`--sqlite` takes precedence over a configured SQL Server connection, so this command cannot
accidentally import the local artifact into Azure. Omit `--sqlite` only when you intentionally want
the provider selected from `ConnectionStrings`.

## Azure SQL

Set `ConnectionStrings__Sql` on the API container. Startup applies the same versioned migrations
to an empty or existing Azure SQL database. For local administrative migration commands:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations has-pending-model-changes \
  --project ShopDelivery.Api --startup-project ShopDelivery.Api
dotnet ef database update \
  --project ShopDelivery.Api --startup-project ShopDelivery.Api
```

Create a new migration whenever `ShopDbContext` changes; review it for both SQLite and SQL Server
before deployment.
