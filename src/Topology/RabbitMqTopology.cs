using RabbitMQ.Client;

namespace Topology;

// İki servis de kuyruk, exchange ve binding tanımları için bu ortak sınıfı kullanır.
public static class RabbitMqTopology
{
    public const string Exchange = "orders.events";
    public const string RoutingKey = "order.created";
    public const string Queue = "notifications.order-created";
    public const string DemoQueue = "demo.order-created";
    public const string DeadLetterExchange = "orders.dead-letter";
    public const string FailedQueue = "notifications.failed";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        
        await channel.QueueDeclareAsync(FailedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(FailedQueue, DeadLetterExchange, RoutingKey, cancellationToken: ct);
        
        var arguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);

        // Consumer bu kuyruğu dinlemez; yayınlanan mesajların son 100 kopyası inceleme için kalır.
        var demoArguments = new Dictionary<string, object?>
        {
            ["x-max-length"] = 100,
            ["x-overflow"] = "drop-head"
        };
        
        await channel.QueueDeclareAsync(DemoQueue, durable: true, exclusive: false, autoDelete: false, arguments: demoArguments, cancellationToken: ct);
        await channel.QueueBindAsync(DemoQueue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
