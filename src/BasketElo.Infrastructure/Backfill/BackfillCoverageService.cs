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

        var aliases = await dbContext.CompetitionAliases
            .AsNoTracking()
            .Include(x => x.Competition)
            .ToListAsync(cancellationToken);

        var aliasMatches = aliases
            .Where(alias => configuredLeagues.Any(league =>
                string.Equals(league.Provider, alias.Source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(league.LeagueName, alias.AliasName, StringComparison.OrdinalIgnoreCase)))
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
                        string.Equals(alias.AliasName, league.LeagueName, StringComparison.OrdinalIgnoreCase)) ||
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

        var rows = new List<BackfillCoverageRow>();
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

                rows.Add(new BackfillCoverageRow(
                    league.Provider,
                    league.Country,
                    league.LeagueName,
                    league.DisplayName,
                    season,
                    coverageStatus,
                    latestJob?.FinishedAtUtc ?? latestJob?.StartedAtUtc,
                    latestJob?.RequestsUsed ?? 0,
                    latestJob?.WarningCount ?? 0,
                    ExtractWarnings(latestJob),
                    dataPresent,
                    gameCount,
                    latestJob?.Status,
                    latestJob?.Id));
            }
        }

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
            "LV" => "Latvia",
            "LVA" => "Latvia",
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
            _ => countryCode ?? string.Empty
        };
    }

    private readonly record struct CompetitionKey(string Country, string LeagueName);

    private static string LeagueKey(string provider, string country, string leagueName)
        => $"{provider}|{country}|{leagueName}";
}
