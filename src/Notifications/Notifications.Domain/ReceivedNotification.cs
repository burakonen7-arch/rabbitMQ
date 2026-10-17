namespace Notifications.Domain;

public sealed record ReceivedNotification(Guid EventId, Guid OrderId, string Product, int Quantity, DateTimeOffset ReceivedAt);
