using System.Globalization;
using System.Text;
using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillJobProcessor(
    BasketEloDbContext dbContext,
    IEnumerable<IBasketballDataProvider> providers,
    IIdentityHealthCheckService identityHealthCheckService,
    IBackfillCatalog backfillCatalog,
    ILogger<BackfillJobProcessor> logger,
    IOptions<BackfillOptions> backfillOptions) : IBackfillJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var job = await dbContext.BackfillJobs
            .Where(x => x.Status == BackfillJobStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        job.Status = BackfillJobStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ProcessJobAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = BackfillJobStatus.Pending;
            job.StartedAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill job {jobId} failed.", job.Id);
            var httpException = ex as HttpRequestException;
            var retryException = ex as BackfillHttpRequestException;
            var failedAtUtc = DateTime.UtcNow;
            job.Status = BackfillJobStatus.Failed;
            job.ErrorMessage = Truncate(
                $"{job.Provider} backfill failed for {job.Country}: {job.LeagueName} {job.Season}. " +
                $"{ex.GetType().Name}: {ex.Message}",
                4000);
            job.SummaryJson = JsonSerializer.Serialize(new
            {
                failure = new
                {
                    jobId = job.Id,
                    provider = job.Provider,
                    country = job.Country,
                    leagueName = job.LeagueName,
                    season = job.Season,
                    exceptionType = ex.GetType().FullName,
                    message = ex.Message,
                    isTransientHttpFailure = retryException is not null,
                    httpStatusCode = httpException?.StatusCode is null ? null : (int?)httpException.StatusCode,
                    attempts = retryException?.Attempts ?? 1,
                    requestsUsed = job.RequestsUsed,
                    failedAtUtc,
                    retryEndpoint = "/api/backfill/jobs"
                }
            });
            job.FinishedAtUtc = failedAtUtc;
            job.UpdatedAtUtc = failedAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task ProcessJobAsync(BackfillJob job, CancellationToken cancellationToken)
    {
        string? changedPoolKey = null;
        var canQueuePoolRebuild = false;
        var provider = providers.FirstOrDefault(x =>
            string.Equals(x.SourceKey, job.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new InvalidOperationException($"Provider '{job.Provider}' is not registered.");
        }

        var executionContext = new BackfillExecutionContext(job.MaxRequests, job.RequestsUsed);
        var configuredLeague = backfillCatalog.GetLeagues().FirstOrDefault(x =>
            string.Equals(x.Provider, job.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Country, job.Country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.LeagueName, job.LeagueName, StringComparison.OrdinalIgnoreCase));

        var providerLeagueMappings = GetProviderLeagueMappings(configuredLeague, job);
        var resolvedLeagues = new List<BasketballProviderLeague>();
        var allGames = new List<BasketballProviderGame>();
        var filteredGameReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var hasMorePages = false;
        var usesSingleYearSeasonLabel = configuredLeague?.UsesSingleYearSeasonLabel == true;
        var canonicalSeason = SeasonLabelNormalizer.ToCanonicalSeasonLabel(job.Season, usesSingleYearSeasonLabel);
        if (!string.Equals(job.Season, canonicalSeason, StringComparison.Ordinal))
        {
            job.Season = canonicalSeason;
            job.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var mapping in providerLeagueMappings)
        {
            BasketballProviderLeague? resolvedLeague;
            try
            {
                resolvedLeague = await provider.ResolveLeagueAsync(
                    mapping.Country,
                    mapping.LeagueName,
                    executionContext,
                    cancellationToken);
            }
            finally
            {
                job.RequestsUsed = executionContext.RequestsUsed;
            }

            if (resolvedLeague is null)
            {
                job.WarningCount += 1;
                warnings.Add($"League not found for provider mapping '{mapping.Country}: {mapping.LeagueName}'.");
                continue;
            }

            var league = resolvedLeague with
            {
                SeasonParameterFormat = mapping.SeasonParameterFormat
            };
            resolvedLeagues.Add(league);

            (IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings) gamesResult;
            try
            {
                gamesResult = await provider.GetGamesAsync(
                    league,
                    canonicalSeason,
                    executionContext,
                    cancellationToken);
            }
            finally
            {
                job.RequestsUsed = executionContext.RequestsUsed;
            }

            if (gamesResult.HasMorePages)
            {
                hasMorePages = true;
                job.WarningCount += 1;
            }

            job.WarningCount += gamesResult.Warnings.Count;
            warnings.AddRange(gamesResult.Warnings.Select(warning => $"{league.Name}: {warning}"));
            foreach (var game in gamesResult.Games)
            {
                allGames.Add(game);
                if (game.ExclusionReason is not null)
                {
                    filteredGameReasons[game.ExclusionReason] =
                        filteredGameReasons.GetValueOrDefault(game.ExclusionReason) + 1;
                }
            }
        }

        if (resolvedLeagues.Count == 0)
        {
            CompleteJob(job, BackfillJobStatus.CompletedWithWarnings, new
            {
                message = "No provider league mapping could be resolved.",
                requestsUsed = job.RequestsUsed,
                warnings
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var summary = new BackfillSummary
        {
            LeagueName = configuredLeague?.LeagueName ?? resolvedLeagues[0].Name,
            Season = canonicalSeason,
            Source = provider.SourceKey,
            RequestsUsed = job.RequestsUsed,
            HasMorePages = hasMorePages,
            GamesFetched = allGames.Count,
            GamesFiltered = filteredGameReasons.Values.Sum()
        };
        summary.FilteredGameReasons.AddRange(filteredGameReasons
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}:{x.Value}"));

        if (hasMorePages)
        {
            summary.Warnings.Add("The provider returned more pages than the current request budget allowed, so only the first page of games was imported.");
        }

        summary.ProviderLeagues.AddRange(resolvedLeagues.Select(x => $"{x.Name} ({x.SourceLeagueId})"));
        summary.Warnings.AddRange(warnings);
        summary.SourceUrls.AddRange(allGames
            .Select(x => x.Provenance?.SourceUrl)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.SourceSeasonKeys.AddRange(allGames
            .Select(x => x.Provenance?.SourceSeasonKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.ParserVersions.AddRange(allGames
            .Select(x => x.Provenance?.ParserVersion)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.SourceFetchedAtUtc = allGames
            .Select(x => x.Provenance?.FetchedAtUtc)
            .Where(x => x.HasValue)
            .Max();

        if (!job.DryRun)
        {
            var competition = await GetOrCreateCompetitionAsync(resolvedLeagues[0], configuredLeague, cancellationToken);

            foreach (var additionalLeague in resolvedLeagues.Skip(1))
            {
                await EnsureCompetitionAliasAsync(competition, additionalLeague, cancellationToken);
            }

            var season = await GetOrCreateSeasonAsync(competition, canonicalSeason, usesSingleYearSeasonLabel, cancellationToken);

            var isRichEuropeanChampionsCupArchive = string.Equals(provider.SourceKey, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
                resolvedLeagues.Any(league => league.SourceLeagueId.StartsWith(
                    "112-fiba-mens-european-club-competitions-tier-1",
                    StringComparison.OrdinalIgnoreCase)) &&
                allGames.Count >= 40 &&
                allGames.All(game => !string.Equals(
                    game.Provenance?.ParserVersion,
                    WikipediaFibaEuropeanChampionsCupParser.ParserVersion,
                    StringComparison.Ordinal));
            var isSaportaArchive = string.Equals(provider.SourceKey, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
                resolvedLeagues.Any(league => league.SourceLeagueId.StartsWith(
                    "212-fiba-mens-european-club-competitions-tier-2",
                    StringComparison.OrdinalIgnoreCase)) &&
                allGames.Count > 0;
            var isKoracArchive = string.Equals(provider.SourceKey, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
                resolvedLeagues.Any(league => league.SourceLeagueId.StartsWith(
                    "164-eurocup-challenge",
                    StringComparison.OrdinalIgnoreCase)) &&
                allGames.Count > 0;
            var replacesSparseFibaArchive = string.Equals(provider.SourceKey, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
                (isSaportaArchive || isKoracArchive || allGames.Any(game => string.Equals(
                    game.Provenance?.ParserVersion,
                    WikipediaFibaEuropeanChampionsCupParser.ParserVersion,
                    StringComparison.Ordinal)) || isRichEuropeanChampionsCupArchive);
            var replacesHistoricalWikipediaEuroleague = string.Equals(
                provider.SourceKey,
                WikipediaEuroleagueHistoricalDataProvider.Source,
                StringComparison.OrdinalIgnoreCase) &&
                allGames.Count > 0;
            var replacesHistoricalBasketballReferenceEuroleague = string.Equals(
                provider.SourceKey,
                BasketballReferenceBasketballDataProvider.Source,
                StringComparison.OrdinalIgnoreCase) &&
                resolvedLeagues.Any(league => string.Equals(league.SourceLeagueId, "Euroleague", StringComparison.OrdinalIgnoreCase)) &&
                allGames.Count > 0;
            var replacesHistoricalFlashscoreEuroleague = string.Equals(
                provider.SourceKey,
                FlashscoreEuroleagueHistoricalDataProvider.Source,
                StringComparison.OrdinalIgnoreCase) &&
                allGames.Count > 0;
            var replacesHistoricalEuroleagueR = string.Equals(
                provider.SourceKey,
                EuroleagueRHistoricalDataProvider.Source,
                StringComparison.OrdinalIgnoreCase) &&
                allGames.Count > 0;
            var legacyEuroleagueSources = new[]
            {
                BasketballReferenceBasketballDataProvider.Source,
                WikipediaEuroleagueHistoricalDataProvider.Source,
                FlashscoreEuroleagueHistoricalDataProvider.Source
            };
            if (replacesSparseFibaArchive || replacesHistoricalWikipediaEuroleague || replacesHistoricalBasketballReferenceEuroleague || replacesHistoricalFlashscoreEuroleague || replacesHistoricalEuroleagueR)
            {
                var incomingSourceGameIds = allGames
                    .Select(game => game.SourceGameId)
                    .ToHashSet(StringComparer.Ordinal);
                var staleFibaGames = await dbContext.Games
                    .Where(game =>
                        game.SeasonId == season.Id &&
                        ((game.Source == provider.SourceKey &&
                            !incomingSourceGameIds.Contains(game.SourceGameId)) ||
                         ((replacesHistoricalFlashscoreEuroleague || replacesHistoricalEuroleagueR) &&
                            legacyEuroleagueSources.Contains(game.Source))))
                    .ToListAsync(cancellationToken);
                if (staleFibaGames.Count > 0)
                {
                    dbContext.Games.RemoveRange(staleFibaGames);
                    // Flush the replacement deletion before looking up incoming
                    // Wikipedia/Todor66 source IDs. Otherwise EF can return a
                    // tracked row already marked Deleted, count it as updated,
                    // and delete it again instead of inserting the replacement.
                    await dbContext.SaveChangesAsync(cancellationToken);
                    summary.Warnings.Add($"Removed {staleFibaGames.Count} stale FIBA game row(s) before applying the richer season result; this keeps the season replacement deduplicated.");
                }
            }

            foreach (var providerGame in allGames)
            {
                var homeCountryCode = NormalizeCountryCode(providerGaç~·¶‰žËkºwµçM•…Í½¹1…‰•°ì(€€€€€€€€€€€€€€€•á¥ÍÑ¥¹œ¹MÑ…ÉÑ…Ñ•UÑŒ€ôÕÁ‘…Ñ•‘MÑ…ÉÑ…Ñ”ì4(€€€€€€€€€€€€€€€•á¥ÍÑ¥¹œ¹¹‘…Ñ•UÑŒ€ôÕÁ‘…Ñ•‘¹‘…Ñ”ì4(€€€€€€€€€€€€€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸•á¥ÍÑ¥¹œì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€Ù…È€¡ÍÑ…ÉÑ…Ñ”°•¹‘…Ñ”¤€ôA…ÉÍ•M•…Í½¹…Ñ•Ì¡…¹½¹¥…±M•…Í½¹1…‰•°°ÕÍ•ÍM¥¹±•e•…ÉM•…Í½¹1…‰•°¤ì(€€€€€€€Ù…ÈÍ•…Í½¸€ô¹•ÜM•…Í½¸4(€€€€€€€ì4(€€€€€€€€€€€%€ôÕ¥¹9•ÝÕ¥ ¤°4(€€€€€€€€€€€½µÁ•Ñ¥Ñ¥½¹%€ô½µÁ•Ñ¥Ñ¥½¸¹%°4(€€€€€€€€€€€1…‰•°€ô…¹½¹¥…±M•…Í½¹1…‰•°°4(€€€€€€€€€€€MÑ…ÉÑ…Ñ•UÑŒ€ôÍÑ…ÉÑ…Ñ”°4(€€€€€€€€€€€¹‘…Ñ•UÑŒ€ô•¹‘…Ñ”°4(€€€€€€€€€€€É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü4(€€€€€€€ôì4(4(€€€€€€€‘‰½¹Ñ•áÐ¹M•…Í½¹Ì¹‘¡Í•…Í½¸¤ì4(€€€€€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€É•ÑÕÉ¸Í•…Í½¸ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬ñQ•…´ø•Ñ=ÉÉ•…Ñ•Q•…µÍå¹Œ 4(€€€€€€€ÍÑÉ¥¹œÍ½ÕÉ”°4(€€€€€€€ÍÑÉ¥¹œÍ½ÕÉ•Q•…µ%°4(€€€€€€€ÍÑÉ¥¹œÑ•…µ9…µ”°(€€€€€€€ÍÑÉ¥¹œü½Õ¹ÑÉå½‘”°(€€€€€€€ÍÑÉ¥¹œÍ•…Í½¸°(€€€€€€€ÍÑÉ¥¹œü•±½A½½±-•ä°(€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸…¹•±±…Ñ¥½¹Q½­•¸¤(€€€ì(€€€€€€€½Õ¹ÑÉå½‘”€ô9½Éµ…±¥é•½Õ¹ÑÉå½‘”¡½Õ¹ÑÉå½‘”¤ì(€€€€€€€Í½ÕÉ•Q•…µ%€ô9½Éµ…±¥é•M½ÕÉ•Q•…µ%¡Í½ÕÉ•Q•…µ%°Ñ•…µ9…µ”¤ì(€€€€€€€Ù…È¥Í%¹Ñ•É¹…Ñ¥½¹…°€ôÍÑÉ¥¹œ¹ÅÕ…±Ì (€€€€€€€€€€€•±½A½½±-•ä°(€€€€€€€€€€€±½A½½±-•åÌ¹9…Ñ¥½¹…±Q•…µÌ°(€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì(€€€€€€€Ù…È¥¹Ñ•É¹…Ñ¥½¹…±…¹½¹¥…±9…µ”€ôÍÑÉ¥¹œ¹µÁÑäì(€€€€€€€Ù…È¥¹Ñ•É¹…Ñ¥½¹…±½Õ¹ÑÉå½‘”€ôÍÑÉ¥¹œ¹µÁÑäì(€€€€€€€Ù…È¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€ô¥Í%¹Ñ•É¹…Ñ¥½¹…°€˜˜%¹Ñ•É¹…Ñ¥½¹…±Q•…µ…Ñ…±½œ¹QÉåI•Í½±Ù” (€€€€€€€€€€€Í½ÕÉ•Q•…µ%°(€€€€€€€€€€€Ñ•…µ9…µ”°(€€€€€€€€€€€½Õ¹ÑÉå½‘”°(€€€€€€€€€€€½ÕÐ¥¹Ñ•É¹…Ñ¥½¹…±…¹½¹¥…±9…µ”°(€€€€€€€€€€€½ÕÐ¥¹Ñ•É¹…Ñ¥½¹…±½Õ¹ÑÉå½‘”¤ì(€€€€€€€Ù…ÈÉ•Í½±Ù•‘Q•…µ9…µ”€ô¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€ü¥¹Ñ•É¹…Ñ¥½¹…±…¹½¹¥…±9…µ”€èÑ•…µ9…µ”ì(€€€€€€€Ù…ÈÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”€ô¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€ü¥¹Ñ•É¹…Ñ¥½¹…±½Õ¹ÑÉå½‘”€è½Õ¹ÑÉå½‘”€üüÍÑÉ¥¹œ¹µÁÑäì(€€€€€€€Ù…ÈÕÍ•Í9‰…É…¹¡¥Í•…Ñ…±½œ€ôÍÑÉ¥¹œ¹ÅÕ…±Ì (€€€€€€€€€€€€€€€Í½ÕÉ”°4(€€€€€€€€€€€€€€€	…Í­•Ñ‰…±±I•™•É•¹•	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”°4(€€€€€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ñð4(€€€€€€€€€€€ÍÑÉ¥¹œ¹ÅÕ…±Ì 4(€€€€€€€€€€€€€€€Í½ÕÉ”°4(€€€€€€€€€€€€€€€¥Ù•Q¡¥ÉÑå¥¡Ñ	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”°4(€€€€€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì4(€€€€€€€Ù…È™É…¹¡¥Í•5…Ñ €ôÕÍ•Í9‰…É…¹¡¥Í•…Ñ…±½œ4(€€€€€€€€€€€€ü9‰…É…¹¡¥Í•…Ñ…±½œ¹I•Í½±Ù”¡Í½ÕÉ•Q•…µ%°Ñ•…µ9…µ”°M•…Í½¹1…‰•±9½Éµ…±¥é•È¹A…ÉÍ•MÑ…ÉÑe•…È¡Í•…Í½¸¤¤4(€€€€€€€€€€€€è¹Õ±°ì4(€€€€€€€Ù…È…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”€ôÍÑÉ¥¹œ¹ÅÕ…±Ì 4(€€€€€€€€€€€€€€€Í½ÕÉ”°4(€€€€€€€€€€€€€€€Á¥MÁ½ÉÑÍ	…Í­•Ñ‰…±±…Ñ…AÉ½Ù¥‘•È¹M½ÕÉ”°4(€€€€€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤4(€€€€€€€€€€€€ü9‰…Á¥MÁ½ÉÑÍ…Ñ…±½œ¹•Ñ…¹½¹¥…±9…µ”¡Í½ÕÉ•Q•…µ%¤4(€€€€€€€€€€€€è¹Õ±°ì4(4(€€€€€€€Ù…È…±¥…Ì€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µ±¥…Í•Ì4(€€€€€€€€€€€€¹%¹±Õ‘”¡à€ôøà¹Q•…´¤4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±ÑÍå¹Œ 4(€€€€€€€€€€€€€€€à€ôøà¹M½ÕÉ”€ôôÍ½ÕÉ”€˜˜à¹M½ÕÉ•Q•…µ%€ôôÍ½ÕÉ•Q•…µ%°4(€€€€€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì4(4(€€€€€€€¥˜€¡…±¥…Ì¥Ì¹½Ð¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È…±¥…Í¡…¹•€ô™…±Í”ì4(4(€€€€€€€€€€€¥˜€¡™É…¹¡¥Í•5…Ñ ¥Ì¹½Ð¹Õ±°¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€¥˜€¡…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€„ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹…¹½¹¥…±9…µ”¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹…¹½¹¥…±9…µ”ì4(€€€€€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€¥˜€¡…±¥…Ì¹Q•…´¹%ÍÑ¥Ù”€„ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹%ÍÑ¥Ù”¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹%ÍÑ¥Ù”€ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹%ÍÑ¥Ù”ì4(€€€€€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€Ù…È€¡Ù…±¥‘É½µUÑŒ°Ù…±¥‘Q½UÑŒ¤€ô9‰…É…¹¡¥Í•…Ñ…±½œ¹•ÑY…±¥‘¥Ñä¡™É…¹¡¥Í•5…Ñ ¹±¥…Ì¤ì4(€€€€€€€€€€€€€€€¥˜€¡…±¥…Ì¹Y…±¥‘É½µUÑŒ€„ôÙ…±¥‘É½µUÑŒñð…±¥…Ì¹Y…±¥‘Q½UÑŒ€„ôÙ…±¥‘Q½UÑŒ¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€…±¥…Ì¹Y…±¥‘É½µUÑŒ€ôÙ…±¥‘É½µUÑŒì4(€€€€€€€€€€€€€€€€€€€…±¥…Ì¹Y…±¥‘Q½UÑŒ€ôÙ…±¥‘Q½UÑŒì4(€€€€€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€¡…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”¥Ì¹½Ð¹Õ±°€˜˜…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€„ô…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€ô…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”ì(€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì(€€€€€€€€€€€ô((€€€€€€€€€€€¥˜€¡¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€˜˜…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€„ôÉ•Í½±Ù•‘Q•…µ9…µ”¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹…¹½¹¥…±9…µ”€ôÉ•Í½±Ù•‘Q•…µ9…µ”ì(€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì(€€€€€€€€€€€ô((€€€€€€€€€€€¥˜€¡¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€˜˜…±¥…Ì¹Q•…´¹½Õ¹ÑÉå½‘”€„ôÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹½Õ¹ÑÉå½‘”€ôÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”ì(€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì(€€€€€€€€€€€ô(€€€€€€€€€€€•±Í”¥˜€¡…±¥…Ì¹Q•…´¹½Õ¹ÑÉå½‘”€ôô€‰U9,ˆ€˜˜€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡½Õ¹ÑÉå½‘”¤¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€…±¥…Ì¹Q•…´¹½Õ¹ÑÉå½‘”€ô½Õ¹ÑÉå½‘”„ì(€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…È¡…Í=‰Í•ÉÙ•‘9…µ”€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µ±¥…Í•Ì¹¹åÍå¹Œ 4(€€€€€€€€€€€€€€€à€ôø4(€€€€€€€€€€€€€€€€€€€à¹M½ÕÉ”€ôôÍ½ÕÉ”€˜˜4(€€€€€€€€€€€€€€€€€€€à¹M½ÕÉ•Q•…µ%€ôôÍ½ÕÉ•Q•…µ%€˜˜4(€€€€€€€€€€€€€€€€€€€à¹Q•…µ%€ôô…±¥…Ì¹Q•…µ%€˜˜4(€€€€€€€€€€€€€€€€€€€à¹±¥…Í9…µ”€ôôÑ•…µ9…µ”°4(€€€€€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì4(4(€€€€€€€€€€€¥˜€ …¡…Í=‰Í•ÉÙ•‘9…µ”¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È€¡Ù…±¥‘É½µUÑŒ°Ù…±¥‘Q½UÑŒ¤€ô™É…¹¡¥Í•5…Ñ ¥Ì¹Õ±°4(€€€€€€€€€€€€€€€€€€€€ü€¡¹Õ±°°¹Õ±°¤4(€€€€€€€€€€€€€€€€€€€€è9‰…É…¹¡¥Í•…Ñ…±½œ¹•ÑY…±¥‘¥Ñä¡™É…¹¡¥Í•5…Ñ ¹±¥…Ì¤ì4(€€€€€€€€€€€€€€€‘‰½¹Ñ•áÐ¹Q•…µ±¥…Í•Ì¹‘¡¹•ÜQ•…µ±¥…Ì4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€%€ôÕ¥¹9•ÝÕ¥ ¤°4(€€€€€€€€€€€€€€€€€€€Q•…µ%€ô…±¥…Ì¹Q•…µ%°4(€€€€€€€€€€€€€€€€€€€M½ÕÉ”€ôÍ½ÕÉ”°4(€€€€€€€€€€€€€€€€€€€M½ÕÉ•Q•…µ%€ôÍ½ÕÉ•Q•…µ%°4(€€€€€€€€€€€€€€€€€€€±¥…Í9…µ”€ôÑ•…µ9…µ”°4(€€€€€€€€€€€€€€€€€€€Y…±¥‘É½µUÑŒ€ôÙ…±¥‘É½µUÑŒ°4(€€€€€€€€€€€€€€€€€€€Y…±¥‘Q½UÑŒ€ôÙ…±¥‘Q½UÑŒ°4(€€€€€€€€€€€€€€€€€€€É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü4(€€€€€€€€€€€€€€€ô¤ì4(4(€€€€€€€€€€€€€€€…±¥…Í¡…¹•€ôÑÉÕ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€¡…±¥…Í¡…¹•¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€É•ÑÕÉ¸…±¥…Ì¹Q•…´ì4(€€€€€€€ô4(4(€€€€€€€Q•…´üÑ•…´€ô¹Õ±°ì4(€€€€€€€¥˜€¡™É…¹¡¥Í•5…Ñ ¥Ì¹½Ð¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È™É…¹¡¥Í•M½ÕÉ•%‘Ì€ô9‰…É…¹¡¥Í•…Ñ…±½œ¹•ÑM½ÕÉ•Q•…µ%‘Ì¡™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹-•ä¤ì4(€€€€€€€€€€€Ñ•…´€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µ±¥…Í•Ì4(€€€€€€€€€€€€€€€€¹]¡•É”¡•á¥ÍÑ¥¹±¥…Ì€ôø4(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹±¥…Ì¹M½ÕÉ”€ôôÍ½ÕÉ”€˜˜4(€€€€€€€€€€€€€€€€€€€™É…¹¡¥Í•M½ÕÉ•%‘Ì¹½¹Ñ…¥¹Ì¡•á¥ÍÑ¥¹±¥…Ì¹M½ÕÉ•Q•…µ%¤¤4(€€€€€€€€€€€€€€€€¹M•±•Ð¡•á¥ÍÑ¥¹±¥…Ì€ôø•á¥ÍÑ¥¹±¥…Ì¹Q•…´¤4(€€€€€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±ÑÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€€€€€Ñ•…´€üüô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µÌ¹¥ÉÍÑ=É•™…Õ±ÑÍå¹Œ 4(€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´€ôø4(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´¹…¹½¹¥…±9…µ”€ôô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹…¹½¹¥…±9…µ”€˜˜4(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´¹½Õ¹ÑÉå½‘”€ôô½Õ¹ÑÉå½‘”°4(€€€€€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€ô4(€€€€€€€•±Í”¥˜€¡…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”¥Ì¹½Ð¹Õ±°¤(€€€€€€€ì(€€€€€€€€€€€Ñ•…´€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µÌ¹¥ÉÍÑ=É•™…Õ±ÑÍå¹Œ (€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´€ôø(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´¹…¹½¹¥…±9…µ”€ôô…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”€˜˜(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´¹½Õ¹ÑÉå½‘”€ôô½Õ¹ÑÉå½‘”°(€€€€€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì(€€€€€€€ô(€€€€€€€•±Í”¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•Í½±Ù•‘½Õ¹ÑÉå½‘”¤¤(€€€€€€€ì(€€€€€€€€€€€Ù…È¹½Éµ…±¥é•‘Q•…µ9…µ”€ô9½Éµ…±¥é•%¹Ñ•É¹…Ñ¥½¹…±Q•…µ9…µ”¡É•Í½±Ù•‘Q•…µ9…µ”¤ì(€€€€€€€€€€€Ù…È…¹‘¥‘…Ñ•Ì€ô…Ý…¥Ð‘‰½¹Ñ•áÐ¹Q•…µÌ(€€€€€€€€€€€€€€€€¹]¡•É”¡•á¥ÍÑ¥¹Q•…´€ôø(€€€€€€€€€€€€€€€€€€€•á¥ÍÑ¥¹Q•…´¹½Õ¹ÑÉå½‘”€ôôÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”ñð(€€€€€€€€€€€€€€€€€€€€¡¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€˜˜•á¥ÍÑ¥¹Q•…´¹½Õ¹ÑÉå½‘”€ôô€‰U9,ˆ¤¤(€€€€€€€€€€€€€€€€¹Q½1¥ÍÑÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì(€€€€€€€€€€€Ñ•…´€ô…¹‘¥‘…Ñ•Ì¹¥ÉÍÑ=É•™…Õ±Ð¡•á¥ÍÑ¥¹Q•…´€ôø(€€€€€€€€€€€€€€€9½Éµ…±¥é•%¹Ñ•É¹…Ñ¥½¹…±Q•…µ9…µ”¡•á¥ÍÑ¥¹Q•…´¹…¹½¹¥…±9…µ”¤€ôô¹½Éµ…±¥é•‘Q•…µ9…µ”ñð(€€€€€€€€€€€€€€€€¡¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä€˜˜9½Éµ…±¥é•%¹Ñ•É¹…Ñ¥½¹…±Q•…µ9…µ”¡•á¥ÍÑ¥¹Q•…´¹…¹½¹¥…±9…µ”¤€ôô9½Éµ…±¥é•%¹Ñ•É¹…Ñ¥½¹…±Q•…µ9…µ”¡Í½ÕÉ•Q•…µ%¤¤¤ì(€€€€€€€ô(4(€€€€€€€¥˜€¡Ñ•…´¥Ì¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€Ñ•…´€ô¹•ÜQ•…´4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€%€ôÕ¥¹9•ÝÕ¥ ¤°(€€€€€€€€€€€€€€€…¹½¹¥…±9…µ”€ô™É…¹¡¥Í•5…Ñ ü¹É…¹¡¥Í”¹…¹½¹¥…±9…µ”€üü…Á¥MÁ½ÉÑÍ…¹½¹¥…±9…µ”€üüÉ•Í½±Ù•‘Q•…µ9…µ”°(€€€€€€€€€€€€€€€½Õ¹ÑÉå½‘”€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•Í½±Ù•‘½Õ¹ÑÉå½‘”¤€ü€‰U9,ˆ€èÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”°(€€€€€€€€€€€€€€€%ÍÑ¥Ù”€ô™É…¹¡¥Í•5…Ñ ü¹É…¹¡¥Í”¹%ÍÑ¥Ù”€üüÑÉÕ”°(€€€€€€€€€€€€€€€É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü(€€€€€€€€€€€ôì(€€€€€€€€€€€‘‰½¹Ñ•áÐ¹Q•…µÌ¹‘¡Ñ•…´¤ì4(€€€€€€€ô4(€€€€€€€•±Í”¥˜€¡™É…¹¡¥Í•5…Ñ ¥Ì¹½Ð¹Õ±°¤(€€€€€€€ì4(€€€€€€€€€€€Ñ•…´¹…¹½¹¥…±9…µ”€ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹…¹½¹¥…±9…µ”ì4(€€€€€€€€€€€Ñ•…´¹%ÍÑ¥Ù”€ô™É…¹¡¥Í•5…Ñ ¹É…¹¡¥Í”¹%ÍÑ¥Ù”ì4(€€€€€€€€€€€¥˜€¡Ñ•…´¹½Õ¹ÑÉå½‘”€ôô€‰U9,ˆ€˜˜€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡½Õ¹ÑÉå½‘”¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ñ•…´¹½Õ¹ÑÉå½‘”€ô½Õ¹ÑÉå½‘”„ì(€€€€€€€€€€€ô(€€€€€€€ô(€€€€€€€•±Í”¥˜€¡¡…Í%¹Ñ•É¹…Ñ¥½¹…±%‘•¹Ñ¥Ñä¤(€€€€€€€ì(€€€€€€€€€€€Ñ•…´¹…¹½¹¥…±9…µ”€ôÉ•Í½±Ù•‘Q•…µ9…µ”ì(€€€€€€€€€€€Ñ•…´¹½Õ¹ÑÉå½‘”€ôÉ•Í½±Ù•‘½Õ¹ÑÉå½‘”ì(€€€€€€€ô(4(€€€€€€€Ù…È€¡¹•Ý±¥…ÍY…±¥‘É½µUÑŒ°¹•Ý±¥…ÍY…±¥‘Q½UÑŒ¤€ô™É…¹¡¥Í•5…Ñ ¥Ì¹Õ±°4(€€€€€€€€€€€€ü€¡¹Õ±°°¹Õ±°¤4(€€€€€€€€€€€€è9‰…É…¹¡¥Í•…Ñ…±½œ¹•ÑY…±¥‘¥Ñä¡™É…¹¡¥Í•5…Ñ ¹±¥…Ì¤ì4(4(€€€€€€€‘‰½¹Ñ•áÐ¹Q•…µ±¥…Í•Ì¹‘¡¹•ÜQ•…µ±¥…Ì4(€€€€€€€ì4(€€€€€€€€€€€%€ôÕ¥¹9•ÝÕ¥ ¤°4(€€€€€€€€€€€Q•…µ%€ôÑ•…´¹%°4(€€€€€€€€€€€M½ÕÉ”€ôÍ½ÕÉ”°4(€€€€€€€€€€€M½ÕÉ•Q•…µ%€ôÍ½ÕÉ•Q•…µ%°4(€€€€€€€€€€€±¥…Í9…µ”€ôÑ•…µ9…µ”°4(€€€€€€€€€€€Y…±¥‘É½µUÑŒ€ô¹•Ý±¥…ÍY…±¥‘É½µUÑŒ°4(€€€€€€€€€€€Y…±¥‘Q½UÑŒ€ô¹•Ý±¥…ÍY…±¥‘Q½UÑŒ°4(€€€€€€€€€€€É•…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Ü4(€€€€€€€ô¤ì4(4(€€€€€€€…Ý…¥Ð‘‰½¹Ñ•áÐ¹M…Ù•¡…¹•ÍÍå¹Œ¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€É•ÑÕÉ¸Ñ•…´ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ9½Éµ…±¥é•M½ÕÉ•Q•…µ%¡ÍÑÉ¥¹œÍ½ÕÉ•Q•…µ%°ÍÑÉ¥¹œÑ•…µ9…µ”¤4(€€€ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Í½ÕÉ•Q•…µ%¤€˜˜4(€€€€€€€€€€€Í½ÕÉ•Q•…µ%€„ô€ˆÀˆ€˜˜4(€€€€€€€€€€€€…Í½ÕÉ•Q•…µ%¹ÅÕ…±Ì ‰¹Õ±°ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸Í½ÕÉ•Q•…µ%¹QÉ¥´ ¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…È¹½Éµ…±¥é•‘9…µ”€ô¹•ÜÍÑÉ¥¹œ¡Ñ•…µ9…µ”4(€€€€€€€€€€€€¹QÉ¥´ ¤4(€€€€€€€€€€€€¹Q½1½Ý•É%¹Ù…É¥…¹Ð ¤4(€€€€€€€€€€€€¹M•±•Ð¡ €ôø¡…È¹%Í1•ÑÑ•É=É¥¥Ð¡ ¤€ü €è€œ´œ¤4(€€€€€€€€€€€€¹Q½ÉÉ…ä ¤¤ì4(€€€€€€€¹½Éµ…±¥é•‘9…µ”€ôÍÑÉ¥¹œ¹)½¥¸ œ´œ°¹½Éµ…±¥é•‘9…µ”¹MÁ±¥Ð œ´œ°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹I•µ½Ù•µÁÑå¹ÑÉ¥•Ì¤¤ì4(4(€€€€€€€É•ÑÕÉ¸€‰¹…µ”éí¹½Éµ…±¥é•‘9…µ•ôˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ€¡…Ñ•Q¥µ”MÑ…ÉÑ…Ñ•UÑŒ°…Ñ•Q¥µ”¹‘…Ñ•UÑŒ¤A…ÉÍ•M•…Í½¹…Ñ•Ì¡ÍÑÉ¥¹œÍ•…Í½¹1…‰•°°‰½½°ÕÍ•ÍM¥¹±•e•…ÉM•…Í½¹1…‰•°¤(€€€ì(€€€€€€€¥˜€¡ÕÍ•ÍM¥¹±•e•…ÉM•…Í½¹1…‰•°€˜˜¥¹Ð¹QÉåA…ÉÍ”¡Í•…Í½¹1…‰•°°½ÕÐÙ…ÈÍ¥¹±•e•…È¤¤(€€€€€€€ì(€€€€€€€€€€€É•ÑÕÉ¸€ (€€€€€€€€€€€€€€€¹•Ü…Ñ•Q¥µ”¡Í¥¹±•e•…È°€Ä°€Ä°€À°€À°€À°…Ñ•Q¥µ•-¥¹¹UÑŒ¤°(€€€€€€€€€€€€€€€¹•Ü…Ñ•Q¥µ”¡Í¥¹±•e•…È°€ÄÈ°€ÌÄ°€ÈÌ°€Ôä°€Ôä°…Ñ•Q¥µ•-¥¹¹UÑŒ¤¤ì(€€€€€€€ô((€€€€€€€Ù…ÈÁ¥••Ì€ôM•…Í½¹1…‰•±9½Éµ…±¥é•È¹Q½Õ±±M•…Í½¹1…‰•°¡Í•…Í½¹1…‰•°¤(€€€€€€€€€€€€¹MÁ±¥Ð œ´œ°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹QÉ¥µ¹ÑÉ¥•Ì¤ì4(€€€€€€€¥˜€¡Á¥••Ì¹1•¹Ñ €ôô€È€˜˜4(€€€€€€€€€€€¥¹Ð¹QÉåA…ÉÍ”¡Á¥••ÍlÁt°½ÕÐÙ…ÈÍÑ…ÉÑe•…È¤€˜˜4(€€€€€€€€€€€¥¹Ð¹QÉåA…ÉÍ”¡Á¥••ÍlÅt°½ÕÐÙ…È•¹‘e•…È¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍÑ…ÉÐ€ô¹•Ü…Ñ•Q¥µ”¡ÍÑ…ÉÑe•…È°€Ü°€Ä°€À°€À°€À°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€€€€€€€€€Ù…È•¹€ô¹•Ü…Ñ•Q¥µ”¡•¹‘e•…È°€Ø°€ÌÀ°€ÈÌ°€Ôä°€Ôä°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€€€€€€€€€É•ÑÕÉ¸€¡ÍÑ…ÉÐ°•¹¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…È™…±±‰…­MÑ…ÉÐ€ô¹•Ü…Ñ•Q¥µ”¡…Ñ•Q¥µ”¹UÑ9½Ü¹e•…È°€Ä°€Ä°€À°€À°€À°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€€€€€Ù…È™…±±‰…­¹€ô™…±±‰…­MÑ…ÉÐ¹‘‘e•…ÉÌ Ä¤¹‘‘M•½¹‘Ì ´Ä¤ì4(€€€€€€€É•ÑÕÉ¸€¡™…±±‰…­MÑ…ÉÐ°™…±±‰…­¹¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œü1•…åM¥¹±•e•…É1…‰•°¡ÍÑÉ¥¹œ…¹½¹¥…±M•…Í½¹1…‰•°¤4(€€€ì4(€€€€€€€Ù…ÈÁ¥••Ì€ô…¹½¹¥…±M•…Í½¹1…‰•°¹MÁ±¥Ð œ´œ°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹QÉ¥µ¹ÑÉ¥•ÌðMÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹I•µ½Ù•µÁÑå¹ÑÉ¥•Ì¤ì4(€€€€€€€É•ÑÕÉ¸Á¥••Ì¹1•¹Ñ €ôô€È€˜˜¥¹Ð¹QÉåA…ÉÍ”¡Á¥••ÍlÁt°½ÕÐÙ…È|¤4(€€€€€€€€€€€€üÁ¥••ÍlÁt4(€€€€€€€€€€€€è¹Õ±°ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œü9½Éµ…±¥é•½Õ¹ÑÉå½‘”¡ÍÑÉ¥¹œüÁÉ½Ù¥‘•É½Õ¹ÑÉå½‘”¤(€€€€€€€€ôø½Õ¹ÑÉå½‘•…Ñ…±½œ¹9½Éµ…±¥é”¡ÁÉ½Ù¥‘•É½Õ¹ÑÉå½‘”¤ì((€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ9½Éµ…±¥é•%¹Ñ•É¹…Ñ¥½¹…±Q•…µ9…µ”¡ÍÑÉ¥¹œÙ…±Õ”¤(€€€ì(€€€€€€€Ù…È‘•½µÁ½Í•€ôÙ…±Õ”¹QÉ¥´ ¤¹9½Éµ…±¥é”¡9½Éµ…±¥é…Ñ¥½¹½É´¹½Éµ¤ì(€€€€€€€É•ÑÕÉ¸¹•ÜÍÑÉ¥¹œ¡‘•½µÁ½Í•(€€€€€€€€€€€€¹]¡•É”¡¡…É…Ñ•È€ôø(€€€€€€€€€€€€€€€¡…ÉU¹¥½‘•%¹™¼¹•ÑU¹¥½‘•…Ñ•½Éä¡¡…É…Ñ•È¤€„ôU¹¥½‘•…Ñ•½Éä¹9½¹MÁ…¥¹5…É¬€˜˜(€€€€€€€€€€€€€€€¡…È¹%Í1•ÑÑ•É=É¥¥Ð¡¡…É…Ñ•È¤¤(€€€€€€€€€€€€¹Q½ÉÉ…ä ¤¤(€€€€€€€€€€€€¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì(€€€ô(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œü½Õ¹ÑÉå½‘•É½µ¥ÍÁ±…ä¡ÍÑÉ¥¹œ½Õ¹ÑÉä¤4(€€€ì4(€€€€€€€É•ÑÕÉ¸½Õ¹ÑÉäÍÝ¥Ñ 4(€€€€€€€ì4(€€€€€€€€€€€€‰MÁ…¥¸ˆ€ôø€‰Lˆ°4(€€€€€€€€€€€€‰É…¹”ˆ€ôø€‰Hˆ°4(€€€€€€€€€€€€‰1¥Ñ¡Õ…¹¥„ˆ€ôø€‰1Pˆ°4(€€€€€€€€€€€€‰É••”ˆ€ôø€‰Hˆ°4(€€€€€€€€€€€€‰%Ñ…±äˆ€ôø€‰%Pˆ°4(€€€€€€€€€€€€‰QÕÉ­•äˆ€ôø€‰QHˆ°4(€€€€€€€€€€€€‰	•±¥Õ´ˆ€ôø€‰	ˆ°4(€€€€€€€€€€€€‰•Éµ…¹äˆ€ôø€‰ˆ°4(€€€€€€€€€€€€‰%ÍÉ…•°ˆ€ôø€‰%0ˆ°4(€€€€€€€€€€€€‰A½±…¹ˆ€ôø€‰A0ˆ°4(€€€€€€€€€€€€‰é• I•ÁÕ‰±¥Œˆ€ôø€‰hˆ°4(€€€€€€€€€€€€‰IÕÍÍ¥„ˆ€ôø€‰ITˆ°4(€€€€€€€€€€€€‰M•É‰¥„ˆ€ôø€‰ILˆ°4(€€€€€€€€€€€€‰É½…Ñ¥„ˆ€ôø€‰!Hˆ°4(€€€€€€€€€€€€‰M±½Ù•¹¥„ˆ€ôø€‰M$ˆ°4(€€€€€€€€€€€€‰1…ÑÙ¥„ˆ€ôø€‰1Xˆ°4(€€€€€€€€€€€€‰ÍÑ½¹¥„ˆ€ôø€‰ˆ°4(€€€€€€€€€€€€‰UMˆ€ôø€‰ULˆ°(€€€€€€€€€€€€‰U¹¥Ñ•MÑ…Ñ•Ìˆ€ôø€‰ULˆ°(€€€€€€€€€€€€‰ÕÉ½Á”ˆ€ôø¹Õ±°°4(€€€€€€€€€€€|€ôø¹Õ±°4(€€€€€€€ôì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥½µÁ±•Ñ•)½ˆ¡	…­™¥±±)½ˆ©½ˆ°ÍÑÉ¥¹œÍÑ…ÑÕÌ°½‰©•ÐÍÕµµ…Éä¤4(€€€ì4(€€€€€€€©½ˆ¹MÑ…ÑÕÌ€ôÍÑ…ÑÕÌì4(€€€€€€€©½ˆ¹¥¹¥Í¡•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€©½ˆ¹UÁ‘…Ñ•‘ÑUÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€©½ˆ¹MÕµµ…Éå)Í½¸€ô)Í½¹M•É¥…±¥é•È¹M•É¥…±¥é”¡ÍÕµµ…Éä¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œQÉÕ¹…Ñ”¡ÍÑÉ¥¹œÙ…±Õ”°¥¹Ðµ…á1•¹Ñ ¤€ôø4(€€€€€€€Ù…±Õ”¹1•¹Ñ €ðôµ…á1•¹Ñ €üÙ…±Õ”€èÙ…±Õ•l¸¹µ…á1•¹Ñ¡tì4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•±…ÍÌ	…­™¥±±MÕµµ…Éä4(€€€ì4(€€€€€€€ÁÕ‰±¥ŒÍÑÉ¥¹œM½ÕÉ”ì•ÐìÍ•Ðìô€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€ÁÕ‰±¥ŒÍÑÉ¥¹œ1•…Õ•9…µ”ì•ÐìÍ•Ðìô€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€ÁÕ‰±¥ŒÍÑÉ¥¹œM•…Í½¸ì•ÐìÍ•Ðìô€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€ÁÕ‰±¥Œ¥¹ÐI•ÅÕ•ÍÑÍUÍ•ì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ‰½½°!…Í5½É•A…•Ìì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ¥¹Ð…µ•Í•Ñ¡•ì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ¥¹Ð…µ•Í¥±Ñ•É•ì•ÐìÍ•Ðìô(€€€€€€€ÁÕ‰±¥Œ¥¹Ð…µ•Í•‘ÕÁ±¥…Ñ•ì•ÐìÍ•Ðìô(€€€€€€€ÁÕ‰±¥Œ¥¹Ð…µ•Í%¹Í•ÉÑ•ì•ÐìÍ•Ðìô(€€€€€€€ÁÕ‰±¥Œ¥¹Ð…µ•ÍUÁ‘…Ñ•ì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œøAÉ½Ù¥‘•É1•…Õ•Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œøM½ÕÉ•UÉ±Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œøM½ÕÉ•M•…Í½¹-•åÌì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œøA…ÉÍ•ÉY•ÉÍ¥½¹Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œø¥±Ñ•É•‘…µ•I•…Í½¹Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ…Ñ•Q¥µ”üM½ÕÉ••Ñ¡•‘ÑUÑŒì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñÍÑÉ¥¹œø]…É¹¥¹Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥ŒÍÑÉ¥¹œü%‘•¹Ñ¥Ñå!•…±Ñ¡MÑ…ÑÕÌì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ¥¹Ð%‘•¹Ñ¥Ñå¥¹‘¥¹Í½Õ¹Ðì•ÐìÍ•Ðìô4(€€€€€€€ÁÕ‰±¥Œ¥¹Ð%‘•¹Ñ¥Ñå	±½­•ÉÍ½Õ¹Ðì•ÐìÍ•Ðìô4(€€€ô4)ô4(