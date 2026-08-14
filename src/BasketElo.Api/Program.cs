using BasketElo.Infrastructure;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Domain.Elo;
using BasketElo.Api.Elo;
using BasketElo.Api.Controllers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers().AddControllersAsServices();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EloResponseCache>();
builder.Services.AddHostedService<PostgresEloRebuildCacheInvalidationListener>();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();

    var eloController = scope.ServiceProvider.GetRequiredService<EloController>();
    foreach (var pool in EloPoolCatalog.All.OrderBy(x => x.DisplayOrder))
    {
        await eloController.GetBrowse(pool.Key, null, null, null, 100);
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "BasketElo API v1");
    options.RoutePrefix = "swagger";
});

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

