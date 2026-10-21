using System.Text.Json;
using Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notifications.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Topology;

namespace Notifications.Infrastructure;

public sealed class OrderCreatedConsumer(IOptions<RabbitMqOptions> options,
    NotificationInbox inbox) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.ConsumerEnabled)
        {
            return;
        }

        var factory = new ConnectionFactory {
            HostName = o.HostName, 
            Port = o.Port,
            UserName = o.UserName, 
            Password = o.Password, 
            VirtualHost = o.VirtualHost };
        
        //factory üzerinden connection oluştur, oluşan connection üzerinden de channel aç
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);
        
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<OrderCreated>(delivery.Body.Span) ?? throw new JsonException("Boş mesaj.");
                
                inbox.Handle(message);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: stoppingToken);
                return;
            }
        
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };
        
        // Kuyruğa abone olup, mesajın alınmasını sağlar
        await channel.BasicConsumeAsync(RabbitMqTopology.Queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
