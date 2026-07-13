using BasketElo.Domain.Backfill;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BasketElo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

        services.AddDbContext<BasketEloDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.Configure<ApiSportsOptions>(configuration.GetSection(ApiSportsOptions.SectionName));
        services.Configure<ModelLabPlanOptions>(configuration.GetSection(ModelLabPlanOptions.SectionName));
        services.AddSingleton<IApiSportsRateLimiter, ApiSportsRateLimiter>();
        services.AddSingleton<IApiSportsLeagueCache, ApiSportsLeagueCache>();
        services.AddHttpClient<ApiSportsBasketballDataProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiSportsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ApiSportsBasketballDataProvider>());
        services.AddScoped<IBackfillJobProcessor, BackfillJobProcessor>();
        services.AddScoped<IBackfillCoverageService, BackfillCoverageService>();
        services.AddSingleton<IBackfillCatalog, BackfillCatalog>();
        services.AddScoped<IEloRebuildService, EloRebuildService>();
        services.AddScoped<IEloRebuildJobProcessor, EloRebuildJobProcessor>();
        services.AddScoped<IModelLabBacktestService, ModelLabBacktestService>();
        services.AddScoped<IModelLabModelService, ModelLabModelService>();
        services.AddScoped<IModelLabRunService, ModelLabRunService>();
        services.AddScoped<IModelLabEntitlementService, ModelLabEntitlementService>();
        services.AddSingleton<IEloRebuildNotificationPublisher, PostgresEloRebuildNotificationPublisher>();
        services.AddScoped<IIdentityHealthCheckService, IdentityHealthCheckService>();

        return services;
    }
}
