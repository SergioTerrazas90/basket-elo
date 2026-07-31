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

    if (args.Length > 0 && args[0].Equals("italy-serie-a-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunItalySerieADryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("italy-serie-a-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunItalySerieAIngestAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("italy-cup-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunItalyCupDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("italy-cup-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunItalyCupIngestAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("france-dry-run", StringComparison.OrdinalIgnoreCase))
    {
        return await RunFranceDryRunAsync(args[1..]);
    }

    if (args.Length > 0 && args[0].Equals("france-ingest", StringComparison.OrdinalIgnoreCase))
    {
        return await RunFranceIngestAsync(args[1..]);
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

static async Task<int> RunItalySerieADryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 100);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["LbaOfficial:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>()
        .Single(x => x.SourceKey == LbaOfficialSerieABasketballDataProvider.Source);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("Italy", "Serie A", context, CancellationToken.None);
    var result = await provider.GetGamesAsync(league!, season, context, CancellationToken.None);

    Console.WriteLine($"Official LBA dry-run: Italy: Serie A {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var phase in result.Games.GroupBy(game => game.CompetitionPhase).OrderBy(group => group.Key))
    {
        Console.WriteLine($"{phase.Key ?? "Unknown phase"}: {phase.Count()} games");
    }
    foreach (var game in result.Games.Take(10))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} [{game.CompetitionPhase}] {game.Provenance?.SourceUrl}");
    }
    foreach (var warning in result.Warnings.Take(20))
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    return result.Games.Count == 0 ? 2 : 0;
}

static async Task<int> RunItalySerieAIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var startSeason = Required(values, "--start");
    var endSeason = values.GetValueOrDefault("--end") ?? startSeason;
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 100);
    var startYear = SeasonLabelNormalizer.ParseStartYear(startSeason);
    var endYear = SeasonLabelNormalizer.ParseStartYear(endSeason);
    var lowerYear = Math.Min(startYear, endYear);
    var upperYear = Math.Max(startYear, endYear);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["LbaOfficial:MinRequestIntervalMilliseconds"] = interval.ToString();
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
        .CountAsync(job => job.Status == BackfillJobStatus.Pending &&
            job.Provider != LbaOfficialSerieABasketballDataProvider.Source);
    if (unrelatedPendingJobs > 0)
    {
        Console.Error.WriteLine(
            $"Refusing to start: {unrelatedPendingJobs} unrelated backfill job(s) are pending and would be processed first.");
        return 2;
    }

    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var league = catalog.GetLeagues().Single(candidate =>
        candidate.Provider == LbaOfficialSerieABasketballDataProvider.Source &&
        candidate.Country == "Italy" && candidate.LeagueName == "Serie A");
    var seasons = catalog.GetSeasonsForLeague(league)
        .Where(season =>
        {
            var year = SeasonLabelNormalizer.ParseStartYear(season);
            return year >= lowerYear && year <= upperYear;
        })
        .OrderByDescending(SeasonLabelNormalizer.ParseStartYear)
        .ToList();
    if (seasons.Count == 0)
    {
        Console.Error.WriteLine(
            $"No official LBA seasons fall inside {startSeason} through {endSeason}; supported range is 1974-1975 through 2007-2008.");
        return 1;
    }

    var completed = await dbContext.BackfillJobs
        .Where(job => job.Provider == LbaOfficialSerieABasketballDataProvider.Source &&
            job.Country == "Italy" && job.LeagueName == "Serie A" &&
            (job.Status == BackfillJobStatus.Completed || job.Status == BackfillJobStatus.CompletedWithWarnings))
        .Select(job => job.Season)
        .ToListAsync();
    var active = await dbContext.BackfillJobs
        .Where(job => job.Provider == LbaOfficialSerieABasketballDataProvider.Source &&
            job.Country == "Italy" && job.LeagueName == "Serie A" &&
            (job.Status == BackfillJobStatus.Pending || job.Status == BackfillJobStatus.Running))
        .Select(job => job.Season)
        .ToListAsync();
    var jobs = seasons
        .Where(season => !completed.Contains(season, StringComparer.OrdinalIgnoreCase) &&
            !active.Contains(season, StringComparer.OrdinalIgnoreCase))
        .Select((season, index) => new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = LbaOfficialSerieABasketballDataProvider.Source,
            Country = "Italy",
            LeagueName = "Serie A",
            Season = season,
            DryRun = false,
            MaxRequests = maxRequests,
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddTicks(index),
            UpdatedAtUtc = DateTime.UtcNow.AddTicks(index)
        })
        .ToList();
    dbContext.BackfillJobs.AddRange(jobs);
    await dbContext.SaveChangesAsync();
    Console.WriteLine($"Queued {jobs.Count} official LBA season(s), newest first; skipped {completed.Count} completed and {active.Count} active season(s).");

    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        Console.WriteLine($"Processed official LBA job {processed}/{jobs.Count}.");
    }

    var summary = await dbContext.BackfillJobs
        .Where(job => job.Provider == LbaOfficialSerieABasketballDataProvider.Source && seasons.Contains(job.Season))
        .GroupBy(job => job.Status)
        .Select(group => new { Status = group.Key, Count = group.Count() })
        .OrderBy(item => item.Status)
        .ToListAsync();
    Console.WriteLine($"Official LBA ingest processed {processed} jobs. Status: {string.Join(", ", summary.Select(item => $"{item.Status}={item.Count}"))}");
    return summary.Any(item => item.Status == BackfillJobStatus.Failed) ? 2 : 0;
}

static async Task<int> RunItalyCupDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 1000);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["ItalianCupWikipedia:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Configuration["LbaOfficial:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var catalog = host.Services.GetRequiredService<IBackfillCatalog>();
    var configuredLeague = catalog.GetLeagues().SingleOrDefault(candidate =>
        candidate.Country == "Italy" && candidate.LeagueName == "Italian Cup" &&
        candidate.Provider != ApiSportsBasketballDataProvider.Source &&
        catalog.GetSeasonsForLeague(candidate).Contains(season, StringComparer.OrdinalIgnoreCase));
    if (configuredLeague is null)
    {
        Console.Error.WriteLine($"No historical Italian Cup source is configured for {season}.");
        return 1;
    }

    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>()
        .Single(candidate => candidate.SourceKey == configuredLeague.Provider);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("Italy", "Italian Cup", context, CancellationToken.None);
    var result = await provider.GetGamesAsync(league!, season, context, CancellationToken.None);

    Console.WriteLine($"Italian Cup dry-run: {season}; source: {configuredLeague.Provider}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var phase in result.Games.GroupBy(game => game.CompetitionRound).OrderBy(group => group.Key))
    {
        Console.WriteLine($"{phase.Key ?? "Unknown round"}: {phase.Count()} games");
    }
    foreach (var game in result.Games.Take(12))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} [{game.CompetitionRound}] {game.Provenance?.SourceUrl}");
    }
    foreach (var warning in result.Warnings.Take(20))
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    return result.Games.Count == 0 ? 2 : 0;
}

static async Task<int> RunItalyCupIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var startSeason = Required(values, "--start");
    var endSeason = values.GetValueOrDefault("--end") ?? startSeason;
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 1000);
    var lowerYear = Math.Min(
        SeasonLabelNormalizer.ParseStartYear(startSeason),
        SeasonLabelNormalizer.ParseStartYear(endSeason));
    var upperYear = Math.Max(
        SeasonLabelNormalizer.ParseStartYear(startSeason),
        SeasonLabelNormalizer.ParseStartYear(endSeason));

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["ItalianCupWikipedia:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Configuration["LbaOfficial:MinRequestIntervalMilliseconds"] = interval.ToString();
    if (values.TryGetValue("--connection-string", out var connectionString))
    {
        builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    }
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();

    var historicalProviders = new[]
    {
        ItalianCupWikipediaBasketballDataProvider.Source,
        LbaOfficialSerieABasketballDataProvider.Source
    };
    var unrelatedPendingJobs = await dbContext.BackfillJobs.CountAsync(job =>
        job.Status == BackfillJobStatus.Pending &&
        !(historicalProviders.Contains(job.Provider) && job.Country == "Italy" && job.LeagueName == "Italian Cup"));
    if (unrelatedPendingJobs > 0)
    {
        Console.Error.WriteLine(
            $"Refusing to start: {unrelatedPendingJobs} unrelated backfill job(s) are pending and would be processed first.");
        return 2;
    }

    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var seasons = catalog.GetLeagues()
        .Where(league => historicalProviders.Contains(league.Provider) &&
            league.Country == "Italy" && league.LeagueName == "Italian Cup")
        .SelectMany(league => catalog.GetSeasonsForLeague(league)
            .Select(season => new { league.Provider, Season = season }))
        .Where(item =>
        {
            var year = SeasonLabelNormalizer.ParseStartYear(item.Season);
            return year >= lowerYear && year <= upperYear;
        })
        .OrderByDescending(item => SeasonLabelNormalizer.ParseStartYear(item.Season))
        .ToList();
    if (seasons.Count == 0)
    {
        Console.Error.WriteLine(
            $"No played Italian Cup editions fall inside {startSeason} through {endSeason}.");
        return 1;
    }

    var existing = await dbContext.BackfillJobs
        .Where(job => historicalProviders.Contains(job.Provider) &&
            job.Country == "Italy" && job.LeagueName == "Italian Cup" &&
            (job.Status == BackfillJobStatus.Completed ||
             job.Status == BackfillJobStatus.CompletedWithWarnings ||
             job.Status == BackfillJobStatus.Pending ||
             job.Status == BackfillJobStatus.Running))
        .Select(job => new { job.Provider, job.Season })
        .ToListAsync();
    var now = DateTime.UtcNow;
    var jobs = seasons
        .Where(item => !existing.Any(job =>
            job.Provider == item.Provider && job.Season.Equals(item.Season, StringComparison.OrdinalIgnoreCase)))
        .Select((item, index) => new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = item.Provider,
            Country = "Italy",
            LeagueName = "Italian Cup",
            Season = item.Season,
            DryRun = false,
            MaxRequests = maxRequests,
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = now.AddTicks(index),
            UpdatedAtUtc = now.AddTicks(index)
        })
        .ToList();
    dbContext.BackfillJobs.AddRange(jobs);
    await dbContext.SaveChangesAsync();
    Console.WriteLine($"Queued {jobs.Count} historical Italian Cup edition(s), newest first; skipped {existing.Count} completed or active edition(s).");

    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        Console.WriteLine($"Processed Italian Cup job {processed}/{jobs.Count}.");
    }

    var selectedSeasonLabels = seasons.Select(item => item.Season).Distinct().ToList();
    var summary = await dbContext.BackfillJobs
        .Where(job => historicalProviders.Contains(job.Provider) &&
            job.Country == "Italy" && job.LeagueName == "Italian Cup" &&
            selectedSeasonLabels.Contains(job.Season))
        .GroupBy(job => job.Status)
        .Select(group => new { Status = group.Key, Count = group.Count() })
        .OrderBy(item => item.Status)
        .ToListAsync();
    Console.WriteLine($"Italian Cup ingest processed {processed} jobs. Status: {string.Join(", ", summary.Select(item => $"{item.Status}={item.Count}"))}");
    return summary.Any(item => item.Status == BackfillJobStatus.Failed) ? 2 : 0;
}

static async Task<int> RunFranceDryRunAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var competition = Required(values, "--competition");
    var season = Required(values, "--season");
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 100);

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["FrenchHistorical:MinRequestIntervalMilliseconds"] = interval.ToString();
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    var catalog = host.Services.GetRequiredService<IBackfillCatalog>();
    var configuredLeague = catalog.GetLeagues().SingleOrDefault(candidate =>
        candidate.Provider == FrenchHistoricalBasketballDataProvider.Source &&
        candidate.Country == "France" && candidate.LeagueName.Equals(competition, StringComparison.OrdinalIgnoreCase) &&
        catalog.GetSeasonsForLeague(candidate).Contains(season, StringComparer.OrdinalIgnoreCase));
    if (configuredLeague is null)
    {
        Console.Error.WriteLine($"No French historical source is configured for {competition} {season}.");
        return 1;
    }

    var provider = host.Services.GetRequiredService<IEnumerable<IBasketballDataProvider>>()
        .Single(candidate => candidate.SourceKey == FrenchHistoricalBasketballDataProvider.Source);
    var context = new BackfillExecutionContext(maxRequests, 0);
    var league = await provider.ResolveLeagueAsync("France", configuredLeague.LeagueName, context, CancellationToken.None);
    var result = await provider.GetGamesAsync(league!, season, context, CancellationToken.None);

    Console.WriteLine($"French historical dry-run: {competition} {season}");
    Console.WriteLine($"Requests: {context.RequestsUsed}/{context.MaxRequests}; games: {result.Games.Count}; warnings: {result.Warnings.Count}");
    foreach (var phase in result.Games.GroupBy(game => $"{game.CompetitionPhase} / {game.CompetitionRound}").OrderBy(group => group.Key))
    {
        Console.WriteLine($"{phase.Key}: {phase.Count()} games");
    }
    foreach (var game in result.Games.Take(12))
    {
        Console.WriteLine($"{game.GameDateTimeUtc:yyyy-MM-dd} {game.HomeTeamName} {game.HomeScore}-{game.AwayScore} {game.AwayTeamName} [{game.CompetitionRound}]");
    }
    foreach (var warning in result.Warnings.Take(20))
    {
        Console.WriteLine($"WARNING: {warning}");
    }
    return result.Games.Count == 0 ? 2 : 0;
}

static async Task<int> RunFranceIngestAsync(string[] args)
{
    var values = ParseKeyValueArgs(args);
    var competition = Required(values, "--competition");
    var startSeason = Required(values, "--start");
    var endSeason = values.GetValueOrDefault("--end") ?? startSeason;
    var maxRequests = ParseNonNegative(values, "--max-requests", 0);
    var interval = ParseNonNegative(values, "--interval-ms", 100);
    var lowerYear = Math.Min(SeasonLabelNormalizer.ParseStartYear(startSeason), SeasonLabelNormalizer.ParseStartYear(endSeason));
    var upperYear = Math.Max(SeasonLabelNormalizer.ParseStartYear(startSeason), SeasonLabelNormalizer.ParseStartYear(endSeason));

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Configuration["FrenchHistorical:MinRequestIntervalMilliseconds"] = interval.ToString();
    if (values.TryGetValue("--connection-string", out var connectionString))
    {
        builder.Configuration["ConnectionStrings:Postgres"] = connectionString;
    }
    builder.Services.AddInfrastructure(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BasketEloDbContext>();
    await dbContext.Database.MigrateAsync();

    var unrelatedPendingJobs = await dbContext.BackfillJobs.CountAsync(job =>
        job.Status == BackfillJobStatus.Pending &&
        !(job.Provider == FrenchHistoricalBasketballDataProvider.Source &&
          job.Country == "France" && job.LeagueName == competition));
    if (unrelatedPendingJobs > 0)
    {
        Console.Error.WriteLine($"Refusing to start: {unrelatedPendingJobs} unrelated backfill job(s) are pending.");
        return 2;
    }

    var catalog = scope.ServiceProvider.GetRequiredService<IBackfillCatalog>();
    var configuredLeague = catalog.GetLeagues().SingleOrDefault(candidate =>
        candidate.Provider == FrenchHistoricalBasketballDataProvider.Source &&
        candidate.Country == "France" && candidate.LeagueName.Equals(competition, StringComparison.OrdinalIgnoreCase));
    if (configuredLeague is null)
    {
        Console.Error.WriteLine($"No historical French competition named '{competition}' is configured.");
        return 1;
    }
    var seasons = catalog.GetSeasonsForLeague(configuredLeague)
        .Where(season =>
        {
            var year = SeasonLabelNormalizer.ParseStartYear(season);
            return year >= lowerYear && year <= upperYear;
        })
        .OrderByDescending(SeasonLabelNormalizer.ParseStartYear)
        .ToList();
    if (seasons.Count == 0)
    {
        Console.Error.WriteLine($"No {competition} seasons fall inside {startSeason} through {endSeason}.");
        return 1;
    }

    var existing = await dbContext.BackfillJobs
        .Where(job => job.Provider == FrenchHistoricalBasketballDataProvider.Source &&
            job.Country == "France" && job.LeagueName == configuredLeague.LeagueName &&
            (job.Status == BackfillJobStatus.Completed || job.Status == BackfillJobStatus.CompletedWithWarnings ||
             job.Status == BackfillJobStatus.Pending || job.Status == BackfillJobStatus.Running))
        .Select(job => job.Season)
        .ToListAsync();
    var now = DateTime.UtcNow;
    var jobs = seasons
        .Where(season => !existing.Contains(season, StringComparer.OrdinalIgnoreCase))
        .Select((season, index) => new BackfillJob
        {
            Id = Guid.NewGuid(), Provider = FrenchHistoricalBasketballDataProvider.Source,
            Country = "France", LeagueName = configuredLeague.LeagueName, Season = season,
            DryRun = false, MaxRequests = maxRequests, Status = BackfillJobStatus.Pending,
            CreatedAtUtc = now.AddTicks(index), UpdatedAtUtc = now.AddTicks(index)
        })
        .ToList();
    dbContext.BackfillJobs.AddRange(jobs);
    await dbContext.SaveChangesAsync();
    Console.WriteLine($"Queued {jobs.Count} {competition} season(s), newest first; skipped {existing.Count} completed or active season(s).");

    var processor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
    var processed = 0;
    while (await processor.TryProcessNextPendingJobAsync(CancellationToken.None))
    {
        processed++;
        Console.WriteLine($"Processed French historical job {processed}/{jobs.Count}.");
    }

    var attempts = await dbContext.BackfillJobs
        .Where(job => job.Provider == FrenchHistoricalBasketballDataProvider.Source &&
            job.Country == "France" && job.LeagueName == configuredLeague.LeagueName && seasons.Contains(job.Season))
        .ToListAsync();
    var summary = attempts
        .GroupBy(job => job.Season, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(job => job.UpdatedAtUtc).First())
        .GroupBy(job => job.Status)
        .Select(group => new { Status = group.Key, Count = group.Count() })
        .OrderBy(item => item.Status)
        .ToList();
    Console.WriteLine($"French {competition} ingest processed {processed} jobs. Status: {string.Join(", ", summary.Select(item => $"{item.Status}={item.Count}"))}");
    return summary.Any(item => item.Status == BackfillJobStatus.Failed) ? 2 : 0;
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

        Official Lega Basket Serie A dry-run (read-only)

        dotnet run --project src/BasketElo.Tools -- italy-serie-a-dry-run \
          --season 2007-2008 [--max-requests 0] [--interval-ms 100]

        Official Lega Basket Serie A ingest (writes local Postgres, newest first)

        dotnet run --project src/BasketElo.Tools -- italy-serie-a-ingest \
          --start 2007-2008 --end 1974-1975 \
          [--max-requests 0] [--interval-ms 100] [--connection-string "..."]

        Historical Italian Cup dry-run (Wikipedia through 2007-2008; official LBA for 2008-2009)

        dotnet run --project src/BasketElo.Tools -- italy-cup-dry-run \
          --season 2007-2008 [--max-requests 0] [--interval-ms 1000]

        Historical Italian Cup ingest (writes configured Postgres, newest first)

        dotnet run --project src/BasketElo.Tools -- italy-cup-ingest \
          --start 2008-2009 --end 1967-1968 \
          [--max-requests 0] [--interval-ms 1000] [--connection-string "..."]

        Historical French league or cup dry-run

        dotnet run --project src/BasketElo.Tools -- france-dry-run \
          --competition "LNB" --season 2007-2008 [--max-requests 0] [--interval-ms 100]

        Historical French league or cup ingest (writes configured Postgres, newest first)

        dotnet run --project src/BasketElo.Tools -- france-ingest \
          --competition "LNB" --start 2007-2008 --end 1981-1982 \
          [--max-requests 0] [--interval-ms 100] [--connection-string "..."]
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
