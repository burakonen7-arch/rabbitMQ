using Notifications.Application;
using Notifications.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<NotificationInbox>();
builder.Services.AddHostedService<OrderCreatedConsumer>();
var app = builder.Build();
app.MapControllers();
app.Run();
