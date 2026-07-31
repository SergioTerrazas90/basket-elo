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
    dbContext.BackfillÛOv¶‰žËkºwµçu9…µ•ôí…µ”¹AÉ½Ù•¹…¹”ü¹M½ÕÉ•UÉ±ôˆ¤ì(€€€™½É•… €¡Ù…ÈÝ…É¹¥¹œ¥¸É•ÍÕ±Ð¹]…É¹¥¹Ì¹Q…­” ÄÀ¤¤½¹Í½±”¹]É¥Ñ•1¥¹” ‰]I9%9èíÝ…É¹¥¹ôˆ¤ì(€€€É•ÑÕÉ¸€Àì)ô()ÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ¥¹ÐøIÕ¹‰1¥…9…¥½¹…±%¹•ÍÑÍå¹Œ¡ÍÑÉ¥¹mt…ÉÌ¤)ì(€€€Ù…ÈÙ…±Õ•Ì€ôA…ÉÍ•-•åY…±Õ•ÉÌ¡…ÉÌ¤ì(€€€Ù…ÈÍ•…Í½¸€ôI•ÅÕ¥É•¡Ù…±Õ•Ì°€ˆ´µÍ•…Í½¸ˆ¤ì(€€€Ù…Èµ…áI•ÅÕ•ÍÑÌ€ôA…ÉÍ•9½¹9•…Ñ¥Ù”¡Ù…±Õ•Ì°€ˆ´µµ…àµÉ•ÅÕ•ÍÑÌˆ°€À¤ì(€€€Ù…È‰Õ¥±‘•È€ô!½ÍÐ¹É•…Ñ•ÁÁ±¥…Ñ¥½¹	Õ¥±‘•È ¤ì(€€€‰Õ¥±‘•È¹1½¥¹œ¹M•Ñ5¥¹¥µÕµ1•Ù•°¡1½1•Ù•°¹]…É¹¥¹œ¤ì(€€€¥˜€¡Ù…±Õ•Ì¹QÉå•ÑY…±Õ” ˆ´µ½¹¹•Ñ¥½¸µÍÑÉ¥¹œˆ°½ÕÐÙ…È½¹¹•Ñ¥½¹MÑÉ¥¹œ¤¤‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¹l‰½¹¹•Ñ¥½¹MÑÉ¥¹ÌéA½ÍÑÉ•Ì‰t€ô½¹¹•Ñ¥½¹MÑÉ¥¹œì(€€€‰Õ¥±‘•È¹M•ÉÙ¥•Ì¹‘‘%¹™É…ÍÑÉÕÑÕÉ”¡‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¸¤ì(€€€ÕÍ¥¹œÙ…È¡½ÍÐ€ô‰Õ¥±‘•È¹	Õ¥± ¤ì(€€€ÕÍ¥¹œÙ…ÈÍ½Á”€ô¡½ÍÐ¹M•ÉÙ¥•Ì¹É•…Ñ•M½Á” ¤ì(€€€Ù…È‘‰½¹Ñ•áÐ€ôÍ½Á”¹M•ÉÙ¥•AÉ½Ù¥‘•È¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ	…Í­•Ñ±½‰½¹Ñ•áÐø ¤ì(€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹…Ñ…‰…Í”¹5¥É…Ñ•Íå¹Œ ¤ì(€€€Ù…È½µÁ±•Ñ•€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì¹]¡•É”¡à€ôøà¹AÉ½Ù¥‘•È€ôô‰=™™¥¥…±1¥…9…¥½¹…±	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”€˜˜à¹½Õ¹ÑÉä€ôô€‰MÁ…¥¸ˆ€˜˜à¹1•…Õ•9…µ”€ôô€‰1¥„9…¥½¹…°ˆ€˜˜€¡à¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹½µÁ±•Ñ•ñðà¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹½µÁ±•Ñ•‘]¥Ñ¡]…É¹¥¹Ì¤¤¹M•±•Ð¡à€ôøà¹M•…Í½¸¤¹Q½1¥ÍÑÍå¹Œ ¤ì(€€€¥˜€ …½µÁ±•Ñ•¹½¹Ñ…¥¹Ì¡Í•…Í½¸°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤¤(€€€ì(€€€€€€€‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì¹‘¡¹•Ü	…­™¥±±)½ˆì%€ôÕ¥¹9•ÝÕ¥ ¤°AÉ½Ù¥‘•È€ô‰=™™¥¥…±1¥…9…¥½¹…±	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”°½Õ¹ÑÉä€ô€‰MÁ…¥¸ˆ°1•…Õ•9…µ”€ô€‰1¥„9…¥½¹…°ˆ°M•…Í½¸€ôÍ•…Í½¸°ÉåIÕ¸€ô™…±Í”°5…áI•ÅÕ•ÍÑÌ€ôµ…áI•ÅÕ•ÍÑÌ°MÑ…ÑÕÌ€ô	…­™¥±±)½‰MÑ…ÑÕÌ¹A•¹‘¥¹œ°É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü°UÁ‘…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üô¤ì(€€€€€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ ¤ì(€€€ô(€€€Ù…ÈÁÉ½•ÍÍ½È€ôÍ½Á”¹M•ÉÙ¥•AÉ½Ù¥‘•È¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ%	…­™¥±±)½‰AÉ½•ÍÍ½Èø ¤ì(€€€Ù…ÈÁÉ½•ÍÍ•€ô€Àì(€€€Ý¡¥±”€¡…Ý…¥ÐÁÉ½•ÍÍ½È¹QÉåAÉ½•ÍÍ9•áÑA•¹‘¥¹)½‰Íå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¹9½¹”¤¤ÁÉ½•ÍÍ•¬¬ì(€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰1¥„9…¥½¹…°¥¹•ÍÐ½µÁ±•Ñ”èíÍ•…Í½¹ôìÁÉ½•ÍÍ•íÁÉ½•ÍÍ•‘ô©½‰Ì¸ˆ¤ì(€€€É•ÑÕÉ¸€Àì)ô()ÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ¥¹ÐøIÕ¹%Ñ…±åM•É¥•ÉåIÕ¹Íå¹Œ¡ÍÑÉ¥¹mt…ÉÌ¤)ì(€€€Ù…ÈÙ…±Õ•Ì€ôA…ÉÍ•-•åY…±Õ•ÉÌ¡…ÉÌ¤ì(€€€Ù…ÈÍ•…Í½¸€ôI•ÅÕ¥É•¡Ù…±Õ•Ì°€ˆ´µÍ•…Í½¸ˆ¤ì(€€€Ù…Èµ…áI•ÅÕ•ÍÑÌ€ôA…ÉÍ•9½¹9•…Ñ¥Ù”¡Ù…±Õ•Ì°€ˆ´µµ…àµÉ•ÅÕ•ÍÑÌˆ°€À¤ì(€€€Ù…È¥¹Ñ•ÉÙ…°€ôA…ÉÍ•9½¹9•…Ñ¥Ù”¡Ù…±Õ•Ì°€ˆ´µ¥¹Ñ•ÉÙ…°µµÌˆ°€ÄÀÀ¤ì((€€€Ù…È‰Õ¥±‘•È€ô!½ÍÐ¹É•…Ñ•ÁÁ±¥…Ñ¥½¹	Õ¥±‘•È ¤ì(€€€‰Õ¥±‘•È¹1½¥¹œ¹M•Ñ5¥¹¥µÕµ1•Ù•°¡1½1•Ù•°¹]…É¹¥¹œ¤ì(€€€‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¹l‰1‰…=™™¥¥…°é5¥¹I•ÅÕ•ÍÑ%¹Ñ•ÉÙ…±5¥±±¥Í•½¹‘Ì‰t€ô¥¹Ñ•ÉÙ…°¹Q½MÑÉ¥¹œ ¤ì(€€€‰Õ¥±‘•È¹M•ÉÙ¥•Ì¹‘‘%¹™É…ÍÑÉÕÑÕÉ”¡‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¸¤ì(€€€ÕÍ¥¹œÙ…È¡½ÍÐ€ô‰Õ¥±‘•È¹	Õ¥± ¤ì(€€€Ù…ÈÁÉ½Ù¥‘•È€ô¡½ÍÐ¹M•ÉÙ¥•Ì¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ%¹Õµ•É…‰±”ñ%	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•Èøø ¤(€€€€€€€€¹M¥¹±”¡à€ôøà¹M½ÕÉ•-•ä€ôô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”¤ì(€€€Ù…È½¹Ñ•áÐ€ô¹•Ü	…­™¥±±á•ÕÑ¥½¹½¹Ñ•áÐ¡µ…áI•ÅÕ•ÍÑÌ°€À¤ì(€€€Ù…È±•…Õ”€ô…Ý…¥ÐÁÉ½Ù¥‘•È¹I•Í½±Ù•1•…Õ•Íå¹Œ ‰%Ñ…±äˆ°€‰M•É¥”ˆ°½¹Ñ•áÐ°…¹•±±…Ñ¥½¹Q½­•¸¹9½¹”¤ì(€€€Ù…ÈÉ•ÍÕ±Ð€ô…Ý…¥ÐÁÉ½Ù¥‘•È¹•Ñ…µ•ÍÍå¹Œ¡±•…Õ”„°Í•…Í½¸°½¹Ñ•áÐ°…¹•±±…Ñ¥½¹Q½­•¸¹9½¹”¤ì((€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰=™™¥¥…°1	‘ÉäµÉÕ¸è%Ñ…±äèM•É¥”íÍ•…Í½¹ôˆ¤ì(€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰I•ÅÕ•ÍÑÌèí½¹Ñ•áÐ¹I•ÅÕ•ÍÑÍUÍ•‘ô½í½¹Ñ•áÐ¹5…áI•ÅÕ•ÍÑÍôì…µ•ÌèíÉ•ÍÕ±Ð¹…µ•Ì¹½Õ¹ÑôìÝ…É¹¥¹ÌèíÉ•ÍÕ±Ð¹]…É¹¥¹Ì¹½Õ¹Ñôˆ¤ì(€€€™½É•… €¡Ù…ÈÁ¡…Í”¥¸É•ÍÕ±Ð¹…µ•Ì¹É½ÕÁ	ä¡…µ”€ôø…µ”¹½µÁ•Ñ¥Ñ¥½¹A¡…Í”¤¹=É‘•É	ä¡É½ÕÀ€ôøÉ½ÕÀ¹-•ä¤¤(€€€ì(€€€€€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰íÁ¡…Í”¹-•ä€üü€‰U¹­¹½Ý¸Á¡…Í”‰ôèíÁ¡…Í”¹½Õ¹Ð ¥ô…µ•Ìˆ¤ì(€€€ô(€€€™½É•… €¡Ù…È…µ”¥¸É•ÍÕ±Ð¹…µ•Ì¹Q…­” ÄÀ¤¤(€€€ì(€€€€€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰í…µ”¹…µ•…Ñ•Q¥µ•UÑŒéåååäµ54µ‘‘ôí…µ”¹!½µ•Q•…µ9…µ•ôí…µ”¹!½µ•M½É•ôµí…µ”¹Ý…åM½É•ôí…µ”¹Ý…åQ•…µ9…µ•ômí…µ”¹½µÁ•Ñ¥Ñ¥½¹A¡…Í•õtí…µ”¹AÉ½Ù•¹…¹”ü¹M½ÕÉ•UÉ±ôˆ¤ì(€€€ô(€€€™½É•… €¡Ù…ÈÝ…É¹¥¹œ¥¸É•ÍÕ±Ð¹]…É¹¥¹Ì¹Q…­” ÈÀ¤¤(€€€ì(€€€€€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰]I9%9èíÝ…É¹¥¹ôˆ¤ì(€€€ô((€€€É•ÑÕÉ¸É•ÍÕ±Ð¹…µ•Ì¹½Õ¹Ð€ôô€À€ü€È€è€Àì)ô()ÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ¥¹ÐøIÕ¹%Ñ…±åM•É¥•%¹•ÍÑÍå¹Œ¡ÍÑÉ¥¹mt…ÉÌ¤)ì(€€€Ù…ÈÙ…±Õ•Ì€ôA…ÉÍ•-•åY…±Õ•ÉÌ¡…ÉÌ¤ì(€€€Ù…ÈÍÑ…ÉÑM•…Í½¸€ôI•ÅÕ¥É•¡Ù…±Õ•Ì°€ˆ´µÍÑ…ÉÐˆ¤ì(€€€Ù…È•¹‘M•…Í½¸€ôÙ…±Õ•Ì¹•ÑY…±Õ•=É•™…Õ±Ð ˆ´µ•¹ˆ¤€üüÍÑ…ÉÑM•…Í½¸ì(€€€Ù…Èµ…áI•ÅÕ•ÍÑÌ€ôA…ÉÍ•9½¹9•…Ñ¥Ù”¡Ù…±Õ•Ì°€ˆ´µµ…àµÉ•ÅÕ•ÍÑÌˆ°€À¤ì(€€€Ù…È¥¹Ñ•ÉÙ…°€ôA…ÉÍ•9½¹9•…Ñ¥Ù”¡Ù…±Õ•Ì°€ˆ´µ¥¹Ñ•ÉÙ…°µµÌˆ°€ÄÀÀ¤ì(€€€Ù…ÈÍÑ…ÉÑe•…È€ôM•…Í½¹1…‰•±9½Éµ…±¥é•È¹A…ÉÍ•MÑ…ÉÑe•…È¡ÍÑ…ÉÑM•…Í½¸¤ì(€€€Ù…È•¹‘e•…È€ôM•…Í½¹1…‰•±9½Éµ…±¥é•È¹A…ÉÍ•MÑ…ÉÑe•…È¡•¹‘M•…Í½¸¤ì(€€€Ù…È±½Ý•Ée•…È€ô5…Ñ ¹5¥¸¡ÍÑ…ÉÑe•…È°•¹‘e•…È¤ì(€€€Ù…ÈÕÁÁ•Ée•…È€ô5…Ñ ¹5…à¡ÍÑ…ÉÑe•…È°•¹‘e•…È¤ì((€€€Ù…È‰Õ¥±‘•È€ô!½ÍÐ¹É•…Ñ•ÁÁ±¥…Ñ¥½¹	Õ¥±‘•È ¤ì(€€€‰Õ¥±‘•È¹1½¥¹œ¹M•Ñ5¥¹¥µÕµ1•Ù•°¡1½1•Ù•°¹]…É¹¥¹œ¤ì(€€€‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¹l‰1‰…=™™¥¥…°é5¥¹I•ÅÕ•ÍÑ%¹Ñ•ÉÙ…±5¥±±¥Í•½¹‘Ì‰t€ô¥¹Ñ•ÉÙ…°¹Q½MÑÉ¥¹œ ¤ì(€€€¥˜€¡Ù…±Õ•Ì¹QÉå•ÑY…±Õ” ˆ´µ½¹¹•Ñ¥½¸µÍÑÉ¥¹œˆ°½ÕÐÙ…È½¹¹•Ñ¥½¹MÑÉ¥¹œ¤¤(€€€ì(€€€€€€€‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¹l‰½¹¹•Ñ¥½¹MÑÉ¥¹ÌéA½ÍÑÉ•Ì‰t€ô½¹¹•Ñ¥½¹MÑÉ¥¹œì(€€€ô(€€€‰Õ¥±‘•È¹M•ÉÙ¥•Ì¹‘‘%¹™É…ÍÑÉÕÑÕÉ”¡‰Õ¥±‘•È¹½¹™¥ÕÉ…Ñ¥½¸¤ì(€€€ÕÍ¥¹œÙ…È¡½ÍÐ€ô‰Õ¥±‘•È¹	Õ¥± ¤ì(€€€ÕÍ¥¹œÙ…ÈÍ½Á”€ô¡½ÍÐ¹M•ÉÙ¥•Ì¹É•…Ñ•M½Á” ¤ì(€€€Ù…È‘‰½¹Ñ•áÐ€ôÍ½Á”¹M•ÉÙ¥•AÉ½Ù¥‘•È¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ	…Í­•Ñ±½‰½¹Ñ•áÐø ¤ì(€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹…Ñ…‰…Í”¹5¥É…Ñ•Íå¹Œ ¤ì((€€€Ù…ÈÕ¹É•±…Ñ•‘A•¹‘¥¹)½‰Ì€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì(€€€€€€€€¹½Õ¹ÑÍå¹Œ¡©½ˆ€ôø©½ˆ¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹A•¹‘¥¹œ€˜˜(€€€€€€€€€€€©½ˆ¹AÉ½Ù¥‘•È€„ô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”¤ì(€€€¥˜€¡Õ¹É•±…Ñ•‘A•¹‘¥¹)½‰Ì€ø€À¤(€€€ì(€€€€€€€½¹Í½±”¹ÉÉ½È¹]É¥Ñ•1¥¹” (€€€€€€€€€€€€‰I•™ÕÍ¥¹œÑ¼ÍÑ…ÉÐèíÕ¹É•±…Ñ•‘A•¹‘¥¹)½‰ÍôÕ¹É•±…Ñ•‰…­™¥±°©½ˆ¡Ì¤…É”Á•¹‘¥¹œ…¹Ý½Õ±‰”ÁÉ½•ÍÍ•™¥ÉÍÐ¸ˆ¤ì(€€€€€€€É•ÑÕÉ¸€Èì(€€€ô((€€€Ù…È…Ñ…±½œ€ôÍ½Á”¹M•ÉÙ¥•AÉ½Ù¥‘•È¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ%	…­™¥±±…Ñ…±½œø ¤ì(€€€Ù…È±•…Õ”€ô…Ñ…±½œ¹•Ñ1•…Õ•Ì ¤¹M¥¹±”¡…¹‘¥‘…Ñ”€ôø(€€€€€€€…¹‘¥‘…Ñ”¹AÉ½Ù¥‘•È€ôô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”€˜˜(€€€€€€€…¹‘¥‘…Ñ”¹½Õ¹ÑÉä€ôô€‰%Ñ…±äˆ€˜˜…¹‘¥‘…Ñ”¹1•…Õ•9…µ”€ôô€‰M•É¥”ˆ¤ì(€€€Ù…ÈÍ•…Í½¹Ì€ô…Ñ…±½œ¹•ÑM•…Í½¹Í½É1•…Õ”¡±•…Õ”¤(€€€€€€€€¹]¡•É”¡Í•…Í½¸€ôø(€€€€€€€ì(€€€€€€€€€€€Ù…Èå•…È€ôM•…Í½¹1…‰•±9½Éµ…±¥é•È¹A…ÉÍ•MÑ…ÉÑe•…È¡Í•…Í½¸¤ì(€€€€€€€€€€€É•ÑÕÉ¸å•…È€øô±½Ý•Ée•…È€˜˜å•…È€ðôÕÁÁ•Ée•…Èì(€€€€€€€ô¤(€€€€€€€€¹=É‘•É	å•Í•¹‘¥¹œ¡M•…Í½¹1…‰•±9½Éµ…±¥é•È¹A…ÉÍ•MÑ…ÉÑe•…È¤(€€€€€€€€¹Q½1¥ÍÐ ¤ì(€€€¥˜€¡Í•…Í½¹Ì¹½Õ¹Ð€ôô€À¤(€€€ì(€€€€€€€½¹Í½±”¹ÉÉ½È¹]É¥Ñ•1¥¹” (€€€€€€€€€€€€‰9¼½™™¥¥…°1	Í•…Í½¹Ì™…±°¥¹Í¥‘”íÍÑ…ÉÑM•…Í½¹ôÑ¡É½Õ í•¹‘M•…Í½¹ôìÍÕÁÁ½ÉÑ•É…¹”¥Ì€ÄäÜÐ´ÄäÜÔÑ¡É½Õ €ÈÀÀÜ´ÈÀÀà¸ˆ¤ì(€€€€€€€É•ÑÕÉ¸€Äì(€€€ô((€€€Ù…È½µÁ±•Ñ•€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì(€€€€€€€€¹]¡•É”¡©½ˆ€ôø©½ˆ¹AÉ½Ù¥‘•È€ôô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”€˜˜(€€€€€€€€€€€©½ˆ¹½Õ¹ÑÉä€ôô€‰%Ñ…±äˆ€˜˜©½ˆ¹1•…Õ•9…µ”€ôô€‰M•É¥”ˆ€˜˜(€€€€€€€€€€€€¡©½ˆ¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹½µÁ±•Ñ•ñð©½ˆ¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹½µÁ±•Ñ•‘]¥Ñ¡]…É¹¥¹Ì¤¤(€€€€€€€€¹M•±•Ð¡©½ˆ€ôø©½ˆ¹M•…Í½¸¤(€€€€€€€€¹Q½1¥ÍÑÍå¹Œ ¤ì(€€€Ù…È…Ñ¥Ù”€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì(€€€€€€€€¹]¡•É”¡©½ˆ€ôø©½ˆ¹AÉ½Ù¥‘•È€ôô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”€˜˜(€€€€€€€€€€€©½ˆ¹½Õ¹ÑÉä€ôô€‰%Ñ…±äˆ€˜˜©½ˆ¹1•…Õ•9…µ”€ôô€‰M•É¥”ˆ€˜˜(€€€€€€€€€€€€¡©½ˆ¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹A•¹‘¥¹œñð©½ˆ¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹IÕ¹¹¥¹œ¤¤(€€€€€€€€¹M•±•Ð¡©½ˆ€ôø©½ˆ¹M•…Í½¸¤(€€€€€€€€¹Q½1¥ÍÑÍå¹Œ ¤ì(€€€Ù…È©½‰Ì€ôÍ•…Í½¹Ì(€€€€€€€€¹]¡•É”¡Í•…Í½¸€ôø€…½µÁ±•Ñ•¹½¹Ñ…¥¹Ì¡Í•…Í½¸°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤€˜˜(€€€€€€€€€€€€……Ñ¥Ù”¹½¹Ñ…¥¹Ì¡Í•…Í½¸°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤¤(€€€€€€€€¹M•±•Ð ¡Í•…Í½¸°¥¹‘•à¤€ôø¹•Ü	…­™¥±±)½ˆ(€€€€€€€ì(€€€€€€€€€€€%€ôÕ¥¹9•ÝÕ¥ ¤°(€€€€€€€€€€€AÉ½Ù¥‘•È€ô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”°(€€€€€€€€€€€½Õ¹ÑÉä€ô€‰%Ñ…±äˆ°(€€€€€€€€€€€1•…Õ•9…µ”€ô€‰M•É¥”ˆ°(€€€€€€€€€€€M•…Í½¸€ôÍ•…Í½¸°(€€€€€€€€€€€ÉåIÕ¸€ô™…±Í”°(€€€€€€€€€€€5…áI•ÅÕ•ÍÑÌ€ôµ…áI•ÅÕ•ÍÑÌ°(€€€€€€€€€€€MÑ…ÑÕÌ€ô	…­™¥±±)½‰MÑ…ÑÕÌ¹A•¹‘¥¹œ°(€€€€€€€€€€€É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü¹‘‘Q¥­Ì¡¥¹‘•à¤°(€€€€€€€€€€€UÁ‘…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü¹‘‘Q¥­Ì¡¥¹‘•à¤(€€€€€€€ô¤(€€€€€€€€¹Q½1¥ÍÐ ¤ì(€€€‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì¹‘‘I…¹”¡©½‰Ì¤ì(€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ ¤ì(€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰EÕ•Õ•í©½‰Ì¹½Õ¹Ñô½™™¥¥…°1	Í•…Í½¸¡Ì¤°¹•Ý•ÍÐ™¥ÉÍÐìÍ­¥ÁÁ•í½µÁ±•Ñ•¹½Õ¹Ñô½µÁ±•Ñ•…¹í…Ñ¥Ù”¹½Õ¹Ñô…Ñ¥Ù”Í•…Í½¸¡Ì¤¸ˆ¤ì((€€€Ù…ÈÁÉ½•ÍÍ½È€ôÍ½Á”¹M•ÉÙ¥•AÉ½Ù¥‘•È¹•ÑI•ÅÕ¥É•‘M•ÉÙ¥”ñ%	…­™¥±±)½‰AÉ½•ÍÍ½Èø ¤ì(€€€Ù…ÈÁÉ½•ÍÍ•€ô€Àì(€€€Ý¡¥±”€¡…Ý…¥ÐÁÉ½•ÍÍ½È¹QÉåAÉ½•ÍÍ9•áÑA•¹‘¥¹)½‰Íå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¹9½¹”¤¤(€€€ì(€€€€€€€ÁÉ½•ÍÍ•¬¬ì(€€€€€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰AÉ½•ÍÍ•½™™¥¥…°1	©½ˆíÁÉ½•ÍÍ•‘ô½í©½‰Ì¹½Õ¹Ñô¸ˆ¤ì(€€€ô((€€€Ù…ÈÍÕµµ…Éä€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹	…­™¥±±)½‰Ì(€€€€€€€€¹]¡•É”¡©½ˆ€ôø©½ˆ¹AÉ½Ù¥‘•È€ôô1‰…=™™¥¥…±M•É¥•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”€˜˜Í•…Í½¹Ì¹½¹Ñ…¥¹Ì¡©½ˆ¹M•…Í½¸¤¤(€€€€€€€€¹É½ÕÁ	ä¡©½ˆ€ôø©½ˆ¹MÑ…ÑÕÌ¤(€€€€€€€€¹M•±•Ð¡É½ÕÀ€ôø¹•ÜìMÑ…ÑÕÌ€ôÉ½ÕÀ¹-•ä°½Õ¹Ð€ôÉ½ÕÀ¹½Õ¹Ð ¤ô¤(€€€€€€€€¹=É‘•É	ä¡¥Ñ•´€ôø¥Ñ•´¹MÑ…ÑÕÌ¤(€€€€€€€€¹Q½1¥ÍÑÍå¹Œ ¤ì(€€€½¹Í½±”¹]É¥Ñ•1¥¹” ‰=™™¥¥…°1	¥¹•ÍÐÁÉ½•ÍÍ•íÁÉ½•ÍÍ•‘ô©½‰Ì¸MÑ…ÑÕÌèíÍÑÉ¥¹œ¹)½¥¸ ˆ°€ˆ°ÍÕµµ…Éä¹M•±•Ð¡¥Ñ•´€ôø€‰í¥Ñ•´¹MÑ…ÑÕÍôõí¥Ñ•´¹½Õ¹Ñôˆ¤¥ôˆ¤ì(€€€É•ÑÕÉ¸ÍÕµµ…Éä¹¹ä¡¥Ñ•´€ôø¥Ñ•´¹MÑ…ÑÕÌ€ôô	…­™¥±±)½‰MÑ…ÑÕÌ¹…¥±•¤€ü€È€è€Àì)ô()ÍÑ…Ñ¥Œ¥¹ÐA…ÉÍ•9½¹9•…Ñ¥Ù”¡%I•…‘=¹±å¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œøÙ…±Õ•Ì°ÍÑÉ¥¹œ¹…µ”°¥¹Ð‘•™…Õ±ÑY…±Õ”¤)ì(€€€Ù…ÈÙ…±Õ”€ôÙ…±Õ•Ì¹•ÑY…±Õ•=É•™…Õ±Ð¡¹…µ”¤ì(€€€É•ÑÕÉ¸ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Ù…±Õ”¤(€€€€€€€€ü‘•™…Õ±ÑY…±Õ”(€€€€€€€€è¥¹Ð¹QÉåA…ÉÍ”¡Ù…±Õ”°½ÕÐÙ…ÈÁ…ÉÍ•¤€˜˜Á…ÉÍ•€øô€À(€€€€€€€€€€€€üÁ…ÉÍ•(€€€€€€€€€€€€èÑ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰í¹…µ•ôµÕÍÐ‰”„¹½¸µ¹•…Ñ¥Ù”¥¹Ñ••È¸ˆ¤ì)ô()ÍÑ…Ñ¥Œ¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œøA…ÉÍ•-•åY…±Õ•ÉÌ¡ÍÑÉ¥¹mt…ÉÌ¤)ì(€€€Ù…ÈÙ…±Õ•Ì€ô¹•Ü¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œø¡MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤ì(€€€™½È€¡Ù…È¥¹‘•à€ô€Àì¥¹‘•à€ð…ÉÌ¹1•¹Ñ ì¥¹‘•à¬¬¤(€€€ì(€€€€€€€¥˜€ ……ÉÍm¥¹‘•át¹MÑ…ÉÑÍ]¥Ñ  ˆ´´ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤ñð¥¹‘•à€¬€Ä€øô…ÉÌ¹1•¹Ñ ¤(€€€€€€€ì(€€€€€€€€€€€Ñ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰U¹­¹½Ý¸½È¥¹½µÁ±•Ñ”…ÉÕµ•¹Ð€í…ÉÍm¥¹‘•áuôœ¸ˆ¤ì(€€€€€€€ô((€€€€€€€Ù…±Õ•Ím…ÉÍm¥¹‘•áut€ô…ÉÍl¬­¥¹‘•átì(€€€ô((€€€É•ÑÕÉ¸Ù…±Õ•Ìì)ô()ÍÑ…Ñ¥ŒÍÑÉ¥¹œI•ÅÕ¥É•¡%I•…‘=¹±å¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œøÙ…±Õ•Ì°ÍÑÉ¥¹œ¹…µ”¤€ôø(€€€Ù…±Õ•Ì¹QÉå•ÑY…±Õ”¡¹…µ”°½ÕÐÙ…ÈÙ…±Õ”¤€˜˜€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Ù…±Õ”¤(€€€€€€€€üÙ…±Õ”(€€€€€€€€èÑ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰í¹…µ•ô¥ÌÉ•ÅÕ¥É•¸ˆ¤ì(4)ÍÑ…Ñ¥ŒÙ½¥AÉ¥¹ÑUÍ…” ¤4)ì4(€€€½¹Í½±”¹]É¥Ñ•1¥¹” ˆˆˆ(€€€€€€€	…Í­•Ñ±¼9	¡¥ÍÑ½É¥…°…Õ‘¥Ð€¡É•…µ½¹±ä¤(4(€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´¹‰„µ…Õ‘¥Ðp4(€€€€€€€€€€´µÍÑ…ÉÐ€ÄäÐØ´ÄäÐÜ€´µ•¹€ÄäÔä´ÄäØÀp4(€€€€€€€€€€´µ½ÕÑÁÕÐ…ÉÑ¥™…ÑÌ½¹‰„µ…Õ‘¥Ð´ÄäÐØ´ÄäØÀ¹©Í½¸p4(€€€€€€€€€l´µµ…àµÉ•ÅÕ•ÍÑÌ€Átl´µÉ•ÍÕµ•t4(4(€€€€€€€=ÕÑÁÕÐµÕÍÐ‰”€¹©Í½¸½È€¹ÍØ¸I•ÍÕµ”É•ÅÕ¥É•Ì)M=8¸AÉ½Ù¥‘•È…É¡¥Ù”…¹(€€€€€€€…ÕÑ¡½É¥é•¹•ÑÝ½É¬Í•ÑÑ¥¹ÌÕÍ”	…Í­•Ñ‰…±±I•™•É•¹•}|¨½¹™¥ÕÉ…Ñ¥½¸¸((€€€€€€€%	½™™¥¥…°…É¡¥Ù”‘ÉäµÉÕ¸€¡É•…µ½¹±ä¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´™¥‰„µ‘ÉäµÉÕ¸p(€€€€€€€€€€´µ½Õ¹ÑÉäÕÉ½Á”€´µ±•…Õ”€‰%	ÕÉ½	…Í­•ÐEÕ…±¥™¥•ÉÌˆ€´µÍ•…Í½¸€ÈÀÈÈ´ÈÀÈÌp(€€€€€€€€€l´µµ…àµÉ•ÅÕ•ÍÑÌ€Ét((€€€€€€€%	‘…Ñ…‰…Í”¥¹•ÍÐ€¡ÝÉ¥Ñ•Ì±½…°A½ÍÑÉ•Ì¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´™¥‰„µ¥¹•ÍÐp(€€€€€€€€€l´µµ…àµ©½‰Ì€Átl´µµ…àµÉ•ÅÕ•ÍÑÌ€Ét((€€€€€€€¡¥ÍÑ½É¥…°…É¡¥Ù”‘ÉäµÉÕ¸((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´…ˆµ‘ÉäµÉÕ¸p(€€€€€€€€€€´µÍ•…Í½¸€ÈÀÀÜ´ÈÀÀàl´µµ…àµÉ•ÅÕ•ÍÑÌ€Étl´µ¥¹Ñ•ÉÙ…°µµÌ€Át((€€€€€€€¡¥ÍÑ½É¥…°…É¡¥Ù”¥¹•ÍÐ€¡ÝÉ¥Ñ•Ì±½…°A½ÍÑÉ•Ì¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´…ˆµ¥¹•ÍÐp(€€€€€€€€€€´µÍÑ…ÉÐ€ÈÀÀÜ´ÈÀÀà€´µ•¹€ÈÀÀÜ´ÈÀÀàp(€€€€€€€€€l´µµ…àµÉ•ÅÕ•ÍÑÌ€Átl´µ¥¹Ñ•ÉÙ…°µµÌ€ÈÔÁt((€€€€€€€½™™¥¥…°Ñ½ÕÉ¹…µ•¹Ð‘ÉäµÉÕ¸((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´…ˆµÑ½ÕÉ¹…µ•¹Ðµ‘ÉäµÉÕ¸p(€€€€€€€€€€´µ½µÁ•Ñ¥Ñ¥½¸€‰MÁ…¹¥Í ÕÀˆ€´µÍ•…Í½¸€ÈÀÀÜ´ÈÀÀàl´µµ…àµÉ•ÅÕ•ÍÑÌ€Át((€€€€€€€½™™¥¥…°Ñ½ÕÉ¹…µ•¹Ð¥¹•ÍÐ€¡ÝÉ¥Ñ•Ì±½…°A½ÍÑÉ•Ì¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´…ˆµÑ½ÕÉ¹…µ•¹Ðµ¥¹•ÍÐp(€€€€€€€€€€´µ½µÁ•Ñ¥Ñ¥½¸€‰MÁ…¹¥Í ÕÀˆ€´µÍÑ…ÉÐ€ÄäàÌ´ÄäàÐ€´µ•¹€ÈÀÀÜ´ÈÀÀàp(€€€€€€€€€l´µµ…àµÉ•ÅÕ•ÍÑÌ€Át((€€€€€€€=™™¥¥…°1•„	…Í­•ÐM•É¥”‘ÉäµÉÕ¸€¡É•…µ½¹±ä¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´¥Ñ…±äµÍ•É¥”µ„µ‘ÉäµÉÕ¸p(€€€€€€€€€€´µÍ•…Í½¸€ÈÀÀÜ´ÈÀÀàl´µµ…àµÉ•ÅÕ•ÍÑÌ€Átl´µ¥¹Ñ•ÉÙ…°µµÌ€ÄÀÁt((€€€€€€€=™™¥¥…°1•„	…Í­•ÐM•É¥”¥¹•ÍÐ€¡ÝÉ¥Ñ•Ì±½…°A½ÍÑÉ•Ì°¹•Ý•ÍÐ™¥ÉÍÐ¤((€€€€€€€‘½Ñ¹•ÐÉÕ¸€´µÁÉ½©•ÐÍÉŒ½	…Í­•Ñ±¼¹Q½½±Ì€´´¥Ñ…±äµÍ•É¥”µ„µ¥¹•ÍÐp(€€€€€€€€€€´µÍÑ…ÉÐ€ÈÀÀÜ´ÈÀÀà€´µ•¹€ÄäÜÐ´ÄäÜÔp(€€€€€€€€€l´µµ…àµÉ•ÅÕ•ÍÑÌ€Átl´µ¥¹Ñ•ÉÙ…°µµÌ€ÄÀÁtl´µ½¹¹•Ñ¥½¸µÍÑÉ¥¹œ€ˆ¸¸¸‰t(€€€€€€€€ˆˆˆ¤ì)ô(4)™¥±”Í•…±•É•½ÉÕ‘¥Ñ½µµ…¹‘=ÁÑ¥½¹Ì 4(€€€ÍÑÉ¥¹œMÑ…ÉÑM•…Í½¸°4(€€€ÍÑÉ¥¹œ¹‘M•…Í½¸°4(€€€ÍÑÉ¥¹œ=ÕÑÁÕÑA…Ñ °4(€€€¥¹Ð5…áI•ÅÕ•ÍÑÌ°4(€€€‰½½°I•ÍÕµ”°4(€€€‰½½°M¡½Ý!•±À¤4)ì4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÕ‘¥Ñ½µµ…¹‘=ÁÑ¥½¹ÌA…ÉÍ”¡ÍÑÉ¥¹mt…ÉÌ¤4(€€€ì4(€€€€€€€¥˜€¡…ÉÌ¹1•¹Ñ €ôô€Àñð…ÉÌ¹¹ä¡…Éœ€ôø…Éœ¥Ì€ˆ´µ¡•±Àˆ½È€ˆµ ˆ¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹•Ü ˆÄäÐØ´ÄäÐÜˆ°€ˆÄäÐØ´ÄäÐÜˆ°ÍÑÉ¥¹œ¹µÁÑä°€À°™…±Í”°ÑÉÕ”¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…È½™™Í•Ð€ô…ÉÍlÁt¹ÅÕ…±Ì ‰¹‰„µ…Õ‘¥Ðˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤€ü€Ä€è€Àì4(€€€€€€€Ù…ÈÙ…±Õ•Ì€ô¹•Ü¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œø¡MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤ì4(€€€€€€€Ù…ÈÉ•ÍÕµ”€ô™…±Í”ì4(€€€€€€€™½È€¡Ù…È¥¹‘•à€ô½™™Í•Ðì¥¹‘•à€ð…ÉÌ¹1•¹Ñ ì¥¹‘•à¬¬¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È…ÉÕµ•¹Ð€ô…ÉÍm¥¹‘•átì4(€€€€€€€€€€€¥˜€¡…ÉÕµ•¹Ð¹ÅÕ…±Ì ˆ´µÉ•ÍÕµ”ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÍÕµ”€ôÑÉÕ”ì4(€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€ ……ÉÕµ•¹Ð¹MÑ…ÉÑÍ]¥Ñ  ˆ´´ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤ñð¥¹‘•à€¬€Ä€øô…ÉÌ¹1•¹Ñ ¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ñ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰U¹­¹½Ý¸½È¥¹½µÁ±•Ñ”…ÉÕµ•¹Ð€í…ÉÕµ•¹Ñôœ¸ˆ¤ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…±Õ•Ím…ÉÕµ•¹Ñt€ô…ÉÍl¬­¥¹‘•átì4(€€€€€€€ô4(4(€€€€€€€Ù…ÈÍÑ…ÉÐ€ôI•ÅÕ¥É•¡Ù…±Õ•Ì°€ˆ´µÍÑ…ÉÐˆ¤ì4(€€€€€€€Ù…È•¹€ôI•ÅÕ¥É•¡Ù…±Õ•Ì°€ˆ´µ•¹ˆ¤ì4(€€€€€€€|€ô9‰…!¥ÍÑ½É¥…±Õ‘¥ÑM•ÉÙ¥”¹•ÑM•…Í½¹I…¹”¡ÍÑ…ÉÐ°•¹¤ì4(€€€€€€€Ù…È½ÕÑÁÕÐ€ôÙ…±Õ•Ì¹•ÑY…±Õ•=É•™…Õ±Ð ˆ´µ½ÕÑÁÕÐˆ¤€üü4(€€€€€€€€€€€€‰…ÉÑ¥™…ÑÌ½¹‰„µ…Õ‘¥ÐµíÍÑ…ÉÑôµí•¹‘ô¹©Í½¸ˆì4(€€€€€€€Ù…Èµ…áI•ÅÕ•ÍÑÍQ•áÐ€ôÙ…±Õ•Ì¹•ÑY…±Õ•=É•™…Õ±Ð ˆ´µµ…àµÉ•ÅÕ•ÍÑÌˆ¤€üü€ˆÀˆì4(€€€€€€€¥˜€ …¥¹Ð¹QÉåA…ÉÍ”¡µ…áI•ÅÕ•ÍÑÍQ•áÐ°½ÕÐÙ…Èµ…áI•ÅÕ•ÍÑÌ¤ñðµ…áI•ÅÕ•ÍÑÌ€ð€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ñ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ˆ´µµ…àµÉ•ÅÕ•ÍÑÌµÕÍÐ‰”„¹½¸µ¹•…Ñ¥Ù”¥¹Ñ••È¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…È•áÑ•¹Í¥½¸€ôA…Ñ ¹•ÑáÑ•¹Í¥½¸¡½ÕÑÁÕÐ¤ì4(€€€€€€€¥˜€ …•áÑ•¹Í¥½¸¹ÅÕ…±Ì ˆ¹©Í½¸ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤€˜˜4(€€€€€€€€€€€€…•áÑ•¹Í¥½¸¹ÅÕ…±Ì ˆ¹ÍØˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ñ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ˆ´µ½ÕÑÁÕÐµÕÍÐ•¹¥¸€¹©Í½¸½È€¹ÍØ¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡É•ÍÕµ”€˜˜€…•áÑ•¹Í¥½¸¹ÅÕ…±Ì ˆ¹©Í½¸ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ñ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ˆ´µÉ•ÍÕµ”É•ÅÕ¥É•Ì)M=8½ÕÑÁÕÐ¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸¹•Ü¡ÍÑ…ÉÐ°•¹°½ÕÑÁÕÐ°µ…áI•ÅÕ•ÍÑÌ°É•ÍÕµ”°™…±Í”¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œI•ÅÕ¥É•¡%I•…‘=¹±å¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œøÙ…±Õ•Ì°ÍÑÉ¥¹œ¹…µ”¤€ôø4(€€€€€€€Ù…±Õ•Ì¹QÉå•ÑY…±Õ”¡¹…µ”°½ÕÐÙ…ÈÙ…±Õ”¤€˜˜€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Ù…±Õ”¤4(€€€€€€€€€€€€üÙ…±Õ”4(€€€€€€€€€€€€èÑ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰í¹…µ•ô¥ÌÉ•ÅÕ¥É•¸ˆ¤ì4)ô4(