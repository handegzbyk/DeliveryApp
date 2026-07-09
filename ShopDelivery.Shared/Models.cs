namespace ShopDelivery.Shared;

public class Product
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
}

public class OrderItem
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
}

public class Order
{
    public int Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset PlacedAt { get; init; }
    public List<OrderItem> Items { get; init; } = [];
}

public record ChatRequest(string Message);
public record ChatReply(string Reply);