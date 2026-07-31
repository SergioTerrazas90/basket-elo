using BasketElo.Infrastructure.Backfill;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length > 0 && args[0].Equals("fiba-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunFibaDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("fiba-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunFibaIngestAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbIngestAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-tournament-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbTournamentDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-tournament-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbTournamentIngestAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-liga-nacional-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbLigaNacionalDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("acb-liga-nacional-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunAcbLigaNacionalIngestAsync(args[1..]);
    }

    AuditCommandOptions command;
    try
    {
        command = AuditCommandOptions.Parse(args);
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        PrintUsage();
        return 1;
    }

    if (command.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Services.Configure<BasketballReferenceOptions>(
        builder.Configuration.GetSection(BasketballReferenceOptions.SectionName));
    builder.Services.AddSingleton<IBasketballReferenceRateLimiter, BasketballReferenceRateLimiter>();
    builder.Services.AddHttpClient<BasketballReferenceBasketballDataProvider>((serviceProvider, client) =>
    {
        var providerOptions = serviceProvider.GetRequiredService<IOptions<BasketballReferenceOptions>>().Value;
        client.BaseAddress = new Uri(providerOptions.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddTransient<INbaHistoricalAuditService, NbaHistoricalAuditService>();

    using var host = builder.Build();
    var resumeReport = command.Resume
        ? await NbaAuditReportWriter.ReadResumeReportAsync(command.OutputPath, CancellationToken.None)
        : null;
    var audit = host.Services.GetRequiredService<INbaHistoricalAuditService>();
    var report = await audit.RunAsync(
        new NbaAuditRequest(command.StartSeason, command.EndSeason, command.MaxRequests),
        resumeReport,
        CancellationToken.None);
    await NbaAuditReportWriter.WriteAsync(report, command.OutputPath, CancellationToken.None);

    var failed = report.Seasons.Count(result => result.Status == "failed");
    Console.WriteLine(
        $"NBA audit wrote {report.Seasons.Count} seasons to '{Path.GetFullPath(command.OutputPath)}': " +
        $"{failed} failed, {report.RequestCount} requests, {report.ElapsedMilliseconds} ms, 0 database writes.");
    return failed == 0 ? 0 : 2;
}

static async Task<int> RunFibaDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var country = Required(values, "--country");
    var leagueName = Required(values, "--league");
    var season = Required(values, "--season");
    var maxRequests = int.TryParse(values.GetValueOrDefault("--max-requests") ?? "2", out var parsed) ? parsed : 2;

    using var client = new HttpClient { BaseAddress = new Uri("https://www.fiba.basketball") };
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
    var provider = new FibaBasketballDataProvider(client);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync(country, leagueName, context, CancellationToken.None);
    if (league is null)
    {
        Console.Error.WriteLine($"FIBA catalog mapping not found for {country}: {leagueName}.");
        return 1;
    }

    var result = await provider.GetGamesAsync(league, season, context, CancellationToken.None);
    var finished = result.Games.Count(game => game.HomeScore.HasValue && game.AwayScore.HasValue);
    var phases = result.Games
        .GroupBy(game => game.CompetitionPhase is null ? "(none)" : $"{game.CompetitionPhase} / {game.CompetitionRound}")
        .OrderByDescending(group => group.Count())
        .Select(group => $"{group.Key}={group.Count()}");
    Console.WriteLine($"FIBA dry-run: {country}: {leagueName} {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; finished: {finished}");
    Console.WriteLine($"Phases: {string.Join(", ", phases)}");
    Console.WriteLine($"Warnings: {string.Join(" | ", result.Warnings)}");
    foreach (var game in result.Games.Take(3))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.SourceHomeTeamId} {game.HomeScore}-{game.AwayScore} {game.SourceAwayTeamId} [{game.CompetitionPhase} / {game.CompetitionRound}] {game.Provenance?.SourceUrl}");
    }

    return 0;
}

static async Task<int> RunFibaIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var maxJobs = ParseNonNegative(values, "--max-jobs", 0);
    var maxRequests = ParseNonNegative(values, "--max-requests", 2);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    if (values.TryGetValue("--connection-string", out var connectionString))
    {
        builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    }
    builder.Services.AddInfrastructure(builder.Configuration);

    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();

    var unrelatedPendingJobs = await dbContext.BackfillJobs
        .CountAsync(job => job.Status == BackfillJobStatus.Pending && job.Provider != FibaBasketballDataProvider.Source);
    if (unrelatedPendingJobs > 0)
    {
        Console.Error.WriteLine($"Refusing to run FIBA ingest while {unrelatedPendingJobs} non-FIBA backfill jobs are pending.");
        return 2;
    }

    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var fibaSeasons = catalog.GetLeagues()
        .Where(league => string.Equals(league.Provider, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase))
        .SelectMany(league => catalog.GetSeasonsForLeague(league).Select(season => new
        {
            league.Country,
            league.LeagueName,
            season,
            league.DisplayName
        }))
        .ToList();

    var completedKeys = await dbContext.BackfillJobs
        .Where(job => job.Provider == FibaBasketballDataProvider.Source &&
            (job.Status == BackfillJobStatus.Completed || job.Status == BackfillJobStatus.CompletedWithWarnings))
        .Select(job => new { job.Country, job.LeagueName, job.Season })
        .ToListAsync();
    var completedSet = completedKeys
        .Select(key => $"{key.Country}\u001f{key.LeagueName}\u001f{key.Season}")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var activeSet = (await dbContext.BackfillJobs
            .Where(job => job.Provider == FibaBasketballDataProvider.Source &&
                (job.Status == BackfillJobStatus.Pending || job.Status == BackfillJobStatus.Running))
            .Select(job => new { job.Country, job.LeagueName, job.Season })
            .ToListAsync())
        .Select(key => $"{key.Country}\u001f{key.LeagueName}\u001f{key.Season}")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var jobsToQueue = fibaSeasons
        .Where(item =>
        {
            var key = $"{item.Country}\u001f{item.LeagueName}\u001f{item.season}";
            return !completedSet.Contains(key) && !activeSet.Contains(key);
        })
        .OrderBy(item => item.season)
        .ThenBy(item => item.Country)
        .ThenBy(item => item.LeagueName)
        .Take(maxJobs > 0 ? maxJobs : int.MaxValue)
        .Select(item => new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = FibaBasketballDataProvider.Source,
            Country = item.Country,
            LeagueName = item.LeagueName,
            Season = item.season,
            DryRun = false,
            MaxRequests = maxRequests,
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        })
        .ToList();

    dbContext.BackfillJobs.AddRange(jobsToQueue);
    await dbContext.SaveChangesAsync();
    Console.WriteLine($"Queued {jobsToQueue.Count} FIBA jobs; skipped {completedSet.Count} completed and {activeSet.Count} active keys.");

    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        if (processed % 10 == 0)
        {
            Console.WriteLine($"Processed {processed} FIBA jobs...");
        }
    }

    var summary = await dbContext.BackfillJobs
        .Where(job => job.Provider == FibaBasketballDataProvider.Source)
        .GroupBy(job => job.Status)
        .Select(group => new { Status = group.Key, Count = group.Count() })
        .OrderBy(item => item.Status)
        .ToListAsync();
    Console.WriteLine($"FIBA ingest processed {processed} jobs. Status: {string.Join(", ", summary.Select(item => $"{item.Status}={item.Count}"))}");
    return 0;
}

static async Task<int> RunAcbDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 2);
    var interval = ParseNonNegative(values, "--interval-ms", 0);

    var builder = Host.CreateApplicationBuilder();
    builder.Configuration["Dbasket:NetworkAccessEnabled"] = "true";
    builder.Configuration["Dbasket:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>()
        .Single(x => x.SourceKey == DbasketAcbBasketballDataProvider.Source);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("Spain", "ACB", context, CancellationToken.None);
    var result = await provider.GetGamesAsync(league!, season, context, CancellationToken.None);
    Console.WriteLine($"ACB dry-run: Spain: ACB {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var game in result.Games.Take(5))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} {game.Provenance?.SourceUrl}");
    }
    foreach (var warning in result.Warnings.Take(10))
    {
        Console.WriteLine($"WARNING: {warning}");
    }
    return 0;
}

static async Task<int> RunAcbIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var startSeason = Required(values, "--start");
    var endSeason = values.GetValueOrDefault("--end") ?? startSeason;
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 250);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["Dbasket:NetworkAccessEnabled"] = "true";
    builder.Configuration["Dbasket:MinRequestIntervalMilliseconds"] = interval.ToString();
    if (values.TryGetValue("--connection-string", out var connectionString))
    {
        builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    }
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();
    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var league = catalog.GetLeagues().Single(x =>
        x.Provider == DbasketAcbBasketballDataProvider.Source &&
        x.Country == "Spain" &&
        x.LeagueName == "ACB");
    var seasons = catalog.GetSeasonsForLeague(league)
        .Where(x => SeasonLabelNormalizer.ParseStartYear(x) >= SeasonLabelNormalizer.ParseStartYear(startSeason) &&
                    SeasonLabelNormalizer.ParseStartYear(x) <= SeasonLabelNormalizer.ParseStartYear(endSeason))
        .ToList();
    var completed = await dbContext.BackfillJobs
        .Where(x => x.Provider == DbasketAcbBasketballDataProvider.Source &&
                    x.Country == "Spain" && x.LeagueName == "ACB" &&
                    (x.Status == BackfillJobStatus.Completed || x.Status == BackfillJobStatus.CompletedWithWarnings))
        .Select(x => x.Season)
        .ToListAsync();
    var jobs = seasons.Where(x => !completed.Contains(x, StringComparer.OrdinalIgnoreCase))
        .Select(x => new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = DbasketAcbBasketballDataProvider.Source,
            Country = "Spain",
            LeagueName = "ACB",
            Season = x,
            DryRun = false,
            MaxRequests = maxRequests,
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToList();
    dbContext.BackfillJobs.AddRange(jobs);
    await dbContext.SaveChangesAsync();
    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        Console.WriteLine($"Processed ACB job {processed}/{jobs.Count}.");
    }
    Console.WriteLine($"ACB ingest complete: processed {processed} jobs.");
    return 0;
}

static async Task<int> RunAcbTournamentDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var competition = Required(values, "--competition");
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>()
        .Single(x => x.SourceKey == AcbOfficialTournamentBasketballDataProvider.Source);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("Spain", competition, context, CancellationToken.None);
    if (league is null)
    {
        Console.Error.WriteLine($"Unknown ACB tournament: {competition}.");
        return 1;
    }

    var result = await provider.GetGamesAsync(league, season, context, CancellationToken.None);
    Console.WriteLine($"ACB tournament dry-run: Spain: {competition} {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var game in result.Games.Take(10))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} {game.Provenance?.SourceUrl}");
    }
    foreach (var warning in result.Warnings.Take(10))
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    return 0;
}

static async Task<int> RunAcbTournamentIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var competition = Required(values, "--competition");
    var startSeason = Required(values, "--start");
    var endSeason = values.GetValueOrDefault("--end") ?? startSeason;
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    if (values.TryGetValue("--connection-string", out var connectionString))
    {
        builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    }
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();
    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var league = catalog.GetLeagues().Single(x =>
        x.Provider == AcbOfficialTournamentBasketballDataProvider.Source &&
        x.Country == "Spain" && x.LeagueName.Equals(competition, StringComparison.OrdinalIgnoreCase));
    var seasons = catalog.GetSeasonsForLeague(league)
        .Where(x => SeasonLabelNormalizer.ParseStartYear(x) >= SeasonLabelNormalizer.ParseStartYear(startSeason) &&
                    SeasonLabelNormalizer.ParseStartYear(x) <= SeasonLabelNormalizer.ParseStartYear(endSeason))
        .ToList();
    var completed = await dbContext.BackfillJobs
        .Where(x => x.Provider == AcbOfficialTournamentBasketballDataProvider.Source &&
                    x.Country == "Spain" && x.LeagueName == competition &&
                    (x.Status == BackfillJobStatus.Completed || x.Status == BackfillJobStatus.CompletedWithWarnings))
        .Select(x => x.Season)
        .ToListAsync();
    var jobs = seasons.Where(x => !completed.Contains(x, StringComparer.OrdinalIgnoreCase))
        .Select(x => new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = AcbOfficialTournamentBasketballDataProvider.Source,
            Country = "Spain",
            LeagueName = competition,
            Season = x,
            DryRun = false,
            MaxRequests = maxRequests,
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToList();
    dbContext.BackfillJobs.AddRange(jobs);
    await dbContext.SaveChangesAsync();
    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        Console.WriteLine($"Processed {competition} job {processed}/{jobs.Count}.");
    }
    Console.WriteLine($"ACB tournament ingest complete: {competition}; processed {processed} jobs.");
    return 0;
}

static async Task<int> RunAcbLigaNacionalDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>().Single(x => x.SourceKey == AcbOfficialLigaNacionalBasketballDataProvider.Source);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("Spain", "Liga Nacional", context, CancellationToken.None);
    var result = await provider.GetGamesAsync(league!, season, context, CancellationToken.None);
    Console.WriteLine($"ACB Liga Nacional dry-run: Spain: Liga Nacional {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var game in result.Games.Take(10))
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} {game.Provenance?.SourceUrl}");
    foreach (var warning in result.Warnings.Take(10)) Console.WriteLine($"WARNING: {warning}");
    return 0;
}

static async Task<int> RunAcbLigaNacionalIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    if (values.TryGetValue("--connection-string", out var connectionString)) builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();
    var completed = await dbContext.BackfillJobs.Where(x => x.Provider == AcbOfficialLigaNacionalBasketballDataProvider.Source && x.Country == "Spain" && x.LeagueName == "Liga Nacional" && (x.Status == BackfillJobStatus.Completed || x.Status == BackfillJobStatus.CompletedWithWarnings)).Select(x => x.Season).ToListAsync();
    if (!completed.Contains(season, StringComparer.OrdinalIgnoreCase))
    {
        dbContext.BackfillJobs.Add(new BackfillJob { Id = Guid.NewGuid(), Provider = AcbOfficialLigaNacionalBasketballDataProvider.Source, Country = "Spain", LeagueName = "Liga Nacional", Season = season, DryRun = false, MaxRequests = maxRequests, Status = BackfillJobStatus.Pending, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
    }
    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None)) processed++;
    Console.WriteLine($"ACB Liga Nacional ingest complete: {season}; processed {processed} jobs.");
    return 0;
}

static int ParseNonNegative(IReadOnlyDictionary<string, string> values, string name, int defaultValue)
{
    var value = values.GetValueOrDefault(name);
    return string.IsNullOrWhiteSpace(value)
        ? defaultValue
        : int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{name} must be a non-negative integer.");
}

static Dictionary<string, string> ParseKeyValueArgs(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index++)
    {
        if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
        {
            throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
        }

        values[args[index]] = args[++index];
    }

    return values;
}

static string Required(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"{name} is required.");

static void PrintUsage()
{
    Console.WriteLine("""
        BasketElo NBA historical audit (read-only)

        dotnet run --project src/BasketElo.Tools -- nba-audit \
          --start 1946-1947 --end 1959-1960 \
          --output artifacts/nba-audit-1946-1960.json \
          [--max-requests 0] [--resume]

        Output must be .json or .csv. Resume requires JSON. Provider archive and
        authorized network settings use BasketballReference__* configuration.

        FIBA official archive dry-run (read-only)

        dotnet run --project src/BasketElo.Tools -- fiba-dry-run \
          --country Europe --league "FIBA EuroBasket Qualifiers" --season 2022-2023 \
          [--max-requests 2]

        FIBA database ingest (writes local Postgres)

        dotnet run --project src/BasketElo.Tools -- fiba-ingest \
          [--max-jobs 0] [--max-requests 2]

        ACB historical archive dry-run

        dotnet run --project src/BasketElo.Tools -- acb-dry-run \
          --season 2007-2008 [--max-requests 2] [--interval-ms 0]

        ACB historical archive ingest (writes local Postgres)

        dotnet run --project src/BasketElo.Tools -- acb-ingest \
          --start 2007-2008 --end 2007-2008 \
          [--max-requests 0] [--interval-ms 250]

        ACB official tournament dry-run

        dotnet run --project src/BasketElo.Tools -- acb-tournament-dry-run \
          --competition "Spanish Cup" --season 2007-2008 [--max-requests 0]

        ACB official tournament ingest (writes local Postgres)

        dotnet run --project src/BasketElo.Tools -- acb-tournament-ingest \
          --competition "Spanish Cup" --start 1983-1984 --end 2007-2008 \
          [--max-requests 0]
        """);
}

file sealed record AuditCommandOptions(
    string StartSeason,
    string EndSeason,
    string OutputPath,
    int MaxRequests,
    bool Resume,
    bool ShowHelp)
{
    public static AuditCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "--help" or "-h"))
        {
            return new("1946-1947", "1946-1947", string.Empty, 0, false, true);
        }

        var offset = args[0].Equals("nba-audit", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resume = false;
        for (var index = offset; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--resume", StringComparison.OrdinalIgnoreCase))
            {
                resume = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Unknown or incomplete argument '{argument}'.");
            }

            values[argument] = args[++index];
        }

        var start = Required(values, "--start");
        var end = Required(values, "--end");
        _ = NbaHistoricalAuditService.GetSeasonRange(start, end);
        var output = values.GetValueOrDefault("--output") ??
            $"artifacts/nba-audit-{start}-{end}.json";
        var maxRequestsText = values.GetValueOrDefault("--max-requests") ?? "0";
        if (!int.TryParse(maxRequestsText, out var maxRequests) || maxRequests < 0)
        {
            throw new ArgumentException("--max-requests must be a non-negative integer.");
        }

        var extension = Path.GetExtension(output);
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--output must end in .json or .csv.");
        }

        if (resume && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--resume requires JSON output.");
        }

        return new(start, end, output, maxRequests, resume, false);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");
}
