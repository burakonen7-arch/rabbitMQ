using RabbitMQ.Client;

namespace Orders.Infrastructure;

// Her iki servis aynı topolojiyi idempotent biçimde tanımlar; başlatma sırası önemsizdir.
internal static class RabbitMqTopology
{
    public const string Exchange = "orders.events";
    public const string RoutingKey = "order.created";
    public const string Queue = "notifications.order-created";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync("orders.dead-letter", ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync("notifications.failed", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync("notifications.failed", "orders.dead-letter", RoutingKey, cancellationToken: ct);
        var arguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "orders.dead-letter" };
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
