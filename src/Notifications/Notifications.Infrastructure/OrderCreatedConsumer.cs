using System.Text.Json;
using Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notifications.Infrastructure;

public sealed class OrderCreatedConsumer(IOptions<RabbitMqOptions> options,
    NotificationInbox inbox, ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var factory = new ConnectionFactory { HostName = o.HostName, Port = o.Port,
            UserName = o.UserName, Password = o.Password, VirtualHost = o.VirtualHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<OrderCreated>(delivery.Body.Span)
                    ?? throw new JsonException("Boş mesaj.");
                inbox.Handle(message);
                logger.LogInformation("Sipariş alındı: {OrderId}, ürün: {Product}, adet: {Quantity}",
                    message.OrderId, message.Product, message.Quantity);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                logger.LogWarning(ex, "Geçersiz mesaj dead-letter kuyruğuna gönderiliyor.");
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: stoppingToken);
                return;
            }
            // Yalnızca işlem tamamlandıktan sonra ACK. ACK öncesi kopmada mesaj tekrar teslim edilebilir.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };
        await channel.BasicConsumeAsync(RabbitMqTopology.Queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        logger.LogInformation("RabbitMQ tüketicisi hazır: {Queue}", RabbitMqTopology.Queue);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
