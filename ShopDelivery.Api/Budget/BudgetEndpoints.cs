using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using ShopDelivery.Shared;

namespace ShopDelivery.Api.Budget;

public static class BudgetEndpoints
{
    private const decimal DefaultMonthlyBudget = 500m;

    public static void MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/receipts/{receiptId:int}/expense", async (
                int receiptId,
                decimal? monthlyBudget,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                var response = await BuildExpenseResponseAsync(receiptId, monthlyBudget, db, ct);
                if (response is null)
                    return Results.NotFound();

                return Results.Ok(response);
            })
            .WithName("GetReceiptExpense");

        app.MapGet("/api/expenses/latest", async (
                decimal? monthlyBudget,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                var latestReceiptId = await db.Receipts
                    .AsNoTracking()
                    .OrderByDescending(receipt => receipt.PurchasedAt)
                    .ThenByDescending(receipt => receipt.Id)
                    .Select(receipt => (int?)receipt.Id)
                    .FirstOrDefaultAsync(ct);

                if (latestReceiptId is null)
                    return Results.NotFound();

                var response = await BuildExpenseResponseAsync(latestReceiptId.Value, monthlyBudget, db, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("GetLatestExpense");
    }

    private static async Task<BasketExpenseResponse?> BuildExpenseResponseAsync(
        int receiptId,
        decimal? monthlyBudget,
        ShopDbContext db,
        CancellationToken ct)
    {
        var receipt = await db.Receipts
            .AsNoTracking()
            .Include(receipt => receipt.Store)
            .Include(receipt => receipt.Lines)
            .ThenInclude(line => line.Product)
            .ThenInclude(product => product.Brand)
            .Include(receipt => receipt.Lines)
            .ThenInclude(line => line.StoreProduct)
            .FirstOrDefaultAsync(receipt => receipt.Id == receiptId, ct);

        if (receipt is null)
            return null;

        var budget = monthlyBudget.GetValueOrDefault(DefaultMonthlyBudget);
        var periodStart = new DateTimeOffset(
            receipt.PurchasedAt.Year,
            receipt.PurchasedAt.Month,
            1,
            0,
            0,
            0,
            receipt.PurchasedAt.Offset);
        var periodEnd = periodStart.AddMonths(1);

        var receiptsInPeriod = await db.Receipts
            .AsNoTracking()
            .Include(periodReceipt => periodReceipt.Store)
            .Where(periodReceipt => periodReceipt.PurchasedAt >= periodStart && periodReceipt.PurchasedAt < periodEnd)
            .ToListAsync(ct);

        var observationsInPeriod = await db.PriceObservations
            .AsNoTracking()
            .Include(observation => observation.Product)
            .Include(observation => observation.Receipt)
            .Where(observation => observation.Receipt.PurchasedAt >= periodStart && observation.Receipt.PurchasedAt < periodEnd)
            .ToListAsync(ct);

        var spent = receiptsInPeriod.Sum(periodReceipt => periodReceipt.Total);
        var dashboard = new BudgetDashboardSummary(
            periodStart,
            periodEnd,
            budget,
            spent,
            budget - spent,
            receiptsInPeriod
                .GroupBy(periodReceipt => periodReceipt.Store.Name)
                .Select(group => new BudgetBreakdown(group.Key, group.Sum(receipt => receipt.Total)))
                .OrderByDescending(item => item.Amount)
                .ToList(),
            observationsInPeriod
                .GroupBy(observation => string.IsNullOrWhiteSpace(observation.Product.Category)
                    ? "Uncategorized"
                    : observation.Product.Category)
                .Select(group => new BudgetBreakdown(group.Key, group.Sum(observation => observation.Price)))
                .OrderByDescending(item => item.Amount)
                .Take(8)
                .ToList());

        var lines = receipt.Lines
            .OrderBy(line => line.Id)
            .Select(line => new BasketExpenseLine(
                line.RawText,
                line.Product.Name,
                line.StoreProduct?.Name,
                line.Product.Brand?.Name,
                line.Product.Category,
                line.Product.ImageUrl,
                line.Price,
                line.Quantity))
            .ToList();

        return new BasketExpenseResponse(
            receipt.Id,
            receipt.Store.Name,
            receipt.PurchasedAt,
            receipt.Total,
            lines,
            dashboard);
    }
}
