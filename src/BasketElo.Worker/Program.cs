using BasketElo.Infrastructure;
using BasketElo.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
