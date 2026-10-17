using Orders.Application;
using Orders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<RabbitMqOrderPublisher>();
builder.Services.AddSingleton<IOrderEventPublisher>(sp => sp.GetRequiredService<RabbitMqOrderPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqOrderPublisher>());
var app = builder.Build();
app.MapControllers();
app.Run();
