using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;

namespace ShopDelivery.Api.Enrichment;

public class EnrichmentWorker(
    IEnrichmentQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<EnrichmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Enrichment worker started.");

        await foreach (var productId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await EnrichOneAsync(productId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                // Never let one failure kill the worker loop.
                logger.LogError(ex, "Failed to enrich product {ProductId}", productId);
            }
        }

        logger.LogInformation("Enrichment worker stopping.");
    }

    private async Task EnrichOneAsync(int productId, CancellationToken ct)
    {
        // BackgroundService is a singleton; DbContext/enricher are scoped → create a scope.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var enricher = scope.ServiceProvider.GetRequiredService<IProductEnricher>();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            logger.LogWarning("Product {ProductId} not found; skipping.", productId);
            return;
        }

        // Skip if already enriched (idempotent — safe to re-enqueue).
        if (!string.IsNullOrEmpty(product.ImageUrl) && !string.IsNullOrEmpty(product.Category))
            return;

        var info = await enricher.EnrichAsync(product.Name, ct);   // was (product.Name, product.Barcode, ct)
        if (info is null)
        {
            logger.LogInformation("No enrichment data for product {ProductId} ({Name})", productId, product.Name);
            return;
        }

        // Fill only missing fields; don't overwrite user-entered values.
        product.OpenFoodFactsCode ??= info.ExternalId;
        product.ImageUrl ??= info.ImageUrl;
        product.Category ??= info.Category;
        if (string.IsNullOrWhiteSpace(product.Name) && !string.IsNullOrWhiteSpace(info.CanonicalName))
            product.Name = info.CanonicalName!;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Enriched product {ProductId} ({Name})", productId, product.Name);
    }
}
