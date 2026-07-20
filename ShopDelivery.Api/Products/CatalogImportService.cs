using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using SixLabors.ImageSharp;

namespace ShopDelivery.Api.Products;

public sealed record CatalogImportResult(
    int CreatedProducts,
    int UpdatedProducts,
    int CreatedAliases,
    int ReviewItems,
    int StoredImages,
    long SourceImageBytes,
    long StoredImageBytes,
    int FailedImages);

public sealed class CatalogImportService(
    ShopDbContext db,
    IProductImageStore imageStore,
    ILogger<CatalogImportService> logger)
{
    public async Task<CatalogImportResult> ImportAsync(string catalogPath, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(catalogPath);
        var catalogDirectory = Path.GetDirectoryName(fullPath)
                               ?? throw new InvalidOperationException("Catalog directory is unavailable.");
        await using var input = File.OpenRead(fullPath);
        var catalog = await JsonSerializer.DeserializeAsync<ImportCatalogDocument>(
                          input,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                          ct)
                      ?? throw new InvalidDataException("Catalog JSON is empty.");
        if (catalog.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported catalog schema version {catalog.SchemaVersion}.");

        var store = await ResolveStoreAsync(catalog.StoreName, ct);
        var productsByCatalogKey = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;
        var storedImages = 0;
        var failedImages = 0;
        long sourceImageBytes = 0;
        long storedImageBytes = 0;

        foreach (var item in catalog.Products)
        {
            ct.ThrowIfCancellationRequested();
            var brand = await ResolveBrandAsync(item.BrandName, ct);
            var normalizedName = CatalogTextNormalizer.Normalize(item.Name);
            var gtin = CatalogTextNormalizer.NormalizeGtin(item.ExternalCode);
            var product = await FindProductAsync(item.ProductKey, gtin, normalizedName, brand?.Id, ct);
            if (product is null)
            {
                product = new Product
                {
                    CatalogKey = item.ProductKey,
                    Gtin = gtin,
                    Name = item.Name.Trim(),
                    NormalizedName = normalizedName,
                    Brand = brand,
                    Category = NormalizeOptional(item.Category),
                    Status = ProductStatus.Confirmed,
                    SourceUrl = NormalizeOptional(item.SourceUrl),
                };
                db.Products.Add(product);
                created++;
            }
            else
            {
                product.CatalogKey ??= item.ProductKey;
                product.Gtin ??= gtin;
                product.Category ??= NormalizeOptional(item.Category);
                product.Brand ??= brand;
                product.SourceUrl ??= NormalizeOptional(item.SourceUrl);
                product.UpdatedAt = DateTimeOffset.UtcNow;
                if (product.Status is ProductStatus.Provisional or ProductStatus.ReviewRequired)
                    product.Status = ProductStatus.Confirmed;
                updated++;
            }

            productsByCatalogKey[item.ProductKey] = product;
            if (!string.IsNullOrWhiteSpace(item.LocalImagePath))
            {
                var imagePath = SafeCatalogPath(catalogDirectory, item.LocalImagePath);
                if (File.Exists(imagePath))
                {
                    try
                    {
                        var sourceLength = new FileInfo(imagePath).Length;
                        await using var image = File.OpenRead(imagePath);
                        var storedImage = await imageStore.SaveAsync(image, ct);
                        sourceImageBytes += sourceLength;
                        if (storedImage.Created)
                        {
                            storedImageBytes += storedImage.ByteLength;
                            storedImages++;
                        }

                        var existingImage = product.Images.FirstOrDefault(link => link.IsPrimary)
                                            ?? (product.Id > 0
                                                ? await db.ProductImages.FirstOrDefaultAsync(
                                                    link => link.ProductId == product.Id && link.IsPrimary,
                                                    ct)
                                                : null);
                        if (existingImage is null)
                        {
                            existingImage = new ProductImage
                            {
                                IsPrimary = true,
                                SourceUrl = NormalizeOptional(item.ImageUrl),
                            };
                            ApplyStoredImage(existingImage, storedImage);
                            product.Images.Add(existingImage);
                        }
                        else if (existingImage.ImageAssetId != storedImage.Id || storedImage.PendingEntity is not null)
                        {
                            ApplyStoredImage(existingImage, storedImage);
                            existingImage.SourceUrl = NormalizeOptional(item.ImageUrl);
                        }
                    }
                    catch (Exception exception) when (exception is InvalidDataException or IOException or UnknownImageFormatException)
                    {
                        failedImages++;
                        logger.LogWarning(exception, "Could not import image {ImagePath}", imagePath);
                    }
                }
            }

            if ((created + updated) % 25 == 0)
                await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        var createdAliases = 0;
        var reviewItems = 0;
        foreach (var aliasItem in catalog.StoreAliases)
        {
            if (!productsByCatalogKey.TryGetValue(aliasItem.ProductKey, out var product))
                continue;

            var normalizedAlias = CatalogTextNormalizer.Normalize(aliasItem.StoreProductName);
            var code = NormalizeOptional(aliasItem.StoreProductCode);
            var alias = await db.StoreProducts.FirstOrDefaultAsync(
                candidate => candidate.StoreId == store.Id
                             && (candidate.NormalizedName == normalizedAlias
                                 || (code != null && candidate.StoreProductCode == code)),
                ct);
            if (alias is null)
            {
                db.StoreProducts.Add(new StoreProduct
                {
                    StoreId = store.Id,
                    ProductId = product.Id,
                    Name = aliasItem.StoreProductName.Trim(),
                    NormalizedName = normalizedAlias,
                    StoreProductCode = code,
                    Status = StoreAliasStatus.Confirmed,
                    ConfirmationCount = 1,
                });
                createdAliases++;
            }
            else if (alias.ProductId != product.Id)
            {
                alias.Status = StoreAliasStatus.Disputed;
                db.ProductReviewItems.Add(new ProductReviewItem
                {
                    ProposedProductId = product.Id,
                    CandidateProductId = alias.ProductId,
                    RawName = aliasItem.StoreProductName,
                    NormalizedName = normalizedAlias,
                    SourceType = "CatalogAliasConflict",
                    SourceReference = $"store-alias:{alias.Id}",
                });
                reviewItems++;
            }
            else
            {
                alias.LastSeenAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        await db.ImageAssets
            .Where(asset => !asset.Products.Any())
            .ExecuteDeleteAsync(ct);
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync("VACUUM", ct);
        return new CatalogImportResult(
            created,
            updated,
            createdAliases,
            reviewItems,
            storedImages,
            sourceImageBytes,
            storedImageBytes,
            failedImages);
    }

    private async Task<Product?> FindProductAsync(
        string catalogKey,
        string? gtin,
        string normalizedName,
        int? brandId,
        CancellationToken ct)
    {
        if (gtin is not null)
        {
            var byGtin = await db.Products.FirstOrDefaultAsync(product => product.Gtin == gtin, ct);
            if (byGtin is not null)
                return byGtin;
        }

        var byKey = await db.Products.FirstOrDefaultAsync(product => product.CatalogKey == catalogKey, ct);
        if (byKey is not null)
            return byKey;

        return await db.Products.FirstOrDefaultAsync(
            product => product.NormalizedName == normalizedName && product.BrandId == brandId,
            ct);
    }

    private async Task<Brand?> ResolveBrandAsync(string? rawName, CancellationToken ct)
    {
        var name = NormalizeOptional(rawName);
        if (name is null)
            return null;
        var normalized = CatalogTextNormalizer.Normalize(name);
        var local = db.Brands.Local.FirstOrDefault(brand => brand.NormalizedName == normalized);
        if (local is not null)
            return local;
        var existing = await db.Brands.FirstOrDefaultAsync(brand => brand.NormalizedName == normalized, ct);
        if (existing is not null)
            return existing;
        var brand = new Brand { Name = name, NormalizedName = normalized };
        db.Brands.Add(brand);
        return brand;
    }

    private async Task<Store> ResolveStoreAsync(string rawName, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Unknown store" : rawName.Trim();
        var normalized = CatalogTextNormalizer.Normalize(name);
        var existing = await db.Stores.FirstOrDefaultAsync(
            store => store.NormalizedName == normalized && store.BranchIdentifier == "",
            ct);
        if (existing is not null)
            return existing;
        var store = new Store { Name = name, NormalizedName = normalized };
        db.Stores.Add(store);
        await db.SaveChangesAsync(ct);
        return store;
    }

    private static string SafeCatalogPath(string catalogDirectory, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(catalogDirectory, relativePath));
        var root = Path.GetFullPath(catalogDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException("Image path escapes the catalog directory.");
        return fullPath;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ApplyStoredImage(ProductImage link, StoredImageAsset storedImage)
    {
        if (storedImage.PendingEntity is not null)
            link.ImageAsset = storedImage.PendingEntity;
        else
            link.ImageAssetId = storedImage.Id;
    }

    private sealed record ImportCatalogDocument(
        int SchemaVersion,
        string StoreName,
        List<ImportProduct> Products,
        List<ImportAlias> StoreAliases);

    private sealed record ImportProduct(
        string ProductKey,
        string Name,
        string? BrandName,
        string? Category,
        string? ExternalCode,
        string? ImageUrl,
        string? LocalImagePath,
        string? SourceUrl);

    private sealed record ImportAlias(
        string StoreName,
        string StoreProductName,
        string? StoreProductCode,
        string ProductKey,
        string? SourceUrl);
}
