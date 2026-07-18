namespace ShopDelivery.Shared;

public record BasketExpenseResponse(
    int ReceiptId,
    string StoreName,
    DateTimeOffset PurchasedAt,
    decimal Total,
    List<BasketExpenseLine> Lines,
    BudgetDashboardSummary Dashboard);

public record BasketExpenseLine(
    string RawText,
    string ProductName,
    string? StoreProductName,
    string? BrandName,
    string? Category,
    string? ImageUrl,
    decimal Price,
    int Quantity);

public record BudgetDashboardSummary(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal MonthlyBudget,
    decimal Spent,
    decimal Remaining,
    List<BudgetBreakdown> ByStore,
    List<BudgetBreakdown> ByCategory);

public record BudgetBreakdown(string Name, decimal Amount);
