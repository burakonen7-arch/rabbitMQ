using Notifications.Application;
using Notifications.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Notifications API", Version = "v1" }));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<NotificationInbox>();
builder.Services.AddHostedService<OrderCreatedConsumer>();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Notifications API";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Notifications API v1");
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.Run();
