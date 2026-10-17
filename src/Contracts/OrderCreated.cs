namespace Contracts;

// İki servis arasındaki JSON mesaj sözleşmesi. EventId tekrar teslimi tanımlar.
public sealed record OrderCreated(Guid EventId, Guid OrderId, string Product, int Quantity, DateTimeOffset CreatedAt);
