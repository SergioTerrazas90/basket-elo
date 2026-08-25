using BasketElo.Infrastructure;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Api.Elo;
using BasketElo.Api.Controllers;
using BasketElo.Api.Auth;
using BasketElo.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return RateLimitPartition.GetNoLimiter("non-api");
        }

        var expectedSecret = builder.Configuration["InternalAuth:SharedSecret"];
        var suppliedSecret = httpContext.Request.Headers[InternalAuthHeaders.SharedSecret].ToString();
        var roles = httpContext.Request.Headers[InternalAuthHeaders.Roles]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (roles.Contains(ApplicationRoleKeys.Admin, StringComparer.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(expectedSecret) || string.Equals(suppliedSecret, expectedSecret, StringComparison.Ordinal)))
        {
            return RateLimitPartition.GetNoLimiter("admin");
        }

        var userId = httpContext.Request.Headers[InternalAuthHeaders.UserId].ToString();
        var identity = Guid.TryParse(userId, out var parsedUserId)
            ? parsedUserId.ToString("D")
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isHistoryRequest = httpContext.Request.Path.StartsWithSegments("/api/elo/teams") ||
            httpContext.Request.Path.StartsWithSegments("/api/elo/rankings/evolution");
        var limit = isHistoryRequest ? 20 : 60;
        var bucket = isHistoryRequest ? "history" : "general";

        return RateLimitPartition.GetTokenBucketLimiter(
            $"{bucket}:{identity}",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limit,
                TokensPerPeriod = limit,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});

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

    await eloController.WarmRankingCachesAsync();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "BasketElo API v1");
    options.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

