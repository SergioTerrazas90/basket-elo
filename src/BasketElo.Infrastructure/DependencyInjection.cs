using BasketElo.Domain.Backfill;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.CurrentResults;
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
        services.Configure<AcbArchiveOptions>(configuration.GetSection(AcbArchiveOptions.SectionName));
        services.Configure<AbaLeagueOfficialOptions>(configuration.GetSection(AbaLeagueOfficialOptions.SectionName));
        services.Configure<DbasketOptions>(configuration.GetSection(DbasketOptions.SectionName));
        services.Configure<LbaOfficialOptions>(configuration.GetSection(LbaOfficialOptions.SectionName));
        services.Configure<ItalianCupWikipediaOptions>(configuration.GetSection(ItalianCupWikipediaOptions.SectionName));
        services.Configure<FrenchHistoricalOptions>(configuration.GetSection(FrenchHistoricalOptions.SectionName));
        services.Configure<GreekOfficialOptions>(configuration.GetSection(GreekOfficialOptions.SectionName));
        services.Configure<GermanBasketballOptions>(configuration.GetSection(GermanBasketballOptions.SectionName));
        services.Configure<TurkishBasketballOptions>(configuration.GetSection(TurkishBasketballOptions.SectionName));
        services.Configure<SerbianHistoricalOptions>(configuration.GetSection(SerbianHistoricalOptions.SectionName));
        services.Configure<BackfillOptions>(configuration.GetSection(BackfillOptions.SectionName));
        services.Configure<BasketballReferenceOptions>(configuration.GetSection(BasketballReferenceOptions.SectionName));
        services.Configure<FiveThirtyEightOptions>(configuration.GetSection(FiveThirtyEightOptions.SectionName));
        services.Configure<NbaRefreshOptions>(configuration.GetSection(NbaRefreshOptions.SectionName));
        services.Configure<CurrentResultsOptions>(configuration.GetSection(CurrentResultsOptions.SectionName));
        services.Configure<LiveScoreOptions>(configuration.GetSection(LiveScoreOptions.SectionName));
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
        services.AddHttpClient<AcbArchiveBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<DbasketAcbBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://dbasket.net");
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<AcbOfficialTournamentBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://acb.com");
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<AcbOfficialLigaNacionalBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://www.acb.com");
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<AbaLeagueOfficialBasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<AbaLeagueOfficialOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(providerOptions.UserAgent);
        });
        services.AddHttpClient<LbaOfficialSerieABasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<LbaOfficialOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
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
        services.AddHttpClient<WikipediaEuroleagueHistoricalDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://en.wikipedia.org");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<WikipediaUlebCupHistoricalDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://en.wikipedia.org");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<FlashscoreEuroleagueHistoricalDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://www.flashscore.com");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<FlashscoreCzechNblHistoricalDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://www.flashscore.com");
            client.Timeout = TimeSpan.FromSeconds(90);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<EuroleagueRHistoricalDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<ItalianCupWikipediaBasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ItalianCupWikipediaOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(providerOptions.UserAgent);
        });
        services.AddHttpClient<FrenchHistoricalBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<GreekOfficialBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<GermanBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<TurkishBasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<TurkishBasketballOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(providerOptions.UserAgent);
        });
        services.AddHttpClient<SerbianHistoricalBasketballDataProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<SerbianHistoricalOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(providerOptions.UserAgent);
        });
        services.AddHttpClient<GlobalSportsArchiveBasketballDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://globalsportsarchive.com");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<BasketballDatabaseBalticLeagueDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<BblWaybackChallengeCupDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<EurobasketLithuaniaBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<WikipediaLithuanianCupBasketballDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        });
        services.AddHttpClient<LiveScoreDailyResultsProvider>((serviceProvider, client) =>
        {
            var providerOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<LiveScoreOptions>>()
                .Value;
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(providerOptions.UserAgent);
        });

        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ApiSportsBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AcbArchiveBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<DbasketAcbBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AcbOfficialTournamentBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AcbOfficialLigaNacionalBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AbaLeagueOfficialBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LbaOfficialSerieABasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<BasketballReferenceBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FibaBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<WikipediaEuroBasketQualificationDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<WikipediaEuroleagueHistoricalDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<WikipediaUlebCupHistoricalDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FlashscoreEuroleagueHistoricalDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FlashscoreCzechNblHistoricalDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EuroleagueRHistoricalDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ItalianCupWikipediaBasketballDataProvider>());
        services.AddScoped<GermanCupWikipediaBasketballDataProvider>();
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GermanCupWikipediaBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FrenchHistoricalBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GreekOfficialBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GermanBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TurkishBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<SerbianHistoricalBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GlobalSportsArchiveBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<BasketballDatabaseBalticLeagueDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<BblWaybackChallengeCupDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EurobasketLithuaniaBasketballDataProvider>());
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<WikipediaLithuanianCupBasketballDataProvider>());
        services.AddSingleton<FiveThirtyEightBasketballDataProvider>();
        services.AddScoped<IBasketballDataProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<FiveThirtyEightBasketballDataProvider>());
        services.AddScoped<IBackfillJobProcessor, BackfillJobProcessor>();
        services.AddScoped<INbaCurrentSeasonRefreshService, NbaCurrentSeasonRefreshService>();
        services.AddScoped<ICurrentResultsProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LiveScoreDailyResultsProvider>());
        services.AddScoped<ICurrentResultsIngestionService, CurrentResultsIngestionService>();
        services.AddScoped<ICurrentResultsSchedulerService, CurrentResultsSchedulerService>();
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

