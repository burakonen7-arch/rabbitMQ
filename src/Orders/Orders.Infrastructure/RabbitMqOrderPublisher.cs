using System.Text.Json;
using Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orders.Application;
using RabbitMQ.Client;

namespace Orders.Infrastructure;

public sealed class RabbitMqOrderPublisher(IOptions<RabbitMqOptions> options) : IOrderEventPublisher, IHostedService
{
    private IConnection? _connection;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var factory = new ConnectionFactory { HostName = o.HostName, Port = o.Port,
            UserName = o.UserName, Password = o.Password, VirtualHost = o.VirtualHost };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
    }

    public async Task PublishAsync(OrderCreated message, CancellationToken cancellationToken)
    {
        // Bağlantı uzun ömürlüdür. Eşzamanlı HTTP istekleri ayrı channel kullanır.
        var connection = _connection ?? throw new InvalidOperationException("RabbitMQ bağlantısı hazır değil.");
        var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
        var properties = new BasicProperties { Persistent = true, ContentType = "application/json",
            MessageId = message.EventId.ToString(), Type = nameof(OrderCreated) };
        // mandatory + confirm: yönlendirilemeyen veya broker'ın reddettiği mesaj başarılı sayılmaz.
        await channel.BasicPublishAsync(RabbitMqTopology.Exchange, RabbitMqTopology.RoutingKey,
            mandatory: true, basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(message), cancellationToken: cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
