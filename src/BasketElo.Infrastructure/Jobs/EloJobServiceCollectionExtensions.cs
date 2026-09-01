using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BasketElo.Infrastructure.Jobs;

public static class EloJobServiceCollectionExtensions
{
    public static IServiceCollection AddEloJobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

        services.Configure<EloJobOptions>(configuration.GetSection(EloJobOptions.SectionName));
        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                storage => storage.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true
                }));

        return services;
    }
}
