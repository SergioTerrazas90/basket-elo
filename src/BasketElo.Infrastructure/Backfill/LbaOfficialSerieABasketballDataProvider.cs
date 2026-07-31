using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasketElo.Domain.Backfill;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads historical Serie A regular-season and playoff calendars from the
/// public JSON endpoints used by the official Lega Basket Serie A website.
/// </summary>
public sealed class LbaOfficialSerieABasketballDataProvider(
    HttpClient httpClient,
    IOptions<LbaOfficialOptions> options) : IBasketballDataProvider
{
    public const string Source = "lba-official";
    public const string ParserVersion = "lba-official-calendar-v1";
    private const int SerieACompetitionSeriesId = 1;

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Italy", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(leagueName, "Serie A", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(leagueName, "Lega A", StringComparison.OrdinalIgnoreCase))
                ? new BasketballProviderLeague(Source, "SERIE_A", "Lega Basket Serie A", "IT", "start_year")
                : null;
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "SERIE_A", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Official LBA provider only supports Italy: Serie A.");
        }

        var startYear = ParseStartYear(season);
        var warnings = new List<string>();
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);

        using var championships = await FetchJsonAsync(
            $"/api/championships/get-championships?items=1000&cs_id={SerieACompetitionSeriesId}",
            context,
            cancellationToken);
        if (championships is null)
        {
            return ([], false, ["The official LBA championship catalog could not be fetched."]);
        }

        var competitions = ReadCompetitions(championships.RootElement, startYear).ToList();
        if (competitions.All(competition => !competition.TypeCode.Equals("RS", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"{season}: no official Serie A regular-season championship was found.");
        }

        if (competitions.All(competition => !competition.TypeCode.Equals("PO", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"{season}: the official LBA catalog does not expose a separate playoff championship.");
        }

        IReadOnlyDictionary<long, LbaTeam> teamMap;
        if (context.CanUseRequest())
        {
            teamMap = await LoadTeamsAsync(startYear, context, warnings, cancellationToken);
        }
        else
        {
            warnings.Add($"{season}: request budget reached before official LBA team identities could be read.");
            teamMap = new Dictionary<long, LbaTeam>();
        }
        foreach (var competition in competitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget reached before championship {competition.Id} could be read.");
                break;
            }

            using var calendar = await FetchJsonAsync(
                CalendarPath(competition.Id),
                context,
                cancellationToken);
            if (calendar is null)
            {
                warnings.Add($"{season}: calendar metadata for championship {competition.Id} was unavailable.");
                continue;
            }

            var days = ReadDays(calendar.RootElement).ToList();
            if (days.Count == 0)
            {
                warnings.Add($"{season}: championship {competition.Id} exposed no matchdays.");
                continue;
            }

            foreach (var day in days)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!context.CanUseRequest())
                {
                    warnings.Add(
                        $"Request budget reached after {games.Count} games; remaining LBA matchdays were not attempted.");
                    break;
                }

                var path = $"{CalendarPath(competition.Id)}&d={day.EventSerial}";
                using var matchday = await FetchJsonAsync(path, context, cancellationToken);
                if (matchday is null)
                {
                    warnings.Add($"{season}: {competition.Name}, {day.Name} could not be fetched.");
                    continue;
                }

                foreach (var match in ReadMatches(matchday.RootElement))
                {
                    if (TryParseGame(match, season, competition, day, teamMap, path, out var game, out var warning))
                    {
                        games[game!.SourceGameId] = game;
                    }
                    else
                    {
                        warnings.Add($"{season}: {competition.Name}, {day.Name}: {warning}");
                    }
                }
            }
        }

        return (
            games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId).ToArray(),
            false,
            warnings);
    }

    private async Task<IReadOnlyDictionary<long, LbaTeam>> LoadTeamsAsync(
        int startYear,
        BackfillExecutionContext context,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        using var document = await FetchJsonAsync(
            $"/api/teams/get-teams?year={startYear}&items=1000",
            context,
            cancellationToken);
        if (document is null)
        {
            warnings.Add($"{startYear}: official LBA team-to-club identities were unavailable; season team IDs will be used.");
            return new Dictionary<long, LbaTeam>();
        }

        if (!document.RootElement.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"{startYear}: official LBA team response did not contain a teams array.");
            return new Dictionary<long, LbaTeam>();
        }

        return teams.EnumerateArray()
            .Select(team => new LbaTeam(
                team.GetProperty("id").GetInt64(),
                team.TryGetProperty("club_id", out var clubId) && clubId.ValueKind == JsonValueKind.Number
                    ? clubId.GetInt64()
                    : null,
                String(team, "club_code"),
                String(team, "name")))
            .ToDictionary(team => team.TeamId);
    }

    internal static bool TryParseGame(
        JsonElement match,
        string season,
        LbaCompetition competition,
        LbaDay day,
        IReadOnlyDictionary<long, LbaTeam> teamMap,
        string sourcePath,
        out BasketballProviderGame? game,
        out string warning)
    {
        game = null;
        warning = string.Empty;
        if (!match.TryGetProperty("id", out var idNode) || idNode.ValueKind != JsonValueKind.Number ||
            !match.TryGetProperty("match_datetime", out var dateNode) || dateNode.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(dateNode.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !match.TryGetProperty("h_team_id", out var homeIdNode) || homeIdNode.ValueKind != JsonValueKind.Number ||
            !match.TryGetProperty("v_team_id", out var awayIdNode) || awayIdNode.ValueKind != JsonValueKind.Number)
        {
            warning = "match ID, date, or team IDs are missing";
            return false;
        }

        var matchId = idNode.GetInt64();
        var homeTeamId = homeIdNode.GetInt64();
        var awayTeamId = awayIdNode.GetInt64();
        var homeName = String(match, "h_team_name");
        var awayName = String(match, "v_team_name");
        if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
        {
            warning = $"match {matchId} has a missing team name";
            return false;
        }

        var homeScore = Short(match, "home_final_score");
        var awayScore = Short(match, "visitor_final_score");
        if (homeScore.HasValue != awayScore.HasValue)
        {
            warning = $"match {matchId} has only one final score";
            return false;
        }

        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(match.GetRawText()))).ToLowerInvariant();
        game = new BasketballProviderGame(
            Source,
            matchId.ToString(CultureInfo.InvariantCulture),
            date.UtcDateTime,
            homeScore.HasValue && awayScore.HasValue ? "finished" : "scheduled",
            StableTeamId(homeTeamId, teamMap),
            homeName,
            StableTeamId(awayTeamId, teamMap),
            awayName,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(
                new Uri(new Uri("https://www.legabasket.it"), sourcePath).ToString(),
                season,
                DateTime.UtcNow,
                ParserVersion,
                revision),
            CompetitionPhase: competition.TypeName,
            CompetitionRound: String(match, "day_name") ?? day.Name,
            SourceHomeTeamCountryCode: "IT",
            SourceAwayTeamCountryCode: "IT");
        return true;
    }

    private async Task<JsonDocument?> FetchJsonAsync(
        string path,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var response = await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("Official LBA request budget reached.");
                }

                context.ConsumeRequest();
                if (options.Value.MinRequestIntervalMilliseconds > 0)
                {
                    await Task.Delay(options.Value.MinRequestIntervalMilliseconds, retryCancellationToken);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static IEnumerable<LbaCompetition> ReadCompetitions(JsonElement root, int startYear)
    {
        if (!root.TryGetProperty("competitions", out var competitions) || competitions.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var competition in competitions.EnumerateArray())
        {
            if (competition.GetProperty("year").GetInt32() != startYear)
            {
                continue;
            }

            var typeCode = String(competition, "ctype_code");
            if (typeCode is not ("RS" or "PO"))
            {
                continue;
            }

            yield return new LbaCompetition(
                competition.GetProperty("id").GetInt64(),
                typeCode,
                String(competition, "ctype_name") ?? typeCode,
                String(competition, "full_name") ?? String(competition, "name") ?? typeCode);
        }
    }

    private static IEnumerable<LbaDay> ReadDays(JsonElement root)
    {
        if (!root.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Object ||
            !filters.TryGetProperty("days", out var days) || days.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var day in days.EnumerateArray())
        {
            if (!day.TryGetProperty("event_serial", out var serial) || serial.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            yield return new LbaDay(serial.GetInt64(), String(day, "name") ?? serial.GetRawText());
        }
    }

    private static IEnumerable<JsonElement> ReadMatches(JsonElement root)
    {
        if (!root.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var match in matches.EnumerateArray())
        {
            yield return match;
        }
    }

    private static int ParseStartYear(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonical.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out var startYear) ||
            !int.TryParse(pieces[1], out var endYear) || endYear != startYear + 1 || startYear is < 1974 or > 2007)
        {
            throw new ArgumentException(
                "Official historical LBA coverage currently supports 1974-1975 through 2007-2008.",
                nameof(season));
        }

        return startYear;
    }

    private static string CalendarPath(long championshipId) =>
        $"/api/championships/get-championships-calendar-by-id?id={championshipId}";

    private static string StableTeamId(long seasonTeamId, IReadOnlyDictionary<long, LbaTeam> teamMap)
    {
        if (teamMap.TryGetValue(seasonTeamId, out var team))
        {
            if (team.ClubId.HasValue)
            {
                return $"club:{team.ClubId.Value}";
            }

            if (!string.IsNullOrWhiteSpace(team.ClubCode))
            {
                return $"club-code:{team.ClubCode.ToUpperInvariant()}";
            }
        }

        return $"team:{seasonTeamId}";
    }

    private static string? String(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static short? Short(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt16(out var value)
            ? value
            : null;

    internal sealed record LbaCompetition(long Id, string TypeCode, string TypeName, string Name);
    internal sealed record LbaDay(long EventSerial, string Name);
    internal sealed record LbaTeam(long TeamId, long? ClubId, string? ClubCode, string? Name);
}
