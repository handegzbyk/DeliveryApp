using Microsoft.AspNetCore.SignalR;

namespace ShopDelivery.Api.Hubs;

public sealed class DeliveryHub : Hub
{
    // Clients join a per-order group to receive location updates.
    public Task TrackOrder(int orderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, OrderGroup(orderId));

    public Task StopTracking(int orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OrderGroup(orderId));

    public static string OrderGroup(int orderId) => $"order-{orderId}";
}