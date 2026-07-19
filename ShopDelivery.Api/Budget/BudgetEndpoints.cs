using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Auth;
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
                HttpContext httpContext,
                CustomerIdentity customerIdentity,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
                var response = await BuildExpenseResponseAsync(receiptId, customerId, db, ct);
                if (response is null)
                    return Results.NotFound();

                return Results.Ok(response);
            })
            .RequireAuthorization()
            .WithName("GetReceiptExpense");

        app.MapGet("/api/expenses/latest", async (
                HttpContext httpContext,
                CustomerIdentity customerIdentity,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
                var customerReceipts = db.Receipts
                    .AsNoTracking()
                    .Where(receipt => receipt.CustomerId == customerId);

                int? latestReceiptId;
                if (db.Database.IsSqlite())
                {
                    latestReceiptId = (await customerReceipts
                            .Select(receipt => new { receipt.Id, receipt.PurchasedAt })
                            .ToListAsync(ct))
                        .OrderByDescending(receipt => receipt.PurchasedAt)
                        .ThenByDescending(receipt => receipt.Id)
                        .Select(receipt => (int?)receipt.Id)
                        .FirstOrDefault();
                }
                else
                {
                    latestReceiptId = await customerReceipts
                        .OrderByDescending(receipt => receipt.PurchasedAt)
                        .ThenByDescending(receipt => receipt.Id)
                        .Select(receipt => (int?)receipt.Id)
                        .FirstOrDefaultAsync(ct);
                }

                if (latestReceiptId is null)
                    return Results.NotFound();

                var response = await BuildExpenseResponseAsync(latestReceiptId.Value, customerId, db, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .RequireAuthorization()
            .WithName("GetLatestExpense");

        app.MapPut("/api/budget", async (
                UpdateBudgetRequest request,
                HttpContext httpContext,
                CustomerIdentity customerIdentity,
                ShopDbContext db,
                CancellationToken ct) =>
            {
                if (request.MonthlyBudget <= 0 || request.MonthlyBudget > 1_000_000m)
                    return Results.BadRequest("Monthly budget must be between 0 and 1,000,000.");

                var customerId = customerIdentity.GetRequiredCustomerId(httpContext.User);
                var budget = await db.CustomerBudgets.FindAsync([customerId], ct);
                if (budget is null)
                {
                    budget = new CustomerBudget
                    {
                        CustomerId = customerId,
                        MonthlyBudget = request.MonthlyBudget,
                    };
                    db.CustomerBudgets.Add(budget);
                }
                else
                {
                    budget.MonthlyBudget = request.MonthlyBudget;
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new { budget.MonthlyBudget });
            })
            .RequireAuthorization()
            .WithName("UpdateCustomerBudget");
    }

    private static async Task<BasketExpenseResponse?> BuildExpenseResponseAsync(
        int receiptId,
        string customerId,
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
            .FirstOrDefaultAsync(
                receipt => receipt.Id == receiptId && receipt.CustomerId == customerId,
                ct);

        if (receipt is null)
            return null;

        var budget = await db.CustomerBudgets
            .AsNoTracking()
            .Where(customerBudget => customerBudget.CustomerId == customerId)
            .Select(customerBudget => (decimal?)customerBudget.MonthlyBudget)
            .FirstOrDefaultAsync(ct) ?? DefaultMonthlyBudget;
        var periodStart = new DateTimeOffset(
            receipt.PurchasedAt.Year,
            receipt.PurchasedAt.Month,
            1,
            0,
            0,
            0,
            receipt.PurchasedAt.Offset);
        var periodEnd = periodStart.AddMonths(1);

        var receiptsQuery = db.Receipts
            .AsNoTracking()
            .Include(periodReceipt => periodReceipt.Store)
            .Where(periodReceipt => periodReceipt.CustomerId == customerId);
        var receiptsInPeriod = db.Database.IsSqlite()
            ? (await receiptsQuery.ToListAsync(ct))
                .Where(periodReceipt => periodReceipt.PurchasedAt >= periodStart
                                        && periodReceipt.PurchasedAt < periodEnd)
                .ToList()
            : await receiptsQuery
                .Where(periodReceipt => periodReceipt.PurchasedAt >= periodStart
                                        && periodReceipt.PurchasedAt < periodEnd)
                .ToListAsync(ct);

        var observationsQuery = db.PriceObservations
            .AsNoTracking()
            .Include(observation => observation.Product)
            .Include(observation => observation.Receipt)
            .Where(observation => observation.Receipt.CustomerId == customerId);
        var observationsInPeriod = db.Database.IsSqlite()
            ? (await observationsQuery.ToListAsync(ct))
                .Where(observation => observation.Receipt.PurchasedAt >= periodStart
                                      && observation.Receipt.PurchasedAt < periodEnd)
                .ToList()
            : await observationsQuery
                .Where(observation => observation.Receipt.PurchasedAt >= periodStart
                                      && observation.Receipt.PurchasedAt < periodEnd)
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
