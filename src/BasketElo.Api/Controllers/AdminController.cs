using BasketElo.Api.Auth;
using BasketElo.Domain.Admin;
using BasketElo.Domain.CurrentResults;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Teams;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/admin")]
[RequireInternalAdmin]
public class AdminController(BasketEloDbContext dbContext) : ControllerBase
{
    private static readonly CompetitionTarget[] SocialCompetitionTargets =
    [
        new("NBA", "NBA", EloPoolKeys.Nba, "/nba-elo", "#NBA"),
        new("ACB", "Liga Endesa", EloPoolKeys.EuropeClubs, "/acb-elo", "#LigaEndesa"),
        new("Euroleague", "EuroLeague", EloPoolKeys.EuropeClubs, "/euroleague-elo", "#EuroLeague")
    ];

    [HttpGet("attention-summary")]
    public async Task<ActionResult<AdminAttentionSummary>> GetAttentionSummary(
        CancellationToken cancellationToken)
    {
        var openResultReviews = await dbContext.CurrentResultReviews
            .AsNoTracking()
            .CountAsync(x => x.Status == CurrentResultReviewStatuses.Open, cancellationToken);

        var activeOpenIdentityFindings = dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .Where(x =>
                x.Status == IdentityFindingStatus.Open &&
                x.Run.InvalidatedAtUtc == null);
        var openIdentityWarnings = await activeOpenIdentityFindings
            .CountAsync(x => x.Severity == IdentityFindingSeverity.Warning, cancellationToken);
        var openIdentityBlockers = await activeOpenIdentityFindings
            .CountAsync(x => x.Severity == IdentityFindingSeverity.Blocker, cancellationToken);

        var aliasRows = await dbContext.TeamAliases
            .AsNoTracking()
            .Select(x => new
            {
                x.AliasName,
                x.Team.CountryCode,
                x.TeamId
            })
            .ToListAsync(cancellationToken);
        var acceptedAliasGroupKeys = (await dbContext.IdentityReviewDecisions
            .AsNoTracking()
            .Where(x =>
                x.FindingType == TeamAliasCollisionReview.FindingType &&
                x.ResolutionAction == TeamAliasCollisionReview.AcceptedAction)
            .Select(x => x.DecisionKey)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var possibleDuplicateAliasGroups = aliasRows
            .GroupBy(x => new
            {
                AliasName = TeamAliasCollisionReview.NormalizeAlias(x.AliasName),
                CountryCode = TeamAliasCollisionReview.NormalizeCountry(x.CountryCode)
            })
            .Count(group =>
                group.Select(x => x.TeamId).Distinct().Skip(1).Any() &&
                !acceptedAliasGroupKeys.Contains(TeamAliasCollisionReview.CreateDecisionKey(
                    group.Key.AliasName,
                    group.Key.CountryCode,
                    group.Select(x => x.TeamId))));

        var teamsMissingCountry = await dbContext.Teams
            .AsNoTracking()
            .CountAsync(x => x.CountryCode == "" || x.CountryCode == "UNK", cancellationToken);

        var unratedEligibleGames = await dbContext.Games
            .AsNoTracking()
            .CountAsync(x =>
                x.EloEligible &&
                x.Competition.EloPoolKey != null &&
                x.HomeScore.HasValue &&
                x.AwayScore.HasValue &&
                x.HomeScore != x.AwayScore &&
                !dbContext.RatingHistories.Any(history =>
                    history.GameId == x.Id &&
                    history.EloPoolKey == x.Competition.EloPoolKey &&
                    history.RulesetVersion == EloRulesetVersions.Default),
                cancellationToken);

        return Ok(new AdminAttentionSummary(
            openResultReviews,
            openIdentityWarnings,
            openIdentityBlockers,
            possibleDuplicateAliasGroups,
            teamsMissingCountry,
            unratedEligibleGames));
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(
        [FromQuery] string? rulesetVersion,
        CancellationToken cancellationToken)
    {
        var selectedRuleset = ResolveRulesetOrDefault(rulesetVersion);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }
        var poolKey = EloPoolKeys.Default;

        var databaseCanConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        var pendingMigrations = databaseCanConnect
            ? await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)
            : [];

        var completedGamesQuery = dbContext.Games
            .AsNoTracking()
            .Where(x => x.Competition.EloPoolKey == poolKey &&
                x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore);

        var completedGames = await completedGamesQuery.CountAsync(cancellationToken);
        var unratedCompletedGames = await completedGamesQuery.CountAsync(
            x => !dbContext.RatingHistories.Any(history =>
                history.GameId == x.Id &&
                history.EloPoolKey == poolKey &&
                history.RulesetVersion == selectedRuleset),
            cancellationToken);

        var latestSuccessfulRebuildUtc = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x =>
                x.RulesetVersion == selectedRuleset &&
                x.EloPoolKey == poolKey &&
                x.Status == EloRebuildRunStatus.Completed)
            .MaxAsync(x => x.FinishedAtUtc, cancellationToken);

        var recentRuns = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(10)
            .Select(x => new EloRebuildRunDto(
                x.Id,
                x.EloPoolKey,
                x.RulesetVersion,
                x.CompetitionName,
                x.Status,
                x.GamesProcessed,
                x.TeamsRated,
                x.QueuedAtUtc,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.FromGameDateTimeUtc,
                x.Notes))
            .ToListAsync(cancellationToken);

        var elo = new EloDashboardResponse(
            new EloRulesetCatalogResponse(EloRulesetVersions.Default, EloRulesetVersions.All),
            new EloDashboardSummary(
                poolKey,
                selectedRuleset,
                completedGames,
                unratedCompletedGames,
                await dbContext.TeamRatings
                    .AsNoTracking()
                    .CountAsync(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset, cancellationToken),
                await completedGamesQuery.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
                latestSuccessfulRebuildUtc,
                await dbContext.EloRebuildRuns
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
                    .MaxAsync(x => (DateTime?)x.QueuedAtUtc, cancellationToken)),
            recentRuns);

        var recentBackfillJobs = await dbContext.BackfillJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(12)
            .Select(x => new AdminBackfillJobRow(
                x.Id,
                x.Provider,
                x.Country,
                x.LeagueName,
                x.Season,
                x.DryRun,
                x.Status,
                x.RequestsUsed,
                x.WarningCount,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.SummaryJson,
                x.ErrorMessage))
            .ToListAsync(cancellationToken);

        var ratedGameIds = (await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == selectedRuleset)
            .Select(x => x.GameId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var gameCoverageRows = await dbContext.Games
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.HomeScore,
                x.AwayScore,
                x.GameDateTimeUtc,
                CompetitionName = x.Competition.Name,
                SeasonLabel = x.Season.Label,
                x.Competition.CountryCode
            })
            .ToListAsync(cancellationToken);

        var gameCoverage = gameCoverageRows
            .GroupBy(x => new { x.CompetitionName, x.SeasonLabel, x.CountryCode })
            .Select(group =>
            {
                var completedGroupGames = group
                    .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore)
                    .ToList();

                return new AdminGameCoverageRow(
                    group.Key.CompetitionName,
                    group.Key.SeasonLabel,
                    group.Key.CountryCode ?? string.Empty,
                    group.Count(),
                    completedGroupGames.Count,
                    completedGroupGames.Count(x => !ratedGameIds.Contains(x.Id)),
                    group.Max(x => (DateTime?)x.GameDateTimeUtc));
            })
            .OrderByDescending(x => x.LatestGameUtc)
            .ThenBy(x => x.Competition)
            .Take(25)
            .ToList();

        var teamsMissingCountry = await dbContext.Teams
            .AsNoTracking()
            .CountAsync(x => x.CountryCode == "" || x.CountryCode == "UNK", cancellationToken);

        var aliasRows = await dbContext.TeamAliases
            .AsNoTracking()
            .Select(x => new
            {
                x.AliasName,
                x.Team.CountryCode,
                x.TeamId
            })
            .ToListAsync(cancellationToken);

        var acceptedAliasGroupKeys = (await dbContext.IdentityReviewDecisions
            .AsNoTracking()
            .Where(x =>
                x.FindingType == TeamAliasCollisionReview.FindingType &&
                x.ResolutionAction == TeamAliasCollisionReview.AcceptedAction)
            .Select(x => x.DecisionKey)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var possibleDuplicateAliasGroups = aliasRows
            .GroupBy(x => new
            {
                AliasName = TeamAliasCollisionReview.NormalizeAlias(x.AliasName),
                CountryCode = TeamAliasCollisionReview.NormalizeCountry(x.CountryCode)
            })
            .Count(group =>
                group.Select(x => x.TeamId).Distinct().Skip(1).Any() &&
                !acceptedAliasGroupKeys.Contains(TeamAliasCollisionReview.CreateDecisionKey(
                    group.Key.AliasName,
                    group.Key.CountryCode,
                    group.Select(x => x.TeamId))));

        var openIdentityWarnings = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .CountAsync(x => x.Status == "open" && x.Severity == "warning", cancellationToken);

        var openIdentityBlockers = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .CountAsync(x => x.Status == "open" && x.Severity == "blocker", cancellationToken);

        var latestBackfillActivityUtc = recentBackfillJobs
            .Select(x => x.FinishedAtUtc ?? x.StartedAtUtc ?? x.CreatedAtUtc)
            .DefaultIfEmpty()
            .Max();
        var latestRebuildActivityUtc = recentRuns
            .Select(x => x.FinishedAtUtc ?? x.StartedAtUtc ?? x.QueuedAtUtc)
            .DefaultIfEmpty()
            .Max();
        var latestWorkerActivityUtc = new[] { latestBackfillActivityUtc, latestRebuildActivityUtc }
            .Where(x => x != default)
            .DefaultIfEmpty()
            .Max();

        var system = new AdminSystemStatus(
            databaseCanConnect,
            pendingMigrations.Count(),
            DateTime.UtcNow,
            ResolveWorkerStatus(recentBackfillJobs, recentRuns),
            latestWorkerActivityUtc == default ? null : latestWorkerActivityUtc);

        var dataHealth = new AdminDataHealthSummary(
            completedGames,
            unratedCompletedGames,
            teamsMissingCountry,
            possibleDuplicateAliasGroups,
            openIdentityWarnings,
            openIdentityBlockers,
            latestSuccessfulRebuildUtc);

        return Ok(new AdminDashboardResponse(
            elo,
            system,
            recentBackfillJobs,
            gameCoverage,
            dataHealth));
    }

    [HttpGet("social-posts")]
    public async Task<ActionResult<AdminSocialPostsResponse>> GetSocialPosts(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var requestedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var targetNames = SocialCompetitionTargets.Select(x => x.Name).ToArray();
        var baseHistoryQuery = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x =>
                x.RulesetVersion == EloRulesetVersions.Default &&
                targetNames.Contains(x.Game.Competition.Name) &&
                x.Game.HomeScore.HasValue &&
                x.Game.AwayScore.HasValue &&
                x.Game.HomeScore != x.Game.AwayScore);

        var contentDate = requestedDate;
        var dayStartUtc = ToUtcStart(contentDate);
        var dayEndUtc = dayStartUtc.AddDays(1);
        var hasRequestedDateResults = await baseHistoryQuery.AnyAsync(
            x => x.GameDateTimeUtc >= dayStartUtc && x.GameDateTimeUtc < dayEndUtc,
            cancellationToken);

        if (!hasRequestedDateResults)
        {
            var requestedEndUtc = ToUtcStart(requestedDate).AddDays(1);
            var latestResultUtc = await baseHistoryQuery
                .Where(x => x.GameDateTimeUtc < requestedEndUtc)
                .MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken);

            if (latestResultUtc.HasValue)
            {
                contentDate = DateOnly.FromDateTime(latestResultUtc.Value);
                dayStartUtc = ToUtcStart(contentDate);
                dayEndUtc = dayStartUtc.AddDays(1);
            }
        }

        var historyRows = await baseHistoryQuery
            .Where(x => x.GameDateTimeUtc >= dayStartUtc && x.GameDateTimeUtc < dayEndUtc)
            .Select(x => new SocialHistoryRow(
                x.GameId,
                x.GameDateTimeUtc,
                x.Game.Competition.Name,
                x.EloPoolKey,
                x.TeamId,
                x.Team.CanonicalName,
                x.OpponentTeamId,
                x.OpponentTeam.CanonicalName,
                x.Game.HomeTeamId,
                x.Game.HomeScore!.Value,
                x.Game.AwayScore!.Value,
                x.PreElo,
                x.PostElo,
                x.EloDelta,
                x.ExpectedScore,
                x.ActualScore))
            .ToListAsync(cancellationToken);

        var rowsByTeamAndGame = historyRows.ToDictionary(x => (x.GameId, x.TeamId));
        var winners = historyRows
            .Where(x => x.ActualScore > 0.5m)
            .OrderBy(x => x.GameDateTimeUtc)
            .ToList();
        var drafts = new List<AdminSocialPostDraft>(3);

        var biggestUpset = winners
            .Where(x => x.ExpectedScore < 0.5m)
            .OrderBy(x => x.ExpectedScore)
            .ThenByDescending(x => x.EloDelta)
            .FirstOrDefault();

        if (biggestUpset is not null)
        {
            drafts.Add(BuildGameDraft(biggestUpset, rowsByTeamAndGame, "upset", contentDate));
        }

        var biggestMove = winners
            .Where(x => biggestUpset is null || x.GameId != biggestUpset.GameId)
            .OrderByDescending(x => x.EloDelta)
            .ThenBy(x => x.ExpectedScore)
            .FirstOrDefault();

        if (biggestMove is not null)
        {
            drafts.Add(BuildGameDraft(biggestMove, rowsByTeamAndGame, "mover", contentDate));
        }

        var rankingDraft = await BuildRotatingRankingDraftAsync(requestedDate, cancellationToken);
        if (rankingDraft is not null)
        {
            drafts.Add(rankingDraft);
        }

        return Ok(new AdminSocialPostsResponse(
            requestedDate,
            contentDate,
            contentDate != requestedDate,
            DateTime.UtcNow,
            historyRows.Select(x => x.GameId).Distinct().Count(),
            drafts));
    }

    private async Task<AdminSocialPostDraft?> BuildRotatingRankingDraftAsync(
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var firstTargetIndex = Math.Abs(requestedDate.DayNumber % SocialCompetitionTargets.Length);

        for (var offset = 0; offset < SocialCompetitionTargets.Length; offset++)
        {
            var target = SocialCompetitionTargets[(firstTargetIndex + offset) % SocialCompetitionTargets.Length];
            var latestSeasonId = await dbContext.Games
                .AsNoTracking()
                .Where(x => x.Competition.Name == target.Name)
                .OrderByDescending(x => x.GameDateTimeUtc)
                .Select(x => (Guid?)x.SeasonId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!latestSeasonId.HasValue)
            {
                continue;
            }

            var homeTeamIds = dbContext.Games
                .AsNoTracking()
                .Where(x => x.Competition.Name == target.Name && x.SeasonId == latestSeasonId.Value)
                .Select(x => x.HomeTeamId);
            var awayTeamIds = dbContext.Games
                .AsNoTracking()
                .Where(x => x.Competition.Name == target.Name && x.SeasonId == latestSeasonId.Value)
                .Select(x => x.AwayTeamId);
            var currentTeamIds = homeTeamIds.Union(awayTeamIds);

            var rankings = await dbContext.TeamRatings
                .AsNoTracking()
                .Where(x =>
                    x.EloPoolKey == target.PoolKey &&
                    x.RulesetVersion == EloRulesetVersions.Default &&
                    currentTeamIds.Contains(x.TeamId))
                .OrderByDescending(x => x.Elo)
                .ThenBy(x => x.Team.CanonicalName)
                .Take(5)
                .Select(x => new { x.TeamId, TeamName = x.Team.CanonicalName, x.Elo })
                .ToListAsync(cancellationToken);

            if (rankings.Count == 0)
            {
                continue;
            }

            var rankedRows = rankings
                .Select((x, index) => new AdminSocialRankingEntry(
                    index + 1,
                    x.TeamId,
                    x.TeamName,
                    decimal.Round(x.Elo, 1)))
                .ToList();
            var leader = rankedRows[0];
            var rankingLinesEnglish = string.Join("\n", rankedRows.Select(x => $"{x.Rank}. {x.TeamName} — {x.Elo:0.0}"));
            var rankingLinesSpanish = string.Join("\n", rankedRows.Select(x => $"{x.Rank}. {x.TeamName} — {x.Elo:0.0}"));

            return new AdminSocialPostDraft(
                $"ranking-{target.Name.ToLowerInvariant()}-{requestedDate:yyyyMMdd}",
                "ranking",
                target.DisplayName,
                "CURRENT POWER RANKING",
                $"{target.DisplayName}: the top five",
                $"{leader.TeamName} leads at {leader.Elo:0.0} ELO",
                $"Current {target.DisplayName} ELO top five 🏀\n\n{rankingLinesEnglish}\n\n{target.Hashtag} #Basketball #EloRatings",
                $"Top 5 ELO actual de {target.DisplayName} 🏀\n\n{rankingLinesSpanish}\n\n{target.Hashtag} #Baloncesto #EloRatings",
                target.Route,
                null,
                rankedRows);
        }

        return null;
    }

    private static AdminSocialPostDraft BuildGameDraft(
        SocialHistoryRow winner,
        IReadOnlyDictionary<(Guid GameId, Guid TeamId), SocialHistoryRow> rowsByTeamAndGame,
        string type,
        DateOnly contentDate)
    {
        rowsByTeamAndGame.TryGetValue((winner.GameId, winner.OpponentTeamId), out var opponent);
        var target = SocialCompetitionTargets.First(x => x.Name == winner.League);
        var winnerWasHome = winner.TeamId == winner.HomeTeamId;
        var winnerScore = winnerWasHome ? winner.HomeScore : winner.AwayScore;
        var loserScore = winnerWasHome ? winner.AwayScore : winner.HomeScore;
        var expectedPercentage = decimal.Round(winner.ExpectedScore * 100m, 0);
        var roundedDelta = decimal.Round(winner.EloDelta, 1);
        var roundedPreElo = decimal.Round(winner.PreElo, 1);
        var roundedPostElo = decimal.Round(winner.PostElo, 1);
        var roundedOpponentPreElo = decimal.Round(opponent?.PreElo ?? 1500m, 1);
        var scoreline = $"{winner.TeamName} {winnerScore}–{loserScore} {winner.OpponentName}";
        var teamPath = $"/team/{winner.TeamId:D}/{ToSlug(winner.TeamName)}?pool={Uri.EscapeDataString(winner.EloPoolKey)}&ruleset={Uri.EscapeDataString(EloRulesetVersions.Default)}";
        var visual = new AdminSocialGameVisual(
            winner.GameId,
            winner.TeamName,
            winner.OpponentName,
            winnerScore,
            loserScore,
            roundedPreElo,
            roundedPostElo,
            roundedOpponentPreElo,
            expectedPercentage,
            roundedDelta);

        if (type == "upset")
        {
            return new AdminSocialPostDraft(
                $"upset-{winner.GameId:N}",
                type,
                target.DisplayName,
                "UPSET OF THE DAY",
                $"{winner.TeamName} defied the model",
                $"Only a {expectedPercentage:0}% pre-game chance · +{roundedDelta:0.0} ELO",
                $"Upset of the day in {target.DisplayName} 🏀\n\n{scoreline}\n\nBasketElo gave {winner.TeamName} a {expectedPercentage:0}% chance before tip-off. The win added {roundedDelta:0.0} ELO points.\n\n{target.Hashtag} #Basketball #EloRatings",
                $"Sorpresa del día en {target.DisplayName} 🏀\n\n{scoreline}\n\nBasketElo daba a {winner.TeamName} un {expectedPercentage:0}% de probabilidad antes del partido. La victoria añadió {roundedDelta:0.0} puntos ELO.\n\n{target.Hashtag} #Baloncesto #EloRatings",
                teamPath,
                visual,
                []);
        }

        return new AdminSocialPostDraft(
            $"mover-{winner.GameId:N}",
            type,
            target.DisplayName,
            "BIGGEST ELO MOVE",
            $"{winner.TeamName} made the biggest jump",
            $"{scoreline} · +{roundedDelta:0.0} ELO",
            $"Biggest ELO move from {contentDate:MMM d} 🏀\n\n{scoreline}\n\n{winner.TeamName}: {roundedPreElo:0.0} → {roundedPostElo:0.0} ELO ({roundedDelta:+0.0;-0.0;0.0}).\n\n{target.Hashtag} #Basketball #EloRatings",
            $"Mayor subida ELO del {contentDate:dd/MM} 🏀\n\n{scoreline}\n\n{winner.TeamName}: {roundedPreElo:0.0} → {roundedPostElo:0.0} ELO ({roundedDelta:+0.0;-0.0;0.0}).\n\n{target.Hashtag} #Baloncesto #EloRatings",
            teamPath,
            visual,
            []);
    }

    private static DateTime ToUtcStart(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private static string ToSlug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var slug = new string(normalized
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        return string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? ResolveRulesetOrDefault(string? rulesetVersion)
    {
        if (string.IsNullOrWhiteSpace(rulesetVersion))
        {
            return EloRulesetVersions.Default;
        }

        var normalized = rulesetVersion.Trim().ToLowerInvariant();
        return EloRulesetVersions.All.Contains(normalized) ? normalized : null;
    }

    private static string ResolveWorkerStatus(
        IReadOnlyCollection<AdminBackfillJobRow> backfillJobs,
        IReadOnlyCollection<EloRebuildRunDto> rebuildRuns)
    {
        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Running) ||
            rebuildRuns.Any(x => x.Status == EloRebuildRunStatus.Running))
        {
            return "running";
        }

        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Pending) ||
            rebuildRuns.Any(x => x.Status == EloRebuildRunStatus.Pending))
        {
            return "pending";
        }

        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Failed) ||
            rebuildRuns.Any(x => x.Status is EloRebuildRunStatus.Failed or EloRebuildRunStatus.Blocked))
        {
            return "attention";
        }

        return "idle";
    }

    private sealed record CompetitionTarget(
        string Name,
        string DisplayName,
        string PoolKey,
        string Route,
        string Hashtag);

    private sealed record SocialHistoryRow(
        Guid GameId,
        DateTime GameDateTimeUtc,
        string League,
        string EloPoolKey,
        Guid TeamId,
        string TeamName,
        Guid OpponentTeamId,
        string OpponentName,
        Guid HomeTeamId,
        short HomeScore,
        short AwayScore,
        decimal PreElo,
        decimal PostElo,
        decimal EloDelta,
        decimal ExpectedScore,
        decimal ActualScore);
}
