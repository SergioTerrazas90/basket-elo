using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCoverageService(
    BasketEloDbContext dbContext,
    IBackfillCatalog catalog) : IBackfillCoverageService
{
    public async Task<BackfillCoverageResponse> GetCoverageAsync(
        string? provider,
        string? country,
        string? leagueName,
        CancellationToken cancellationToken)
    {
        var configuredLeagues = catalog.GetLeagues()
            .Where(x => string.IsNullOrWhiteSpace(provider) || string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(country) || string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(leagueName) || string.Equals(x.LeagueName, leagueName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var jobs = await dbContext.BackfillJobs
            .AsNoTracking()
            .Where(x => configuredLeagues.Select(l => l.Provider).Contains(x.Provider))
            .ToListAsync(cancellationToken);

        var decisions = await dbContext.BackfillInspectionDecisions
            .AsNoTracking()
            .Where(x => configuredLeagues.Select(l => l.Provider).Contains(x.Provider))
            .ToListAsync(cancellationToken);

        var aliases = await dbContext.CompetitionAliases
            .AsNoTracking()
            .Include(x => x.Competition)
            .ToListAsync(cancellationToken);

        var aliasMatches = aliases
            .Where(alias => configuredLeagues.Any(league =>
                string.Equals(league.Provider, alias.Source, StringComparison.OrdinalIgnoreCase) &&
                ProviderLeagueMappings(league).Any(mapping =>
                    string.Equals(mapping.LeagueName, alias.AliasName, StringComparison.OrdinalIgnoreCase))))
            .Select(alias => alias.Competition)
            .ToList();

        var competitionKeys = configuredLeagues
            .Select(x => new CompetitionKey(x.Country, x.LeagueName))
            .Distinct()
            .ToHashSet();

        var allCompetitions = await dbContext.Competitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var directMatches = allCompetitions
            .Where(c => competitionKeys.Contains(new CompetitionKey(DisplayCountryFromCode(c.CountryCode), c.Name)))
            .ToList();

        var matchedCompetitions = aliasMatches
            .Concat(directMatches)
            .GroupBy(c => c.Id)
            .Select(group => group.First())
            .ToList();

        var competitionIdsByLeague = configuredLeagues.ToDictionary(
            league => LeagueKey(league.Provider, league.Country, league.LeagueName),
            league => matchedCompetitions
                .Where(competition => aliases.Any(alias =>
                        alias.CompetitionId == competition.Id &&
                        string.Equals(alias.Source, league.Provider, StringComparison.OrdinalIgnoreCase) &&
                        ProviderLeagueMappings(league).Any(mapping =>
                            string.Equals(alias.AliasName, mapping.LeagueName, StringComparison.OrdinalIgnoreCase))) ||
                    (string.Equals(competition.Name, league.LeagueName, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(DisplayCountryFromCode(competition.CountryCode), league.Country, StringComparison.OrdinalIgnoreCase)))
                .Select(competition => competition.Id)
                .Distinct()
                .ToHashSet());

        var seasons = await dbContext.Seasons
            .AsNoTracking()
            .Where(x => matchedCompetitions.Select(c => c.Id).Contains(x.CompetitionId))
            .ToListAsync(cancellationToken);

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(x => seasons.Select(s => s.Id).Contains(x.SeasonId))
            .ToListAsync(cancellationToken);

        var latestJobs = jobs
            .GroupBy(x => $"{x.Provider}|{x.Country}|{x.LeagueName}|{x.Season}")
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(j => j.CreatedAtUtc).First());

        var gameCountsBySeasonId = games
            .GroupBy(game => game.SeasonId)
            .ToDictionary(group => group.Key, group => group.Count());

        var rowCandidates = new List<BackfillCoverageCandidate>();
        foreach (var league in configuredLeagues)
        {
            competitionIdsByLeague.TryGetValue(LeagueKey(league.Provider, league.Country, league.LeagueName), out var competitionIds);
            competitionIds ??= [];

            foreach (var season in catalog.GetSeasonsForLeague(league))
            {
                var jobKey = $"{league.Provider}|{league.Country}|{league.LeagueName}|{season}";
                latestJobs.TryGetValue(jobKey, out var latestJob);

                var gameCount = seasons
                    .Where(s => competitionIds.Contains(s.CompetitionId) && string.Equals(s.Label, season, StringComparison.OrdinalIgnoreCase))
                    .Sum(s => gameCountsBySeasonId.GetValueOrDefault(s.Id));
                var dataPresent = gameCount > 0;
                var coverageStatus = ComputeCoverageStatus(latestJob, dataPresent);

                rowCandidates.Add(new BackfillCoverageCandidate(
                    league.Provider,
                    league.Country,
                    league.LeagueName,
                    league.DisplayName,
                    league.CompetitionType ?? InferCompetitionType(league),
                    season,
                    coverageStatus,
                    latestJob?.FinishedAtUtc ?? latestJob?.StartedAtUtc,
                    latestJob?.RequestsUsed ?? 0,
                    latestJob?.WarningCount ?? 0,
                    ExtractWarnings(latestJob),
                    dataPresent,
                    gameCount,
                    latestJob?.Status,
                    latestJob?.Id,
                    latestJob is not null));
            }
        }

        var rows = ApplyInspectionFlags(rowCandidates);
        rows = ApplyInspectionDecisions(rows, decisions);

        return new BackfillCoverageResponse(rows
            .OrderBy(x => x.Country)
            .ThenBy(x => x.LeagueName)
            .ThenByDescending(x => x.Season)
            .ToList());
    }

    private static string ComputeCoverageStatus(BackfillJob? job, bool dataPresent)
    {
        if (job is null)
        {
            return dataPresent ? "partial" : "not_started";
        }

        return job.Status switch
        {
            BackfillJobStatus.Pending => "in_progress",
            BackfillJobStatus.Running => "in_progress",
            BackfillJobStatus.CompletedWithWarnings => "completed_with_warnings",
            BackfillJobStatus.Completed when job.DryRun && !dataPresent => "dry_run",
            BackfillJobStatus.Completed when dataPresent => "completed",
            BackfillJobStatus.Completed => "no_data",
            BackfillJobStatus.Failed when dataPresent => "partial",
            BackfillJobStatus.Failed => "failed",
            _ => dataPresent ? "partial" : "not_started"
        };
    }

    private static IReadOnlyCollection<string> ExtractWarnings(BackfillJob? job)
    {
        if (job is null || job.WarningCount == 0)
        {
            return [];
        }

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(job.SummaryJson))
        {
            try
            {
                using var document = JsonDocument.Parse(job.SummaryJson);
                var root = document.RootElement;

                if (TryGetString(root, "message", out var message))
                {
                    warnings.Add(message);
                }

                if (TryGetProperty(root, "warnings", out var warningArray) &&
                    warningArray.ValueKind == JsonValueKind.Array)
                {
                    warnings.AddRange(warningArray
                        .EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))!);
                }

                if (TryGetProperty(root, "hasMorePages", out var hasMorePages) &&
                    hasMorePages.ValueKind is JsonValueKind.True)
                {
                    warnings.Add("The provider returned more pages than the current request budget allowed, so only the first page of games was imported.");
                }
            }
            catch (JsonException)
            {
                warnings.Add("This job completed with warnings, but its stored summary could not be read.");
            }
        }

        if (warnings.Count == 0)
        {
            warnings.Add("This job completed with warnings, but no warning details were recorded.");
        }

        return warnings.Distinct().ToList();
    }

    private static IReadOnlyCollection<BackfillCoverageRow> ApplyInspectionFlags(IReadOnlyCollection<BackfillCoverageCandidate> rows)
    {
        var flaggedRows = new List<BackfillCoverageRow>();

        foreach (var leagueRows in rows.GroupBy(x => LeagueKey(x.Provider, x.Country, x.LeagueName)))
        {
            var completedCounts = leagueRows
                .Where(x => x.HasRun && x.DataPresent && x.GameCount > 0)
                .Select(x => x.GameCount)
                .Order()
                .ToList();

            var medianCompletedCount = Median(completedCounts);
            foreach (var row in leagueRows)
            {
                var reasons = new List<string>();

                if (row.HasRun && row.CoverageStatus == "no_data")
                {
                    reasons.Add("Latest completed run returned 0 games.");
                }

                if (row.WarningCount > 0)
                {
                    reasons.Add("Latest run completed with provider or import warnings.");
                }

                if (row.HasRun &&
                    row.DataPresent &&
                    medianCompletedCount is > 0 &&
                    ShouldFlagLowCount(row, medianCompletedCount.Value))
                {
                    var nbaExpectation = NbaCoverageExpectations.IsNba(row.Provider, row.Country, row.LeagueName)
                        ? NbaCoverageExpectations.ForSeason(row.Season)
                        : null;
                    reasons.Add(nbaExpectation is null
                        ? $"Game count is unusually low for this league ({row.GameCount} vs median {medianCompletedCount.Value:0})."
                        : $"NBA {nbaExpectation.EraDescription} expects at least {nbaExpectation.MinimumCompleteGames} games including playoffs; found {row.GameCount}.");
                }

                flaggedRows.Add(row.ToCoverageRow(reasons, InspectionSeverity(row, reasons), null));
            }
        }

        return flaggedRows;
    }

    private static IReadOnlyCollection<BackfillCoverageRow> ApplyInspectionDecisions(
        IReadOnlyCollection<BackfillCoverageRow> rows,
        IReadOnlyCollection<BackfillInspectionDecision> decisions)
    {
        var decisionsByKey = decisions.ToDictionary(
            x => LeagueSeasonKey(x.Provider, x.Country, x.LeagueName, x.Season),
            StringComparer.OrdinalIgnoreCase);

        return rows.Select(row =>
        {
            if (!decisionsByKey.TryGetValue(
                    LeagueSeasonKey(row.Provider, row.Country, row.LeagueName, row.Season),
                    out var decision))
            {
                return row;
            }

            var reasons = row.InspectionReasons.ToList();
            if (!string.IsNullOrWhiteSpace(decision.Note))
            {
                reasons.Add(decision.Note);
            }

            return row with
            {
                NeedsInspection = false,
                InspectionReasons = reasons.Distinct().ToList(),
                InspectionSeverity = "reviewed",
                InspectionStatus = decision.Status,
                InspectionNote = decision.Note,
                InspectionReviewedAtUtc = decision.ReviewedAtUtc
            };
        }).ToList();
    }

    private static bool ShouldFlagLowCount(BackfillCoverageCandidate row, decimal medianCompletedCount)
    {
        if (NbaCoverageExpectations.IsNba(row.Provider, row.Country, row.LeagueName))
        {
            var expectation = NbaCoverageExpectations.ForSeason(row.Season);
            return expectation is not null && row.GameCount < expectation.MinimumCompleteGames;
        }

        if (medianCompletedCount <= 0)
        {
            return false;
        }

        if (IsSuperCup(row))
        {
            return row.GameCount == 0;
        }

        if (IsCup(row))
        {
            if (row.GameCount >= 4 && medianCompletedCount <= 20)
            {
                return false;
            }

            return row.GameCount < Math.Max(4m, medianCompletedCount * 0.35m);
        }

        return row.GameCount < medianCompletedCount * 0.65m;
    }

    private static string InspectionSeverity(BackfillCoverageCandidate row, IReadOnlyCollection<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return "none";
        }

        if (row.CoverageStatus == "no_data")
        {
            return "high";
        }

        return IsCup(row) ? "medium" : "high";
    }

    private static decimal? Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(root, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement property)
    {
        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string DisplayCountryFromCode(string? countryCode)
    {
        return countryCode?.ToUpperInvariant() switch
        {
            "ES" => "Spain",
            "ESP" => "Spain",
            "FR" => "France",
            "FRA" => "France",
            "LT" => "Lithuania",
            "LTU" => "Lithuania",
            "GR" => "Greece",
            "GRC" => "Greece",
            "IT" => "Italy",
            "ITA" => "Italy",
            "TR" => "Turkey",
            "TUR" => "Turkey",
            "BE" => "Belgium",
            "BEL" => "Belgium",
            "DE" => "Germany",
            "DEU" => "Germany",
            "IL" => "Israel",
            "ISR" => "Israel",
            "PL" => "Poland",
            "POL" => "Poland",
            "CZ" => "Czech Republic",
            "CZE" => "Czech Republic",
            "RU" => "Russia",
            "RUS" => "Russia",
            "RS" => "Serbia",
            "SRB" => "Serbia",
            "HR" => "Croatia",
            "HRV" => "Croatia",
            "SI" => "Slovenia",
            "SVN" => "Slovenia",
            "LV" => "Latvia",
            "LVA" => "Latvia",
            "EE" => "Estonia",
            "EST" => "Estonia",
            "US" => "United States",
            "USA" => "United States",
            _ => countryCode ?? string.Empty
        };
    }

    private static string InferCompetitionType(ConfiguredBackfillLeague league)
    {
        if (league.Country == "Europe")
        {
            return "international";
        }

        return IsCup(league.LeagueName, league.CompetitionType)
            ? "domestic_cup"
            : "domestic_first_division";
    }

    private static bool IsCup(BackfillCoverageCandidate row)
        => IsCup(row.LeagueName, row.CompetitionType);

    private static bool IsCup(string leagueName, string? competitionType)
    {
        var normalizedType = competitionType ?? string.Empty;
        return normalizedType.Contains("cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("copa", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("supercopa", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuperCup(BackfillCoverageCandidate row)
    {
        return row.LeagueName.Contains("super cup", StringComparison.OrdinalIgnoreCase) ||
            row.LeagueName.Contains("supercup", StringComparison.OrdinalIgnoreCase) ||
            row.LeagueName.Contains("supercopa", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<ConfiguredProviderLeague> ProviderLeagueMappings(ConfiguredBackfillLeague league)
    {
        return league.ProviderLeagues is { Count: > 0 }
            ? league.ProviderLeagues
            : [new ConfiguredProviderLeague(league.Country, league.LeagueName)];
    }

    private readonly record struct CompetitionKey(string Country, string LeagueName);

    private static string LeagueKey(string provider, string country, string leagueName)
        => $"{provider}|{country}|{leagueName}";

    private static string LeagueSeasonKey(string provider, string country, string leagueName, string season)
        => $"{provider}|{country}|{leagueName}|{season}";

    private sealed record BackfillCoverageCandidate(
        string Provider,
        string Country,
        string LeagueName,
        string DisplayName,
        string CompetitionType,
        string Season,
        string CoverageStatus,
        DateTime? LastRunUtc,
        int RequestsUsed,
        int WarningCount,
        IReadOnlyCollection<string> Warnings,
        bool DataPresent,
        int GameCount,
        string? LatestJobStatus,
        Guid? LatestJobId,
        bool HasRun)
    {
        public BackfillCoverageRow ToCoverageRow(
            IReadOnlyCollection<string> inspectionReasons,
            string inspectionSeverity,
            BackfillInspectionDecision? decision)
            => new(
                Provider,
                Country,
                LeagueName,
                DisplayName,
                Season,
                CoverageStatus,
                LastRunUtc,
                RequestsUsed,
                WarningCount,
                Warnings,
                DataPresent,
                GameCount,
                LatestJobStatus,
                LatestJobId,
                inspectionReasons.Count > 0 && decision is null,
                inspectionReasons,
                decision is null ? inspectionSeverity : "reviewed",
                decision?.Status,
                decision?.Note,
                decision?.ReviewedAtUtc);
    }
}
