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
        services.Configure<BasketballReferenceOptions>(configuration.GetSection(BasketballReferenceOptions.SectionName));
        services.Configure<FiveThirtyEightOptions>(configuration.GetSection(FiveThirtyEightOptions.SectionName));
        services.Configure<NbaRefreshOptions>(configuration.GetSection(NbaRefreshOptions.SectionName));
        services.Configure<ModelLabPlanOptions>(configuration.GetSection(ModelLabPlanOptions.SectionName));
        services.AddSingleton<IApiSportsRateLimiter, ApiSportsRateLimiter>();
        services.AddSingleton<IApiSportsLeagueCache, ApiSportsLeagueCache>();
        services.AddSingleton<IBasketballReferenceRateLimiter, BasketballReferenceRateLimiter>();
        services.AddHttpClient<ApiSportsBasketballDataProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiSportsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<BasketballReferenceBasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<BasketballReferenceOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<FibaBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://www.fiba.basketball");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<WikipediaEuroBasketQualificationDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://en.wikipedia.org");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<GlobalSportsArchiveBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://globalsportsarchive.com");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });

        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ApiSportsBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<BasketballReferenceBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FibaBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<WikipediaEuroBasketQualificationDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GlobalSportsArchiveBasketballDataProvider>());
        services.AddSingleton<FiveThirtyEightBasketballDataProvider>();
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FiveThirtyEightBasketballDataProvider>());
        services.AddScoped<IBackfillJobProcessor, BackfillJobProcessor>();
        services.AddScoped<INbaCurrentSeasonRefreshService, NbaCurrentSeasonRefreshService>();
        services.AddSingleton(TimeProvider.System);
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
