using BasketElo.Infrastructure;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Worker;
using Hangfire;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<HangfireStorageHealthCheck>("hangfire-storage")
    .AddCheck<HangfireServerHealthCheck>("hangfire-server");
builder.Services.AddHostedService<Worker>();
builder.Services.AddHangfireServer((serviceProvider, options) =>
{
    var jobOptions = serviceProvider.GetRequiredService<IOptions<EloJobOptions>>().Value;
    options.WorkerCount = jobOptions.EffectiveWorkerCount;
    options.Queues = EloJobQueues.InPriorityOrder;
    options.ServerName = $"basket-elo-worker:{Environment.MachineName}";
});

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
