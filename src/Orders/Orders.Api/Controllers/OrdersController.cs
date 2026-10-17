using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Orders.Application;
using Orders.Domain;
using RabbitMQ.Client.Exceptions;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService service, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await service.CreateAsync(request.Product, request.Quantity, cancellationToken);
            // Broker mesajı onayladı; tüketicinin işlemesi asenkron devam eder.
            return Accepted(value: order);
        }
        catch (Exception ex) when (ex is RabbitMQClientException or IOException or TimeoutException or InvalidOperationException)
        {
            logger.LogError(ex, "Sipariş mesajı RabbitMQ'ya gönderilemedi.");
            return Problem(statusCode: 503, title: "Mesaj gönderilemedi; RabbitMQ bağlantısını kontrol edin.");
        }
    }
}

public sealed record CreateOrderRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*", ErrorMessage = "Ürün adı boş olamaz.")] string Product,
    [Range(1, 1000)] int Quantity);
