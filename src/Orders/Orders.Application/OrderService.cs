using Contracts;
using Orders.Domain;

namespace Orders.Application;

public interface IOrderEventPublisher
{
    Task PublishAsync(OrderCreated message, CancellationToken cancellationToken);
}

public sealed class OrderService(IOrderEventPublisher publisher)
{
    public async Task<Order> CreateAsync(string product, int quantity, CancellationToken cancellationToken)
    {
        var order = Order.Create(product, quantity);
        var message = new OrderCreated(Guid.NewGuid(), order.Id, order.Product, order.Quantity, order.CreatedAt);
        await publisher.PublishAsync(message, cancellationToken);
        return order;
    }
}
