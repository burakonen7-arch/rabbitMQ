using System.Collections.Concurrent;
using Contracts;
using Notifications.Domain;

namespace Notifications.Application;

// Öğretim için bellek içi depo: yeniden başlatmada içerik ve tekrar tespit bilgisi silinir.
public sealed class NotificationInbox
{
    private readonly ConcurrentDictionary<Guid, ReceivedNotification> _messages = new();

    public void Handle(OrderCreated message)
    {
        if (message.EventId == Guid.Empty || message.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(message.Product) || message.Quantity < 1)
            throw new ArgumentException("Geçersiz sipariş mesajı.");
        _messages.TryAdd(message.EventId, new ReceivedNotification(
            message.EventId, message.OrderId, message.Product, message.Quantity, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<ReceivedNotification> GetAll() =>
        _messages.Values.OrderByDescending(x => x.ReceivedAt).ToArray();
}
