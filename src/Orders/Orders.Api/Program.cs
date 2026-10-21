using Orders.Application;
using Orders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Orders API", Version = "v1" }));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<RabbitMqOrderPublisher>();
builder.Services.AddSingleton<IOrderEventPublisher>(sp => sp.GetRequiredService<RabbitMqOrderPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqOrderPublisher>());
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Orders API";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Orders API v1");
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.Run();
