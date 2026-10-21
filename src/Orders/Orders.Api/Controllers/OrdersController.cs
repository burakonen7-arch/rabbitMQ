using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Orders.Application;
using Orders.Domain;
using RabbitMQ.Client.Exceptions;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(Order), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await service.CreateAsync(request.Product, request.Quantity, cancellationToken);
            return Accepted(value: order);
        }
        catch (Exception ex) when (ex is RabbitMQClientException or IOException or TimeoutException or InvalidOperationException)
        {
            return Problem(statusCode: 503, title: "Mesaj gönderilemedi; RabbitMQ bağlantısını kontrol edin.");
        }
    }
}

public sealed record CreateOrderRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*", ErrorMessage = "Ürün adı boş olamaz.")] string Product,
    [Range(1, 1000)] int Quantity);
